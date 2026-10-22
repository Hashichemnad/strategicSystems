using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Sustainsys.Saml2.AspNetCore2;
using Sustainsys.Saml2.Configuration;
using Sustainsys.Saml2.Metadata;

namespace Strategy.Platform.Identity.External;

/// <summary>
/// Registers every enabled OIDC / UAE Pass / SAML provider as an authentication scheme named after its code, at start-up,
/// after each change and every minute (ADR 0011). Options are rebuilt from the database whenever a provider changes.
/// </summary>
public sealed partial class ExternalSchemeRegistry(
    IServiceScopeFactory scopeFactory,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> oidcCache,
    IOptionsMonitorCache<OAuthOptions> oauthCache,
    IOptionsMonitorCache<Saml2Options> samlCache,
    ILogger<ExternalSchemeRegistry> logger) : IDisposable
{
    private readonly ConcurrentDictionary<string, IdentityProvider> _active = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public bool TryGet(string code, out IdentityProvider provider) => _active.TryGetValue(code, out provider!);

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<IdentityProvider> enabled;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
                enabled = await db.Providers.AsNoTracking()
                    .Where(p => p.Enabled && p.Type != ProviderTypes.Ldap)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var code in _active.Keys.Except(enabled.Select(p => p.Code)).ToList())
            {
                Remove(code);
            }

            foreach (var provider in enabled)
            {
                if (_active.TryGetValue(provider.Code, out var current) && current.Version == provider.Version && current.UpdatedAt == provider.UpdatedAt)
                {
                    continue;
                }

                Remove(provider.Code);
                _active[provider.Code] = provider;
                schemes.TryAddScheme(new AuthenticationScheme(provider.Code, provider.DisplayNameEn ?? provider.Code, HandlerType(provider.Type)));
                LogRegistered(logger, provider.Code, provider.Type);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Remove(string code)
    {
        if (_active.TryRemove(code, out _))
        {
            schemes.RemoveScheme(code);
            LogRemoved(logger, code);
        }

        oidcCache.TryRemove(code);
        oauthCache.TryRemove(code);
        samlCache.TryRemove(code);
    }

    private static Type HandlerType(string type) => type switch
    {
        ProviderTypes.Oidc => typeof(OpenIdConnectHandler),
        ProviderTypes.UaePass => typeof(OAuthHandler<OAuthOptions>),
        ProviderTypes.Saml2 => typeof(Saml2Handler),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a redirect provider."),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity provider {Code} ({Type}) registered")]
    private static partial void LogRegistered(ILogger logger, string code, string type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity provider {Code} removed")]
    private static partial void LogRemoved(ILogger logger, string code);
}

internal sealed partial class ExternalSchemeRefresher(ExternalSchemeRegistry registry, ILogger<ExternalSchemeRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try
            {
                await registry.ReloadAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReloadFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reload identity providers")]
    private static partial void LogReloadFailed(ILogger logger, Exception exception);
}

/// <summary>Callback paths per provider; outside <c>/api</c>, protected by the protocols' own state / nonce / correlation.</summary>
public static class ExternalPaths
{
    public static string OidcCallback(string code) => $"/signin-ext/{code}";

    public static string SamlModule(string code) => $"/saml2/{code}";

    public const string Callback = "/api/v1/identity/external/callback";
}

/// <summary>Correlation / nonce cookies: Lax for query responses (works over HTTP in development); None + Secure for form_post.</summary>
internal static class RemoteCookies
{
    public static void Configure(RemoteAuthenticationOptions options, bool formPost)
    {
        options.CorrelationCookie.SameSite = formPost ? SameSiteMode.None : SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = formPost ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    }

    public static Task RemoteFailure(RemoteFailureContext context)
    {
        context.Response.Redirect("/login?error=" + Uri.EscapeDataString("auth.errors.externalFailed"));
        context.HandleResponse();
        return Task.CompletedTask;
    }
}

internal sealed class OidcOptionsSetup(ExternalSchemeRegistry registry, ISecretProtector protector) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options)
    {
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name is null || !registry.TryGet(name, out var provider) || provider.Type != ProviderTypes.Oidc)
        {
            return;
        }

        var formPost = provider.Setting("responseMode") == "form_post";
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.Authority = provider.Setting("authority");
        options.ClientId = provider.Setting("clientId");
        options.ClientSecret = protector.Unprotect(SecretPurposes.ProviderSecret, provider.SecretProtected);
        options.ResponseType = "code";
        options.ResponseMode = formPost ? "form_post" : "query";
        options.UsePkce = true;
        options.CallbackPath = ExternalPaths.OidcCallback(name);
        options.SignedOutCallbackPath = $"/signout-callback-ext/{name}";
        options.RemoteSignOutPath = $"/signout-ext/{name}";
        options.Scope.Clear();
        foreach (var scope in (provider.Setting("scopes") ?? "openid").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = provider.SettingFlag("getClaimsFromUserInfo", fallback: true);
        options.RequireHttpsMetadata = !provider.SettingFlag("allowInsecureHttp");
        options.SaveTokens = false;
        options.TokenValidationParameters.NameClaimType = provider.Setting("claimUserName") ?? "name";
        RemoteCookies.Configure(options, formPost);
        options.NonceCookie.SameSite = options.CorrelationCookie.SameSite;
        options.NonceCookie.SecurePolicy = options.CorrelationCookie.SecurePolicy;
        options.Events.OnRemoteFailure = RemoteCookies.RemoteFailure;
    }
}

