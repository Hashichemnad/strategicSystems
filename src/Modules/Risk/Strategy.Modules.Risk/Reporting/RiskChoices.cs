using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>
/// Labels of Risk Management's codes for reports, dossiers and feeds (ADR 0209): fixed codes as <c>web/src/i18n/*/risk.json</c> words them;
/// rating bands and control effectiveness levels are the framework's (<see cref="RiskDimensionLabels"/>).
/// </summary>
internal static class RiskChoices
{
    public static readonly FieldLabelSource Bands = new(RiskPermissions.SystemCode, "band");

    public static readonly FieldLabelSource Effectiveness = new(RiskPermissions.SystemCode, "effectiveness");

    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(RiskStatuses.Draft, "مسودة", "Draft"),
        new(RiskStatuses.UnderReview, "قيد الاعتماد", "Under review"),
        new(RiskStatuses.Active, "نشط", "Active"),
        new(RiskStatuses.Closed, "مغلق", "Closed"),
        new(RiskStatuses.Rejected, "مرفوض", "Rejected"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Types =
    [
        new(RiskTypes.Internal, "داخلي", "Internal"),
        new(RiskTypes.External, "خارجي", "External"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ResidualStatuses =
    [
        new(Domain.ResidualStatuses.NoIssue, "لا توجد مشكلة", "No issue"),
        new(Domain.ResidualStatuses.ContinuousReview, "مراجعة مستمرة", "Continuous review"),
        new(Domain.ResidualStatuses.ActiveManagement, "إدارة فعّالة", "Active management"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ActionStatuses =
    [
        new(Domain.ActionStatuses.NotStarted, "لم يبدأ", "Not started"),
        new(Domain.ActionStatuses.InProgress, "قيد التنفيذ", "In progress"),
        new(Domain.ActionStatuses.Completed, "مكتمل", "Completed"),
        new(Domain.ActionStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    /// <summary>Statuses of controls and of key risk indicators.</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> ControlStatuses =
    [
        new(Domain.ControlStatuses.Active, "نشط", "Active"),
        new(Domain.ControlStatuses.Retired, "متوقف", "Retired"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> KriLevels =
    [
        new(Domain.KriLevels.Normal, "طبيعي", "Normal"),
        new(Domain.KriLevels.Warning, "تحذير", "Warning"),
        new(Domain.KriLevels.Breach, "تجاوز", "Breach"),
        new(Domain.KriLevels.None, "لا توجد قراءة", "No reading"),
    ];
}
