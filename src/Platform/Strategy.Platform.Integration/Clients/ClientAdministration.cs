using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Feeds;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Strategy.Platform.Integration.Clients;

public sealed record ClientDto(
    Guid Id,
    string ClientId,
    string? NameAr,
    string? NameEn,
    string? Description,
    string AuthKind,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AllowedSystems,
    IReadOnlyList<string> AllowedRecordTypes,
    IReadOnlyList<string> IpAllowList,
    int RateLimitPerMinute,
    DateTimeOffset ExpiresAt,
    string State,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    Guid? ActingUnitId,
    string? ActingUnitNameAr,
    string? ActingUnitNameEn,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record CredentialDto(
    Guid Id, string Kind, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt, string State);

/// <summary>A new client with its first credential; <see cref="Secret"/> is shown once.</summary>
public sealed record CreatedClientDto(ClientDto Client, string Secret);

/// <summary>A new credential after rotation; <see cref="Secret"/> is shown once.</summary>
public sealed record RotatedCredentialDto(CredentialDto Credential, string Secret);

public sealed record ClientInput(
    string? NameAr,
    string? NameEn,
    string? Description,
    string? AuthKind,
    Guid? ActingUnitId,
    IReadOnlyList<string>? Scopes,
    IReadOnlyList<string>? AllowedSystems,
    IReadOnlyList<string>? AllowedRecordTypes,
    IReadOnlyList<string>? IpAllowList,
    int? RateLimitPerMinute,
    DateTimeOffset? ExpiresAt,
    uint Version = 0);

public sealed record ClientRevokeInput(string? Reason, uint Version);

public sealed record ClientRotateInput(int? OverlapDays);

public sealed record ClientRolesInput(IReadOnlyList<Guid>? RoleIds);

public sealed record ClientUsageRowDto(DateTimeOffset At, string Method, string Path, int StatusCode, int DurationMs, int? Rows, string? Ip, string? RefusedReason);

public sealed record ClientUsageDayDto(DateOnly Day, int Calls, int Refused, int Rows);

public sealed record ClientUsageDto(PagedResult<ClientUsageRowDto> Log, IReadOnlyList<ClientUsageDayDto> Days);

public sealed record IntegrationSettingsDto(IntegrationSettings Settings, uint Version);

public sealed record IntegrationSettingsInput(IntegrationSettings Settings, uint Version);

/// <summary>Settings → Integrations → Clients (ADR 0191). Entities are audited by the interceptor; credential values are never stored.</summary>
public sealed class ClientAdministration(
    IntegrationDbContext db,
    ServiceAccounts accounts,
    ServiceAccountRoles roles,
    FeedCatalog catalog,
    IOrgDirectory org,
    ISettingsStore settings,
    ISettingsEditor editor,
    IOpenIddictApplicationManager applications,
    IAuditLog audit,
    IClock clock)
{
    public const int MaxListEntries = 50;

    public async Task<IReadOnlyList<ClientDto>> ListAsync(CancellationToken ct)
    {
        var clients = await db.Clients.AsNoTracking().OrderBy(c => c.Status).ThenBy(c => c.NameEn ?? c.NameAr).ToListAsync(ct).ConfigureAwait(false);
        return await ToDtosAsync(clients, ct).ConfigureAwait(false);
    }

    public async Task<Result<ClientDto>> GetAsync(Guid id, CancellationToken ct) =>
        await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false) is { } client
            ? (await ToDtosAsync([client], ct).ConfigureAwait(false))[0]
            : Error.NotFound();

    public async Task<Result<CreatedClientDto>> CreateAsync(ClientInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var limits = await settings.GetAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        var errors = await ValidateAsync(input, limits, ct).ConfigureAwait(false);
        var kind = input.AuthKind ?? ClientAuthKinds.ApiKey;
        if (!ClientAuthKinds.All.Contains(kind))
        {
            errors.Add(Field("authKind", "validation.unknownCode"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var clientId = ClientSecrets.NewClientId();
        var serviceUser = await accounts.CreateAsync(clientId, ct).ConfigureAwait(false);
        var client = IntegrationClient.Create(clientId, serviceUser, kind, Settings(input, limits));
        db.Clients.Add(client);
        var (secret, credential) = NewCredential(client);
        db.Credentials.Add(credential);
        if (kind == ClientAuthKinds.OAuth)
        {
            await applications.CreateAsync(Descriptor(client, secret), ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.client.create", true, IntegrationPermissions.SystemCode, IntegrationClient.EntityType, client.Id.ToString(),
            new { client.ClientId, client.AuthKind, client.Scopes, client.AllowedSystems }), ct).ConfigureAwait(false);
        return new CreatedClientDto((await ToDtosAsync([client], ct).ConfigureAwait(false))[0], secret);
    }

    public async Task<Result<ClientDto>> UpdateAsync(Guid id, ClientInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        if (client.Status == ClientStatuses.Revoked)
        {
            return Error.Rule("integration.client.revoked", "integration.errors.clientRevoked");
        }

        var limits = await settings.GetAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        var errors = await ValidateAsync(input, limits, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(client).Property(x => x.Version).OriginalValue = input.Version;
        client.Apply(Settings(input, limits));
        if (client.AuthKind == ClientAuthKinds.OAuth && await applications.FindByClientIdAsync(client.ClientId, ct).ConfigureAwait(false) is { } application)
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(descriptor, application, ct).ConfigureAwait(false);
            descriptor.DisplayName = client.NameEn ?? client.NameAr;
            SetPermissions(descriptor, client);
            await applications.UpdateAsync(application, descriptor, ct).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return (await ToDtosAsync([client], ct).ConfigureAwait(false))[0];
    }

    /// <summary>Revokes the client for good: credentials revoked, service account deactivated, OAuth registration and tokens removed.</summary>
    public async Task<Result<ClientDto>> RevokeAsync(Guid id, ClientRevokeInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            return Error.Validation([Field("reason", "validation.notEmpty")]);
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        db.Entry(client).Property(x => x.Version).OriginalValue = input.Version;
        var now = clock.UtcNow;
        client.Revoke(now, input.Reason.Trim());
        foreach (var credential in await db.Credentials.Where(c => c.ClientRef == id && c.RevokedAt == null).ToListAsync(ct).ConfigureAwait(false))
        {
            credential.Revoke(now);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await accounts.SetActiveAsync(client.ServiceUserId, false, ct).ConfigureAwait(false);
        if (await applications.FindByClientIdAsync(client.ClientId, ct).ConfigureAwait(false) is { } application)
        {
            await applications.DeleteAsync(application, ct).ConfigureAwait(false);
        }

        await audit.WriteAsync(new AuditEvent("integration.client.revoke", true, IntegrationPermissions.SystemCode, IntegrationClient.EntityType, client.Id.ToString(),
            new { client.ClientId }, input.Reason.Trim()), ct).ConfigureAwait(false);
        return (await ToDtosAsync([client], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<IReadOnlyList<CredentialDto>>> CredentialsAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == id, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.Credentials.AsNoTracking().Where(c => c.ClientRef == id).OrderByDescending(c => c.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// A new credential. API keys: the current ones keep working for <c>OverlapDays</c> (0–30, default 7). OAuth: the secret is replaced
    /// at once (OpenIddict keeps one secret per application).
    /// </summary>
    public async Task<Result<RotatedCredentialDto>> RotateAsync(Guid id, ClientRotateInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var overlap = input.OverlapDays ?? 7;
        if (overlap is < 0 or > 30)
        {
            return Error.Validation([Field("overlapDays", "validation.inclusiveBetween", new() { ["from"] = 0, ["to"] = 30 })]);
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        if (client.Status == ClientStatuses.Revoked)
        {
            return Error.Rule("integration.client.revoked", "integration.errors.clientRevoked");
        }

        var now = clock.UtcNow;
        var current = await db.Credentials.Where(c => c.ClientRef == id && c.RevokedAt == null).ToListAsync(ct).ConfigureAwait(false);
        var (secret, credential) = NewCredential(client);
        db.Credentials.Add(credential);
        foreach (var old in current)
        {
            if (client.AuthKind == ClientAuthKinds.OAuth || overlap == 0)
            {
                old.Revoke(now);
            }
            else
            {
                old.EndAt(now.AddDays(overlap));
            }
        }

        if (client.AuthKind == ClientAuthKinds.OAuth && await applications.FindByClientIdAsync(client.ClientId, ct).ConfigureAwait(false) is { } application)
        {
            await applications.UpdateAsync(application, secret, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.client.rotate", true, IntegrationPermissions.SystemCode, IntegrationClient.EntityType, client.Id.ToString(),
            new { credential.Prefix, overlapDays = client.AuthKind == ClientAuthKinds.OAuth ? 0 : overlap }), ct).ConfigureAwait(false);
        return new RotatedCredentialDto(ToDto(credential), secret);
    }

    public async Task<Result<CredentialDto>> RevokeCredentialAsync(Guid id, Guid credentialId, CancellationToken ct)
    {
        var credential = await db.Credentials.FirstOrDefaultAsync(c => c.Id == credentialId && c.ClientRef == id, ct).ConfigureAwait(false);
        if (credential is null)
        {
            return Error.NotFound();
        }

        credential.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.client.credential.revoke", true, IntegrationPermissions.SystemCode, IntegrationClient.EntityType, id.ToString(),
            new { credential.Prefix }), ct).ConfigureAwait(false);
        return ToDto(credential);
    }

    /// <summary>Roles of the client's allowed systems with their data scopes; <c>Assigned</c> marks those its service account holds.</summary>
    public async Task<Result<IReadOnlyList<ServiceAccountRoleOption>>> RolesAsync(Guid id, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        return client is null ? Error.NotFound() : Result<IReadOnlyList<ServiceAccountRoleOption>>.Success(await roles.ListAsync(client.ServiceUserId, client.AllowedSystems, ct).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<ServiceAccountRoleOption>>> SetRolesAsync(Guid id, ClientRolesInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        if (client.Status == ClientStatuses.Revoked)
        {
            return Error.Rule("integration.client.revoked", "integration.errors.clientRevoked");
        }

        var roleIds = (input.RoleIds ?? []).Distinct().ToList();
        var result = await roles.SetAsync(client.ServiceUserId, roleIds, client.AllowedSystems, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await audit.WriteAsync(new AuditEvent("integration.client.roles", true, IntegrationPermissions.SystemCode, IntegrationClient.EntityType, id.ToString(),
            new { roleIds }), ct).ConfigureAwait(false);
        return Result<IReadOnlyList<ServiceAccountRoleOption>>.Success(await roles.ListAsync(client.ServiceUserId, client.AllowedSystems, ct).ConfigureAwait(false));
    }

    public async Task<Result<ClientUsageDto>> UsageAsync(Guid id, PageRequest paging, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paging);
        if (!await db.Clients.AnyAsync(c => c.Id == id, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var query = db.Usage.AsNoTracking().Where(u => u.ClientRef == id);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(u => u.At).Skip(paging.Skip).Take(paging.PageSize)
            .Select(u => new ClientUsageRowDto(u.At, u.Method, u.Path, u.StatusCode, u.DurationMs, u.Rows, u.Ip, u.RefusedReason))
            .ToListAsync(ct).ConfigureAwait(false);
        var since = clock.UtcNow.AddDays(-30);
        var recent = await query.Where(u => u.At >= since).Select(u => new { u.At, u.RefusedReason, u.Rows }).ToListAsync(ct).ConfigureAwait(false);
        var days = recent.GroupBy(u => DateOnly.FromDateTime(u.At.UtcDateTime))
            .Select(g => new ClientUsageDayDto(g.Key, g.Count(), g.Count(u => u.RefusedReason != null), g.Sum(u => u.Rows ?? 0)))
            .OrderBy(d => d.Day).ToList();
        return new ClientUsageDto(new PagedResult<ClientUsageRowDto>(rows, paging.Page, paging.PageSize, total), days);
    }

    public async Task<IntegrationSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var document = await editor.GetDocumentAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        return new IntegrationSettingsDto(document.Value, document.Version);
    }

    public async Task<Result<IntegrationSettingsDto>> SaveSettingsAsync(IntegrationSettingsInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var saved = await editor.SaveAsync(input.Settings, input.Version, value =>
        {
            var errors = new List<FieldError>();
            Range(errors, "maxClientDays", value.MaxClientDays, 1, 3650);
            Range(errors, "defaultClientDays", value.DefaultClientDays, 1, value.MaxClientDays);
            Range(errors, "accessTokenMinutes", value.AccessTokenMinutes, 5, 1440);
            Range(errors, "oDataPageSize", value.ODataPageSize, 1, 5000);
            Range(errors, "expandLimit", value.ExpandLimit, 100, 100_000);
            Range(errors, "extractMaxPageSize", value.ExtractMaxPageSize, 10, 5000);
            Range(errors, "usageRetentionDays", value.UsageRetentionDays, 1, 3650);
            Range(errors, "webhookMaxAttempts", value.WebhookMaxAttempts, 1, 20);
            Range(errors, "webhookRetryBaseSeconds", value.WebhookRetryBaseSeconds, 5, 3600);
            Range(errors, "webhookTimeoutSeconds", value.WebhookTimeoutSeconds, 1, 60);
            return errors;
        }, ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : new IntegrationSettingsDto(saved.Value.Value, saved.Value.Version);
    }

    private static void Range(List<FieldError> errors, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add(Field(field, "validation.inclusiveBetween", new() { ["from"] = min, ["to"] = max }));
        }
    }

    private ClientSettingsInput Settings(ClientInput input, IntegrationSettings limits) => new(
        Clean(input.NameAr),
        Clean(input.NameEn),
        Clean(input.Description),
        input.ActingUnitId,
        input.Scopes is { Count: > 0 } scopes ? scopes : [ClientScopes.Feeds],
        input.AllowedSystems ?? [],
        input.AllowedRecordTypes ?? [],
        input.IpAllowList ?? [],
        input.RateLimitPerMinute ?? 120,
        input.ExpiresAt ?? clock.UtcNow.AddDays(limits.DefaultClientDays));

    private async Task<List<FieldError>> ValidateAsync(ClientInput input, IntegrationSettings limits, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(Field("nameAr", "validation.atLeastOneLanguage"));
        }

        foreach (var (name, value, max) in new[]
                 {
                     ("nameAr", input.NameAr, IntegrationClient.NameMaxLength), ("nameEn", input.NameEn, IntegrationClient.NameMaxLength),
                     ("description", input.Description, IntegrationClient.DescriptionMaxLength),
                 })
        {
            if (value is { } text && text.Trim().Length > max)
            {
                errors.Add(Field(name, "validation.maximumLength", new() { ["maxLength"] = max }));
            }
        }

        if (input.Scopes is { } scopes && scopes.Any(s => !ClientScopes.All.Contains(s)))
        {
            errors.Add(Field("scopes", "validation.unknownCode"));
        }

        var systems = input.AllowedSystems ?? [];
        if (systems.Count == 0)
        {
            errors.Add(Field("allowedSystems", "validation.notEmpty"));
        }
        else if (systems.Any(s => !catalog.Systems.Contains(s)))
        {
            errors.Add(Field("allowedSystems", "validation.unknownCode"));
        }

        if (input.AllowedRecordTypes is { } types
            && types.Any(t => catalog.Find(t) is not { } type || !systems.Contains(type.SystemCode)))
        {
            errors.Add(Field("allowedRecordTypes", "integration.errors.recordTypeOutsideSystems"));
        }

        if (input.IpAllowList is { } ips && (ips.Count > MaxListEntries || ips.Any(ip => !IpAllowList.TryParse(ip, out _))))
        {
            errors.Add(Field("ipAllowList", "integration.errors.ipEntry"));
        }

        if (input.RateLimitPerMinute is { } rate && (rate < 1 || rate > 10_000))
        {
            errors.Add(Field("rateLimitPerMinute", "validation.inclusiveBetween", new() { ["from"] = 1, ["to"] = 10_000 }));
        }

        if (input.ExpiresAt is { } expires && (expires <= clock.UtcNow || expires > clock.UtcNow.AddDays(limits.MaxClientDays)))
        {
            errors.Add(Field("expiresAt", "integration.errors.expiry", new() { ["max"] = limits.MaxClientDays }));
        }

        if (input.ActingUnitId is { } unit && await org.GetUnitAsync(unit, ct).ConfigureAwait(false) is null)
        {
            errors.Add(Field("actingUnitId", "validation.unknownReference"));
        }

        return errors;
    }

    private static (string Secret, ClientCredential Credential) NewCredential(IntegrationClient client)
    {
        var (value, prefix, hash) = client.AuthKind == ClientAuthKinds.OAuth ? ClientSecrets.NewOAuthSecret() : ClientSecrets.NewApiKey();
        var credential = ClientCredential.Create(client.Id, client.AuthKind == ClientAuthKinds.OAuth ? CredentialKinds.Secret : CredentialKinds.ApiKey, prefix, hash);
        return (value, credential);
    }

    private static OpenIddictApplicationDescriptor Descriptor(IntegrationClient client, string secret)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            ClientSecret = secret,
            ClientType = ClientTypes.Confidential,
            DisplayName = client.NameEn ?? client.NameAr,
        };
        SetPermissions(descriptor, client);
        return descriptor;
    }

    private static void SetPermissions(OpenIddictApplicationDescriptor descriptor, IntegrationClient client)
    {
        descriptor.Permissions.Clear();
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        foreach (var scope in client.Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }
    }

    private async Task<IReadOnlyList<ClientDto>> ToDtosAsync(IReadOnlyList<IntegrationClient> clients, CancellationToken ct)
    {
        var units = new Dictionary<Guid, UnitReference?>();
        foreach (var unit in clients.Select(c => c.ActingUnitId).OfType<Guid>().Distinct())
        {
            units[unit] = await org.GetUnitAsync(unit, ct).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        return clients.Select(c =>
        {
            var unit = c.ActingUnitId is { } u ? units.GetValueOrDefault(u) : null;
            var state = c.Status == ClientStatuses.Revoked ? "revoked" : c.ExpiresAt <= now ? "expired" : "active";
            return new ClientDto(c.Id, c.ClientId, c.NameAr, c.NameEn, c.Description, c.AuthKind, c.Scopes, c.AllowedSystems, c.AllowedRecordTypes, c.IpAllowList,
                c.RateLimitPerMinute, c.ExpiresAt, state, c.RevokedAt, c.RevokeReason, c.ActingUnitId, unit?.NameAr, unit?.NameEn, c.LastUsedAt, c.LastUsedIp,
                c.CreatedAt, c.Version);
        }).ToList();
    }

    private CredentialDto ToDto(ClientCredential c)
    {
        var now = clock.UtcNow;
        var state = c.RevokedAt is not null ? "revoked" : c.ExpiresAt is { } end && end <= now ? "expired" : c.ExpiresAt is not null ? "ending" : "active";
        return new CredentialDto(c.Id, c.Kind, c.Prefix, c.CreatedAt, c.ExpiresAt, c.RevokedAt, c.LastUsedAt, state);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FieldError Field(string field, string key, Dictionary<string, object?>? parameters = null) => new(field, key, key, parameters);
}
