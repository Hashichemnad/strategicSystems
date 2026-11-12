using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Sync;

/// <summary>Settings → Integrations → Salesforce sync (installation-wide; ADR 0206).</summary>
public sealed record SyncSettings(
    IReadOnlyList<string>? SuspendedSystems = null,
    int ApiReserveRatioPercent = 10,
    int RunLogRetentionDays = 180) : ISettingsArea
{
    public static string Key => "integration.sync";

    public static readonly IReadOnlyList<string> DefaultSuspendedSystems =
        ["portfolio", "kpi", "risk", "teams", "requests", "docreg", "ip", "bench", "cap", "auditmgmt", "tasks"];

    public IReadOnlyList<string> EffectiveSuspendedSystems => SuspendedSystems ?? DefaultSuspendedSystems;
}

/// <summary>Opens a Salesforce session for a connection (private key decrypted in memory only).</summary>
public sealed class SalesforceSessionFactory(ISalesforceTransport transport, IDataProtectionProvider protection, TimeProvider time)
{
    public const string KeyPurpose = "integration.salesforce.key.v1";

    public string Protect(string pem) => protection.CreateProtector(KeyPurpose).Protect(pem);

    public SalesforceSession Open(SyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.ClientId) || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrEmpty(connection.PrivateKeyProtected))
        {
            throw new SourceConnectorException("integration.sync.errors.notConfigured");
        }

        string pem;
        try
        {
            pem = protection.CreateProtector(KeyPurpose).Unprotect(connection.PrivateKeyProtected);
        }
        catch (CryptographicException ex)
        {
            throw new SourceConnectorException("integration.sync.errors.invalidKey", null, ex);
        }

        return new SalesforceSession(transport, new SalesforceSessionOptions(
            connection.LoginUrl, connection.ClientId, connection.Username, pem, connection.ApiVersion, Math.Clamp(connection.PageSize, 200, 2000)), time);
    }
}

/// <summary>What one record became in a run.</summary>
public enum RecordOutcome
{
    Created,
    Updated,
    Unchanged,
    Deleted,
    Failed,
    Conflict,
    Skipped,
    Deferred,
}

