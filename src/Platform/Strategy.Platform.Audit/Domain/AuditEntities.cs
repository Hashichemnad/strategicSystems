namespace Strategy.Platform.Audit.Domain;

/// <summary>
/// A row of <c>audit.entry</c>. Read through EF; written only by <see cref="Application.AuditChainWriter"/> (append-only, hash-chained).
/// </summary>
public sealed class AuditEntry
{
    public long Seq { get; set; }

    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? SystemCode { get; set; }

    public string? EntityType { get; set; }

    public string? RecordId { get; set; }

    public string? ParentType { get; set; }

    public string? ParentId { get; set; }

    public string? Field { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public Guid? ChangeSetId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public Guid? OnBehalfOfId { get; set; }

    public string? OnBehalfOfName { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Reason { get; set; }

    public bool? Success { get; set; }

    /// <summary>Event detail as canonical JSON text (text, not jsonb, so the stored bytes are exactly the hashed bytes).</summary>
    public string? Data { get; set; }

    public byte[] PrevHash { get; set; } = [];

    public byte[] Hash { get; set; } = [];
}

/// <summary>One verification of the chain (job or "Verify now").</summary>
public sealed class VerificationRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    /// <summary><c>full</c> (from the first entry) or <c>incremental</c> (from the last checkpoint).</summary>
    public string Mode { get; set; } = string.Empty;

    public long FromSeq { get; set; }

    public long EntriesChecked { get; set; }

    public long LastSeq { get; set; }

    public bool Ok { get; set; }

    public long? BrokenSeq { get; set; }

    /// <summary>Translatable reason, e.g. <c>audit.verify.hashMismatch</c>.</summary>
    public string? MessageKey { get; set; }

    public Guid? TriggeredBy { get; set; }

    public string Channel { get; set; } = string.Empty;
}

/// <summary>The last verified position of the chain; a later run that no longer finds it reports truncation.</summary>
public sealed class ChainCheckpoint
{
    public int Id { get; set; } = 1;

    public long Seq { get; set; }

    public byte[] Hash { get; set; } = [];

    public DateTimeOffset VerifiedAt { get; set; }
}

/// <summary>
/// Staging for records carried over from older interim stores (Phase 1 <c>identity.auth_event</c>). Filled by a migration of the
/// owning component, drained into the chain by the audit seeder in <see cref="OccurredAt"/> order.
/// </summary>
public sealed class LegacyImport
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? SystemCode { get; set; }

    public string? EntityType { get; set; }

    public string? RecordId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public bool? Success { get; set; }

    public string? Data { get; set; }
}
