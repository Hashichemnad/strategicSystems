namespace Strategy.Platform.Scheduling.Domain;

/// <summary>A unit of reliable async work (e-mail, in-app message, later integration events), CLAUDE.md §5 / §14.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Handler key, e.g. <c>notifications.email</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON payload read by the handler.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Not before this time (retry back-off).</summary>
    public DateTimeOffset AvailableAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Given up (too many attempts, or expired): listed for administrators, who may re-queue it.</summary>
    public DateTimeOffset? DeadAt { get; set; }

    /// <summary>Dropped instead of delivered after this time (e.g. a sign-in code).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public string? CorrelationId { get; set; }
}

public static class JobRunStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>Not run: the job belongs to a system suspended while a source system is primary (standby, ADR 0206).</summary>
    public const string Suspended = "suspended";
}

/// <summary>One execution of a scheduled job (job dashboard).</summary>
public sealed class JobRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string JobKey { get; set; } = string.Empty;

    public DateTimeOffset FiredAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string Status { get; set; } = JobRunStatuses.Running;

    /// <summary>Short result or error text (no secrets).</summary>
    public string? Message { get; set; }

    /// <summary>User who pressed "Run now"; null for scheduled runs.</summary>
    public Guid? TriggeredBy { get; set; }

    public string? Instance { get; set; }
}
