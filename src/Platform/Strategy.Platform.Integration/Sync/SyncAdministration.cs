using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Audit;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Net;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Sync;

// ---- DTOs ----

/// <summary>What Salesforce answered to a failed call (ADR 0211): HTTP status, sanitised call, Salesforce errorCode and message.</summary>
public sealed record SalesforceErrorDto(int? Status, string Call, string? ErrorCode, string? Message)
{
    public static SalesforceErrorDto? From(SalesforceError? error) => error is null ? null : new(error.Status, error.Call, error.ErrorCode, error.Message);
}

/// <summary>A non-blocking finding of a connection test (e.g. <c>/limits</c> refused, ADR 0210).</summary>
public sealed record SyncWarningDto(string MessageKey, IReadOnlyDictionary<string, string?>? Params);

/// <summary>Stored detail of the last connection test (<c>sync_connection.last_test_detail</c>).</summary>
public sealed record SyncTestDetail
{
    public SalesforceErrorDto? Error { get; init; }

    public List<SyncWarningDto> Warnings { get; init; } = [];
}

public sealed record SyncTestDto(DateTimeOffset At, bool Ok, string? MessageKey, SalesforceErrorDto? Error, IReadOnlyList<SyncWarningDto> Warnings);

public sealed record SyncConnectionDto(
    Guid Id,
    string SourceSystem,
    string? SourceNameAr,
    string? SourceNameEn,
    string Connector,
    string Mode,
    IReadOnlyList<string> ModeActions,
    string LoginUrl,
    string? ClientId,
    string? Username,
    bool KeyStored,
    string? KeyThumbprint,
    string ApiVersion,
    int BulkThreshold,
    int PageSize,
    SyncScheduleSettings Schedule,
    DateTimeOffset? NextRunAt,
    int? LateAfterMinutes,
    Guid? MappingPackId,
    string? InstanceUrl,
    ApiLimits? Limits,
    SyncTestDto? LastTest,
    bool RunRequested,
    uint Version);

public sealed record SyncConnectionInput(
    string? LoginUrl, string? ClientId, string? Username, string? ApiVersion, int? BulkThreshold, int? PageSize, Guid? MappingPackId, uint Version);

public sealed record SyncKeyInput(string? PrivateKeyPem);

public sealed record SyncScheduleInput(SyncScheduleSettings Schedule, int? LateAfterMinutes, uint Version);

public sealed record SyncScheduleDto(SyncScheduleSettings Schedule, int? LateAfterMinutes, string TimeZone, IReadOnlyList<DateTimeOffset> NextRuns, DateTimeOffset? NextRunAt);

public sealed record SyncNowInput(bool Full = false, IReadOnlyList<string>? Objects = null);

public sealed record SyncRunDto(
    Guid Id, string Trigger, bool Full, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, double? DurationSeconds,
    IReadOnlyDictionary<string, ObjectRunStats> Objects, int ApiCalls, string? MessageKey, IReadOnlyDictionary<string, string?>? Params);

public sealed record SyncRunLogDto(DateTimeOffset At, string Level, string? ObjectKey, string MessageKey, IReadOnlyDictionary<string, string?>? Params);

public sealed record SyncRunDetailDto(SyncRunDto Run, IReadOnlyList<SyncRunLogDto> Log);

public sealed record SyncObjectStatusDto(
    Guid Id, string Key, string SourceObject, string Kind, string? TargetType, bool Enabled, bool Confirmed, bool Provisional, int LoadOrder,
    DateTimeOffset? Watermark, DateTimeOffset? LastFullLoadAt, ObjectRunStats? LastRun, int OpenExceptions,
    ReconciliationResult? Reconciliation = null, DateTimeOffset? ReconciledAt = null);

public sealed record SyncOutageDto(
    Guid Id, string Status, DateTimeOffset StartedAt, string? StartedByName, string Reason, DateTimeOffset? ReturnedAt, DateTimeOffset? ReportAcknowledgedAt,
    DateTimeOffset? ClosedAt, int ResubmitRequired, int ChangedLocally, int ReEntered, int PendingConflicts);

public sealed record SyncStatusDto(
    SyncConnectionDto Connection, SyncRunDto? LastRun, SyncRunDto? LastSuccess, bool Running, bool Late, int OpenExceptions,
    IReadOnlyList<SyncObjectStatusDto> Objects, SyncOutageDto? Outage);

public sealed record SyncTargetFieldDto(string Code, string Kind, bool Required, string? LookupType, string? ReferenceType, IReadOnlyList<string>? Choices);

public sealed record SyncTargetDto(string Code, string SystemCode, string NameAr, string NameEn, IReadOnlyList<SyncTargetFieldDto> Fields);

public sealed record MappingPackDto(Guid Id, string Code, string? NameAr, string? NameEn, int PackVersion, string? Description, string Connector, int Objects, uint Version);

public sealed record ObjectMappingDto(
    Guid Id, Guid PackId, string Key, string SourceObject, string Kind, string? TargetType, bool Enabled, int LoadOrder, DateTimeOffset? ConfirmedAt,
    string? Where, string WatermarkField, string ExternalIdTemplate, bool UseBulk, IReadOnlyList<FieldMapping> Fields, IReadOnlyDictionary<string, JsonElement> Defaults,
    IReadOnlyList<string> Ignore, JsonElement Options, string? NotesAr, string? NotesEn, bool Provisional, DateTimeOffset? Watermark, uint Version);

public sealed record ObjectMappingInput(
    string? TargetType, int LoadOrder, string? Where, string? WatermarkField, string? ExternalIdTemplate, bool UseBulk, IReadOnlyList<FieldMapping>? Fields,
    Dictionary<string, JsonElement>? Defaults, IReadOnlyList<string>? Ignore, JsonElement? Options, string? NotesAr, string? NotesEn, uint Version);

public sealed record ObjectMappingEnableInput(bool Enabled, uint Version);

public sealed record MappingIssueDto(string Severity, string? Field, string MessageKey, IReadOnlyDictionary<string, string?>? Params);

public sealed record MappingValidationDto(bool Ok, DateTimeOffset? ConfirmedAt, IReadOnlyList<MappingIssueDto> Issues);

public sealed record SourceObjectDto(string Name, string Label, bool Custom);

public sealed record SyncExceptionDto(
    Guid Id, Guid MappingId, string? MappingKey, string SourceObject, string ExternalId, string Reason, string? Field, string? Value, string? MessageKey,
    string Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ResolvedAt,
    IReadOnlyDictionary<string, string?>? Params = null);

public sealed record SyncExceptionDetailDto(SyncExceptionDto Exception, IReadOnlyDictionary<string, string?> Payload, string? TargetType);

public sealed record MapValueInput(string? Value, string? Code);

public sealed record MapPersonInput(Guid EmployeeId);

public sealed record ReprocessInput(string? Reason = null, string? MappingKey = null);

public sealed record ReprocessResultDto(int Processed, int Resolved, int StillOpen);

/// <summary>JSON document of a mapping pack (export / import, ADR 0203). No connection settings, secrets or watermarks.</summary>
public sealed record MappingPackDocument
{
    public const string FormatName = "strategy.sync-pack/1";

    public string Format { get; init; } = FormatName;

    public string Code { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public string? NameAr { get; init; }

    public string? NameEn { get; init; }

    public string? Description { get; init; }

    public string Connector { get; init; } = Connectors.Salesforce;

    public List<MappingPackObject> Objects { get; init; } = [];

    /// <summary>
    /// Lookup values the pack's value maps produce (release 1.0.2, ADR 0216): organisation-specific picklist values live in the pack, not in
    /// code. Import and upgrade add the codes a lookup type does not have yet and never change existing values.
    /// </summary>
    public List<MappingPackLookup> Lookups { get; init; } = [];
}

public sealed record MappingPackLookup
{
    public string Type { get; init; } = string.Empty;

    public List<MappingPackLookupValue> Values { get; init; } = [];
}

public sealed record MappingPackLookupValue(string Code, string? LabelAr, string? LabelEn);

public sealed record MappingPackObject
{
    public string Key { get; init; } = string.Empty;

    public string SourceObject { get; init; } = string.Empty;

    public string Kind { get; init; } = ObjectMappingKinds.Record;

    public string? TargetType { get; init; }

    public int LoadOrder { get; init; }

    public string? Where { get; init; }

    public string WatermarkField { get; init; } = "SystemModstamp";

    public string ExternalIdTemplate { get; init; } = "{Id}";

    public bool UseBulk { get; init; } = true;

    public bool Provisional { get; init; }