/// <summary>UAE Pass: OAuth 2.0 authorization code + userinfo (its token response has no id_token, so the plain OAuth handler is used).</summary>
internal sealed class UaePassOptionsSetup(ExternalSchemeRegistry registry, ISecretProtector protector) : IConfigureNamedOptions<OAuthOptions>
{
    public void Configure(OAuthOptions options)
    {
    }

    public void Configure(string? name, OAuthOptions options)
    {
        if (name is null || !registry.TryGet(name, out var provider) || provider.Type != ProviderTypes.UaePass)
        {
            return;
        }

        var environment = ProviderSchema.UaePassEnvironments[provider.Setting("environment") ?? "staging"];
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.ClientId = provider.Setting("clientId")!;
        options.ClientSecret = protector.Unprotect(SecretPurposes.ProviderSecret, provider.SecretProtected)!;
        options.AuthorizationEndpoint = provider.Setting("authorizationEndpoint") ?? environment.Authorize;
        options.TokenEndpoint = provider.Setting("tokenEndpoint") ?? environment.Token;
        options.UserInformationEndpoint = provider.Setting("userInfoEndpoint") ?? environment.UserInfo;
        options.CallbackPath = ExternalPaths.OidcCallback(name);
        options.UsePkce = true;
        options.SaveTokens = false;
        options.Scope.Clear();
        foreach (var scope in (provider.Setting("scopes") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        RemoteCookies.Configure(options, formPost: false);
        var acrValues = provider.Setting("acrValues");
        options.Events = new OAuthEvents
        {
            OnRemoteFailure = RemoteCookies.RemoteFailure,
            OnRedirectToAuthorizationEndpoint = context =>
            {
                var target = string.IsNullOrEmpty(acrValues)
                    ? context.RedirectUri
                    : Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(context.RedirectUri, "acr_values", acrValues);
                context.Response.Redirect(target);
                return Task.CompletedTask;
            },
            OnCreatingTicket = async context =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted).ConfigureAwait(false));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    {
                        context.Identity!.AddClaim(new Claim(property.Name, property.Value.ToString(), ClaimValueTypes.String, context.Options.ClaimsIssuer));
                    }
                }
            },
        };
    }
}

internal sealed class SamlOptionsSetup(ExternalSchemeRegistry registry, IdentitySettingsCache settings) : IConfigureNamedOptions<Saml2Options>
{
    public void Configure(Saml2Options options)
    {
    }

    public void Configure(string? name, Saml2Options options)
    {
        if (name is null || !registry.TryGet(name, out var provider) || provider.Type != ProviderTypes.Saml2)
        {
            return;
        }

        var publicBase = settings.Smtp.PublicBaseUrl;
        var spEntityId = provider.Setting("spEntityId")
            ?? (publicBase is null ? $"urn:strategy:sp:{name}" : $"{publicBase.TrimEnd('/')}{ExternalPaths.SamlModule(name)}");
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.SPOptions = new SPOptions
        {
            EntityId = new EntityId(spEntityId),
            ModulePath = ExternalPaths.SamlModule(name),
        };
        if (publicBase is not null)
        {
            options.SPOptions.PublicOrigin = new Uri(publicBase.TrimEnd('/') + "/");
        }

        var idp = new Sustainsys.Saml2.IdentityProvider(new EntityId(provider.Setting("idpEntityId")), options.SPOptions)
        {
            AllowUnsolicitedAuthnResponse = provider.SettingFlag("allowUnsolicited"),
            Binding = Sustainsys.Saml2.WebSso.Saml2BindingType.HttpRedirect,
        };
        if (provider.Setting("metadataUrl") is { } metadata)
        {
            idp.MetadataLocation = metadata;
            idp.LoadMetadata = true;
        }
        else
        {
            idp.SingleSignOnServiceUrl = new Uri(provider.Setting("ssoUrl")!);
            idp.SigningKeys.AddConfiguredKey(ProviderSchema.ParseCertificate(provider.Setting("idpCertificate"))!);
        }

        options.IdentityProviders.Add(idp);
    }
}
