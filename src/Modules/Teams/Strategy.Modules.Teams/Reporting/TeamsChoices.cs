using Strategy.Platform.Reporting;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>
/// Labels of Teams Management's codes for reports, dossiers and feeds (ADR 0209): fixed codes as <c>web/src/i18n/*/teams.json</c> words them;
/// team types and member roles are configuration, named by <see cref="TeamsDimensionLabels"/>.
/// </summary>
internal static class TeamsChoices
{
    public static readonly FieldLabelSource Types = new("teams", TeamsDimensionLabels.Type);

    public static readonly FieldLabelSource Roles = new("teams", TeamsDimensionLabels.Role);

    public static readonly IReadOnlyList<RecordFieldChoice> TeamStatuses =
    [
        new(Domain.TeamStatuses.Draft, "مسودة", "Draft"),
        new(Domain.TeamStatuses.PendingApproval, "بانتظار الاعتماد", "Pending approval"),
        new(Domain.TeamStatuses.Active, "نشط", "Active"),
        new(Domain.TeamStatuses.Inactive, "غير نشط", "Inactive"),
        new(Domain.TeamStatuses.PendingEvaluation, "بانتظار التقييم", "Pending evaluation"),
        new(Domain.TeamStatuses.EvaluationWaitingApproval, "التقييم بانتظار الاعتماد", "Evaluation waiting approval"),
        new(Domain.TeamStatuses.Closed, "مغلق", "Closed"),
        new(Domain.TeamStatuses.Replaced, "مستبدَل", "Replaced"),
        new(Domain.TeamStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Cadences =
    [
        new(Domain.Cadences.Monthly, "شهري", "Monthly"),
        new(Domain.Cadences.Quarterly, "ربع سنوي", "Quarterly"),
        new(Domain.Cadences.Biannually, "نصف سنوي", "Biannually"),
        new(Domain.Cadences.Annually, "سنوي", "Annually"),
        new(Domain.Cadences.Other, "أخرى (بلا تقرير دوري)", "Other (no periodic report)"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Workloads =
    [
        new(Domain.Workloads.High, "عالٍ", "High"),
        new(Domain.Workloads.Medium, "متوسط", "Medium"),
        new(Domain.Workloads.Low, "منخفض", "Low"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> MemberStatuses =
    [
        new(Domain.MemberStatuses.Active, "نشط", "Active"),
        new(Domain.MemberStatuses.Left, "غادر", "Left"),
        new(Domain.MemberStatuses.Replaced, "مستبدَل", "Replaced"),
    ];

    /// <summary>The completion cycle; annual cycles are their year and need no label.</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> Cycles =
    [
        new(Domain.Cycles.Completion, "عند الانتهاء", "On completion"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> EvaluationStatuses =
    [
        new(Domain.EvaluationStatuses.Open, "مفتوح", "Open"),
        new(Domain.EvaluationStatuses.Submitted, "مقدَّم", "Submitted"),
        new(Domain.EvaluationStatuses.Returned, "معاد", "Returned"),
        new(Domain.EvaluationStatuses.Approved, "معتمد", "Approved"),
        new(Domain.EvaluationStatuses.Cancelled, "ملغى", "Cancelled"),
    ];
}