/// <summary>
/// Runs a sync (docs/design/salesforce-sync.md §11, ADR 0200–0205, 0208): per enabled object mapping by load order, Bulk API 2.0 or REST,
/// field mapping, change detection, outage conflict rules, write through the target, external reference; deletions marked, files and source
/// history imported, exceptions recorded. One DI scope per record write (fresh DbContexts, channel <c>sync</c>).
/// </summary>
public sealed partial class SyncEngine(
    IServiceScopeFactory scopes,
    IntegrationDbContext db,
    SalesforceSessionFactory sessions,
    SyncTargetRegistry targets,
    ISettingsStore settings,
    NpgsqlDataSource dataSource,
    IClock clock,
    ILogger<SyncEngine> logger)
{
    private const int InBatch = 150;
    private static readonly TimeSpan WatermarkOverlap = TimeSpan.FromMinutes(5);

    /// <summary>Runs the connection now. Returns null when another run of the connection holds the lock.</summary>
    public async Task<SyncRun?> RunAsync(Guid connectionId, string trigger, bool full, IReadOnlyCollection<string>? objectKeys, Guid? userId, CancellationToken cancellationToken)
    {
        await using var lockConnection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var lockKey = BitConverter.ToInt64(SHA256.HashData(connectionId.ToByteArray()), 0);
        await using (var command = new NpgsqlCommand("select pg_try_advisory_lock(@k)", lockConnection))
        {
            command.Parameters.AddWithValue("k", lockKey);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            {
                return null;
            }
        }

        try
        {
            return await RunLockedAsync(connectionId, trigger, full, objectKeys, userId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("select pg_advisory_unlock(@k)", lockConnection);
            unlock.Parameters.AddWithValue("k", lockKey);
            await unlock.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<SyncRun> RunLockedAsync(Guid connectionId, string trigger, bool full, IReadOnlyCollection<string>? objectKeys, Guid? userId, CancellationToken cancellationToken)
    {
        var connection = await db.SyncConnections.FirstAsync(c => c.Id == connectionId, cancellationToken).ConfigureAwait(false);
        var run = new SyncRun { ConnectionId = connectionId, Trigger = trigger, Full = full, StartedAt = clock.UtcNow, StartedBy = userId };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var stats = new Dictionary<string, ObjectRunStats>(StringComparer.Ordinal);
        var state = new RunState(run, connection, stats);
        SalesforceSession? session = null;
        try
        {
            if (!SyncModes.IsStandby(connection.Mode))
            {
                throw new SourceConnectorException("integration.sync.errors.notStandby");
            }

            session = sessions.Open(connection);
            await session.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
            connection.InstanceUrl = session.InstanceUrl;
            var limits = await session.LimitsAsync(cancellationToken).ConfigureAwait(false);
            connection.LastLimits = SyncJson.Write(limits);
            var options = await settings.GetAsync<SyncSettings>(cancellationToken).ConfigureAwait(false);
            if (limits is { DailyApiMax: > 0, DailyApiRemaining: { } remaining }
                && remaining * 100L < limits.DailyApiMax.Value * (long)Math.Clamp(options.ApiReserveRatioPercent, 0, 90))
            {
                throw new SourceConnectorException("integration.sync.errors.apiLimit");
            }

            var mappings = await db.ObjectMappings.Where(m => m.PackId == connection.MappingPackId && m.Enabled && m.ConfirmedAt != null)
                .OrderBy(m => m.LoadOrder).ThenBy(m => m.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (objectKeys is { Count: > 0 })
            {
                mappings = [.. mappings.Where(m => objectKeys.Contains(m.Key))];
            }

            foreach (var mapping in mappings)
            {
                if (!await StillStandbyAsync(connectionId, cancellationToken).ConfigureAwait(false))
                {
                    run.Status = RunStatuses.Cancelled;
                    await LogAsync(run, "warn", null, "integration.sync.log.cancelled", null, cancellationToken).ConfigureAwait(false);
                    break;
                }

                await RunObjectAsync(state, session, mapping, full, cancellationToken).ConfigureAwait(false);
            }

            if (run.Status != RunStatuses.Cancelled)
            {
                await RetryMissingParentsAsync(state, cancellationToken).ConfigureAwait(false);
                run.Status = stats.Values.Any(s => s.Failed > 0) ? RunStatuses.Partial : RunStatuses.Succeeded;
            }
        }
        catch (SourceConnectorException ex)
        {
            run.Status = RunStatuses.Failed;
            run.MessageKey = ex.MessageKey;
            run.MessageParams = ex.Detail is null ? null : SyncJson.Write(new Dictionary<string, string?> { ["detail"] = ex.Detail });
            await LogAsync(run, "error", null, ex.MessageKey, ex.Detail is null ? null : new() { ["detail"] = ex.Detail }, cancellationToken).ConfigureAwait(false);
            LogRunFailed(logger, connectionId, ex.MessageKey);
        }

        run.ApiCalls = session?.ApiCalls ?? 0;
        run.FinishedAt = clock.UtcNow;
        run.Objects = SyncJson.Write(stats);
        if (run.Status is RunStatuses.Succeeded or RunStatuses.Partial)
        {
            connection.LateAlertSentAt = null;
        }

        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return run;
    }

    private async Task<bool> StillStandbyAsync(Guid connectionId, CancellationToken cancellationToken) =>
        SyncModes.IsStandby(await db.SyncConnections.AsNoTracking().Where(c => c.Id == connectionId).Select(c => c.Mode).FirstAsync(cancellationToken).ConfigureAwait(false));

    // ---- One object ----

    private async Task RunObjectAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, bool full, CancellationToken cancellationToken)
    {
        var stats = state.Stats[mapping.Key] = new ObjectRunStats();
        var since = full || mapping.Watermark is null ? (DateTimeOffset?)null : mapping.Watermark.Value - WatermarkOverlap;
        try
        {
            switch (mapping.Kind)
            {
                case ObjectMappingKinds.Record or ObjectMappingKinds.People:
                    await RunRecordsAsync(state, session, mapping, since, full, stats, cancellationToken).ConfigureAwait(false);
                    break;
                case ObjectMappingKinds.Files:
                    await RunFilesAsync(state, session, mapping, since, stats, cancellationToken).ConfigureAwait(false);
                    break;
                case ObjectMappingKinds.Attachments:
                    await RunAttachmentsAsync(state, session, mapping, since, stats, cancellationToken).ConfigureAwait(false);
                    break;
                case ObjectMappingKinds.Approvals:
                    await RunApprovalsAsync(state, session, mapping, since, stats, cancellationToken).ConfigureAwait(false);
                    break;
                case ObjectMappingKinds.FieldHistory:
                    await RunFieldHistoryAsync(state, session, mapping, since, stats, cancellationToken).ConfigureAwait(false);
                    break;
            }

            stats.Completed = true;
            if (state.MaxWatermark.TryGetValue(mapping.Key, out var max))
            {
                mapping.Watermark = mapping.Watermark is { } current && current > max ? current : max;
            }

            if (full || since is null)
            {
                mapping.LastFullLoadAt = clock.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await LogAsync(state.Run, "info", mapping.Key, "integration.sync.log.objectDone", new()
            {
                ["read"] = stats.Read.ToString(CultureInfo.InvariantCulture), ["created"] = stats.Created.ToString(CultureInfo.InvariantCulture),
                ["updated"] = stats.Updated.ToString(CultureInfo.InvariantCulture), ["deleted"] = stats.Deleted.ToString(CultureInfo.InvariantCulture),
                ["failed"] = stats.Failed.ToString(CultureInfo.InvariantCulture), ["api"] = stats.Api,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceConnectorException ex) when (ex.MessageKey is "integration.sync.errors.http" or "integration.sync.errors.bulkFailed")
        {
            // An object Salesforce refuses (no read access, unknown field) fails alone; the others continue.
            stats.Failed++;
            await LogAsync(state.Run, "error", mapping.Key, ex.MessageKey, ex.Detail is null ? null : new() { ["detail"] = ex.Detail }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Track(RunState state, SyncObjectMapping mapping, SourceRecord record)
    {
        if (record[mapping.WatermarkField] is { } text && FieldMapper.TryParseDateTime(text, out var at)
            && (!state.MaxWatermark.TryGetValue(mapping.Key, out var max) || at > max))
        {
            state.MaxWatermark[mapping.Key] = at;
        }
    }

    private static IAsyncEnumerable<SourceRecord> ReadAsync(SalesforceSession session, SyncObjectMapping mapping, IEnumerable<string> fields, DateTimeOffset? since,
        bool useBulk, ObjectRunStats stats, string? extraCondition, CancellationToken cancellationToken)
    {
        var soql = Soql.Build(mapping.SourceObject, fields, mapping.Where, mapping.WatermarkField, since, extraCondition);
        stats.Api = useBulk ? "bulk" : "rest";
        return useBulk ? session.BulkQueryAsync(soql, cancellationToken) : session.QueryAsync(soql, cancellationToken);
    }

    private static async Task<bool> UseBulkAsync(SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, int threshold, CancellationToken cancellationToken)
    {
        if (!mapping.UseBulk)
        {
            return false;
        }

        if (since is null)
        {
            return true;
        }

        var count = await session.CountAsync(Soql.Count(mapping.SourceObject, mapping.Where, mapping.WatermarkField, since), cancellationToken).ConfigureAwait(false);
        return count > threshold;
    }

    private async Task RunRecordsAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, bool full, ObjectRunStats stats, CancellationToken cancellationToken)
    {
        var fields = SyncJson.Read<List<FieldMapping>>(mapping.Fields);
        var people = mapping.Kind == ObjectMappingKinds.People ? SyncJson.Read<PeopleOptions>(mapping.Options) : null;
        var target = mapping.Kind == ObjectMappingKinds.Record ? targets.Find(mapping.TargetType) : null;
        if (mapping.Kind == ObjectMappingKinds.Record && target is null)
        {
            throw new SourceConnectorException("integration.sync.errors.unknownTarget", mapping.TargetType);
        }

        var useBulk = await UseBulkAsync(session, mapping, since, Math.Max(1, state.Connection.BulkThreshold), cancellationToken).ConfigureAwait(false);
        var selected = Soql.FieldsOf(mapping, fields, people);
        var deferred = new List<SourceRecord>();
        await foreach (var record in ReadAsync(session, mapping, selected, since, useBulk, stats, null, cancellationToken).ConfigureAwait(false))
        {
            stats.Read++;
            Track(state, mapping, record);
            var outcome = people is not null
                ? await ProcessPersonAsync(state, mapping, people, record, cancellationToken).ConfigureAwait(false)
                : await ProcessRecordAsync(state, mapping, target!, fields, record, deferIfSameObjectParent: true, cancellationToken).ConfigureAwait(false);
            if (outcome == RecordOutcome.Deferred)
            {
                deferred.Add(record);
                continue;
            }

            Count(stats, outcome);
        }

        // Same-object parents (department → parent department, work plan → header phase): further passes while a pass makes progress.
        while (deferred.Count > 0)
        {
            var next = new List<SourceRecord>();
            var progress = false;
            foreach (var record in deferred)
            {
                var outcome = await ProcessRecordAsync(state, mapping, target!, fields, record, deferIfSameObjectParent: true, cancellationToken).ConfigureAwait(false);
                if (outcome == RecordOutcome.Deferred)
                {
                    next.Add(record);
                    continue;
                }

                progress = true;
                Count(stats, outcome);
            }

            if (!progress)
            {
                foreach (var record in next)
                {
                    Count(stats, await ProcessRecordAsync(state, mapping, target!, fields, record, deferIfSameObjectParent: false, cancellationToken).ConfigureAwait(false));
                }

                break;
            }

            deferred = next;
        }
    }

    private static void Count(ObjectRunStats stats, RecordOutcome outcome)
    {
        switch (outcome)
        {
            case RecordOutcome.Created: stats.Created++; break;
            case RecordOutcome.Updated: stats.Updated++; break;
            case RecordOutcome.Unchanged: stats.Unchanged++; break;
            case RecordOutcome.Deleted: stats.Deleted++; break;
            case RecordOutcome.Failed: stats.Failed++; break;
            case RecordOutcome.Conflict: stats.Conflicts++; break;
            default: stats.Skipped++; break;
        }
    }

    // ---- One record ----

    /// <summary>Maps and writes one record (also used by re-processing, with the stored payload).</summary>
    public async Task<RecordOutcome> ProcessRecordAsync(RunState state, SyncObjectMapping mapping, SyncTargetDefinition target, IReadOnlyList<FieldMapping> fields,
        SourceRecord record, bool deferIfSameObjectParent, CancellationToken cancellationToken, bool forceApply = false)
    {
        var source = state.Connection.SourceSystem;
        var externalId = FieldMapper.ExternalId(mapping.ExternalIdTemplate, record);
        if (externalId is null)
        {
            return RecordOutcome.Skipped; // e.g. a yearly evaluation of a team without an end date
        }

        var reference = await db.References.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SourceSystem == source && r.EntityTypeCode == target.Code && r.ExternalId == externalId, cancellationToken).ConfigureAwait(false);
        var modified = record[mapping.WatermarkField] is { } m && FieldMapper.TryParseDateTime(m, out var mod) ? mod : (DateTimeOffset?)null;

        if (record.IsDeleted)
        {
            if (reference is null)
            {
                return RecordOutcome.Skipped;
            }

            await LinkInScopeAsync(new ExternalReferenceInput(source, target.Code, externalId, reference.RecordId, SourceModifiedAt: modified,
                SyncStatus: SyncStatuses.DeletedInSource), cancellationToken).ConfigureAwait(false);
            await ResolveExceptionAsync(state.Connection.Id, mapping.Id, externalId, cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Deleted;
        }

        if (!forceApply && reference is not null && reference.SourceModifiedAt is { } synced && modified is { } incoming && incoming < synced)
        {
            await ResolveExceptionAsync(state.Connection.Id, mapping.Id, externalId, cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Unchanged; // an older version (re-processed payload, overlap) never overwrites a newer one
        }

        var defaults = SyncJson.Read<Dictionary<string, JsonElement>>(mapping.Defaults);
        var mapped = await FieldMapper.MapAsync(record, fields, defaults, target, state.Resolver(this, source), cancellationToken).ConfigureAwait(false);
        if (!mapped.Ok)
        {
            if (deferIfSameObjectParent && mapped.Issues.All(i => i.Reason == ExceptionReasons.MissingParent
                    && fields.FirstOrDefault(f => f.Target == i.Field)?.ReferenceType is { } referenceType
                    && referenceType == target.Code))
            {
                return RecordOutcome.Deferred;
            }

            await RecordExceptionAsync(state, mapping, externalId, record, modified, mapped.Issues[0], cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Failed;
        }

        if (!forceApply && reference is not null && reference.SourceHash == mapped.Hash && reference.SyncStatus == SyncStatuses.Synced)
        {
            await ResolveExceptionAsync(state.Connection.Id, mapping.Id, externalId, cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Unchanged;
        }

        if (!forceApply && reference is not null && state.Connection.Mode == SyncModes.Reconciling
            && await HoldForOutageAsync(state.Connection.Id, target.Code, reference.RecordId, mapping.Id, record, modified, cancellationToken).ConfigureAwait(false))
        {
            return RecordOutcome.Conflict;
        }

        await using var scope = CreateSyncScope(state.Run.Id);
        var writer = scope.ServiceProvider.GetRequiredService<EntitySyncWriter>();
        try
        {
            var written = await writer.WriteAsync(target, reference?.RecordId, mapped.Values,
                new ExternalReferenceInput(source, target.Code, externalId, Guid.Empty, IsOrigin: true, SourceModifiedAt: modified, SourceHash: mapped.Hash,
                    SyncStatus: SyncStatuses.Synced), cancellationToken).ConfigureAwait(false);
            if (written.IsFailure)
            {
                var field = written.Error!.Fields is { Count: > 0 } errors ? errors[0] : null;
                await RecordExceptionAsync(state, mapping, externalId, record, modified, new MappingIssue(
                    field?.MessageKey.EndsWith("invalidValue", StringComparison.Ordinal) == true || field?.MessageKey.EndsWith("tooLong", StringComparison.Ordinal) == true
                        ? ExceptionReasons.InvalidValue : field?.MessageKey.EndsWith("requiredMissing", StringComparison.Ordinal) == true ? ExceptionReasons.RequiredMissing : ExceptionReasons.WriteFailed,
                    field?.Field, null, field?.MessageKey ?? written.Error.MessageKey), cancellationToken).ConfigureAwait(false);
                return RecordOutcome.Failed;
            }

            state.Written[(target.Code, externalId)] = written.Value.RecordId;
            await ResolveExceptionAsync(state.Connection.Id, mapping.Id, externalId, cancellationToken).ConfigureAwait(false);
            return written.Value.Created ? RecordOutcome.Created : RecordOutcome.Updated;
        }
        catch (DbUpdateException ex)
        {
            LogWriteFailed(logger, target.Code, externalId, ex);
            await RecordExceptionAsync(state, mapping, externalId, record, modified,
                new MappingIssue(ExceptionReasons.WriteFailed, null, null, "integration.sync.exceptions.writeFailed"), cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Failed;
        }
    }

    /// <summary>Reconciling: a record changed here during the outage keeps its local version until the admin decides (ADR 0207).</summary>
    private async Task<bool> HoldForOutageAsync(Guid connectionId, string entityType, Guid recordId, Guid mappingId, SourceRecord record, DateTimeOffset? modified, CancellationToken cancellationToken)
    {
        var outage = await db.Outages.Where(o => o.ConnectionId == connectionId && o.Status == OutageStatuses.Reconciling).Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (outage is null)
        {
            return false;
        }

        var held = await db.OutageRecords.FirstOrDefaultAsync(r => r.OutageId == outage && r.EntityTypeCode == entityType && r.RecordId == recordId && r.ChangedLocally,
            cancellationToken).ConfigureAwait(false);
        if (held is null || held.Resolution == ConflictResolutions.TakeSource)
        {
            return false;
        }

        await using var scope = CreateSyncScope(null);
        var scoped = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var row = await scoped.OutageRecords.FirstAsync(r => r.Id == held.Id, cancellationToken).ConfigureAwait(false);
        row.Conflict = true;
        row.PendingPayload = record.ToJson();
        row.PendingMappingId = mappingId;
        row.PendingSourceModifiedAt = modified;
        await scoped.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ---- People (ADR 0204) ----

    private async Task<RecordOutcome> ProcessPersonAsync(RunState state, SyncObjectMapping mapping, PeopleOptions options, SourceRecord record, CancellationToken cancellationToken)
    {
        var source = state.Connection.SourceSystem;
        var person = await db.SyncPeople.FirstOrDefaultAsync(p => p.SourceSystem == source && p.ExternalId == record.Id, cancellationToken).ConfigureAwait(false);
        if (record.IsDeleted)
        {
            return person is null ? RecordOutcome.Skipped : RecordOutcome.Deleted; // the match stays: history and lookups keep resolving
        }

        var email = options.EmailField is null ? null : record[options.EmailField]?.Trim();
        var name = options.NameField is null ? null : record[options.NameField];
        if (person is { EmployeeId: not null, MatchedBy: MatchedBy.Manual })
        {
            return RecordOutcome.Unchanged;
        }

        await using var scope = CreateSyncScope(state.Run.Id);
        var directory = scope.ServiceProvider.GetRequiredService<IEmployeeMatcher>();
        (Guid Id, string By)? match = null;
        foreach (var key in options.MatchOrder)
        {
            var value = key switch
            {
                MatchedBy.Email => email,
                MatchedBy.EmployeeNo => options.EmployeeNoField is null ? null : record[options.EmployeeNoField],
                MatchedBy.FederationId => options.FederationIdField is null ? null : record[options.FederationIdField],
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value) && await directory.FindAsync(key, value.Trim(), cancellationToken).ConfigureAwait(false) is { } id)
            {
                match = (id, key);
                break;
            }
        }

        if (person is null)
        {
            person = new SyncPerson { SourceSystem = source, SourceObject = mapping.SourceObject, ExternalId = record.Id };
            db.SyncPeople.Add(person);
        }

        var before = person.EmployeeId;
        person.Email = email;
        person.Name = name;
        person.EmployeeId = match?.Id;
        person.MatchedBy = match?.By;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        state.People[record.Id] = match?.Id;
        if (match is null)
        {
            await RecordExceptionAsync(state, mapping, record.Id, record, null,
                new MappingIssue(ExceptionReasons.UnknownUser, options.EmailField, email ?? name, "integration.sync.exceptions.unmatchedPerson"), cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Failed;
        }

        if (mapping.SourceObject == "User")
        {
            await LinkInScopeAsync(new ExternalReferenceInput(source, "org.employee", record.Id, match.Value.Id, SyncStatus: SyncStatuses.Synced), cancellationToken).ConfigureAwait(false);
        }

        await ResolveExceptionAsync(state.Connection.Id, mapping.Id, record.Id, cancellationToken).ConfigureAwait(false);
        return before is null ? RecordOutcome.Created : before == match.Value.Id ? RecordOutcome.Unchanged : RecordOutcome.Updated;
    }

    // ---- Files (ADR 0208) ----

    private async Task RunFilesAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, ObjectRunStats stats, CancellationToken cancellationToken)
    {
        // ContentVersion changed since the watermark (new files and new versions), then their links to synced records.
        var versions = new List<SourceRecord>();
        var versionMapping = new SyncObjectMapping { SourceObject = "ContentVersion", WatermarkField = mapping.WatermarkField, Where = "IsLatest = true", Key = mapping.Key };
        await foreach (var version in ReadAsync(session, versionMapping, ["Id", "ContentDocumentId", "Title", "PathOnClient", "FileExtension", mapping.WatermarkField], since, false, stats, null, cancellationToken)
            .ConfigureAwait(false))
        {
            versions.Add(version);
            Track(state, mapping, version);
        }

        foreach (var batch in versions.Chunk(InBatch))
        {
            var ids = string.Join(',', batch.Select(v => Soql.Quote(v["ContentDocumentId"] ?? string.Empty)));
            var links = new List<SourceRecord>();
            await foreach (var link in session.QueryAsync($"SELECT Id, ContentDocumentId, LinkedEntityId FROM ContentDocumentLink WHERE ContentDocumentId IN ({ids})", cancellationToken)
                .ConfigureAwait(false))
            {
                links.Add(link);
            }

            foreach (var version in batch)
            {
                foreach (var link in links.Where(l => l["ContentDocumentId"] == version["ContentDocumentId"]))
                {
                    stats.Read++;
                    var name = version["PathOnClient"] ?? (version["Title"] + (version["FileExtension"] is { } ext ? "." + ext : string.Empty));
                    Count(stats, await ImportFileAsync(state, mapping, link["LinkedEntityId"], $"{version["ContentDocumentId"]}:{link["LinkedEntityId"]}",
                        version.Id, name, () => session.DownloadAsync("ContentVersion", version.Id, "VersionData", cancellationToken), version, cancellationToken).ConfigureAwait(false));
                }
            }
        }
    }

    private async Task RunAttachmentsAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, ObjectRunStats stats, CancellationToken cancellationToken)
    {
        await foreach (var attachment in ReadAsync(session, mapping, ["Id", "ParentId", "Name", "ContentType", "IsDeleted", mapping.WatermarkField], since, false, stats, null, cancellationToken)
            .ConfigureAwait(false))
        {
            stats.Read++;
            Track(state, mapping, attachment);
            if (attachment.IsDeleted)
            {
                stats.Skipped++;
                continue;
            }

            Count(stats, await ImportFileAsync(state, mapping, attachment["ParentId"], attachment.Id, attachment[mapping.WatermarkField] ?? attachment.Id,
                attachment["Name"] ?? attachment.Id, () => session.DownloadAsync("Attachment", attachment.Id, "Body", cancellationToken), attachment, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<RecordOutcome> ImportFileAsync(RunState state, SyncObjectMapping mapping, string? parentExternalId, string externalId,
        string versionKey, string fileName, Func<Task<byte[]>> download, SourceRecord payload, CancellationToken cancellationToken)
    {
        var source = state.Connection.SourceSystem;
        var owner = string.IsNullOrEmpty(parentExternalId) ? null : await db.References.AsNoTracking()
            .Where(r => r.SourceSystem == source && r.ExternalId == parentExternalId && r.IsOrigin).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return RecordOutcome.Skipped; // linked to a record that is not synced (user, library…)
        }

        var existing = await db.References.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SourceSystem == source && r.EntityTypeCode == FileReferenceType && r.ExternalId == externalId, cancellationToken).ConfigureAwait(false);
        if (existing?.SourceHash == versionKey)
        {
            return RecordOutcome.Unchanged;
        }

        await using var scope = CreateSyncScope(state.Run.Id);
        var purpose = scope.ServiceProvider.GetServices<IFileOwnerAccess>().FirstOrDefault(a => a.OwnerType == owner.EntityTypeCode)?.AttachmentPurpose;
        if (purpose is null)
        {
            await RecordExceptionAsync(state, mapping, externalId, payload, null,
                new MappingIssue(ExceptionReasons.FileRejected, null, owner.EntityTypeCode, "integration.sync.exceptions.noAttachments"), cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Failed;
        }

        var bytes = await download().ConfigureAwait(false);
        await using var content = new MemoryStream(bytes);
        var files = scope.ServiceProvider.GetRequiredService<IFileService>();
        var uploaded = await files.UploadAsync(new FileUpload(purpose, owner.RecordId.ToString("D"), fileName, content,
            ReplacesFileId: existing?.RecordId), cancellationToken).ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            await RecordExceptionAsync(state, mapping, externalId, payload, null,
                new MappingIssue(ExceptionReasons.FileRejected, null, fileName, uploaded.Error!.MessageKey), cancellationToken).ConfigureAwait(false);
            return RecordOutcome.Failed;
        }

        await LinkInScopeAsync(new ExternalReferenceInput(source, FileReferenceType, externalId, uploaded.Value.Id, IsOrigin: true, SourceHash: versionKey,
            SyncStatus: SyncStatuses.Synced), cancellationToken).ConfigureAwait(false);
        await ResolveExceptionAsync(state.Connection.Id, mapping.Id, externalId, cancellationToken).ConfigureAwait(false);
        return existing is null ? RecordOutcome.Created : RecordOutcome.Updated;
    }

    public const string FileReferenceType = "files.file";

    // ---- Approval and field history (ADR 0208) ----

    private async Task RunApprovalsAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, ObjectRunStats stats, CancellationToken cancellationToken)
    {
        var instances = new SyncObjectMapping { SourceObject = "ProcessInstance", WatermarkField = "SystemModstamp", Key = mapping.Key };
        var instanceStatus = new Dictionary<string, (string? Status, string? Process, string Target)>(StringComparer.Ordinal);
        await foreach (var instance in ReadAsync(session, instances, ["Id", "TargetObjectId", "Status", "ProcessDefinition.Name", "CreatedDate", "SystemModstamp"], since, false, stats, null, cancellationToken)
            .ConfigureAwait(false))
        {
            stats.Read++;
            Track(state, mapping, instance);
            instanceStatus[instance.Id] = (instance["Status"], instance["ProcessDefinition.Name"], instance["TargetObjectId"] ?? string.Empty);
            var outcome = await UpsertHistoryAsync(state, instance["TargetObjectId"], SourceHistoryKinds.Approval, "pi:" + instance.Id, instance["CreatedDate"], e =>
            {
                e.ProcessName = instance["ProcessDefinition.Name"];
                e.ProcessInstanceId = instance.Id;
                e.ProcessStatus = instance["Status"];
            }, cancellationToken).ConfigureAwait(false);
            Count(stats, outcome);
        }

        var steps = new SyncObjectMapping { SourceObject = "ProcessInstanceStep", WatermarkField = "CreatedDate", Key = mapping.Key };
        await foreach (var step in ReadAsync(session, steps,
            ["Id", "ProcessInstanceId", "ProcessInstance.TargetObjectId", "ProcessInstance.Status", "ProcessInstance.ProcessDefinition.Name", "StepStatus", "Actor.Name", "Comments", "CreatedDate"],
            since, false, stats, null, cancellationToken).ConfigureAwait(false))
        {
            stats.Read++;
            Count(stats, await UpsertHistoryAsync(state, step["ProcessInstance.TargetObjectId"], SourceHistoryKinds.Approval, step.Id, step["CreatedDate"], e =>
            {
                e.ProcessInstanceId = step["ProcessInstanceId"];
                e.ProcessName = step["ProcessInstance.ProcessDefinition.Name"];
                e.ProcessStatus = step["ProcessInstance.Status"];
                e.StepStatus = step["StepStatus"];
                e.ActorName = step["Actor.Name"];
                e.Comments = Truncate(step["Comments"], 4000);
            }, cancellationToken).ConfigureAwait(false));
        }

        // Keep the instance status of earlier steps current (Pending → Approved).
        foreach (var (instanceId, info) in instanceStatus)
        {
            await db.SourceHistory.Where(h => h.SourceSystem == state.Connection.SourceSystem && h.ProcessInstanceId == instanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(h => h.ProcessStatus, info.Status), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunFieldHistoryAsync(RunState state, SalesforceSession session, SyncObjectMapping mapping, DateTimeOffset? since, ObjectRunStats stats, CancellationToken cancellationToken)
    {
        await foreach (var row in ReadAsync(session, mapping, ["Id", "ParentId", "Field", "OldValue", "NewValue", "CreatedDate", "CreatedBy.Name"], since, false, stats, null, cancellationToken)
            .ConfigureAwait(false))
        {
            stats.Read++;
            Track(state, mapping, row);
            Count(stats, await UpsertHistoryAsync(state, row["ParentId"], SourceHistoryKinds.Field, row.Id, row["CreatedDate"], e =>
            {
                e.Field = row["Field"];
                e.OldValue = Truncate(row["OldValue"], 4000);
                e.NewValue = Truncate(row["NewValue"], 4000);
                e.ActorName = row["CreatedBy.Name"];
            }, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<RecordOutcome> UpsertHistoryAsync(RunState state, string? recordExternalId, string kind, string externalId, string? at, Action<SourceHistoryEntry> fill, CancellationToken cancellationToken)
    {
        var source = state.Connection.SourceSystem;
        if (string.IsNullOrEmpty(recordExternalId))
        {
            return RecordOutcome.Skipped;
        }

        var owner = await db.References.AsNoTracking().Where(r => r.SourceSystem == source && r.ExternalId == recordExternalId && r.IsOrigin)
            .Select(r => new { r.EntityTypeCode, r.RecordId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return RecordOutcome.Skipped;
        }

        var entry = await db.SourceHistory.FirstOrDefaultAsync(h => h.SourceSystem == source && h.Kind == kind && h.ExternalId == externalId, cancellationToken).ConfigureAwait(false);
        var created = entry is null;
        if (entry is null)
        {
            entry = new SourceHistoryEntry { SourceSystem = source, Kind = kind, ExternalId = externalId, EntityType = owner.EntityTypeCode, RecordId = owner.RecordId, RecordExternalId = recordExternalId };
            db.SourceHistory.Add(entry);
        }

        entry.OccurredAt = at is not null && FieldMapper.TryParseDateTime(at, out var when) ? when : clock.UtcNow;
        fill(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created ? RecordOutcome.Created : RecordOutcome.Updated;
    }

    private static string? Truncate(string? text, int max) => text is { Length: > 0 } && text.Length > max ? text[..max] : text;

    // ---- Exceptions (ADR 0205) ----

    private async Task RecordExceptionAsync(RunState state, SyncObjectMapping mapping, string externalId, SourceRecord record, DateTimeOffset? modified, MappingIssue issue, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var row = await db.SyncExceptions.FirstOrDefaultAsync(e => e.ConnectionId == state.Connection.Id && e.MappingId == mapping.Id && e.ExternalId == externalId
            && e.Status == ExceptionStatuses.Open, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new SyncException { ConnectionId = state.Connection.Id, MappingId = mapping.Id, SourceObject = mapping.SourceObject, ExternalId = externalId, CreatedAt = now, Attempts = 0 };
            db.SyncExceptions.Add(row);
        }

        row.RunId = state.Run.Id;
        row.Reason = issue.Reason;
        row.Field = issue.Field;
        row.Value = Truncate(issue.Value, 1000);
        row.MessageKey = issue.MessageKey;
        row.Payload = record.ToJson();
        row.SourceModifiedAt = modified;
        row.Attempts++;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveExceptionAsync(Guid connectionId, Guid mappingId, string externalId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await db.SyncExceptions.Where(e => e.ConnectionId == connectionId && e.MappingId == mappingId && e.ExternalId == externalId && e.Status == ExceptionStatuses.Open)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, ExceptionStatuses.Resolved).SetProperty(e => e.ResolvedAt, now).SetProperty(e => e.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Re-processes one open exception from its stored payload with the current mapping (no source call).</summary>
    public async Task<RecordOutcome> ReprocessAsync(SyncException exception, Guid? runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var connection = await db.SyncConnections.FirstAsync(c => c.Id == exception.ConnectionId, cancellationToken).ConfigureAwait(false);
        var mapping = await db.ObjectMappings.FirstAsync(m => m.Id == exception.MappingId, cancellationToken).ConfigureAwait(false);
        var run = runId is { } id ? await db.SyncRuns.FirstAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
            : new SyncRun { ConnectionId = connection.Id, Trigger = RunTriggers.Reprocess, StartedAt = clock.UtcNow };
        var state = new RunState(run, connection, []);
        var record = SourceRecord.FromJson(exception.Payload);
        if (mapping.Kind == ObjectMappingKinds.People)
        {
            return await ProcessPersonAsync(state, mapping, SyncJson.Read<PeopleOptions>(mapping.Options), record, cancellationToken).ConfigureAwait(false);
        }

        var target = targets.Find(mapping.TargetType);
        if (target is null || mapping.Kind != ObjectMappingKinds.Record)
        {
            return RecordOutcome.Skipped;
        }

        return await ProcessRecordAsync(state, mapping, target, SyncJson.Read<List<FieldMapping>>(mapping.Fields), record, deferIfSameObjectParent: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Applies a held Salesforce version of an outage record ("Take Salesforce").</summary>
    public async Task<RecordOutcome> ApplyPendingAsync(SyncConnection connection, SyncOutageRecord held, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(held);
        if (held.PendingPayload is null || held.PendingMappingId is null)
        {
            return RecordOutcome.Skipped;
        }

        var mapping = await db.ObjectMappings.FirstAsync(m => m.Id == held.PendingMappingId, cancellationToken).ConfigureAwait(false);
        var target = targets.Find(mapping.TargetType) ?? throw new InvalidOperationException("Unknown target.");
        var state = new RunState(new SyncRun { ConnectionId = connection.Id, Trigger = RunTriggers.Reprocess, StartedAt = clock.UtcNow }, connection, []);
        return await ProcessRecordAsync(state, mapping, target, SyncJson.Read<List<FieldMapping>>(mapping.Fields), SourceRecord.FromJson(held.PendingPayload),
            deferIfSameObjectParent: false, cancellationToken, forceApply: true).ConfigureAwait(false);
    }

    private async Task RetryMissingParentsAsync(RunState state, CancellationToken cancellationToken)
    {
        var open = await db.SyncExceptions.Where(e => e.ConnectionId == state.Connection.Id && e.Status == ExceptionStatuses.Open && e.Reason == ExceptionReasons.MissingParent)
            .OrderBy(e => e.CreatedAt).Take(1000).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var exception in open)
        {
            var mapping = await db.ObjectMappings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == exception.MappingId, cancellationToken).ConfigureAwait(false);
            if (mapping is null || !state.Stats.TryGetValue(mapping.Key, out var stats))
            {
                continue;
            }

            var outcome = await ReprocessAsync(exception, state.Run.Id, cancellationToken).ConfigureAwait(false);
            if (outcome is RecordOutcome.Created or RecordOutcome.Updated)
            {
                stats.Failed = Math.Max(0, stats.Failed - 1);
                Count(stats, outcome);
            }
        }
    }

    // ---- Helpers ----

    private AsyncServiceScope CreateSyncScope(Guid? runId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise((runId ?? Guid.CreateVersion7()).ToString("N"), Channels.Sync, StrategyClaims.CreateSystemPrincipal());
        return scope;
    }

    private async Task LinkInScopeAsync(ExternalReferenceInput input, CancellationToken cancellationToken)
    {
        await using var scope = CreateSyncScope(null);
        await scope.ServiceProvider.GetRequiredService<IExternalReferenceStore>().LinkAsync(input, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task LogAsync(SyncRun run, string level, string? objectKey, string messageKey, Dictionary<string, string?>? parameters, CancellationToken cancellationToken)
    {
        db.SyncRunLog.Add(new SyncRunLog
        {
            RunId = run.Id, At = clock.UtcNow, Level = level, ObjectKey = objectKey, MessageKey = messageKey,
            Params = parameters is null ? null : SyncJson.Write(parameters),
        });
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _ = cancellationToken;
    }

    internal async Task<Guid?> ReferenceAsync(string source, string targetType, string externalId, CancellationToken cancellationToken) =>
        await db.References.AsNoTracking().Where(r => r.SourceSystem == source && r.EntityTypeCode == targetType && r.ExternalId == externalId)
            .Select(r => (Guid?)r.RecordId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    internal async Task<Guid?> PersonAsync(string source, string externalId, CancellationToken cancellationToken) =>
        await db.SyncPeople.AsNoTracking().Where(p => p.SourceSystem == source && p.ExternalId == externalId)
            .Select(p => p.EmployeeId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    internal async Task<bool> IsActiveLookupCodeAsync(string type, string code, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILookupService>().IsActiveCodeAsync(type, code, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync run of connection {ConnectionId} failed: {MessageKey}")]
    private static partial void LogRunFailed(ILogger logger, Guid connectionId, string messageKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync write of {TargetType} {ExternalId} failed")]
    private static partial void LogWriteFailed(ILogger logger, string targetType, string externalId, Exception exception);

    /// <summary>State of one run: counters, watermarks, people / reference caches.</summary>
    public sealed class RunState(SyncRun run, SyncConnection connection, Dictionary<string, ObjectRunStats> stats)
    {
        public SyncRun Run { get; } = run;

        public SyncConnection Connection { get; } = connection;

        public Dictionary<string, ObjectRunStats> Stats { get; } = stats;

        public Dictionary<string, DateTimeOffset> MaxWatermark { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Type, string ExternalId), Guid> Written { get; } = [];

        public Dictionary<string, Guid?> People { get; } = new(StringComparer.Ordinal);

        public Dictionary<(string Type, string Code), bool> Lookups { get; } = [];

        internal IMappingResolver Resolver(SyncEngine engine, string source) => new Resolver(engine, this, source);
    }

    private sealed class Resolver(SyncEngine engine, RunState state, string source) : IMappingResolver
    {
        public async Task<Guid?> ReferenceAsync(string targetType, string externalId, CancellationToken cancellationToken) =>
            state.Written.TryGetValue((targetType, externalId), out var id) ? id : await engine.ReferenceAsync(source, targetType, externalId, cancellationToken).ConfigureAwait(false);

        public async Task<Guid?> PersonAsync(string externalId, CancellationToken cancellationToken)
        {
            if (!state.People.TryGetValue(externalId, out var id))
            {
                id = await engine.PersonAsync(source, externalId, cancellationToken).ConfigureAwait(false);
                state.People[externalId] = id;
            }

            return id;
        }

        public async Task<bool> IsActiveLookupCodeAsync(string lookupType, string code, CancellationToken cancellationToken)
        {
            if (!state.Lookups.TryGetValue((lookupType, code), out var active))
            {
                active = await engine.IsActiveLookupCodeAsync(lookupType, code, cancellationToken).ConfigureAwait(false);
                state.Lookups[(lookupType, code)] = active;
            }

            return active;
        }
    }
}

/// <summary>Builds the SOQL the connector sends (fields, filter, watermark, order). Names are checked against the API-name pattern.</summary>
public static class Soql
{
    public static string Build(string objectName, IEnumerable<string> fields, string? where, string watermarkField, DateTimeOffset? since, string? extraCondition = null)
    {
        Check(objectName);
        var list = fields.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        list.ForEach(CheckPath);
        CheckPath(watermarkField);
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(where))
        {
            conditions.Add("(" + where + ")");
        }

        if (since is { } at)
        {
            conditions.Add($"{watermarkField} >= {Literal(at)}");
        }

        if (!string.IsNullOrWhiteSpace(extraCondition))
        {
            conditions.Add(extraCondition);
        }

        var sb = new StringBuilder("SELECT ").Append(string.Join(", ", list)).Append(" FROM ").Append(objectName);
        if (conditions.Count > 0)
        {
            sb.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }

        return sb.Append(" ORDER BY ").Append(watermarkField).Append(" ASC").ToString();
    }

    public static string Count(string objectName, string? where, string watermarkField, DateTimeOffset? since)
    {
        Check(objectName);
        CheckPath(watermarkField);
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(where))
        {
            conditions.Add("(" + where + ")");
        }

        if (since is { } at)
        {
            conditions.Add($"{watermarkField} >= {Literal(at)}");
        }

        return $"SELECT COUNT() FROM {objectName}" + (conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty);
    }

    /// <summary>The source fields a mapping needs: Id, IsDeleted, the watermark, mapped fields, template fields and people fields.</summary>
    public static IReadOnlyList<string> FieldsOf(SyncObjectMapping mapping, IReadOnlyList<FieldMapping> fields, PeopleOptions? people)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var list = new List<string> { "Id", "IsDeleted", mapping.WatermarkField };
        list.AddRange(fields.Where(f => f.Kind != FieldMappingKinds.Constant && !string.IsNullOrEmpty(f.Source)).Select(f => f.Source!));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(mapping.ExternalIdTemplate, "\\{(?:year:)?([A-Za-z0-9_.]+)\\}"))
        {
            list.Add(m.Groups[1].Value);
        }

        if (people is not null)
        {
            list.AddRange(new[] { people.EmailField, people.EmployeeNoField, people.FederationIdField, people.NameField }.Where(f => f is not null)!);
        }

        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Allowed WHERE text of a mapping: no statement separators, comments or sub-selects.</summary>
    public static bool IsSafeCondition(string? where) =>
        string.IsNullOrWhiteSpace(where)
        || (where.Length <= 1000 && !where.Contains(';', StringComparison.Ordinal) && !where.Contains("--", StringComparison.Ordinal)
            && !where.Contains("/*", StringComparison.Ordinal) && !where.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
            && where.Count(c => c == '\'') % 2 == 0 && where.Count(c => c == '(') == where.Count(c => c == ')'));

    public static string Literal(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string Quote(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    private static void Check(string name)
    {
        if (!SalesforceSession.ApiName().IsMatch(name))
        {
            throw new SourceConnectorException("integration.sync.errors.invalidObject", name);
        }
    }

    private static void CheckPath(string path)
    {
        if (path.Split('.').Any(p => !SalesforceSession.ApiName().IsMatch(p)))
        {
            throw new SourceConnectorException("integration.sync.errors.invalidField", path);
        }
    }
}
