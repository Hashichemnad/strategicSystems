namespace Strategy.Platform.Kernel.Integration;

/// <summary>A record by its entity type code (e.g. <c>portfolio.project</c>) and id.</summary>
public readonly record struct RecordKey(string EntityType, Guid Id);

/// <summary>
/// Standby behaviour of a one-way sync (SPEC-ADDENDUM §G4, ADR 0206): while a source system is primary, the records it synced are read-only
/// here and workflows, jobs and notifications of the business systems are suspended. Implemented by Platform.Integration; consumed by
/// Persistence (write interceptor), Workflow, Scheduling and Notifications without referencing Integration. No registration = never active.
/// </summary>
public interface IStandbyGuard
{
    /// <summary>Is any connection in standby (cached briefly)?</summary>
    ValueTask<bool> IsActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Systems (codes) whose jobs and notifications are suspended while the guard is active.</summary>
    ValueTask<IReadOnlySet<string>> SuspendedSystemsAsync(CancellationToken cancellationToken = default);

    /// <summary>The records among <paramref name="records"/> that are read-only now, with the source system's display name.</summary>
    Task<IReadOnlyDictionary<RecordKey, string>> FindReadOnlyAsync(IReadOnlyCollection<RecordKey> records, CancellationToken cancellationToken = default);

    /// <summary>Told when an approval is submitted for a record here (clears a "Resubmit required" flag of an outage).</summary>
    Task OnApprovalSubmittedAsync(RecordKey record, CancellationToken cancellationToken = default);
}

/// <summary>Stable codes of the read-only rule (the server returns keys, never sentences).</summary>
public static class StandbyErrors
{
    public const string Code = "integration.sync.readOnly";
    public const string MessageKey = "integration.sync.errors.readOnly";
}

/// <summary>A write reached a record that is read-only while its source system is primary (→ HTTP 409 <see cref="StandbyErrors.Code"/>).</summary>
public sealed class RecordReadOnlyException(RecordKey record, string sourceName)
    : InvalidOperationException($"Record {record.EntityType}/{record.Id} is synced from {sourceName} and read-only while it is the primary system.")
{
    public RecordKey Record { get; } = record;

    public string SourceName { get; } = sourceName;
}
