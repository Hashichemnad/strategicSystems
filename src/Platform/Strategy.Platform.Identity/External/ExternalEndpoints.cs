using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Sustainsys.Saml2.AspNetCore2;

namespace Strategy.Platform.Identity.External;

public static class ExternalServiceCollectionExtensions
{
    /// <summary>Runtime-configured OIDC / UAE Pass / SAML schemes and LDAP sign-in (ADR 0011).</summary>
    public static IServiceCollection AddExternalProviders(this IServiceCollection services)
    {
        services.AddSingleton<ExternalSchemeRegistry>();
        services.AddHostedService<ExternalSchemeRefresher>();
        services.AddScoped<AccountLinker>();
        services.AddScoped<LdapDirectory>();
        services.AddScoped<IDirectoryPasswordSignIn>(sp => sp.GetRequiredService<LdapDirectory>());
        services.AddHttpClient(ProviderTester.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<ProviderTester>();
        services.AddScoped<ProviderAdministration>();

        services.TryAddSingleton(System.Text.Encodings.Web.HtmlEncoder.Default);
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, OidcOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>();
        services.AddSingleton<IConfigureOptions<OAuthOptions>, UaePassOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<OAuthOptions>, OAuthPostConfigureOptions<OAuthOptions, OAuthHandler<OAuthOptions>>>();
        services.AddSingleton<IConfigureOptions<Saml2Options>, SamlOptionsSetup>();
        services.AddSingleton<IPostConfigureOptions<Saml2Options>, PostConfigureSaml2Options>();
        return services;
    }
}

/// <summary>Administration → Identity providers, plus the public challenge / callback of the sign-in flow.</summary>
internal static class ExternalEndpoints
{
    public static void Map(RouteGroupBuilder group, IEndpointRouteBuilder root)
    {
        var providers = group.MapGroup("/admin/providers");
        providers.MapGet("/schema", Schema).WithName("GetProviderSchema").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapGet("/", ListAsync).WithName("ListProviders").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapGet("/{id:guid}", GetAsync).WithName("GetProvider").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapPost("/", CreateAsync).WithName("CreateProvider").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateProvider").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapPost("/{id:guid}/enable", (Guid id, ProviderAdministration a, CancellationToken ct) => SetEnabledAsync(id, true, a, ct))
            .WithName("EnableProvider").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapPost("/{id:guid}/disable", (Guid id, ProviderAdministration a, CancellationToken ct) => SetEnabledAsync(id, false, a, ct))
            .WithName("DisableProvider").RequirePermission(IdentityPermissions.ProviderManage);
        providers.MapPost("/{id:guid}/test", TestAsync).WithName("TestProvider").RequirePermission(IdentityPermissions.ProviderManage);

        var external = group.MapGroup("/external");
        external.MapGet("/{code}/challenge", ChallengeAsync).WithName("ExternalChallenge").AsPublic()
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).ExcludeFromDescription();
        external.MapGet("/callback", CallbackAsync).WithName("ExternalCallback").AsPublic()
            .RequireRateLimiting(AuthEndpoints.RateLimitPolicy).ExcludeFromDescription();
        _ = root;
    }

    private static Ok<IReadOnlyList<ProviderTypeSchema>> Schema() => TypedResults.Ok(ProviderSchema.Types);

    private static async Task<Ok<IReadOnlyList<ProviderDto>>> ListAsync(ProviderAdministration administration, CancellationToken ct) =>
        TypedResults.Ok(await administration.ListAsync(ct));

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> GetAsync(Guid id, ProviderAdministration administration, CancellationToken ct) =>
        (await administration.GetAsync(id, ct)).ToOk();

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> CreateAsync(
        ProviderInput body, ProviderAdministration administration, CancellationToken ct) =>
        (await administration.CreateAsync(body, ct)).ToOk();

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> UpdateAsync(
        Guid id, ProviderInput body, ProviderAdministration administration, CancellationToken ct) =>
        (await administration.UpdateAsync(id, body, ct)).ToOk();

