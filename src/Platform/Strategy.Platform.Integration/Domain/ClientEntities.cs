using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Integration.Domain;

public static class ClientAuthKinds
{
    /// <summary>OAuth 2.0 client credentials (OpenIddict confidential application).</summary>
    public const string OAuth = "oauth";

    public const string ApiKey = "api_key";

    public static readonly IReadOnlyList<string> All = [OAuth, ApiKey];
}

public static class ClientStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public static class ClientScopes
{
    public const string Feeds = "feeds";
    public const string ApiRead = "api.read";

    public static readonly IReadOnlyList<string> All = [Feeds, ApiRead];
}

/// <summary>A named service account of another system (ADR 0191). Never deleted: revoked (and never reactivated).</summary>
[Audited(EntityType)]
public sealed class IntegrationClient : AuditableEntity
{
    public const string EntityType = "integration.client";
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private IntegrationClient()
    {
    }

    /// <summary>Public identifier <c>ic_…</c> (OAuth client_id, Basic user name).</summary>
    public string ClientId { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public Guid ServiceUserId { get; private set; }

    public Guid? ActingUnitId { get; private set; }

    public string AuthKind { get; private set; } = ClientAuthKinds.ApiKey;

    public List<string> Scopes { get; private set; } = [];

    public List<string> AllowedSystems { get; private set; } = [];

    /// <summary>Empty = every record type of the allowed systems.</summary>
    public List<string> AllowedRecordTypes { get; private set; } = [];

    /// <summary>CIDR blocks or addresses; empty = any address.</summary>
    public List<string> IpAllowList { get; private set; } = [];

    public int RateLimitPerMinute { get; private set; } = 120;

    public DateTimeOffset ExpiresAt { get; private set; }

    public string Status { get; private set; } = ClientStatuses.Active;

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokeReason { get; private set; }

    [NotAudited]
    public DateTimeOffset? LastUsedAt { get; private set; }

    [NotAudited]
    public string? LastUsedIp { get; private set; }

    public bool IsUsable(DateTimeOffset now) => Status == ClientStatuses.Active && ExpiresAt > now;

    public static IntegrationClient Create(string clientId, Guid serviceUserId, string authKind, ClientSettingsInput input) => new IntegrationClient
    {
        ClientId = clientId,
        ServiceUserId = serviceUserId,
        AuthKind = authKind,
    }.Apply(input);

    public IntegrationClient Apply(ClientSettingsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        NameAr = input.NameAr;
        NameEn = input.NameEn;
        Description = input.Description;
        ActingUnitId = input.ActingUnitId;
        Scopes = [.. input.Scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        AllowedSystems = [.. input.AllowedSystems.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        AllowedRecordTypes = [.. input.AllowedRecordTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        IpAllowList = [.. input.IpAllowList.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal)];
        RateLimitPerMinute = input.RateLimitPerMinute;
        ExpiresAt = input.ExpiresAt;
        return this;
    }

    public void Revoke(DateTimeOffset at, string reason)
    {
        if (Status == ClientStatuses.Revoked)
        {
            return;
        }

        Status = ClientStatuses.Revoked;
        RevokedAt = at;
        RevokeReason = reason;
    }

    public void MarkUsed(DateTimeOffset at, string? ip)
    {
        LastUsedAt = at;
        LastUsedIp = ip;
    }
}

/// <summary>The editable settings of a client.</summary>
public sealed record ClientSettingsInput(
    string? NameAr,
    string? NameEn,
    string? Description,
    Guid? ActingUnitId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AllowedSystems,
    IReadOnlyList<string> AllowedRecordTypes,
    IReadOnlyList<string> IpAllowList,
    int RateLimitPerMinute,
    DateTimeOffset ExpiresAt);

public static class CredentialKinds
{
    public const string Secret = "secret";
    public const string ApiKey = "api_key";
}

/// <summary>An OAuth client secret or an API key of a client. Only the SHA-256 is stored; the value is shown once.</summary>
[Audited(EntityType)]
public sealed class ClientCredential : AuditableEntity
{
    public const string EntityType = "integration.client_credential";

    private ClientCredential()
    {
    }

    public Guid ClientRef { get; private set; }

    public string Kind { get; private set; } = CredentialKinds.ApiKey;

    public string Prefix { get; private set; } = string.Empty;

    [AuditMasked]
    public byte[] SecretHash { get; private set; } = [];

    /// <summary>Null = valid while the client is; set when a rotation gives an overlap.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public static ClientCredential Create(Guid clientRef, string kind, string prefix, byte[] hash) =>
        new() { ClientRef = clientRef, Kind = kind, Prefix = prefix, SecretHash = hash };

    public void EndAt(DateTimeOffset at)
    {
        if (ExpiresAt is null || ExpiresAt > at)
        {
            ExpiresAt = at;
        }
    }

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    public void MarkUsed(DateTimeOffset at) => LastUsedAt = at;
}

/// <summary>One call of a client (technical log, not entity-audited; purged after the retention).</summary>
public sealed class ClientUsage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ClientRef { get; init; }

    public DateTimeOffset At { get; init; }

    public string Method { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public int StatusCode { get; init; }

    public int DurationMs { get; init; }

    public int? Rows { get; init; }

    public string? Ip { get; init; }

    /// <summary><c>ip</c>, <c>rate</c>, <c>scope</c>, <c>expired</c>, <c>revoked</c> — or null.</summary>
    public string? RefusedReason { get; init; }
}

/// <summary>A client's subscription to webhook events (ADR 0194). The signing secret is encrypted (it is needed to sign).</summary>
[Audited(EntityType)]
public sealed class WebhookSubscription : AuditableEntity
{
    public const string EntityType = "integration.webhook_subscription";
    public const int UrlMaxLength = 2000;

    private WebhookSubscription()
    {
    }

    public Guid ClientRef { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TargetUrl { get; private set; } = string.Empty;

    public List<string> Events { get; private set; } = [];

    public bool Active { get; private set; } = true;

    [AuditMasked]
    public string ProtectedSecret { get; private set; } = string.Empty;

    public static WebhookSubscription Create(Guid clientRef, string name, string url, IReadOnlyList<string> events, string protectedSecret) =>
        new WebhookSubscription { ClientRef = clientRef, ProtectedSecret = protectedSecret }.Update(name, url, events, true);

    public WebhookSubscription Update(string name, string url, IReadOnlyList<string> events, bool active)
    {
        Name = name.Trim();
        TargetUrl = url.Trim();
        Events = [.. events.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Active = active;
        return this;
    }

    public void SetSecret(string protectedSecret) => ProtectedSecret = protectedSecret;
}

public static class DeliveryStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";

    /// <summary>Failed, another attempt is scheduled.</summary>
    public const string Retrying = "retrying";

    /// <summary>Every attempt failed: the dead-letter list.</summary>
    public const string Dead = "dead";

    public static readonly IReadOnlyList<string> All = [Pending, Delivered, Retrying, Dead];
}

/// <summary>One event for one subscription (technical log of a delivery and its attempts).</summary>
public sealed class WebhookDelivery
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid SubscriptionRef { get; init; }

    public Guid ClientRef { get; init; }

    public string EventCode { get; init; } = string.Empty;

    /// <summary>SHA-256 of type and payload: one delivery per (subscription, event) even when the outbox retries.</summary>
    public string EventKey { get; init; } = string.Empty;

    public string? ResourceType { get; init; }

    public Guid? ResourceId { get; init; }

    public string Payload { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string Status { get; set; } = DeliveryStatuses.Pending;

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }
}

/// <summary>One HTTP attempt of a delivery.</summary>
public sealed class WebhookAttempt
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid DeliveryRef { get; init; }

    public int Number { get; init; }

    public DateTimeOffset At { get; init; }

    public int? StatusCode { get; init; }

    public int DurationMs { get; init; }

    public string? Error { get; init; }
}

/// <summary>A scheduled extract of a client's record types to a folder under the operator's export root (ADR 0193).</summary>
[Audited(EntityType)]
public sealed class ExportSchedule : AuditableEntity
{
    public const string EntityType = "integration.export_schedule";

    private ExportSchedule()
    {
    }

    public Guid ClientRef { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public List<string> RecordTypes { get; private set; } = [];

    /// <summary><c>json</c> or <c>csv</c>.</summary>
    public string Format { get; private set; } = "json";

    /// <summary>Relative folder under <c>Integration:ExportRoot</c>.</summary>
    public string Folder { get; private set; } = string.Empty;

    public int EveryHours { get; private set; } = 24;

    public bool Active { get; private set; } = true;

    [NotAudited]
    public DateTimeOffset? LastRunAt { get; private set; }

    [NotAudited]
    public string? LastRunStatus { get; private set; }

    /// <summary>Record type → watermark (highest modifiedAt delivered).</summary>
    [NotAudited]
    public Dictionary<string, DateTimeOffset> Watermarks { get; private set; } = [];

    public static ExportSchedule Create(Guid clientRef) => new() { ClientRef = clientRef };

    public ExportSchedule Update(string name, IReadOnlyList<string> recordTypes, string format, string folder, int everyHours, bool active)
    {
        Name = name.Trim();
        RecordTypes = [.. recordTypes.Distinct(StringComparer.Ordinal)];
        Format = format;
        Folder = folder.Trim();
        EveryHours = everyHours;
        Active = active;
        return this;
    }

    public bool IsDue(DateTimeOffset now) => Active && (LastRunAt is null || LastRunAt.Value.AddHours(EveryHours) <= now);

    public void Ran(DateTimeOffset at, string status, IReadOnlyDictionary<string, DateTimeOffset> watermarks)
    {
        LastRunAt = at;
        LastRunStatus = status;
        Watermarks = new Dictionary<string, DateTimeOffset>(watermarks, StringComparer.Ordinal);
    }
}
