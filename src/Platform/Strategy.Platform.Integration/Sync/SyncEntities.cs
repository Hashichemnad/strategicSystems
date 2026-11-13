using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Integration.Sync;

/// <summary>Connection modes (ADR 0207, <see cref="SyncModeLifecycle"/>).</summary>
public static class SyncModes
{
    public const string Disabled = "disabled";
    public const string Standby = "standby";
    public const string Primary = "primary";
    public const string Reconciling = "reconciling";

    public static readonly IReadOnlyList<string> All = [Disabled, Standby, Primary, Reconciling];

    /// <summary>Synced records are read-only and the sync runs.</summary>
    public static bool IsStandby(string mode) => mode is Standby or Reconciling;
}

public static class Connectors
{
    public const string Salesforce = "salesforce";
}

/// <summary>A connection to a source system (one per source; ADR 0200 / 0201). The private key is write-only.</summary>
[Audited(EntityType)]
public sealed class SyncConnection : AuditableEntity
{
    public const string EntityType = "integration.sync_connection";

    public string SourceSystem { get; set; } = string.Empty;

    public string Connector { get; set; } = Connectors.Salesforce;

    public string Mode { get; set; } = SyncModes.Disabled;

    public string LoginUrl { get; set; } = "https://login.salesforce.com";

    public string? ClientId { get; set; }

    public string? Username { get; set; }

    [AuditMasked]
    public string? PrivateKeyProtected { get; set; }

    /// <summary>SHA-256 of the public key (shown so the admin can compare it with the Connected App certificate).</summary>
    public string? KeyThumbprint { get; set; }

    public string ApiVersion { get; set; } = "v62.0";

    public int BulkThreshold { get; set; } = 2000;

    public int PageSize { get; set; } = 2000;

    /// <summary>jsonb <see cref="SyncScheduleSettings"/>.</summary>
    public string Schedule { get; set; } = "{}";

    public DateTimeOffset? NextRunAt { get; set; }

    public int? LateAfterMinutes { get; set; }

    public Guid? MappingPackId { get; set; }

    [NotAudited]
    public string? InstanceUrl { get; set; }

    /// <summary>jsonb <see cref="ApiLimits"/> read at the last run / test.</summary>
    [NotAudited]
    public string? LastLimits { get; set; }

    [NotAudited]
    public DateTimeOffset? LastTestAt { get; set; }

    [NotAudited]
    public bool? LastTestOk { get; set; }

    [NotAudited]
    public string? LastTestMessageKey { get; set; }

    /// <summary>jsonb <see cref="SyncTestDetail"/>: what Salesforce answered to the last test (error, warnings such as /limits refused; ADR 0210, 0211).</summary>
    [NotAudited]
    public string? LastTestDetail { get; set; }

    /// <summary>"Sync now" request picked up by the scheduler job within a minute (or at once through the job trigger).</summary>
    [NotAudited]
    public DateTimeOffset? RunRequestedAt { get; set; }

    [NotAudited]
    public bool RunRequestedFull { get; set; }

    [NotAudited]
    public Guid? RunRequestedBy { get; set; }

    /// <summary>jsonb array of object mapping keys, or null for all.</summary>
    [NotAudited]
    public string? RunRequestedObjects { get; set; }

    /// <summary>When the last "late" alert was sent (one per late period).</summary>
    [NotAudited]
    public DateTimeOffset? LateAlertSentAt { get; set; }
}

/// <summary>A mapping pack (ADR 0203), e.g. "ADoF Salesforce".</summary>
[Audited(EntityType)]
public sealed class SyncMappingPack : AuditableEntity
{
    public const string EntityType = "integration.sync_mapping_pack";

    public string Code { get; set; } = string.Empty;

    public string? NameAr { get; set; }

    public string? NameEn { get; set; }

    public int PackVersion { get; set; } = 1;

    public string? Description { get; set; }

    public string Connector { get; set; } = Connectors.Salesforce;
}

public static class ObjectMappingKinds
{
    public const string Record = "record";
    public const string People = "people";
    public const string Files = "files";
    public const string Attachments = "attachments";
    public const string Approvals = "approvals";
    public const string FieldHistory = "field_history";

