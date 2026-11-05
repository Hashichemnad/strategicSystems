using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.AuditManagement.Contracts;

/// <summary>Linkable types declared by Audit Management (Links registry).</summary>
public static class AuditLinkTypes
{
    public const string Report = "auditmgmt.report";
    public const string Finding = "auditmgmt.finding";
}

/// <summary>Finding statuses (SPEC §10.3 A, docs/design/audit.md §4.2).</summary>
public static class AuditFindingStatuses
{
    public const string Open = "open";
    public const string Late = "late";
    public const string Closed = "closed";
    public const string RiskAccepted = "risk_accepted";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Open, Late, Closed, RiskAccepted, Cancelled];

    public static readonly IReadOnlyList<string> Final = [Closed, RiskAccepted, Cancelled];

    public static bool IsFinal(string status) => Final.Contains(status);
}

/// <summary>Default priority / risk-rating codes (lookup <c>auditmgmt.priority</c>; configurable).</summary>
public static class AuditPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";

    public static readonly IReadOnlyList<string> Defaults = [Low, Medium, High, Critical];
}

/// <summary>A finding reached a final status (closed, risk accepted, cancelled). Delivered at least once.</summary>
public sealed record AuditFindingClosed(Guid FindingId, string Number, string Status, Guid ResponsibleUnitId, DateTimeOffset At) : IIntegrationEvent;

/// <summary>A finding was linked to a risk (and optionally a control). Delivered at least once.</summary>
public sealed record AuditFindingLinkedToRisk(Guid FindingId, string Number, Guid RiskId, Guid? ControlId, DateTimeOffset At) : IIntegrationEvent;
