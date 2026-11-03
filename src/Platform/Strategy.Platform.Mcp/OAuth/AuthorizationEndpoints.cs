using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Mcp.Application;
using Strategy.Platform.Settings;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Strategy.Platform.Mcp.OAuth;

/// <summary>A consent ticket: the signed-in user allowed this client, redirect URI and PKCE challenge (protected, 5 minutes).</summary>
internal sealed record ConsentTicket(Guid UserId, string ClientId, string RedirectUri, string CodeChallenge);

/// <summary>
/// The OAuth 2.1 authorization server for MCP clients (docs/design/mcp.md §2, ADR 0032): authorization (passthrough: sign-in check,
/// consent page, grant), token (code + refresh, user re-checked), dynamic client registration and the protected-resource metadata.
/// </summary>
internal static class AuthorizationEndpoints
{
    public const string ConsentPurpose = "mcp.consent.v1";

    private static readonly string[] BearerMethods = ["header"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], AuthorizeAsync).ExcludeFromDescription();
        endpoints.MapPost("/connect/token", TokenAsync).ExcludeFromDescription();
        endpoints.MapPost("/connect/register", RegisterAsync).ExcludeFromDescription();
        endpoints.MapGet("/.well-known/oauth-protected-resource", ResourceMetadata).ExcludeFromDescription();
        endpoints.MapGet("/.well-known/oauth-protected-resource/mcp", ResourceMetadata).ExcludeFromDescription();
    }

    public static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}";

    private static IResult ResourceMetadata(HttpContext context) => Results.Json(new Dictionary<string, object>
    {
        ["resource"] = BaseUrl(context.Request) + "/mcp",
        ["authorization_servers"] = new[] { BaseUrl(context.Request) },
        ["scopes_supported"] = McpScopes.All,
        ["bearer_methods_supported"] = BearerMethods,
        ["resource_name"] = "Strategy MCP server",
    });

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context, ISettingsStore settingsStore, IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations,
        IDataProtectionProvider protection, IAuditLog audit)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        var settings = await settingsStore.GetAsync<McpSettings>(context.RequestAborted);
        if (!settings.Enabled)
        {
            return Deny("The MCP server is switched off.");
        }

        var resource = BaseUrl(context.Request) + "/mcp";
        if (request.Resources is { Length: > 0 } resources
            && resources.Any(r => !string.Equals(r?.TrimEnd('/'), resource, StringComparison.OrdinalIgnoreCase)))
        {
            return Forbid(Errors.InvalidTarget, "The only resource of this server is " + resource + ".");
        }

        var session = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!session.Succeeded || session.Principal?.UserId() is not { } userId)
        {
            return Results.Redirect("/login?redirect=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString));
        }

        if (!session.Principal.IsInternal())
        {
            return Deny("Vendor accounts cannot connect AI clients.");
        }

        if (string.Equals(request["consent"]?.ToString(), "deny", StringComparison.Ordinal))
        {
            return Deny("The user did not allow access.");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
            ?? throw new InvalidOperationException("Unknown client after OpenIddict validation.");
        var applicationId = await applications.GetIdAsync(application, context.RequestAborted);
        var existing = new List<object>();
        await foreach (var authorization in authorizations.FindAsync(userId.ToString(), applicationId, Statuses.Valid, AuthorizationTypes.Permanent,
            [McpScopes.Mcp, Scopes.OfflineAccess], context.RequestAborted))
        {
            existing.Add(authorization);
        }

        var ticket = request["consent_ticket"]?.ToString();
        var consented = ticket is not null && TicketMatches(protection, ticket, userId, request);
        if (existing.Count == 0 && !consented)
        {
            var query = QueryString.Create(context.Request.Query.Where(q => q.Key is not ("consent" or "consent_ticket"))
                .Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString())));
            return Results.Redirect("/oauth/consent" + query);
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, userId.ToString())
            .SetClaim(Claims.Name, session.Principal.Identity?.Name)
            .SetClaim(Claims.ClientId, request.ClientId);
        // offline_access: a refresh token, so the client keeps working for the refresh lifetime set in Settings → MCP.
        identity.SetScopes(ImmutableArray.Create(McpScopes.Mcp, Scopes.OfflineAccess));
        identity.SetResources(ImmutableArray.Create(resource));
        var authorizationRecord = existing.LastOrDefault() ?? await authorizations.CreateAsync(identity, userId.ToString(), applicationId!,
            AuthorizationTypes.Permanent, identity.GetScopes(), context.RequestAborted);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorizationRecord, context.RequestAborted));
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        var principal = new ClaimsPrincipal(identity);
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenMinutes));
        principal.SetRefreshTokenLifetime(TimeSpan.FromDays(settings.RefreshTokenDays));
        if (consented)
        {
            await audit.WriteAsync(new AuditEvent("mcp.consent.grant", true, "mcp", "mcp.client", request.ClientId,
                new { redirectHost = Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var r) ? r.Host : null }));
        }

        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static bool TicketMatches(IDataProtectionProvider protection, string ticket, Guid userId, OpenIddictRequest request)
    {
        try
        {
            var json = protection.CreateProtector(ConsentPurpose).ToTimeLimitedDataProtector().Unprotect(ticket);
            var value = JsonSerializer.Deserialize<ConsentTicket>(json);
            return value is not null && value.UserId == userId && value.ClientId == request.ClientId && value.RedirectUri == request.RedirectUri
                && value.CodeChallenge == request.CodeChallenge;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    public static string IssueTicket(IDataProtectionProvider protection, ConsentTicket ticket) =>
        protection.CreateProtector(ConsentPurpose).ToTimeLimitedDataProtector().Protect(JsonSerializer.Serialize(ticket), TimeSpan.FromMinutes(5));

    private static async Task<IResult> TokenAsync(HttpContext context, ISettingsStore settingsStore, TokenPrincipalFactory principals)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return Forbid(Errors.UnsupportedGrantType, "Only authorization_code and refresh_token are supported.");
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var settings = await settingsStore.GetAsync<McpSettings>(context.RequestAborted);
        if (result.Principal is not { } principal || !Guid.TryParse(principal.GetClaim(Claims.Subject), out var userId) || !settings.Enabled)
        {
            return Forbid(Errors.InvalidGrant, "The grant is no longer valid.");
        }

        var (usable, userName) = await principals.CheckAsync(userId);
        if (!usable)
        {
            return Forbid(Errors.InvalidGrant, "The account can no longer sign in.");
        }

        var identity = new ClaimsIdentity(principal.Claims, TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Name, userName);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        var refreshed = new ClaimsPrincipal(identity);
        refreshed.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenMinutes));
        refreshed.SetRefreshTokenLifetime(TimeSpan.FromDays(settings.RefreshTokenDays));
        return Results.SignIn(refreshed, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult Deny(string description) => Forbid(Errors.AccessDenied, description);

    private static IResult Forbid(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    /// <summary>RFC 7591 dynamic client registration for public clients (PKCE, no secret), redirect hosts checked against Settings → MCP.</summary>
    private static async Task<IResult> RegisterAsync(
        HttpContext context, ISettingsStore settingsStore, IOpenIddictApplicationManager applications, IAuditLog audit)
    {
        var settings = await settingsStore.GetAsync<McpSettings>(context.RequestAborted);
        if (!settings.Enabled || !settings.AllowDynamicRegistration)
        {
            return RegistrationError("access_denied", "Dynamic client registration is switched off.", StatusCodes.Status403Forbidden);
        }

        ClientRegistration? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<ClientRegistration>(context.RequestAborted);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body?.RedirectUris is not { Count: > 0 and <= 5 } uris)
        {
            return RegistrationError("invalid_redirect_uri", "One to five redirect URIs are required.");
        }

        foreach (var uri in uris)
        {
            if (!RedirectAllowed(uri, settings.RedirectHosts))
            {
                return RegistrationError("invalid_redirect_uri", $"Redirect URI not allowed: {uri}");
            }
        }

        if (body.TokenEndpointAuthMethod is not (null or "none"))
        {
            return RegistrationError("invalid_client_metadata", "Only public clients (token_endpoint_auth_method none) with PKCE are supported.");
        }

        var name = string.IsNullOrWhiteSpace(body.ClientName) ? "MCP client" : body.ClientName.Trim();
        name = name.Length > 100 ? name[..100] : name;
        var clientId = "mcp-" + Guid.CreateVersion7().ToString("N");
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = name,
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + McpScopes.Mcp,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in uris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        descriptor.Properties["registered_at"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);
        await applications.CreateAsync(descriptor, context.RequestAborted);
        await audit.WriteAsync(new AuditEvent("mcp.client.register", true, "mcp", "mcp.client", clientId,
            new { name, redirectHosts = uris.Select(u => new Uri(u).Host).Distinct() }));
        return Results.Json(new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["client_id_issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["client_name"] = name,
            ["redirect_uris"] = uris,
            ["grant_types"] = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken },
            ["response_types"] = new[] { ResponseTypes.Code },
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = McpScopes.Mcp,
        }, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>https to an allowed host, or http to a loopback host that is allowed; <c>*</c> allows any host.</summary>
    public static bool RedirectAllowed(string value, IReadOnlyList<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var allowedHost = hosts.Contains("*") || hosts.Any(h => string.Equals(h.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase)
            || (h.StartsWith("*.", StringComparison.Ordinal) && uri.Host.EndsWith(h[1..], StringComparison.OrdinalIgnoreCase)));
        return allowedHost && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
    }

    private static IResult RegistrationError(string error, string description, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: status);

    internal sealed record ClientRegistration(
        [property: JsonPropertyName("client_name")] string? ClientName,
        [property: JsonPropertyName("redirect_uris")] List<string>? RedirectUris,
        [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod);

    internal static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