    public static readonly IReadOnlyList<string> All = [Record, People, Files, Attachments, Approvals, FieldHistory];
}

/// <summary>One source object of a pack mapped to a target (ADR 0203). Fields / values / defaults are jsonb validated by the server.</summary>
[Audited(EntityType)]
[AuditParent(SyncMappingPack.EntityType, nameof(PackId))]
public sealed class SyncObjectMapping : AuditableEntity
{
    public const string EntityType = "integration.sync_object_mapping";

    public Guid PackId { get; set; }

    /// <summary>Stable key inside the pack (an object may be mapped twice, e.g. team → team and → yearly evaluation).</summary>
    public string Key { get; set; } = string.Empty;

    public string SourceObject { get; set; } = string.Empty;

    public string Kind { get; set; } = ObjectMappingKinds.Record;

    public string? TargetType { get; set; }

    public bool Enabled { get; set; }

    public int LoadOrder { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public string? Where { get; set; }

    public string WatermarkField { get; set; } = "SystemModstamp";

    public string ExternalIdTemplate { get; set; } = "{Id}";

    public bool UseBulk { get; set; } = true;

    /// <summary>jsonb list of <see cref="FieldMapping"/>.</summary>
    public string Fields { get; set; } = "[]";

    /// <summary>jsonb object target field → value.</summary>
    public string Defaults { get; set; } = "{}";

    /// <summary>jsonb array of source fields deliberately not mapped.</summary>
    public string Ignore { get; set; } = "[]";

    /// <summary>jsonb <see cref="PeopleOptions"/> (people mappings) or <see cref="HistoryOptions"/> (history mappings).</summary>
    public string Options { get; set; } = "{}";

    public string? NotesAr { get; set; }

    public string? NotesEn { get; set; }

    /// <summary>True for objects whose names come from documents, not yet seen in the live describe (G3).</summary>
    public bool Provisional { get; set; }

    [NotAudited]
    public DateTimeOffset? Watermark { get; set; }

    [NotAudited]
    public DateTimeOffset? LastFullLoadAt { get; set; }
}

public static class RunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class RunTriggers
{
    public const string Schedule = "schedule";
    public const string Manual = "manual";
    public const string Full = "full";
    public const string Reprocess = "reprocess";
}

/// <summary>One sync run (technical log).</summary>
public sealed class SyncRun
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ConnectionId { get; init; }

    public string Trigger { get; init; } = RunTriggers.Schedule;

    public bool Full { get; init; }

    public string Status { get; set; } = RunStatuses.Running;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? StartedBy { get; init; }

    /// <summary>jsonb map object key → <see cref="ObjectRunStats"/>.</summary>
    public string Objects { get; set; } = "{}";

    public int ApiCalls { get; set; }

    public string? MessageKey { get; set; }

    public string? MessageParams { get; set; }
}

/// <summary>A line of a run log (no record data, no secrets).</summary>
public sealed class SyncRunLog
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid RunId { get; init; }

    public DateTimeOffset At { get; init; }

    public string Level { get; init; } = "info";

    public string? ObjectKey { get; init; }

    public string MessageKey { get; init; } = string.Empty;

    public string? Params { get; init; }
}

public static class ExceptionReasons
{
    public const string UnmappedValue = "unmappedValue";
    public const string UnknownUser = "unknownUser";
    public const string MissingParent = "missingParent";
    public const string InvalidValue = "invalidValue";
    public const string RequiredMissing = "requiredMissing";
    public const string WriteFailed = "writeFailed";
    public const string FileRejected = "fileRejected";
    public const string UnknownTarget = "unknownTarget";

    public static readonly IReadOnlyList<string> All =
        [UnmappedValue, UnknownUser, MissingParent, InvalidValue, RequiredMissing, WriteFailed, FileRejected, UnknownTarget];
}

public static class ExceptionStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