    private static async Task<Results<Ok<ProviderDto>, ProblemHttpResult>> SetEnabledAsync(
        Guid id, bool enabled, ProviderAdministration administration, CancellationToken ct) =>
        (await administration.SetEnabledAsync(id, enabled, ct)).ToOk();

    private static async Task<Results<Ok<ProviderTestResultDto>, ProblemHttpResult>> TestAsync(
        Guid id, ProviderTestInput? body, ProviderAdministration administration, CancellationToken ct) =>
        (await administration.TestAsync(id, body?.TestUserName, ct)).ToOk();

    /// <summary>Starts sign-in at an enabled provider; unknown or disabled codes answer 404.</summary>
    private static async Task<IResult> ChallengeAsync(
        string code, string? returnUrl, ExternalSchemeRegistry registry, HttpContext context)
    {
        if (!registry.TryGet(code, out _))
        {
            await registry.ReloadAsync(context.RequestAborted);
            if (!registry.TryGet(code, out _))
            {
                return Error.NotFound("identity.provider.notFound").ToProblem();
            }
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = $"{ExternalPaths.Callback}?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}",
            Items = { ["provider"] = code },
        };
        return TypedResults.Challenge(properties, [code]);
    }

    /// <summary>
    /// Back from the provider: map claims, link the account, apply the MFA rule, then redirect to the SPA
    /// (<c>/login?error=…</c> on failure, <c>/login/mfa</c> or <c>/login/enrol</c> when a second factor is due).
    /// </summary>
    private static async Task<IResult> CallbackAsync(
        string? returnUrl, HttpContext context, IdentityStoreDbContext db, AccountLinker linker, SignInService signIn, AuthEventLog events)
    {
        var target = LocalUrl(returnUrl);
        var result = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var code = result.Properties?.Items.TryGetValue("provider", out var c) == true ? c : null;
        if (!result.Succeeded || code is null)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: code, detail: "noTicket", cancellationToken: context.RequestAborted);
            return Fail("auth.errors.externalFailed");
        }

        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.Enabled, context.RequestAborted);
        if (provider is null)
        {
            return Fail("auth.errors.externalFailed");
        }

        var principal = result.Principal;
        var identity = MapIdentity(provider, principal);
        if (identity is null)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: code, detail: "noSubject", cancellationToken: context.RequestAborted);
            return Fail("auth.errors.externalFailed");
        }

        if (provider.Type == ProviderTypes.UaePass && !UserTypeAllowed(provider, identity.UserType))
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, provider: code, detail: "userType", cancellationToken: context.RequestAborted);
            return Fail(AccountLinker.UserTypeNotAllowed);
        }

        var link = await linker.LinkAsync(provider, identity, context.RequestAborted);
        if (link.User is null)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, userName: identity.Email ?? identity.UserName, provider: code,
                detail: link.FailureKey, cancellationToken: context.RequestAborted);
            return Fail(link.FailureKey ?? "auth.errors.externalFailed");
        }

        if (link.NewlyLinked)
        {
            await events.WriteAsync(AuthEventTypes.ExternalLinked, true, link.User.Id, link.User.UserName, code, cancellationToken: context.RequestAborted);
        }

        var outcome = await signIn.CompleteFirstFactorAsync(link.User, AuthMethods.External, code, external: true, context.RequestAborted);
        return outcome.Value.Status switch
        {
            SignInStatuses.MfaRequired => TypedResults.Redirect($"/login/mfa?returnUrl={Uri.EscapeDataString(target)}&methods={string.Join(',', outcome.Value.Methods)}"),
            SignInStatuses.MfaEnrolmentRequired => TypedResults.Redirect($"/login/enrol?returnUrl={Uri.EscapeDataString(target)}&methods={string.Join(',', outcome.Value.Methods)}"),
            _ => TypedResults.Redirect(target),
        };

        static IResult Fail(string key) => TypedResults.Redirect("/login?error=" + Uri.EscapeDataString(key));
    }

    internal static ExternalIdentity? MapIdentity(IdentityProvider provider, ClaimsPrincipal principal)
    {
        string? Claim(string key) => provider.Setting(key) is { } type && principal.FindFirst(type)?.Value is { Length: > 0 } value ? value : null;
        var subject = Claim("claimSubject");
        return subject is null
            ? null
            : new ExternalIdentity(subject, Claim("claimEmail"), Claim("claimEmployeeNo"), Claim("claimUserName"), Claim("claimUserType"));
    }

    private static bool UserTypeAllowed(IdentityProvider provider, string? userType)
    {
        var allowed = (provider.Setting("allowedUserTypes") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Length == 0 || (userType is not null && allowed.Contains(userType, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Only same-site relative paths ("/x"), never "//host" or "/\host": prevents open redirects.</summary>
    internal static string LocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) ? url : "/";
}

public sealed record ProviderDto(
    Guid Id,
    string Code,
    string Type,
    string? DisplayNameAr,
    string? DisplayNameEn,
    bool Enabled,
    int Sort,
    JsonObject Settings,
    bool HasSecret,
    string MatchField,
    bool AutoLink,
    bool JitCreateUser,
    DateTimeOffset? LastTestAt,
    bool? LastTestOk,
    string? LastTestMessageKey,
    string? CallbackPath,
    string? MetadataPath,
    uint Version);

public sealed record ProviderInput(
    string? Code,
    string? Type,
    string? DisplayNameAr,
    string? DisplayNameEn,
    int Sort,
    JsonObject? Settings,
    string? Secret,
    bool ClearSecret,
    string? MatchField,
    bool AutoLink,
    bool JitCreateUser,
    uint Version);

public sealed record ProviderTestInput(string? TestUserName);

public sealed record ProviderTestResultDto(bool Ok, string MessageKey, IReadOnlyDictionary<string, string?>? Details);

/// <summary>Create / update / enable / test identity providers. Secrets are encrypted and never returned.</summary>
internal sealed class ProviderAdministration(
    IdentityStoreDbContext db,
    ISecretProtector protector,
    ExternalSchemeRegistry registry,
    ProviderTester tester,
    AuthEventLog events,
    IClock clock)
{
    public async Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken cancellationToken) =>
        (await db.Providers.AsNoTracking().OrderBy(p => p.Sort).ThenBy(p => p.Code).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(ToDto).ToList();

    public async Task<Result<ProviderDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        return provider is null ? Error.NotFound() : ToDto(provider);
    }

    public async Task<Result<ProviderDto>> CreateAsync(ProviderInput input, CancellationToken cancellationToken)
    {
        var type = input.Type ?? string.Empty;
        var fields = new List<FieldError>();
        if (!ProviderSchema.IsValidCode(input.Code))
        {
            fields.Add(new FieldError("code", "validation.codeFormat", "validation.codeFormat"));
        }

        if (ProviderSchema.For(type) is null)
        {
            fields.Add(new FieldError("type", "validation.unknownCode", "validation.unknownCode"));
            return Error.Validation(fields);
        }

        var provider = new IdentityProvider { Code = input.Code ?? string.Empty, Type = type, CreatedAt = clock.UtcNow, MatchField = ProviderSchema.For(type)!.DefaultMatchField };
        var applied = Apply(provider, input, fields);
        if (applied.IsSuccess && type == ProviderTypes.Saml2)
        {
            // The SP signs its requests with its own key pair (published in the SP metadata); kept encrypted in the secret column.
            provider.SecretProtected = protector.Protect(SecretPurposes.SamlSigningKey, SamlServiceCertificate.Create(provider.Code, clock.UtcNow));
        }

        if (applied.IsFailure)
        {
            return applied.Error;
        }

        if (await db.Providers.AnyAsync(p => p.Code == provider.Code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("identity.provider.duplicateCode", "identity.errors.duplicateProviderCode");
        }

        db.Providers.Add(provider);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.ProviderChanged, true, provider: provider.Code, detail: "created", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ToDto(provider);
    }

    public async Task<Result<ProviderDto>> UpdateAsync(Guid id, ProviderInput input, CancellationToken cancellationToken)
    {
        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (provider is null)
        {
            return Error.NotFound();
        }

        db.Entry(provider).Property(p => p.Version).OriginalValue = input.Version;
        var applied = Apply(provider, input with { Code = provider.Code, Type = provider.Type }, []);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        return await SaveAsync(provider, "updated", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ProviderDto>> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (provider is null)
        {
            return Error.NotFound();
        }

        if (enabled)
        {
            var errors = ProviderSchema.Validate(provider.Type, provider.Settings, provider.SecretProtected is not null);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }
        }


        provider.Enabled = enabled;
        return await SaveAsync(provider, enabled ? "enabled" : "disabled", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ProviderTestResultDto>> TestAsync(Guid id, string? testUserName, CancellationToken cancellationToken)
    {
        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (provider is null)
        {
            return Error.NotFound();
        }

        var result = await tester.TestAsync(provider, testUserName, cancellationToken).ConfigureAwait(false);
        provider.LastTestAt = clock.UtcNow;
        provider.LastTestOk = result.Ok;
        provider.LastTestMessageKey = result.MessageKey;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.ProviderChanged, result.Ok, provider: provider.Code, detail: "test:" + result.MessageKey,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<Result<ProviderDto>> SaveAsync(IdentityProvider provider, string detail, CancellationToken cancellationToken)
    {
        provider.UpdatedAt = clock.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.ProviderChanged, true, provider: provider.Code, detail: detail, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ToDto(provider);
    }

    private Result Apply(IdentityProvider provider, ProviderInput input, List<FieldError> fields)
    {
        if (string.IsNullOrWhiteSpace(input.DisplayNameAr) && string.IsNullOrWhiteSpace(input.DisplayNameEn))
        {
            fields.Add(new FieldError("displayNameAr", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (input.DisplayNameAr?.Length > 200 || input.DisplayNameEn?.Length > 200)
        {
            fields.Add(new FieldError("displayNameEn", "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = 200 }));
        }

        var matchField = input.MatchField ?? provider.MatchField;
        if (!LinkMatchFields.All.Contains(matchField))
        {
            fields.Add(new FieldError("matchField", "validation.unknownCode", "validation.unknownCode"));
        }

        var settings = ProviderSchema.Normalise(provider.Type, input.Settings);
        var secretAllowed = ProviderSchema.For(provider.Type)!.HasSecret;
        var hasSecret = secretAllowed && (input.ClearSecret ? false : !string.IsNullOrEmpty(input.Secret) || provider.SecretProtected is not null);
        fields.AddRange(ProviderSchema.Validate(provider.Type, settings, hasSecret));
        if (input.Secret?.Length > 4096)
        {
            fields.Add(new FieldError("secret", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 4096 }));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields);
        }

        provider.DisplayNameAr = input.DisplayNameAr?.Trim();
        provider.DisplayNameEn = input.DisplayNameEn?.Trim();
        provider.Sort = input.Sort;
        provider.Settings = settings;
        provider.MatchField = matchField;
        provider.AutoLink = input.AutoLink;
        provider.JitCreateUser = input.JitCreateUser;
        if (secretAllowed && input.ClearSecret)
        {
            provider.SecretProtected = null;
        }
        else if (secretAllowed && !string.IsNullOrEmpty(input.Secret))
        {
            provider.SecretProtected = protector.Protect(SecretPurposes.ProviderSecret, input.Secret);
        }

        provider.UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    private static ProviderDto ToDto(IdentityProvider p) => new(
        p.Id, p.Code, p.Type, p.DisplayNameAr, p.DisplayNameEn, p.Enabled, p.Sort, p.Settings,
        p.SecretProtected is not null && ProviderSchema.For(p.Type)?.HasSecret == true, p.MatchField,
        p.AutoLink, p.JitCreateUser, p.LastTestAt, p.LastTestOk, p.LastTestMessageKey,
        p.Type switch
        {
            ProviderTypes.Ldap => null,
            ProviderTypes.Saml2 => $"{ExternalPaths.SamlModule(p.Code)}/Acs",
            _ => ExternalPaths.OidcCallback(p.Code),
        },
        p.Type == ProviderTypes.Saml2 ? ExternalPaths.SamlModule(p.Code) : null,
        p.Version);
}

/// <summary>"Test connection": OIDC discovery, UAE Pass endpoint reachability, SAML metadata, LDAP bind + search.</summary>
internal sealed class ProviderTester(IHttpClientFactory httpClients, LdapDirectory ldap)
{
    public const string HttpClientName = "identity-provider-test";

    public async Task<ProviderTestResultDto> TestAsync(IdentityProvider provider, string? testUserName, CancellationToken cancellationToken)
    {
        var errors = ProviderSchema.Validate(provider.Type, provider.Settings, provider.SecretProtected is not null);
        if (errors.Count > 0)
        {
            return new ProviderTestResultDto(false, "admin.providers.test.invalidSettings", null);
        }

        try
        {
            switch (provider.Type)
            {
                case ProviderTypes.Oidc:
                    {
                        var discovery = provider.Setting("authority")!.TrimEnd('/') + "/.well-known/openid-configuration";
                        var json = JsonNode.Parse(await httpClients.CreateClient(HttpClientName).GetStringAsync(discovery, cancellationToken).ConfigureAwait(false));
                        var issuer = json?["issuer"]?.GetValue<string>();
                        return issuer is null
                            ? new ProviderTestResultDto(false, "admin.providers.test.discoveryInvalid", null)
                            : new ProviderTestResultDto(true, "admin.providers.test.discoveryOk", new Dictionary<string, string?> { ["issuer"] = issuer });
                    }

                case ProviderTypes.UaePass:
                    {
                        var environment = ProviderSchema.UaePassEnvironments[provider.Setting("environment") ?? "staging"];
                        var endpoint = provider.Setting("authorizationEndpoint") ?? environment.Authorize;
                        using var response = await httpClients.CreateClient(HttpClientName).GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
                        return (int)response.StatusCode < 500
                            ? new ProviderTestResultDto(true, "admin.providers.test.endpointReachable", new Dictionary<string, string?> { ["endpoint"] = endpoint })
                            : new ProviderTestResultDto(false, "admin.providers.test.endpointError", null);
                    }

                case ProviderTypes.Saml2 when provider.Setting("metadataUrl") is { } metadataUrl:
                    {
                        var xml = XDocument.Parse(await httpClients.CreateClient(HttpClientName).GetStringAsync(metadataUrl, cancellationToken).ConfigureAwait(false));
                        var entityId = xml.Root?.Attribute("entityID")?.Value;
                        return entityId == provider.Setting("idpEntityId")
                            ? new ProviderTestResultDto(true, "admin.providers.test.metadataOk", new Dictionary<string, string?> { ["entityId"] = entityId })
                            : new ProviderTestResultDto(false, "admin.providers.test.metadataEntityMismatch",
                                new Dictionary<string, string?> { ["entityId"] = entityId });
                    }

                case ProviderTypes.Saml2:
                    return new ProviderTestResultDto(true, "admin.providers.test.certificateOk",
                        new Dictionary<string, string?> { ["subject"] = ProviderSchema.ParseCertificate(provider.Setting("idpCertificate"))?.Subject });

                case ProviderTypes.Ldap:
                    {
                        var (ok, key, entry) = await ldap.TestAsync(provider, testUserName, cancellationToken).ConfigureAwait(false);
                        return new ProviderTestResultDto(ok, key, entry is null ? null : new Dictionary<string, string?>
                        {
                            ["dn"] = entry.DistinguishedName,
                            ["email"] = entry.Email,
                            ["employeeNo"] = entry.EmployeeNo,
                            ["userName"] = entry.UserName,
                        });
                    }

                default:
                    return new ProviderTestResultDto(false, "admin.providers.test.invalidSettings", null);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or System.Xml.XmlException or InvalidOperationException)
        {
            return new ProviderTestResultDto(false, "admin.providers.test.unreachable", null);
        }
    }
}
