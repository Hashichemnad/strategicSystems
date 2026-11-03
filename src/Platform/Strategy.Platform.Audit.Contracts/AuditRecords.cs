using System.Data.Common;

namespace Strategy.Platform.Audit;

public static class AuditKinds
{
    public const string Change = "change";
    public const string Event = "event";
}

public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";

    public const string FileUpload = "file.upload";
    public const string FileDownload = "file.download";
    public const string FileDownloadDenied = "file.download.denied";
    public const string FileDelete = "file.delete";
    public const string Export = "export";
    public const string ChainVerified = "audit.verify";
    public const string JobRunRequested = "job.run";
}

/// <summary>
/// One row for the chain, before the actor / context columns and the hashes are added by the sink.
/// Changes: one row per changed field (update) or one snapshot row (create / delete). Events: one row.
/// </summary>
public sealed record AuditRecord(
    string Kind,
    string Action,
    string? SystemCode,
    string? EntityType,
    string? RecordId,
    string? ParentType = null,
    string? ParentId = null,
    string? Field = null,
    string? OldValue = null,
    string? NewValue = null,
    Guid? ChangeSetId = null,
    bool? Success = null,
    string? Data = null,
    string? Reason = null,
    DateTimeOffset? OccurredAt = null)
{
    /// <summary>Overrides the actor taken from the request context (sign-in events name the person signing in).</summary>
    public Guid? ActorId { get; init; }

    public string? ActorName { get; init; }

    /// <summary>Overrides of the request context, used when carrying older records into the chain.</summary>
    public string? Channel { get; init; }

    /// <summary>Channel of an explicit event that differs from the request's (an AI call made from a UI request), actor unchanged.</summary>
    public string? EventChannel { get; init; }

    public string? CorrelationId { get; init; }

    public string? IpAddress { get; init; }

    public string? UserAgent { get; init; }
}

/// <summary>Appends rows to the hash chain on the caller's connection and transaction (used by the persistence interceptor).</summary>
public interface IAuditSink
{
    Task AppendAsync(DbConnection connection, DbTransaction transaction, IReadOnlyList<AuditRecord> records, CancellationToken cancellationToken);
}

/// <summary>An explicit audit event (login, download, export…). Data is serialised as JSON and must never contain secrets or file contents.</summary>
/// <param name="Action">Event code, e.g. <c>auth.login</c>, <c>file.download</c>.</param>
/// <param name="Success">Outcome.</param>
/// <param name="SystemCode">System the event belongs to.</param>
/// <param name="EntityType">Record the event is about, if any.</param>
/// <param name="RecordId">Its id.</param>
/// <param name="Data">Small detail object.</param>
/// <param name="Reason">Optional reason / comment.</param>
public sealed record AuditEvent(
    string Action,
    bool Success,
    string? SystemCode,
    string? EntityType = null,
    string? RecordId = null,
    object? Data = null,
    string? Reason = null)
{
    /// <summary>The person the event is about when nobody is signed in yet (sign-in attempts).</summary>
    public Guid? ActorId { get; init; }

    public string? ActorName { get; init; }

    /// <summary>Overrides the request's channel, e.g. <c>ai</c> for an AI call made from a UI request (CLAUDE.md §12).</summary>
    public string? Channel { get; init; }
}

/// <summary>Writes explicit events into the chain (own transaction). Callers in a DbContext transaction can pass it to join.</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

/// <summary>Ambient reason for the changes saved in the current unit of work (e.g. a bulk transfer's reason).</summary>
public interface IAuditReason
{
    string? Reason { get; set; }
}