/// <summary>A record the sync could not write, with its source payload for re-processing (ADR 0205).</summary>
public sealed class SyncException
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ConnectionId { get; init; }

    public Guid? RunId { get; set; }

    public Guid MappingId { get; init; }

    public string SourceObject { get; init; } = string.Empty;

    public string ExternalId { get; init; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Field { get; set; }

    public string? Value { get; set; }

    public string? MessageKey { get; set; }

    public string? Params { get; set; }

    /// <summary>jsonb: the source record as read.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset? SourceModifiedAt { get; set; }

    public string Status { get; set; } = ExceptionStatuses.Open;

    public int Attempts { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }
}

public static class MatchedBy
{
    public const string Email = "email";
    public const string EmployeeNo = "employeeNo";
    public const string FederationId = "federationId";
    public const string Manual = "manual";
}

/// <summary>A Salesforce User / Contact matched to an employee (ADR 0204).</summary>
[Audited(EntityType)]
public sealed class SyncPerson : AuditableEntity
{
    public const string EntityType = "integration.sync_person";

    public string SourceSystem { get; set; } = string.Empty;

    public string SourceObject { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }

    public string? MatchedBy { get; set; }

    public string? Email { get; set; }

    public string? Name { get; set; }
}

public static class SourceHistoryKinds
{
    public const string Approval = "approval";
    public const string Field = "field";
}

/// <summary>Read-only history from the source (approval steps, field history; ADR 0208).</summary>
public sealed class SourceHistoryEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public string SourceSystem { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public Guid RecordId { get; init; }

    /// <summary>Salesforce Id of the record the history belongs to.</summary>
    public string RecordExternalId { get; init; } = string.Empty;

    public string Kind { get; init; } = SourceHistoryKinds.Approval;

    public string ExternalId { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? ActorName { get; set; }

    public string? ProcessName { get; set; }

    /// <summary>Approval instance id in the source (groups steps of one submission).</summary>
    public string? ProcessInstanceId { get; set; }

    public string? ProcessStatus { get; set; }

    public string? StepStatus { get; set; }

    public string? Comments { get; set; }

    public string? Field { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }
}

public static class OutageStatuses
{
    public const string Open = "open";
    public const string Reconciling = "reconciling";
    public const string Closed = "closed";
}

/// <summary>A period in which this system was primary (ADR 0207).</summary>
[Audited(EntityType)]
public sealed class SyncOutage : AuditableEntity
{
    public const string EntityType = "integration.sync_outage";

    public Guid ConnectionId { get; set; }

    public string Status { get; set; } = OutageStatuses.Open;

    public DateTimeOffset StartedAt { get; set; }

    public Guid? StartedBy { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset? ReturnedAt { get; set; }

    public Guid? ReturnedBy { get; set; }

    public DateTimeOffset? ReportAcknowledgedAt { get; set; }

    public Guid? ReportAcknowledgedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }
}

public static class ConflictResolutions
{
    public const string Pending = "pending";
    public const string KeepLocal = "keep_local";
    public const string TakeSource = "take_source";
}

/// <summary>A synced record touched by an outage: resubmit required, changed here, re-entered, conflict (ADR 0207).</summary>
[Audited(EntityType)]
[AuditParent(SyncOutage.EntityType, nameof(OutageId))]
public sealed class SyncOutageRecord : AuditableEntity
{
    public const string EntityType = "integration.sync_outage_record";

    public Guid OutageId { get; set; }

    public string EntityTypeCode { get; set; } = string.Empty;

    public Guid RecordId { get; set; }

    public string? ExternalId { get; set; }

    public bool ResubmitRequired { get; set; }

    public bool ChangedLocally { get; set; }

    public bool ReEntered { get; set; }

    public DateTimeOffset? ReEnteredAt { get; set; }

    public Guid? ReEnteredBy { get; set; }

    public bool Conflict { get; set; }

    public string Resolution { get; set; } = ConflictResolutions.Pending;

    [NotAudited]
    public string? PendingPayload { get; set; }

    [NotAudited]
    public Guid? PendingMappingId { get; set; }

    [NotAudited]
    public DateTimeOffset? PendingSourceModifiedAt { get; set; }
}
