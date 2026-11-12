using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Tasks.Contracts;

/// <summary>
/// A task reached its completed status — approved (Full template) or done (Simple template) — from <paramref name="FromStatus"/>.
/// <paramref name="LinkedType"/> / <paramref name="LinkedId"/>: the record the task belongs to, when any. Delivered at least once.
/// </summary>
public sealed record TaskCompleted(
    Guid TaskId,
    string? Number,
    string FromStatus,
    string Status,
    string? LinkedType,
    Guid? LinkedId,
    Guid AssigneeEmployeeId,
    bool? CompletedOnTime,
    DateTimeOffset CompletedAt) : IIntegrationEvent;
