using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Requests.Contracts;

/// <summary>Decisions of <see cref="RequestDecided"/>.</summary>
public static class RequestDecisions
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// Integration event (ADR 0043, 0194): the final approval decision of a request was applied — <see cref="RequestDecisions.Approved"/> when the
/// request leaves its approval stages (no further approval pending), <see cref="RequestDecisions.Rejected"/> when an approver rejects it.
/// Published in the approval engine's transaction through the outbox.
/// </summary>
public sealed record RequestDecided(Guid RequestId, string Number, string TypeCode, string Decision, DateTimeOffset At) : IIntegrationEvent;
