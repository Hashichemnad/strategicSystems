using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.AuditManagement.Reporting;

/// <summary>AR / EN labels of Audit Management's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/auditmgmt.json</c> words them.</summary>
internal static class AuditChoices
{
    /// <summary>A lowered control's new effectiveness level is a level of the Risk framework.</summary>
    public static readonly FieldLabelSource ControlEffectiveness = new("risk", "effectiveness");

    public static readonly IReadOnlyList<RecordFieldChoice> FindingStatuses =
    [
        new(AuditFindingStatuses.Open, "مفتوحة", "Open"),
        new(AuditFindingStatuses.Late, "متأخرة", "Late"),
        new(AuditFindingStatuses.Closed, "مغلقة", "Closed"),
        new(AuditFindingStatuses.RiskAccepted, "قبول المخاطر", "Risk accepted"),
        new(AuditFindingStatuses.Cancelled, "ملغاة", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RecommendationStatuses =
    [
        new(Domain.RecommendationStatuses.NotStarted, "لم يبدأ", "Not started"),
        new(Domain.RecommendationStatuses.Continuing, "مستمر", "Continuing"),
        new(Domain.RecommendationStatuses.Late, "متأخر", "Late"),
        new(Domain.RecommendationStatuses.Completed, "مكتمل", "Completed"),
        new(Domain.RecommendationStatuses.RiskAccepted, "قبول المخاطر", "Risk accepted"),
        new(Domain.RecommendationStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ReportStatuses =
    [
        new(Domain.ReportStatuses.Draft, "مسودة", "Draft"),
        new(Domain.ReportStatuses.Issued, "صادر", "Issued"),
        new(Domain.ReportStatuses.UnderFollowUp, "قيد المتابعة", "Under follow-up"),
        new(Domain.ReportStatuses.Closed, "مغلق", "Closed"),
        new(Domain.ReportStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ReviewStatuses =
    [
        new(Domain.ReviewStatuses.None, "بدون مراجعة", "No review"),
        new(Domain.ReviewStatuses.Pending, "بانتظار المراجعة", "Waiting review"),
        new(Domain.ReviewStatuses.Approved, "تمت المراجعة", "Reviewed"),
        new(Domain.ReviewStatuses.Returned, "معادة", "Returned"),
    ];
}
