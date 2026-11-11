using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Clients;

/// <summary>What the gate needs about the calling client (set on <c>HttpContext.Items</c> by the authority).</summary>
public sealed record ClientCallContext(Guid Id, string ClientId, IReadOnlyList<string> IpAllowList, int RateLimitPerMinute, IReadOnlyList<string> AllowedRecordTypes);

/// <summary>A refused integration credential of a known client (usage log).</summary>
public sealed record ClientRefusal(Guid Id, string Reason);

/// <summary>
/// The one place that decides whether an integration client may call (ADR 0191): status, expiry, credential, service account, and the
/// principal it acts as. Used by the API-key scheme, the OAuth token endpoint (client credentials) and the OAuth handler.
/// </summary>
public sealed class IntegrationClientAuthority(
    IntegrationDbContext db, ServiceAccounts accounts, ISettingsStore settings, IClock clock, IHttpContextAccessor http) : IIntegrationClientAuthority
{
    public const string CallContextKey = "integration.client";
    public const string RefusalKey = "integration.refused";

    public IReadOnlyList<string> Scopes => ClientScopes.All;

    public async Task<ClientCredentialsGrant?> GrantAsync(
        string clientId, IReadOnlyCollection<string> requestedScopes, string? ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedScopes);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken).ConfigureAwait(false);
        if (client is null || client.AuthKind != ClientAuthKinds.OAuth || await RefusalAsync(client, cancellationToken).ConfigureAwait(false) is not null
            || !IpAllowList.Allows(client.IpAllowList, System.Net.IPAddress.TryParse(ip, out var address) ? address : null))
        {
            return null;
        }

        var scopes = requestedScopes.Count == 0 ? client.Scopes : requestedScopes.ToList();
        if (scopes.Any(s => !client.Scopes.Contains(s)))
        {
            return null;
        }

        var credential = await db.Credentials.Where(c => c.ClientRef == client.Id && c.Kind == CredentialKinds.Secret && c.RevokedAt == null)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        credential?.MarkUsed(clock.UtcNow);
        client.MarkUsed(clock.UtcNow, ip);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var minutes = (await settings.GetAsync<IntegrationSettings>(cancellationToken).ConfigureAwait(false)).AccessTokenMinutes;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 24 * 60));
        if (client.ExpiresAt - clock.UtcNow < lifetime)
        {
            lifetime = client.ExpiresAt - clock.UtcNow;
        }

        return new ClientCredentialsGrant(client.ClientId, scopes, lifetime);
    }

    public async Task<ClaimsPrincipal?> CreatePrincipalAsync(string clientId, IReadOnlyCollection<string> scopes, string scheme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken).ConfigureAwait(false);
        if (client is null || client.AuthKind != ClientAuthKinds.OAuth)
        {
            return null;
        }

        if (await RefusalAsync(client, cancellationToken).ConfigureAwait(false) is { } reason)
        {
            Refuse(client.Id, reason);
            return null;
        }

        return Principal(client, scopes.Where(client.Scopes.Contains).ToList(), scheme);
    }

    /// <summary>
    /// An API key (Bearer, or Basic with <paramref name="basicClientId"/>): the principal, or null (then a refusal of a known client is
    /// recorded for the usage log).
    /// </summary>
    public async Task<ClaimsPrincipal?> AuthenticateKeyAsync(string? basicClientId, string key, string scheme, CancellationToken cancellationToken)
    {
        if (!ClientSecrets.TryParse(key, out var prefix, out var hash) || !key.StartsWith(IntegrationClientKeys.KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var credential = await db.Credentials.FirstOrDefaultAsync(c => c.Prefix == prefix && c.Kind == CredentialKinds.ApiKey, cancellationToken).ConfigureAwait(false);
        if (credential is null || !ClientSecrets.Matches(credential.SecretHash, hash))
        {
            return null;
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == credential.ClientRef, cancellationToken).ConfigureAwait(false);
        if (client is null || (basicClientId is not null && !string.Equals(basicClientId, client.ClientId, StringComparison.Ordinal)))
        {
            return null;
        }

        var now = clock.UtcNow;
        var reason = await RefusalAsync(client, cancellationToken).ConfigureAwait(false) ?? (credential.IsUsable(now) ? null : "credential");
        if (reason is not null)
        {
            Refuse(client.Id, reason);
            return null;
        }

        if (credential.LastUsedAt is null || now - credential.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            credential.MarkUsed(now);
            client.MarkUsed(now, http.HttpContext?.Connection.RemoteIpAddress?.ToString());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Principal(client, client.Scopes, scheme);
    }

    /// <summary>Why <paramref name="client"/> may not call now (<c>revoked</c>, <c>expired</c>, <c>account</c>), or null.</summary>
    private async Task<string?> RefusalAsync(IntegrationClient client, CancellationToken cancellationToken) =>
        client.Status == ClientStatuses.Revoked ? "revoked"
        : client.ExpiresAt <= clock.UtcNow ? "expired"
        : !await accounts.IsUsableAsync(client.ServiceUserId, cancellationToken).ConfigureAwait(false) ? "account"
        : null;

    private void Refuse(Guid id, string reason)
    {
        if (http.HttpContext is { } context)
        {
            context.Items[RefusalKey] = new ClientRefusal(id, reason);
        }
    }

    /// <summary>The principal a client acts as outside a request (webhook fan-out, scheduled exports).</summary>
    public ClaimsPrincipal PrincipalFor(IntegrationClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return Principal(client, client.Scopes, AuthSchemes.Integration, setCallContext: false);
    }

    private ClaimsPrincipal Principal(IntegrationClient client, IReadOnlyList<string> scopes, string scheme, bool setCallContext = true)
    {
        var identity = new ClaimsIdentity(scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, client.ServiceUserId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, "integration." + client.ClientId));
        identity.AddClaim(new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal));
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, AuthMethods.Client));
        identity.AddClaim(new Claim(StrategyClaims.TokenScopes, string.Join(' ', scopes)));
        identity.AddClaim(new Claim(StrategyClaims.Client, client.ClientId));
        identity.AddClaim(new Claim(StrategyClaims.IntegrationClient, client.ClientId));
        identity.AddClaim(new Claim(StrategyClaims.AllowedSystems, string.Join(' ', client.AllowedSystems)));
        if (client.ActingUnitId is { } unit)
        {
            identity.AddClaim(new Claim(StrategyClaims.ActingUnit, unit.ToString()));
        }

        if (setCallContext && http.HttpContext is { } context)
        {
            context.Items[CallContextKey] = new ClientCallContext(client.Id, client.ClientId, client.IpAllowList, client.RateLimitPerMinute, client.AllowedRecordTypes);
        }

        return new ClaimsPrincipal(identity);
    }
}