    public List<FieldMapping> Fields { get; init; } = [];

    public Dictionary<string, JsonElement> Defaults { get; init; } = [];

    public List<string> Ignore { get; init; } = [];

    public JsonElement? Options { get; init; }

    public string? NotesAr { get; init; }

    public string? NotesEn { get; init; }
}

/// <summary>
/// Settings → Integrations → Salesforce sync (docs/design/salesforce-sync.md §4, §5, §11, §13): connection, key, test, schedule, sync now,
/// status, runs, live describe, mapping editor with validation, pack export / import, targets, exceptions queue. Permissions are checked by
/// the endpoints; every change is audited (interceptor + explicit events).
/// </summary>
public sealed partial class SyncAdministration(
    IntegrationDbContext db,
    SalesforceSessionFactory sessions,
    SyncTargetRegistry targets,
    SyncEngine engine,
    SyncStandbyGuard guard,
    OutboundUrlPolicy urlPolicy,
    ISettingsStore settings,
    IAuditLog audit,
    IRequestContext context,
    IClock clock,
    ILookupProvisioning lookups,
    ILogger<SyncAdministration> logger)
{
    public const string SalesforceSource = "salesforce";

    private static Error Field(string field, string messageKey, IReadOnlyDictionary<string, object?>? parameters = null) =>
        Error.Validation([new FieldError(field, messageKey.Split('.')[^1], messageKey, parameters)]);

    // ---- Connection ----

    public async Task<IReadOnlyList<SyncConnectionDto>> ListAsync(CancellationToken ct)
    {
        var connections = await db.SyncConnections.AsNoTracking().OrderBy(c => c.SourceSystem).ToListAsync(ct).ConfigureAwait(false);
        var list = new List<SyncConnectionDto>();
        foreach (var c in connections)
        {
            list.Add(await ToDtoAsync(c, ct).ConfigureAwait(false));
        }

        return list;
    }

    public async Task<Result<SyncConnectionDto>> GetAsync(Guid id, CancellationToken ct) =>
        await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false) is { } c
            ? await ToDtoAsync(c, ct).ConfigureAwait(false) : Error.NotFound();

    public async Task<Result<SyncConnectionDto>> UpdateAsync(Guid id, SyncConnectionInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        var loginUrl = input.LoginUrl?.Trim().TrimEnd('/') ?? connection.LoginUrl;
        if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || urlPolicy.Check(loginUrl) != OutboundRefusal.None)
        {
            errors.Add(new FieldError("loginUrl", "outboundUrlRefused", "integration.sync.errors.loginUrl"));
        }

        var apiVersion = input.ApiVersion?.Trim() ?? connection.ApiVersion;
        if (!System.Text.RegularExpressions.Regex.IsMatch(apiVersion, "^v\\d{2,3}\\.0$"))
        {
            errors.Add(new FieldError("apiVersion", "apiVersion", "integration.sync.errors.apiVersion"));
        }

        if (input.BulkThreshold is < 1 or > 1_000_000)
        {
            errors.Add(new FieldError("bulkThreshold", "inclusiveBetween", "validation.inclusiveBetween"));
        }

        if (input.PageSize is < 200 or > 2000)
        {
            errors.Add(new FieldError("pageSize", "inclusiveBetween", "validation.inclusiveBetween"));
        }

        if (input.MappingPackId is { } packId && !await db.MappingPacks.AnyAsync(p => p.Id == packId, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("mappingPackId", "notFound", "errors.notFound"));
        }

