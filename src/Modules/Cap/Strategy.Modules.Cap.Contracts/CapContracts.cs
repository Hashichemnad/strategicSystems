using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Cap.Contracts;

/// <summary>Source types (lookup <c>cap.source-type</c>) used by integrations; the lookup labels are configuration.</summary>
public static class CapSourceTypes
{
    public const string Workshop = "workshop";
    public const string Benchmarking = "benchmarking";
    public const string InternalAudit = "internal-audit";
    public const string ExternalAudit = "external-audit";
    public const string ExcellenceAssessment = "excellence-assessment";
    public const string KpiPerformance = "kpi-performance";
    public const string RiskEvent = "risk-event";
    public const string CustomerComplaint = "customer-complaint";
    public const string EmployeeSurvey = "employee-survey";
    public const string ManagementReview = "management-review";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All =
        [Workshop, Benchmarking, InternalAudit, ExternalAudit, ExcellenceAssessment, KpiPerformance, RiskEvent, CustomerComplaint, EmployeeSurvey, ManagementReview, Other];
}

/// <summary>CAP picklists (Settings lookups) other modules label CAP codes with, e.g. Benchmarking improvement actions (ADR 0209).</summary>
public static class CapLookupTypes
{
    public const string Priority = "cap.priority";
    public const string ActionType = "cap.action-type";
}

/// <summary>Remark priority codes (SPEC §9.3 B). Due dates by priority are Settings → CAP.</summary>
public static class CapPriorities
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static readonly IReadOnlyList<string> All = [Critical, High, Medium, Low];
}

/// <summary>Remark type codes (lookup <c>cap.remark-type</c>).</summary>
public static class CapRemarkTypes
{
    public const string Nonconformity = "nonconformity";
    public const string Observation = "observation";
    public const string Improvement = "improvement";

    public static readonly IReadOnlyList<string> All = [Nonconformity, Observation, Improvement];
}

/// <summary>Action type codes (lookup <c>cap.action-type</c>).</summary>
public static class CapActionTypes
{
    public const string Correction = "correction";
    public const string Corrective = "corrective";
    public const string Preventive = "preventive";
    public const string Improvement = "improvement";

    public static readonly IReadOnlyList<string> All = [Correction, Corrective, Preventive, Improvement];
}

/// <summary>Remark statuses (docs/design/cap.md §4.1).</summary>
public static class CapRemarkStatuses
{
    public const string Assigned = "assigned";
    public const string RejectionPending = "rejection_pending";
    public const string Rejected = "rejected";
    public const string Planning = "planning";
    public const string Validation = "validation";
    public const string Correction = "correction";
    public const string Implementation = "implementation";
    public const string Verification = "verification";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All =
        [Assigned, RejectionPending, Rejected, Planning, Validation, Correction, Implementation, Verification, Completed, Cancelled];

    public static readonly IReadOnlyList<string> Final = [Rejected, Completed, Cancelled];

    public static bool IsFinal(string status) => Final.Contains(status);
}

/// <summary>Linkable types declared by CAP (Links registry).</summary>
public static class CapLinkTypes
{
    public const string Source = "cap.source";
    public const string Remark = "cap.remark";
    public const string Action = "cap.action";
}

/// <summary>A source a system asks CAP to keep for one of its records (ADR 0123).</summary>
public sealed record CapSourceRequest(
    string LinkedType,
    Guid LinkedId,
    string SourceTypeCode,
    string? TitleAr,
    string? TitleEn,
    string? Description,
    DateOnly ReceivedOn,
    Guid? OwnerEmployeeId,
    Guid? UnitId,
    IReadOnlyList<Guid>? ViewerEmployeeIds = null,
    Guid? TeamId = null);

/// <summary>An action proposed with a remark (created as a draft action the remark owner reviews and submits).</summary>
public sealed record CapProposedAction(
    string? DetailsAr,
    string? DetailsEn,
    string? TypeCode,
    Guid? AssignedEmployeeId,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    bool BudgetRequired = false,
    bool? BudgetAvailable = null,
    decimal? EstimatedCost = null);

/// <summary>A remark a system raises under its source; <see cref="ExternalKey"/> makes the call idempotent within the source.</summary>
public sealed record CapRemarkRequest(
    string ExternalKey,
    string? TextAr,
    string? TextEn,
    string TypeCode,
    string Priority,
    string? CriterionCode,
    Guid DepartmentUnitId,
    Guid? SectionUnitId,
    Guid? OwnerEmployeeId,
    DateOnly? AssignedOn,
    string? LinkedType,
    Guid? LinkedId,
    IReadOnlyList<CapProposedAction>? ProposedActions = null);

public sealed record CapSourceRef(Guid SourceId, string Number, bool Created);

public sealed record CapRemarkRef(Guid RemarkId, string Number, bool Created);

/// <summary>An action as a source system shows it (live, read-only).</summary>
public sealed record CapActionProgress(
    Guid ActionId, string Number, string? DetailsAr, string? DetailsEn, string ValidationStatus, string Status, decimal AchievedPct, bool HasEvidence,
    DateOnly? PlannedEnd, DateOnly? RevisedEnd, Guid? AssignedEmployeeId, string Link);

public sealed record CapRemarkProgress(
    Guid RemarkId, string Number, string? ExternalKey, string? TextAr, string? TextEn, string Status, string Stage, Guid? OwnerEmployeeId, DateOnly? DueOn,
    IReadOnlyList<CapActionProgress> Actions, string Link);

/// <summary>
/// Live progress of a source (ADR 0123). <see cref="AllActionsCompleted"/>: at least one completed action, every remark final and every
/// approved action completed — completion requires evidence, so this means "completed with evidence".
/// </summary>
public sealed record CapSourceProgress(
    Guid SourceId, string Number, IReadOnlyList<CapRemarkProgress> Remarks, int ActionCount, int CompletedCount, bool AllActionsCompleted, string Link);

/// <summary>
/// The source-system contract (SPEC §9.1 / §9.5, ADR 0123): any module creates remarks with a live link back and reads their progress.
/// Calls run with the caller's authority (the caller decides who may trigger them); pass the caller's DbContext with an open transaction as
/// <c>transactionContext</c> so CAP commits with the caller (ADR 0023). Registered only while CAP is enabled.
/// </summary>
public interface ICapRemarkSources
{
    Task<CapSourceRef> EnsureSourceAsync(CapSourceRequest request, object? transactionContext = null, CancellationToken cancellationToken = default);

    Task<Result<CapRemarkRef>> AddRemarkAsync(Guid sourceId, CapRemarkRequest request, object? transactionContext = null, CancellationToken cancellationToken = default);

    /// <summary>Remarks and actions of the source kept for the record, or null when none exists.</summary>
    Task<CapSourceProgress?> GetProgressAsync(string linkedType, Guid linkedId, CancellationToken cancellationToken = default);
}

/// <summary>A remark changed status (every change). Consumers react to their own linked records. Delivered at least once.</summary>
public sealed record CapRemarkStatusChanged(
    Guid RemarkId,
    string Number,
    Guid SourceId,
    string? SourceLinkedType,
    Guid? SourceLinkedId,
    string? RemarkLinkedType,
    Guid? RemarkLinkedId,
    string? ExternalKey,
    string Status,
    string Stage,
    DateTimeOffset At) : IIntegrationEvent;

/// <summary>An action's completion was approved (with evidence). Delivered at least once.</summary>
public sealed record CapActionCompleted(Guid ActionId, string Number, IReadOnlyList<Guid> RemarkIds, DateTimeOffset At) : IIntegrationEvent;
