using System.Security.Claims;

namespace Strategy.Platform.Identity.Authentication;

/// <summary>An OAuth 2.0 client-credentials grant the authorization server may issue (ADR 0191).</summary>
public sealed record ClientCredentialsGrant(string ClientId, IReadOnlyList<string> Scopes, TimeSpan Lifetime);

/// <summary>
/// Implemented by Platform.Integration; called by the OAuth server (Platform.Mcp) for <c>grant_type=client_credentials</c> and by the OAuth
/// handler for tokens issued that way, so the client's status, expiry, IP allow-list and scopes are checked on every call.
/// </summary>
public interface IIntegrationClientAuthority
{
    /// <summary>Scopes integration clients may hold (<c>feeds</c>, <c>api.read</c>).</summary>
    IReadOnlyList<string> Scopes { get; }

    /// <summary>The grant for an OAuth client already authenticated by OpenIddict, or null (unknown, revoked, expired, IP, scopes).</summary>
    Task<ClientCredentialsGrant?> GrantAsync(string clientId, IReadOnlyCollection<string> requestedScopes, string? ip, CancellationToken cancellationToken);

    /// <summary>The integration principal for a token issued to <paramref name="clientId"/>, or null when the client may no longer call.</summary>
    Task<ClaimsPrincipal?> CreatePrincipalAsync(string clientId, IReadOnlyCollection<string> scopes, string scheme, CancellationToken cancellationToken);
}

/// <summary>Credential formats of integration clients (ADR 0191).</summary>
public static class IntegrationClientKeys
{
    public const string KeyPrefix = "sik_";

    public const string ClientIdPrefix = "ic_";

    public static bool LooksLikeKey(string? value) => value is not null && value.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>An integration call: <c>Bearer sik_…</c> or HTTP Basic (only integration clients use Basic).</summary>
    public static bool IsIntegrationCredential(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
            || (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && LooksLikeKey(header["Bearer ".Length..].Trim()));
    }
}

/// <summary>Paths of the integration surfaces.</summary>
public static class IntegrationPaths
{
    public const string OData = "/odata";
    public const string Extracts = "/api/v1/integration/extracts";

    /// <summary>OData feeds and extracts: integration clients with scope <c>feeds</c> only.</summary>
    public static bool IsFeed(string path) =>
        path.StartsWith(OData + "/", StringComparison.OrdinalIgnoreCase) || path.Equals(OData, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Extracts, StringComparison.OrdinalIgnoreCase);
}