        if (input.ClientId is { Length: > 500 } || input.Username is { Length: > 320 })
        {
            errors.Add(new FieldError(input.ClientId is { Length: > 500 } ? "clientId" : "username", "maxLength", "validation.maxLength"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(connection).Property(x => x.Version).OriginalValue = input.Version;
        connection.LoginUrl = loginUrl;
        connection.ClientId = string.IsNullOrWhiteSpace(input.ClientId) ? connection.ClientId : input.ClientId.Trim();
        connection.Username = string.IsNullOrWhiteSpace(input.Username) ? connection.Username : input.Username.Trim();
        connection.ApiVersion = apiVersion;
        connection.BulkThreshold = input.BulkThreshold ?? connection.BulkThreshold;
        connection.PageSize = input.PageSize ?? connection.PageSize;
        connection.MappingPackId = input.MappingPackId ?? connection.MappingPackId;
        return await SaveAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>Stores the private key (PEM, encrypted at rest); only the public-key thumbprint is ever shown.</summary>
    public async Task<Result<SyncConnectionDto>> SetKeyAsync(Guid id, SyncKeyInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        var pem = input.PrivateKeyPem?.Trim();
        var thumbprint = string.IsNullOrEmpty(pem) || pem.Length > 20_000 ? null : SalesforceJwt.Thumbprint(pem);
        if (thumbprint is null)
        {
            return Field("privateKeyPem", "integration.sync.errors.invalidKey");
        }

        connection.PrivateKeyProtected = sessions.Protect(pem!);
        connection.KeyThumbprint = thumbprint;
        connection.LastTestOk = null;
        connection.LastTestDetail = null;
        var saved = await SaveAsync(connection, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.key.store", true, IntegrationPermissions.SystemCode, SyncConnection.EntityType,
            id.ToString(), new { thumbprint }), ct).ConfigureAwait(false);
        return saved;
    }

    /// <summary>
    /// Test connection: token, limits and describe global (read-only calls). A refused <c>/limits</c> (no View Setup and Configuration, as with
    /// the Salesforce Integration licence) is a warning, not a failure: usage then comes from the <c>Sforce-Limit-Info</c> header (ADR 0210).
    /// Failures keep what Salesforce answered (ADR 0211).
    /// </summary>
    public async Task<Result<SyncConnectionDto>> TestAsync(Guid id, CancellationToken ct)
    {
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        string? messageKey = null;
        var detail = new SyncTestDetail();
        try
        {
            var session = sessions.Open(connection);
            await session.AuthenticateAsync(ct).ConfigureAwait(false);
            connection.InstanceUrl = session.InstanceUrl;
            var (limits, refused) = await session.TryLimitsAsync(ct).ConfigureAwait(false);
            if (refused is not null)
            {
                detail.Warnings.Add(new SyncWarningDto("integration.sync.warnings.limitsRefused", refused.ToParams()));
                LogLimitsRefused(logger, id, refused.Status, refused.Call, refused.ErrorCode, refused.Message);
            }

            _ = await session.ListObjectsAsync(ct).ConfigureAwait(false);
            if (session.CurrentLimits(limits) is { } current)
            {
                connection.LastLimits = SyncJson.Write(current);
            }
        }
        catch (SourceConnectorException ex)
        {
            messageKey = ex.MessageKey;
            detail = detail with { Error = SalesforceErrorDto.From(ex.Error) };
            LogTestFailed(logger, id, ex.MessageKey, ex.Error?.Status, ex.Error?.Call, ex.Error?.ErrorCode, ex.Error?.Message ?? ex.Detail);
        }

        connection.LastTestAt = clock.UtcNow;
        connection.LastTestOk = messageKey is null;
        connection.LastTestMessageKey = messageKey;
        connection.LastTestDetail = SyncJson.Write(detail);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.test", messageKey is null, IntegrationPermissions.SystemCode, SyncConnection.EntityType, id.ToString(),
            new { messageKey, status = detail.Error?.Status, errorCode = detail.Error?.ErrorCode, warnings = detail.Warnings.Select(w => w.MessageKey).ToList() }), ct)
            .ConfigureAwait(false);
        return await ToDtoAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>A connector failure of an interactive call (object list, describe) as a rule error carrying what Salesforce answered.</summary>
    private Error ConnectorError(Guid connectionId, SourceConnectorException ex)
    {
        LogTestFailed(logger, connectionId, ex.MessageKey, ex.Error?.Status, ex.Error?.Call, ex.Error?.ErrorCode, ex.Error?.Message ?? ex.Detail);
        return Error.Rule("integration.sync.connector", ex.MessageKey, ex.Params()?.ToDictionary(p => p.Key, p => (object?)p.Value));
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Salesforce call for connection {ConnectionId} failed: {MessageKey} (HTTP {Status}, call {Call}, Salesforce error {ErrorCode}: {SalesforceMessage})")]
    private static partial void LogTestFailed(ILogger logger, Guid connectionId, string messageKey, int? status, string? call, string? errorCode, string? salesforceMessage);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Salesforce refused /limits in the test of {ConnectionId} (HTTP {Status}, call {Call}, Salesforce error {ErrorCode}: {SalesforceMessage}); API usage is read from the Sforce-Limit-Info header")]
    private static partial void LogLimitsRefused(ILogger logger, Guid connectionId, int? status, string call, string? errorCode, string? salesforceMessage);

    /// <summary>Activates (standby) or deactivates the sync. Activation needs a successful test and a mapping pack.</summary>
    public async Task<Result<SyncConnectionDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        var action = active ? SyncModeActions.Activate : SyncModeActions.Deactivate;
        var next = SyncModeLifecycle.Next(connection.Mode, action);
        if (next is null)
        {
            return Error.Rule("integration.sync.modeTransition", "integration.sync.errors.modeTransition", new Dictionary<string, object?> { ["mode"] = connection.Mode });
        }

        if (active && (connection.LastTestOk != true || connection.MappingPackId is null))
        {
            return Error.Rule("integration.sync.notReady", "integration.sync.errors.notReady");
        }

        connection.Mode = next;
        if (active)
        {
            connection.NextRunAt = await NextRunAsync(connection, clock.UtcNow, ct).ConfigureAwait(false);
        }

        var saved = await SaveAsync(connection, ct).ConfigureAwait(false);
        guard.Invalidate();
        await audit.WriteAsync(new AuditEvent(active ? "integration.sync.activate" : "integration.sync.deactivate", true, IntegrationPermissions.SystemCode,
            SyncConnection.EntityType, id.ToString()), ct).ConfigureAwait(false);
        return saved;
    }

    // ---- Schedule ----

    public async Task<Result<SyncScheduleDto>> ScheduleAsync(Guid id, CancellationToken ct)
    {
        var connection = await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        return connection is null ? Error.NotFound() : await ScheduleDtoAsync(connection, ct).ConfigureAwait(false);
    }

    public async Task<Result<SyncScheduleDto>> SaveScheduleAsync(Guid id, SyncScheduleInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        var errors = SyncSchedule.Validate(input.Schedule).Select(e => new FieldError("schedule." + e.Field, e.MessageKey.Split('.')[^1], e.MessageKey)).ToList();
        if (input.LateAfterMinutes is < 15 or > 10_080)
        {
            errors.Add(new FieldError("lateAfterMinutes", "inclusiveBetween", "validation.inclusiveBetween"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(connection).Property(x => x.Version).OriginalValue = input.Version;
        connection.Schedule = SyncJson.Write(input.Schedule with { Days = [.. input.Schedule.Days.Distinct().Order()] });
        connection.LateAfterMinutes = input.LateAfterMinutes;
        connection.NextRunAt = SyncModes.IsStandby(connection.Mode) ? await NextRunAsync(connection, clock.UtcNow, ct).ConfigureAwait(false) : null;
        var saved = await SaveAsync(connection, ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error! : await ScheduleDtoAsync(connection, ct).ConfigureAwait(false);
    }

    private async Task<SyncScheduleDto> ScheduleDtoAsync(SyncConnection connection, CancellationToken ct)
    {
        var schedule = SyncJson.Read<SyncScheduleSettings>(connection.Schedule);
        var zone = await ZoneAsync(ct).ConfigureAwait(false);
        var runs = new List<DateTimeOffset>();
        var at = clock.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            at = SyncSchedule.Next(schedule, at, zone);
            runs.Add(at);
        }

        return new SyncScheduleDto(schedule, connection.LateAfterMinutes, zone.Id, runs, connection.NextRunAt);
    }

    public async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        return TimeZoneInfo.TryFindSystemTimeZoneById(regional.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    public async Task<DateTimeOffset> NextRunAsync(SyncConnection connection, DateTimeOffset after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return SyncSchedule.Next(SyncJson.Read<SyncScheduleSettings>(connection.Schedule), after, await ZoneAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Minutes after the last success when the sync counts as late (default: twice the schedule's interval, at least an hour).</summary>
    public static int LateMinutes(SyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.LateAfterMinutes is { } explicitMinutes)
        {
            return explicitMinutes;
        }

        var schedule = SyncJson.Read<SyncScheduleSettings>(connection.Schedule);
        return schedule.Kind switch
        {
            ScheduleKinds.Interval => Math.Max(60, schedule.EveryHours * 120),
            ScheduleKinds.Daily => 2 * 24 * 60,
            _ => 8 * 24 * 60,
        };
    }

    // ---- Sync now / status / runs ----

    public async Task<Result<SyncConnectionDto>> SyncNowAsync(Guid id, SyncNowInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        if (!SyncModes.IsStandby(connection.Mode))
        {
            return Error.Rule("integration.sync.notStandby", "integration.sync.errors.notStandby");
        }

        connection.RunRequestedAt = clock.UtcNow;
        connection.RunRequestedFull = input.Full;
        connection.RunRequestedBy = context.UserId;
        connection.RunRequestedObjects = input.Objects is { Count: > 0 } objects ? SyncJson.Write(objects) : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent(input.Full ? "integration.sync.fullLoad" : "integration.sync.now", true, IntegrationPermissions.SystemCode,
            SyncConnection.EntityType, id.ToString(), new { objects = input.Objects }), ct).ConfigureAwait(false);
        return await ToDtoAsync(connection, ct).ConfigureAwait(false);
    }

    public async Task<Result<SyncStatusDto>> StatusAsync(Guid id, CancellationToken ct)
    {
        var connection = await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        var runs = db.SyncRuns.AsNoTracking().Where(r => r.ConnectionId == id);
        var last = await runs.Where(r => r.Status != RunStatuses.Running).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var lastSuccess = await runs.Where(r => r.Status == RunStatuses.Succeeded || r.Status == RunStatuses.Partial).OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var running = await runs.AnyAsync(r => r.Status == RunStatuses.Running && r.StartedAt > clock.UtcNow.AddHours(-12), ct).ConfigureAwait(false);
        var exceptions = await db.SyncExceptions.AsNoTracking().Where(e => e.ConnectionId == id && e.Status == ExceptionStatuses.Open)
            .GroupBy(e => e.MappingId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        var mappings = connection.MappingPackId is null ? [] : await db.ObjectMappings.AsNoTracking().Where(m => m.PackId == connection.MappingPackId)
            .OrderBy(m => m.LoadOrder).ThenBy(m => m.Key).ToListAsync(ct).ConfigureAwait(false);
        var lastStats = last is null ? [] : SyncJson.Read<Dictionary<string, ObjectRunStats>>(last.Objects);

        // The latest reconciliation per object (release 1.0.2, fix 6): from the most recent run that full-loaded it.
        var reconciled = new Dictionary<string, (ReconciliationResult Result, DateTimeOffset At)>(StringComparer.Ordinal);
        foreach (var run in await runs.Where(r => r.Status != RunStatuses.Running).OrderByDescending(r => r.StartedAt).Take(50)
                     .Select(r => new { r.Objects, r.StartedAt }).ToListAsync(ct).ConfigureAwait(false))
        {
            foreach (var (key, stats) in SyncJson.Read<Dictionary<string, ObjectRunStats>>(run.Objects))
            {
                if (stats.Reconciliation is { } result)
                {
                    reconciled.TryAdd(key, (result, run.StartedAt));
                }
            }
        }

        var objects = mappings.Select(m => new SyncObjectStatusDto(m.Id, m.Key, m.SourceObject, m.Kind, m.TargetType, m.Enabled, m.ConfirmedAt is not null, m.Provisional,
            m.LoadOrder, m.Watermark, m.LastFullLoadAt, lastStats.GetValueOrDefault(m.Key), exceptions.GetValueOrDefault(m.Id),
            reconciled.TryGetValue(m.Key, out var r) ? r.Result : null, reconciled.TryGetValue(m.Key, out var at) ? at.At : null)).ToList();
        var late = SyncModes.IsStandby(connection.Mode)
            && (lastSuccess?.FinishedAt ?? connection.UpdatedAt) < clock.UtcNow.AddMinutes(-LateMinutes(connection));
        return new SyncStatusDto(await ToDtoAsync(connection, ct).ConfigureAwait(false), last is null ? null : ToDto(last), lastSuccess is null ? null : ToDto(lastSuccess),
            running, late, exceptions.Values.Sum(), objects, await OutageAsync(id, ct).ConfigureAwait(false));
    }

    public async Task<SyncOutageDto?> OutageAsync(Guid connectionId, CancellationToken ct)
    {
        var outage = await db.Outages.AsNoTracking().Where(o => o.ConnectionId == connectionId && o.Status != OutageStatuses.Closed)
            .OrderByDescending(o => o.StartedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return outage is null ? null : await OutageDtoAsync(outage, ct).ConfigureAwait(false);
    }

    public async Task<SyncOutageDto> OutageDtoAsync(SyncOutage outage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outage);
        var records = db.OutageRecords.AsNoTracking().Where(r => r.OutageId == outage.Id);
        return new SyncOutageDto(outage.Id, outage.Status, outage.StartedAt, null, outage.Reason, outage.ReturnedAt, outage.ReportAcknowledgedAt, outage.ClosedAt,
            await records.CountAsync(r => r.ResubmitRequired, ct).ConfigureAwait(false),
            await records.CountAsync(r => r.ChangedLocally, ct).ConfigureAwait(false),
            await records.CountAsync(r => r.ReEntered, ct).ConfigureAwait(false),
            await records.CountAsync(r => r.Conflict && r.Resolution == ConflictResolutions.Pending, ct).ConfigureAwait(false));
    }

    public async Task<Result<PagedResult<SyncRunDto>>> RunsAsync(Guid id, PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = db.SyncRuns.AsNoTracking().Where(r => r.ConnectionId == id);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(r => r.StartedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<SyncRunDto>([.. rows.Select(ToDto)], page.Page, page.PageSize, total);
    }

    public async Task<Result<SyncRunDetailDto>> RunAsync(Guid id, Guid runId, CancellationToken ct)
    {
        var run = await db.SyncRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.ConnectionId == id, ct).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound();
        }

        var log = await db.SyncRunLog.AsNoTracking().Where(l => l.RunId == runId).OrderBy(l => l.At).Take(2000).ToListAsync(ct).ConfigureAwait(false);
        return new SyncRunDetailDto(ToDto(run), [.. log.Select(l => new SyncRunLogDto(l.At, l.Level, l.ObjectKey, l.MessageKey,
            l.Params is null ? null : SyncJson.Read<Dictionary<string, string?>>(l.Params)))]);
    }

    public static SyncRunDto ToDto(SyncRun r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Id, r.Trigger, r.Full, r.Status, r.StartedAt, r.FinishedAt, r.FinishedAt is { } f ? Math.Round((f - r.StartedAt).TotalSeconds, 1) : null,
            SyncJson.Read<Dictionary<string, ObjectRunStats>>(r.Objects), r.ApiCalls, r.MessageKey,
            r.MessageParams is null ? null : SyncJson.Read<Dictionary<string, string?>>(r.MessageParams));
    }

    // ---- Live describe ----

    public async Task<Result<IReadOnlyList<SourceObjectDto>>> SourceObjectsAsync(Guid id, CancellationToken ct)
    {
        var connection = await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        try
        {
            var session = sessions.Open(connection);
            var objects = await session.ListObjectsAsync(ct).ConfigureAwait(false);
            return objects.Where(o => o.Queryable).OrderBy(o => o.Name, StringComparer.Ordinal).Select(o => new SourceObjectDto(o.Name, o.Label, o.Custom)).ToList();
        }
        catch (SourceConnectorException ex)
        {
            return ConnectorError(id, ex);
        }
    }

    public async Task<Result<SourceObjectDescribe>> DescribeAsync(Guid id, string objectName, CancellationToken ct)
    {
        var connection = await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        try
        {
            return await sessions.Open(connection).DescribeAsync(objectName, ct).ConfigureAwait(false);
        }
        catch (SourceConnectorException ex)
        {
            return ConnectorError(id, ex);
        }
    }

    // ---- Targets ----

    public IReadOnlyList<SyncTargetDto> Targets() =>
        [.. targets.All.OrderBy(t => t.SystemCode, StringComparer.Ordinal).ThenBy(t => t.Code, StringComparer.Ordinal).Select(t => new SyncTargetDto(t.Code, t.SystemCode, t.NameAr, t.NameEn,
            [.. t.Fields.Select(f => new SyncTargetFieldDto(f.Code, f.Kind, f.Required, f.LookupType, f.ReferenceType, f.Choices))]))];

    // ---- Mapping packs ----

    public async Task<IReadOnlyList<MappingPackDto>> PacksAsync(CancellationToken ct)
    {
        var packs = await db.MappingPacks.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct).ConfigureAwait(false);
        var counts = await db.ObjectMappings.AsNoTracking().GroupBy(m => m.PackId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        return [.. packs.Select(p => new MappingPackDto(p.Id, p.Code, p.NameAr, p.NameEn, p.PackVersion, p.Description, p.Connector, counts.GetValueOrDefault(p.Id), p.Version))];
    }

    public async Task<Result<IReadOnlyList<ObjectMappingDto>>> MappingsAsync(Guid packId, CancellationToken ct)
    {
        if (!await db.MappingPacks.AnyAsync(p => p.Id == packId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.ObjectMappings.AsNoTracking().Where(m => m.PackId == packId).OrderBy(m => m.LoadOrder).ThenBy(m => m.Key).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<ObjectMappingDto>> MappingAsync(Guid mappingId, CancellationToken ct) =>
        await db.ObjectMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mappingId, ct).ConfigureAwait(false) is { } m ? ToDto(m) : Error.NotFound();

    public static ObjectMappingDto ToDto(SyncObjectMapping m)
    {
        ArgumentNullException.ThrowIfNull(m);
        using var options = JsonDocument.Parse(string.IsNullOrWhiteSpace(m.Options) ? "{}" : m.Options);
        return new(m.Id, m.PackId, m.Key, m.SourceObject, m.Kind, m.TargetType, m.Enabled, m.LoadOrder, m.ConfirmedAt, m.Where, m.WatermarkField, m.ExternalIdTemplate,
            m.UseBulk, SyncJson.Read<List<FieldMapping>>(m.Fields), SyncJson.Read<Dictionary<string, JsonElement>>(m.Defaults), SyncJson.Read<List<string>>(m.Ignore),
            options.RootElement.Clone(), m.NotesAr, m.NotesEn, m.Provisional, m.Watermark, m.Version);
    }

    /// <summary>Edits an object mapping; any change clears the live-describe confirmation (and disables the object until validated again).</summary>
    public async Task<Result<ObjectMappingDto>> UpdateMappingAsync(Guid mappingId, ObjectMappingInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var mapping = await db.ObjectMappings.FirstOrDefaultAsync(m => m.Id == mappingId, ct).ConfigureAwait(false);
        if (mapping is null)
        {
            return Error.NotFound();
        }

        var candidate = new MappingPackObject
        {
            Key = mapping.Key,
            SourceObject = mapping.SourceObject,
            Kind = mapping.Kind,
            TargetType = input.TargetType,
            LoadOrder = input.LoadOrder,
            Where = input.Where,
            WatermarkField = input.WatermarkField ?? mapping.WatermarkField,
            ExternalIdTemplate = input.ExternalIdTemplate ?? mapping.ExternalIdTemplate,
            UseBulk = input.UseBulk,
            Fields = [.. input.Fields ?? []],
            Defaults = input.Defaults ?? [],
            Ignore = [.. input.Ignore ?? []],
            Options = input.Options,
            NotesAr = input.NotesAr,
            NotesEn = input.NotesEn,
        };
        var errors = ValidateStructure(candidate);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(mapping).Property(x => x.Version).OriginalValue = input.Version;
        Apply(mapping, candidate);
        mapping.ConfirmedAt = null;
        mapping.Enabled = false;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(mapping);
    }

    public async Task<Result<ObjectMappingDto>> SetMappingEnabledAsync(Guid mappingId, ObjectMappingEnableInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var mapping = await db.ObjectMappings.FirstOrDefaultAsync(m => m.Id == mappingId, ct).ConfigureAwait(false);
        if (mapping is null)
        {
            return Error.NotFound();
        }

        if (input.Enabled && mapping.ConfirmedAt is null)
        {
            return Error.Rule("integration.sync.notConfirmed", "integration.sync.errors.notConfirmed");
        }

        db.Entry(mapping).Property(x => x.Version).OriginalValue = input.Version;
        mapping.Enabled = input.Enabled;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(mapping);
    }

    /// <summary>Validates an object mapping against the live describe and, when it passes, confirms it (ADR 0203).</summary>
    public async Task<Result<MappingValidationDto>> ValidateMappingAsync(Guid connectionId, Guid mappingId, CancellationToken ct)
    {
        var connection = await db.SyncConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, ct).ConfigureAwait(false);
        var mapping = await db.ObjectMappings.FirstOrDefaultAsync(m => m.Id == mappingId, ct).ConfigureAwait(false);
        if (connection is null || mapping is null)
        {
            return Error.NotFound();
        }

        var issues = new List<MappingIssueDto>();
        try
        {
            var session = sessions.Open(connection);
            var describes = new Dictionary<string, SourceObjectDescribe>(StringComparer.OrdinalIgnoreCase);
            async Task<SourceObjectDescribe> DescribeAsync(string name)
            {
                if (!describes.TryGetValue(name, out var found))
                {
                    found = await session.DescribeAsync(name, ct).ConfigureAwait(false);
                    describes[name] = found;
                }

                return found;
            }

            var describe = await DescribeAsync(mapping.SourceObject).ConfigureAwait(false);

            // A field path on an object; one or more relationship hops (RecordType.DeveloperName, ProcessInstance.ProcessDefinition.Name).
            async Task<SourceFieldInfo?> FieldOnAsync(string objectName, string path)
            {
                var parts = path.Split('.');
                var current = await DescribeAsync(objectName).ConfigureAwait(false);
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var relationship = current.Fields.FirstOrDefault(f => string.Equals(f.RelationshipName, parts[i], StringComparison.OrdinalIgnoreCase));
                    if (relationship is null || relationship.ReferenceTo.Count == 0)
                    {
                        return null;
                    }

                    current = await DescribeAsync(relationship.ReferenceTo[0]).ConfigureAwait(false);
                }

                return current.Field(parts[^1]);
            }

            Task<SourceFieldInfo?> FieldAsync(string path) => FieldOnAsync(mapping.SourceObject, path);

            var fields = SyncJson.Read<List<FieldMapping>>(mapping.Fields);
            var people = mapping.Kind == ObjectMappingKinds.People ? SyncJson.Read<PeopleOptions>(mapping.Options) : null;

            // Every field the run's queries select (system, watermark, external-ID template, people, files, approvals, history; ADR 0212).
            // Mapped fields are checked below with their targets; IsDeleted is only selected when the object has it.
            var queried = SyncQueries.All(mapping, fields, people)
                .Where(q => q.Usage != SyncQueries.UsageMapped && (!q.Optional || q.Usage == SyncQueries.UsagePeople))
                .DistinctBy(q => (q.SourceObject.ToUpperInvariant(), q.Field.ToUpperInvariant()));
            foreach (var q in queried)
            {
                if (await FieldOnAsync(q.SourceObject, q.Field).ConfigureAwait(false) is not null)
                {
                    continue;
                }

                // A missing people field is skipped at run time (matching continues with the other keys): a warning.
                issues.Add(new(q.Usage == SyncQueries.UsagePeople ? "warning" : "error",
                    q.Usage switch { SyncQueries.UsageWatermark => "watermarkField", SyncQueries.UsageExternalId => "externalIdTemplate", SyncQueries.UsagePeople => "options", _ => null },
                    "integration.sync.validation.unknownField",
                    new Dictionary<string, string?> { ["field"] = q.Field, ["object"] = q.SourceObject, ["usage"] = q.Usage }));
            }

            var target = targets.Find(mapping.TargetType);
            if (mapping.Kind == ObjectMappingKinds.Record && target is null)
            {
                issues.Add(new("error", "targetType", "integration.sync.validation.unknownTarget", new Dictionary<string, string?> { ["target"] = mapping.TargetType }));
            }

            foreach (var f in fields)
            {
                var targetField = target?.Field(f.Target);
                if (target is not null && targetField is null)
                {
                    issues.Add(new("error", f.Target, "integration.sync.validation.unknownTargetField", new Dictionary<string, string?> { ["field"] = f.Target }));
                }

                if (f.Kind == FieldMappingKinds.Constant || string.IsNullOrEmpty(f.Source))
                {
                    continue;
                }

                var source = await FieldAsync(f.Source).ConfigureAwait(false);
                if (source is null)
                {
                    issues.Add(new("error", f.Target, "integration.sync.validation.unknownField",
                        new Dictionary<string, string?> { ["field"] = f.Source, ["object"] = mapping.SourceObject, ["usage"] = SyncQueries.UsageMapped }));
                    continue;
                }

                // Picklist → number (ADR 0212): every value the map produces must convert to the target field kind.
                if (targetField is not null && f.Kind is FieldMappingKinds.Picklist or FieldMappingKinds.RecordType && f.Values is { Count: > 0 }
                    && targetField.Kind is SyncFieldKinds.Number or SyncFieldKinds.Ratio or SyncFieldKinds.WholeNumber or SyncFieldKinds.Bool or SyncFieldKinds.Date or SyncFieldKinds.DateTime)
                {
                    var invalid = f.Values.Values.Distinct(StringComparer.Ordinal).Where(v => FieldMapper.Convert(targetField, v).Issue is not null).ToList();
                    if (invalid.Count > 0)
                    {
                        issues.Add(new("error", f.Target, "integration.sync.validation.valueNotConvertible",
                            new Dictionary<string, string?> { ["field"] = f.Source, ["values"] = string.Join(", ", invalid), ["targetKind"] = targetField.Kind }));
                    }
                }

                if (f.Kind is FieldMappingKinds.Picklist && source.PicklistValues.Count > 0)
                {
                    var unmapped = source.PicklistValues.Where(v => v.Active && FieldMapper.MapValue(f, v.Value) is null).Select(v => v.Value).ToList();
                    if (unmapped.Count > 0)
                    {
                        issues.Add(new("warning", f.Target, "integration.sync.validation.unmappedValues",
                            new Dictionary<string, string?> { ["field"] = f.Source, ["values"] = string.Join(", ", unmapped) }));
                    }
                }

                if (targetField is not null && !KindsFit(source.Type, f.Kind, targetField.Kind))
                {
                    issues.Add(new("error", f.Target, "integration.sync.validation.kindMismatch",
                        new Dictionary<string, string?> { ["field"] = f.Source, ["sourceType"] = source.Type, ["targetKind"] = targetField.Kind }));
                }
            }

            if (target is not null)
            {
                foreach (var required in target.Fields.Where(t => t.Required && fields.All(f => f.Target != t.Code) && !SyncJson.Read<Dictionary<string, JsonElement>>(mapping.Defaults).ContainsKey(t.Code)))
                {
                    issues.Add(new("error", required.Code, "integration.sync.validation.requiredNotMapped", new Dictionary<string, string?> { ["field"] = required.Code }));
                }

                var mapped = fields.Select(f => f.Source).Where(s => s is not null).Concat(SyncJson.Read<List<string>>(mapping.Ignore)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unmappedFields = describe.Fields.Where(f => f.Name.EndsWith("__c", StringComparison.Ordinal) && !mapped.Contains(f.Name)).Select(f => f.Name).ToList();
                if (unmappedFields.Count > 0)
                {
                    issues.Add(new("info", null, "integration.sync.validation.unmappedFields",
                        new Dictionary<string, string?> { ["fields"] = string.Join(", ", unmappedFields.Take(40)), ["count"] = unmappedFields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                }
            }
        }
        catch (SourceConnectorException ex)
        {
            issues.Add(new("error", null, ex.MessageKey, ex.Params()));
        }

        var ok = issues.All(i => i.Severity != "error");
        mapping.ConfirmedAt = ok ? clock.UtcNow : null;
        if (!ok)
        {
            mapping.Enabled = false;
        }
        else
        {
            mapping.Provisional = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.mapping.validate", ok, IntegrationPermissions.SystemCode, SyncObjectMapping.EntityType, mapping.Id.ToString(),
            new { mapping.Key, errors = issues.Count(i => i.Severity == "error") }), ct).ConfigureAwait(false);
        return new MappingValidationDto(ok, mapping.ConfirmedAt, issues);
    }

    private static bool KindsFit(string sourceType, string mappingKind, string targetKind) => mappingKind switch
    {
        FieldMappingKinds.Reference or FieldMappingKinds.Person => sourceType is "reference" or "id" or "string",
        FieldMappingKinds.Picklist or FieldMappingKinds.RecordType => true,
        _ => targetKind switch
        {
            SyncFieldKinds.Date => sourceType is "date" or "datetime" or "string",
            SyncFieldKinds.DateTime => sourceType is "datetime" or "date",
            SyncFieldKinds.Number or SyncFieldKinds.Ratio or SyncFieldKinds.WholeNumber => sourceType is "double" or "currency" or "percent" or "int" or "long" or "string",
            SyncFieldKinds.Bool => sourceType is "boolean" or "picklist" or "string",
            _ => true,
        },
    };

    public static List<FieldError> ValidateStructure(MappingPackObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key.Split('.')[^1], key));
        if (!SalesforceSession.ApiName().IsMatch(o.SourceObject))
        {
            Add("sourceObject", "integration.sync.errors.invalidObject");
        }

        if (!ObjectMappingKinds.All.Contains(o.Kind))
        {
            Add("kind", "integration.sync.errors.invalidKind");
        }

        if (!Soql.IsSafeCondition(o.Where))
        {
            Add("where", "integration.sync.errors.where");
        }

        if (o.WatermarkField.Split('.').Any(p => !SalesforceSession.ApiName().IsMatch(p)))
        {
            Add("watermarkField", "integration.sync.errors.invalidField");
        }

        if (!o.ExternalIdTemplate.Contains('{', StringComparison.Ordinal) || o.ExternalIdTemplate.Length > 200)
        {
            Add("externalIdTemplate", "integration.sync.errors.externalIdTemplate");
        }

        for (var i = 0; i < o.Fields.Count; i++)
        {
            var f = o.Fields[i];
            if (!FieldMappingKinds.All.Contains(f.Kind))
            {
                Add($"fields[{i}].kind", "integration.sync.errors.invalidKind");
            }

            if (f.Kind != FieldMappingKinds.Constant && (string.IsNullOrEmpty(f.Source) || f.Source.Split('.').Length > 3 || f.Source.Split('.').Any(p => !SalesforceSession.ApiName().IsMatch(p))))
            {
                Add($"fields[{i}].source", "integration.sync.errors.invalidField");
            }

            if (string.IsNullOrWhiteSpace(f.Target))
            {
                Add($"fields[{i}].target", "validation.required");
            }

            if (!FieldMapper.IsKnownTransform(f.Transform))
            {
                Add($"fields[{i}].transform", "integration.sync.errors.transform");
            }

            if (f.Values is { Count: > 2000 })
            {
                Add($"fields[{i}].values", "validation.maxLength");
            }
        }

        if (o.Kind == ObjectMappingKinds.Record && string.IsNullOrWhiteSpace(o.TargetType))
        {
            Add("targetType", "validation.required");
        }

        return errors;
    }

    private static void Apply(SyncObjectMapping mapping, MappingPackObject o)
    {
        mapping.TargetType = string.IsNullOrWhiteSpace(o.TargetType) ? null : o.TargetType.Trim();
        mapping.LoadOrder = o.LoadOrder;
        mapping.Where = string.IsNullOrWhiteSpace(o.Where) ? null : o.Where.Trim();
        mapping.WatermarkField = o.WatermarkField;
        mapping.ExternalIdTemplate = o.ExternalIdTemplate;
        mapping.UseBulk = o.UseBulk;
        mapping.Fields = SyncJson.Write(o.Fields);
        mapping.Defaults = SyncJson.Write(o.Defaults);
        mapping.Ignore = SyncJson.Write(o.Ignore);
        mapping.Options = o.Options is { ValueKind: JsonValueKind.Object } options ? options.GetRawText() : "{}";
        mapping.NotesAr = o.NotesAr;
        mapping.NotesEn = o.NotesEn;
        mapping.Provisional = o.Provisional;
    }

    /// <summary>
    /// Upgrades an installed shipped pack (ADR 0213): every object of <paramref name="previous"/> (the content the older release shipped for
    /// the objects the new pack changes) is replaced by the new shipped object only while the installed object still equals that content.
    /// Replaced objects lose their confirmation and are disabled until validated again (like an import); objects an administrator changed are
    /// kept and returned, so the guide can tell them to re-import the pack. Sets the pack version.
    /// </summary>
    public async Task<(IReadOnlyList<string> Replaced, IReadOnlyList<string> Kept)> UpgradeShippedPackAsync(
        MappingPackDocument shipped, MappingPackDocument previous, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(previous);
        var pack = await db.MappingPacks.FirstOrDefaultAsync(p => p.Code == shipped.Code, ct).ConfigureAwait(false);
        if (pack is null || pack.PackVersion >= shipped.Version || pack.PackVersion != previous.Version)
        {
            return ([], []);
        }

        var rows = await db.ObjectMappings.Where(m => m.PackId == pack.Id).ToListAsync(ct).ConfigureAwait(false);
        var replaced = new List<string>();
        var kept = new List<string>();
        foreach (var old in previous.Objects)
        {
            var row = rows.FirstOrDefault(m => m.Key == old.Key);
            var next = shipped.Objects.FirstOrDefault(o => o.Key == old.Key);
            if (row is null || next is null)
            {
                continue;
            }

            if (!SameAsShipped(row, old))
            {
                kept.Add(old.Key);
                continue;
            }

            if (row.SourceObject != next.SourceObject)
            {
                row.Watermark = null; // another Salesforce object (ADR 0217): read it from the start
            }

            row.SourceObject = next.SourceObject;
            row.Kind = next.Kind;
            Apply(row, next);
            row.ConfirmedAt = null;
            row.Enabled = false;
            replaced.Add(old.Key);
        }

        // Release 1.0.2 (ADR 0217): objects the new pack adds are inserted (disabled, unconfirmed); shipped objects the new pack drops are
        // disabled while they still equal what was shipped (e.g. objects that used names Salesforce does not have).
        foreach (var added in shipped.Objects.Where(o => rows.All(r => r.Key != o.Key)))
        {
            var row = new SyncObjectMapping { PackId = pack.Id, Key = added.Key, SourceObject = added.SourceObject, Kind = added.Kind };
            Apply(row, added);
            row.Enabled = false;
            db.ObjectMappings.Add(row);
            replaced.Add(added.Key);
        }

        foreach (var dropped in previous.Objects.Where(o => shipped.Objects.All(n => n.Key != o.Key)))
        {
            if (rows.FirstOrDefault(r => r.Key == dropped.Key) is { } row && SameAsShipped(row, dropped))
            {
                row.Enabled = false;
                row.ConfirmedAt = null;
                replaced.Add(dropped.Key);
            }
        }

        pack.PackVersion = shipped.Version;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ProvisionLookupsAsync(shipped, ct).ConfigureAwait(false);
        return (replaced, kept);
    }

    /// <summary>Adds the pack's lookup values that are missing (ADR 0216); returns errors for lookup types this installation does not have.</summary>
    private async Task<IReadOnlyList<FieldError>> ProvisionLookupsAsync(MappingPackDocument document, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        foreach (var (lookup, i) in document.Lookups.Select((l, i) => (l, i)))
        {
            var added = await lookups.AddMissingAsync(lookup.Type, [.. lookup.Values.Select(v => new LookupSeed(v.Code, v.LabelAr ?? v.Code, v.LabelEn ?? v.Code))], ct)
                .ConfigureAwait(false);
            if (added.IsFailure)
            {
                errors.Add(new FieldError($"lookups[{i}].type", "unknownLookupType", "integration.sync.errors.unknownLookupType",
                    new Dictionary<string, object?> { ["type"] = lookup.Type }));
            }
        }

        return errors;
    }

    /// <summary>True when an installed object still has exactly the content a release shipped (JSON compared semantically).</summary>
    private static bool SameAsShipped(SyncObjectMapping row, MappingPackObject shipped)
    {
        static JsonNode? Node(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
        var options = shipped.Options is { ValueKind: JsonValueKind.Object } o ? o.GetRawText() : "{}";
        return row.SourceObject == shipped.SourceObject && row.Kind == shipped.Kind
            && string.Equals(row.TargetType, string.IsNullOrWhiteSpace(shipped.TargetType) ? null : shipped.TargetType.Trim(), StringComparison.Ordinal)
            && string.Equals(row.Where, string.IsNullOrWhiteSpace(shipped.Where) ? null : shipped.Where.Trim(), StringComparison.Ordinal)
            && row.WatermarkField == shipped.WatermarkField && row.ExternalIdTemplate == shipped.ExternalIdTemplate
            && JsonNode.DeepEquals(Node(row.Fields), Node(SyncJson.Write(shipped.Fields)))
            && JsonNode.DeepEquals(Node(row.Defaults), Node(SyncJson.Write(shipped.Defaults)))
            && JsonNode.DeepEquals(Node(row.Options), Node(options));
    }

    public async Task<Result<MappingPackDocument>> ExportPackAsync(Guid packId, CancellationToken ct)
    {
        var pack = await db.MappingPacks.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packId, ct).ConfigureAwait(false);
        if (pack is null)
        {
            return Error.NotFound();
        }

        var rows = await db.ObjectMappings.AsNoTracking().Where(m => m.PackId == packId).OrderBy(m => m.LoadOrder).ThenBy(m => m.Key).ToListAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.pack.export", true, IntegrationPermissions.SystemCode, SyncMappingPack.EntityType, packId.ToString()), ct)
            .ConfigureAwait(false);
        return new MappingPackDocument
        {
            Code = pack.Code,
            Version = pack.PackVersion,
            NameAr = pack.NameAr,
            NameEn = pack.NameEn,
            Description = pack.Description,
            Connector = pack.Connector,
            Objects = [.. rows.Select(m =>
            {
                using var options = JsonDocument.Parse(string.IsNullOrWhiteSpace(m.Options) ? "{}" : m.Options);
                return new MappingPackObject
                {
                    Key = m.Key, SourceObject = m.SourceObject, Kind = m.Kind, TargetType = m.TargetType, LoadOrder = m.LoadOrder, Where = m.Where,
                    WatermarkField = m.WatermarkField, ExternalIdTemplate = m.ExternalIdTemplate, UseBulk = m.UseBulk, Provisional = m.Provisional && m.ConfirmedAt is null,
                    Fields = SyncJson.Read<List<FieldMapping>>(m.Fields), Defaults = SyncJson.Read<Dictionary<string, JsonElement>>(m.Defaults),
                    Ignore = SyncJson.Read<List<string>>(m.Ignore), Options = options.RootElement.Clone(), NotesAr = m.NotesAr, NotesEn = m.NotesEn,
                };
            })],
        };
    }

    /// <summary>Creates or replaces a pack by code. Objects that did not change keep their enabled flag, confirmation and watermark.</summary>
    public async Task<Result<MappingPackDto>> ImportPackAsync(MappingPackDocument document, CancellationToken ct, bool audited = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<FieldError>();
        if (document.Format != MappingPackDocument.FormatName)
        {
            errors.Add(new FieldError("format", "format", "integration.sync.errors.packFormat"));
        }

        if (string.IsNullOrWhiteSpace(document.Code) || document.Code.Length > 100 || document.Objects.Count is 0 or > 500)
        {
            errors.Add(new FieldError("code", "required", "validation.required"));
        }

        foreach (var (o, i) in document.Objects.Select((o, i) => (o, i)))
        {
            errors.AddRange(ValidateStructure(o).Select(e => e with { Field = $"objects[{i}].{e.Field}" }));
        }

        if (document.Objects.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count() != document.Objects.Count || document.Objects.Any(o => string.IsNullOrWhiteSpace(o.Key)))
        {
            errors.Add(new FieldError("objects", "duplicateKey", "integration.sync.errors.duplicateKey"));
        }

        if (document.Lookups.Any(l => string.IsNullOrWhiteSpace(l.Type) || l.Values.Count > 2000 || l.Values.Any(v => string.IsNullOrWhiteSpace(v.Code))))
        {
            errors.Add(new FieldError("lookups", "invalid", "integration.sync.errors.packLookups"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var pack = await db.MappingPacks.FirstOrDefaultAsync(p => p.Code == document.Code, ct).ConfigureAwait(false);
        if (pack is null)
        {
            pack = new SyncMappingPack { Code = document.Code };
            db.MappingPacks.Add(pack);
        }

        pack.NameAr = document.NameAr;
        pack.NameEn = document.NameEn;
        pack.PackVersion = document.Version;
        pack.Description = document.Description;
        pack.Connector = document.Connector;
        var existing = await db.ObjectMappings.Where(m => m.PackId == pack.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var o in document.Objects)
        {
            var row = existing.FirstOrDefault(m => m.Key == o.Key);
            if (row is null)
            {
                row = new SyncObjectMapping { PackId = pack.Id, Key = o.Key, SourceObject = o.SourceObject, Kind = o.Kind };
                db.ObjectMappings.Add(row);
            }

            var before = row.Fields + row.Defaults + row.TargetType + row.Where + row.ExternalIdTemplate + row.WatermarkField + row.SourceObject + row.Kind + row.Options;
            if (row.SourceObject != o.SourceObject)
            {
                row.Watermark = null;
            }

            row.SourceObject = o.SourceObject;
            row.Kind = o.Kind;
            Apply(row, o);
            var after = row.Fields + row.Defaults + row.TargetType + row.Where + row.ExternalIdTemplate + row.WatermarkField + row.SourceObject + row.Kind + row.Options;
            if (before != after)
            {
                row.ConfirmedAt = null;
                row.Enabled = false;
            }
        }

        foreach (var removed in existing.Where(m => document.Objects.All(o => o.Key != m.Key)))
        {
            removed.Enabled = false; // kept (watermarks, exceptions refer to it) but no longer synced
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var lookupErrors = await ProvisionLookupsAsync(document, ct).ConfigureAwait(false);
        if (lookupErrors.Count > 0)
        {
            return Error.Validation(lookupErrors);
        }

        if (audited)
        {
            await audit.WriteAsync(new AuditEvent("integration.sync.pack.import", true, IntegrationPermissions.SystemCode, SyncMappingPack.EntityType, pack.Id.ToString(),
                new { pack.Code, document.Version, objects = document.Objects.Count }), ct).ConfigureAwait(false);
        }

        return (await PacksAsync(ct).ConfigureAwait(false)).First(p => p.Id == pack.Id);
    }

    // ---- Exceptions ----

    public async Task<PagedResult<SyncExceptionDto>> ExceptionsAsync(Guid connectionId, string? status, string? reason, string? mappingKey, string? search, PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        var keys = await db.ObjectMappings.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Key, ct).ConfigureAwait(false);
        var query = db.SyncExceptions.AsNoTracking().Where(e => e.ConnectionId == connectionId && e.Status == (status ?? ExceptionStatuses.Open));
        if (!string.IsNullOrWhiteSpace(reason))
        {
            query = query.Where(e => e.Reason == reason);
        }

        if (!string.IsNullOrWhiteSpace(mappingKey))
        {
            var ids = keys.Where(k => k.Value == mappingKey).Select(k => k.Key).ToList();
            query = query.Where(e => ids.Contains(e.MappingId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            query = query.Where(e => e.ExternalId.StartsWith(text) || (e.Value != null && e.Value.Contains(text)));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(e => e.UpdatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<SyncExceptionDto>([.. rows.Select(e => ToDto(e, keys.GetValueOrDefault(e.MappingId)))], page.Page, page.PageSize, total);
    }

    private static SyncExceptionDto ToDto(SyncException e, string? key) =>
        new(e.Id, e.MappingId, key, e.SourceObject, e.ExternalId, e.Reason, e.Field, e.Value, e.MessageKey, e.Status, e.Attempts, e.CreatedAt, e.UpdatedAt, e.ResolvedAt,
            e.Params is null ? null : SyncJson.Read<Dictionary<string, string?>>(e.Params));

    public async Task<Result<SyncExceptionDetailDto>> ExceptionAsync(Guid id, CancellationToken ct)
    {
        var e = await db.SyncExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (e is null)
        {
            return Error.NotFound();
        }

        var mapping = await db.ObjectMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == e.MappingId, ct).ConfigureAwait(false);
        return new SyncExceptionDetailDto(ToDto(e, mapping?.Key), SourceRecord.FromJson(e.Payload).Values, mapping?.TargetType);
    }

    public async Task<Result<SyncExceptionDto>> ReprocessOneAsync(Guid id, CancellationToken ct)
    {
        var e = await db.SyncExceptions.FirstOrDefaultAsync(x => x.Id == id && x.Status == ExceptionStatuses.Open, ct).ConfigureAwait(false);
        if (e is null)
        {
            return Error.NotFound();
        }

        await engine.ReprocessAsync(e, null, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.exception.reprocess", true, IntegrationPermissions.SystemCode, "integration.sync_exception", id.ToString(),
            new { e.SourceObject, e.ExternalId }), ct).ConfigureAwait(false);
        var reloaded = await db.SyncExceptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
            ?? await db.SyncExceptions.AsNoTracking().FirstAsync(x => x.MappingId == e.MappingId && x.ExternalId == e.ExternalId, ct).ConfigureAwait(false);
        var key = await db.ObjectMappings.AsNoTracking().Where(m => m.Id == e.MappingId).Select(m => m.Key).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return ToDto(reloaded, key);
    }

    private const int ReprocessPage = 1000;

    public async Task<Result<ReprocessResultDto>> ReprocessManyAsync(Guid connectionId, ReprocessInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = db.SyncExceptions.Where(e => e.ConnectionId == connectionId && e.Status == ExceptionStatuses.Open);
        if (!string.IsNullOrWhiteSpace(input.Reason))
        {
            query = query.Where(e => e.Reason == input.Reason);
        }

        if (!string.IsNullOrWhiteSpace(input.MappingKey))
        {
            var ids = await db.ObjectMappings.Where(m => m.Key == input.MappingKey).Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
            query = query.Where(e => ids.Contains(e.MappingId));
        }

        // People mappings first (by load order), then records: a User / Contact matched now lets the records that point at it resolve in the
        // same pass ("Unknown person" bulk re-process). Every open exception of the filter is processed once, page by page.
        var ordered = from e in query
                      join m in db.ObjectMappings on e.MappingId equals m.Id
                      orderby m.Kind == ObjectMappingKinds.People ? 0 : 1, m.LoadOrder, e.CreatedAt, e.Id
                      select e.Id;
        var all = await ordered.ToListAsync(ct).ConfigureAwait(false);

        // Release 1.0.2 (fix 6): passes repeat while a pass resolves something, so dependants whose parent resolved later in the same pass
        // (or whose parent came from another filter) are not left open. Writing a parent also re-processes its waiting dependants (engine).
        var remaining = all;
        while (remaining.Count > 0)
        {
            foreach (var page in remaining.Chunk(ReprocessPage))
            {
                var rows = await db.SyncExceptions.Where(e => page.Contains(e.Id)).ToListAsync(ct).ConfigureAwait(false);
                foreach (var row in page.Select(id => rows.First(r => r.Id == id)))
                {
                    if (row.Status == ExceptionStatuses.Open)
                    {
                        await engine.ReprocessAsync(row, null, ct).ConfigureAwait(false);
                    }
                }

                db.ChangeTracker.Clear();
            }

            var open = await db.SyncExceptions.AsNoTracking().Where(e => remaining.Contains(e.Id) && e.Status == ExceptionStatuses.Open).Select(e => e.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            if (open.Count == remaining.Count)
            {
                break;
            }

            remaining = [.. remaining.Where(open.Contains)];
        }

        var still = await db.SyncExceptions.AsNoTracking().CountAsync(e => all.Contains(e.Id) && e.Status == ExceptionStatuses.Open, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.exception.reprocess", true, IntegrationPermissions.SystemCode, SyncConnection.EntityType, connectionId.ToString(),
            new { input.Reason, input.MappingKey, count = all.Count }), ct).ConfigureAwait(false);
        return new ReprocessResultDto(all.Count, all.Count - still, still);
    }

    public async Task<Result<SyncExceptionDto>> IgnoreAsync(Guid id, CancellationToken ct)
    {
        var e = await db.SyncExceptions.FirstOrDefaultAsync(x => x.Id == id && x.Status == ExceptionStatuses.Open, ct).ConfigureAwait(false);
        if (e is null)
        {
            return Error.NotFound();
        }

        e.Status = ExceptionStatuses.Ignored;
        e.ResolvedAt = clock.UtcNow;
        e.ResolvedBy = context.UserId;
        e.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.exception.ignore", true, IntegrationPermissions.SystemCode, "integration.sync_exception", id.ToString(),
            new { e.SourceObject, e.ExternalId }), ct).ConfigureAwait(false);
        return ToDto(e, null);
    }

    /// <summary>Adds value → code to the field's value map (the fix for <c>unmappedValue</c>) and re-processes the exception.</summary>
    public async Task<Result<SyncExceptionDto>> MapValueAsync(Guid id, MapValueInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var e = await db.SyncExceptions.FirstOrDefaultAsync(x => x.Id == id && x.Status == ExceptionStatuses.Open, ct).ConfigureAwait(false);
        if (e is null)
        {
            return Error.NotFound();
        }

        var mapping = await db.ObjectMappings.FirstAsync(m => m.Id == e.MappingId, ct).ConfigureAwait(false);
        var fields = SyncJson.Read<List<FieldMapping>>(mapping.Fields);
        var index = fields.FindIndex(f => f.Target == e.Field && f.Kind is FieldMappingKinds.Picklist or FieldMappingKinds.RecordType);
        var value = input.Value ?? e.Value;
        if (index < 0 || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100)
        {
            return Field("code", "validation.required");
        }

        var target = targets.Find(mapping.TargetType)?.Field(fields[index].Target);
        if (target?.Kind == SyncFieldKinds.Code && target.Choices is { } choices && !choices.Contains(input.Code))
        {
            return Field("code", "integration.sync.exceptions.invalidValue");
        }

        var values = new Dictionary<string, string>(fields[index].Values ?? [], StringComparer.Ordinal) { [value] = input.Code.Trim() };
        fields[index] = fields[index] with { Values = values };
        mapping.Fields = SyncJson.Write(fields); // a value-map addition keeps the confirmation: the describe was already checked
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.exception.mapValue", true, IntegrationPermissions.SystemCode, SyncObjectMapping.EntityType, mapping.Id.ToString(),
            new { e.Field, value, input.Code }), ct).ConfigureAwait(false);
        return await ReprocessOneAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>Matches a Salesforce User / Contact to an employee by hand (the fix for <c>unknownUser</c>) and re-processes.</summary>
    public async Task<Result<SyncExceptionDto>> MapPersonAsync(Guid id, MapPersonInput input, Organisation.Contracts.IEmployeeDirectory employees, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(employees);
        var e = await db.SyncExceptions.FirstOrDefaultAsync(x => x.Id == id && x.Status == ExceptionStatuses.Open && x.Reason == ExceptionReasons.UnknownUser, ct)
            .ConfigureAwait(false);
        if (e is null)
        {
            return Error.NotFound();
        }

        if (await employees.GetAsync(input.EmployeeId, ct).ConfigureAwait(false) is null)
        {
            return Field("employeeId", "errors.notFound");
        }

        var connection = await db.SyncConnections.AsNoTracking().FirstAsync(c => c.Id == e.ConnectionId, ct).ConfigureAwait(false);
        var mapping = await db.ObjectMappings.AsNoTracking().FirstAsync(m => m.Id == e.MappingId, ct).ConfigureAwait(false);
        // People exceptions carry the person's own Id; record exceptions carry the unknown person's Id as the value.
        var personId = mapping.Kind == ObjectMappingKinds.People ? e.ExternalId : e.Value;
        if (string.IsNullOrWhiteSpace(personId))
        {
            return Field("employeeId", "validation.required");
        }

        var person = await db.SyncPeople.FirstOrDefaultAsync(p => p.SourceSystem == connection.SourceSystem && p.ExternalId == personId, ct).ConfigureAwait(false);
        if (person is null)
        {
            person = new SyncPerson { SourceSystem = connection.SourceSystem, SourceObject = mapping.Kind == ObjectMappingKinds.People ? mapping.SourceObject : "User", ExternalId = personId };
            db.SyncPeople.Add(person);
        }

        person.EmployeeId = input.EmployeeId;
        person.MatchedBy = MatchedBy.Manual;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (mapping.Kind == ObjectMappingKinds.People)
        {
            e.Status = ExceptionStatuses.Resolved;
            e.ResolvedAt = clock.UtcNow;
            e.ResolvedBy = context.UserId;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ToDto(e, mapping.Key);
        }

        return await ReprocessOneAsync(id, ct).ConfigureAwait(false);
    }

    // ---- helpers ----

    private async Task<Result<SyncConnectionDto>> SaveAsync(SyncConnection connection, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ToDtoAsync(connection, ct).ConfigureAwait(false);
    }

    public async Task<SyncConnectionDto> ToDtoAsync(SyncConnection c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        var source = await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Code == c.SourceSystem, ct).ConfigureAwait(false);
        return new SyncConnectionDto(c.Id, c.SourceSystem, source?.NameAr, source?.NameEn, c.Connector, c.Mode, SyncModeLifecycle.ActionsFrom(c.Mode), c.LoginUrl,
            c.ClientId, c.Username, !string.IsNullOrEmpty(c.PrivateKeyProtected), c.KeyThumbprint, c.ApiVersion, c.BulkThreshold, c.PageSize,
            SyncJson.Read<SyncScheduleSettings>(c.Schedule), c.NextRunAt, c.LateAfterMinutes, c.MappingPackId, c.InstanceUrl,
            string.IsNullOrEmpty(c.LastLimits) ? null : JsonSerializer.Deserialize<ApiLimits>(c.LastLimits, SyncJson.Options),
            c.LastTestAt is { } at ? TestDto(at, c) : null, c.RunRequestedAt is not null, c.Version);
    }

    private static SyncTestDto TestDto(DateTimeOffset at, SyncConnection c)
    {
        var detail = SyncJson.Read<SyncTestDetail>(c.LastTestDetail);
        return new SyncTestDto(at, c.LastTestOk == true, c.LastTestMessageKey, detail.Error, detail.Warnings);
    }
}
