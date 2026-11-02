using Strategy.Platform.Kernel.Paging;

namespace Strategy.Platform.Audit;

/// <summary>An entry as shown on History tabs and the Audit Log.</summary>
public sealed record AuditEntryDto(
    Guid Id,
    long Seq,
    DateTimeOffset OccurredAt,
    string Kind,
    string Action,
    string? SystemCode,
    string? EntityType,
    string? RecordId,
    string? ParentType,
    string? ParentId,
    string? Field,
    string? OldValue,
    string? NewValue,
    Guid? ChangeSetId,
    Guid? UserId,
    string? UserName,
    Guid? OnBehalfOfId,
    string? OnBehalfOfName,
    string Channel,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent,
    string? Reason,
    bool? Success,
    string? Data);

/// <summary>Filters of the global Audit Log (all optional).</summary>
public sealed record AuditFilter(
    string? Text = null,
    Guid? UserId = null,
    string? SystemCode = null,
    string? EntityType = null,
    string? RecordId = null,
    string? Action = null,
    string? Channel = null,
    string? Kind = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Field = null,
    bool ExactAction = false);

/// <summary>Field values of a record at a point in time, rebuilt from its create snapshot and later field updates.</summary>
public sealed record RecordSnapshotDto(string EntityType, string RecordId, DateTimeOffset At, bool Existed, IReadOnlyDictionary<string, string?> Fields);

/// <summary>Read side of the audit trail. Callers check access to the record first (404 outside scope).</summary>
public interface IAuditQuery
{
    /// <summary>The record's entries and its child rows' entries, newest first.</summary>
    Task<PagedResult<AuditEntryDto>> HistoryAsync(string entityType, string recordId, PageRequest page, string? field = null, Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<RecordSnapshotDto> AsOfAsync(string entityType, string recordId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditEntryDto>> SearchAsync(AuditFilter filter, PageRequest page, CancellationToken cancellationToken = default);
}
