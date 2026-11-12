using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>
/// AR / EN labels of Portfolio's fixed codes for reports, dossiers and feeds (ADR 0209), worded as the screens show them
/// (<c>web/src/i18n/*/portfolio.json</c>, <c>pmStandards.json</c>).
/// </summary>
internal static class PortfolioChoices
{
    /// <summary>Labels of codes kept in Portfolio settings, answered by <see cref="PortfolioDimensionLabels"/>.</summary>
    public static readonly FieldLabelSource PriorityBands = new(PortfolioPermissions.SystemCode, PortfolioDimensionLabels.PriorityBand);

    /// <summary>Strategic objectives live in the KPI strategy hierarchy (ADR 0075).</summary>
    public static readonly FieldLabelSource StrategicObjectives = new("kpi", "objective");

    /// <summary>With the Risk module on, project risks are rated by its framework (ADR 0074): band names from there, <see cref="RiskBands"/> otherwise.</summary>
    public static readonly FieldLabelSource RiskFrameworkBands = new("risk", "band");

    public static readonly IReadOnlyList<RecordFieldChoice> Classifications =
    [
        new(Domain.Classifications.Project, "مشروع", "Project"),
        new(Domain.Classifications.Initiative, "مبادرة", "Initiative"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Methodologies =
    [
        new(Domain.Methodologies.Waterfall, "تقليدية (شلالية)", "Waterfall"),
        new(Domain.Methodologies.Agile, "رشيقة", "Agile"),
        new(Domain.Methodologies.Hybrid, "هجينة", "Hybrid"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ProjectStages =
    [
        new(Domain.ProjectStages.Initiation, "البدء", "Initiation"),
        new(Domain.ProjectStages.Planning, "التخطيط", "Planning"),
        new(Domain.ProjectStages.Execution, "التنفيذ", "Execution"),
        new(Domain.ProjectStages.Closure, "الإغلاق", "Closure"),
        new(Domain.ProjectStages.Impact, "الأثر", "Impact"),
        new(Domain.ProjectStages.Completed, "مكتمل", "Completed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ProjectStatuses =
    [
        new(Domain.ProjectStatuses.Draft, "مسودة", "Draft"),
        new(Domain.ProjectStatuses.Active, "نشط", "Active"),
        new(Domain.ProjectStatuses.OnHold, "معلق", "On hold"),
        new(Domain.ProjectStatuses.Cancelled, "ملغى", "Cancelled"),
        new(Domain.ProjectStatuses.Completed, "مكتمل", "Completed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ProgressStatuses =
    [
        new(ProgressCalculator.OnTrack, "وفق الخطة", "On track"),
        new(ProgressCalculator.Delayed, "متأخر", "Delayed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> MilestoneStatuses =
    [
        new(Domain.MilestoneStatuses.NotStarted, "لم يبدأ", "Not started"),
        new(Domain.MilestoneStatuses.Active, "قيد التنفيذ", "Active"),
        new(Domain.MilestoneStatuses.Completed, "مكتمل", "Completed"),
        new(Domain.MilestoneStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RequestStatuses =
    [
        new(Domain.RequestStatuses.Draft, "مسودة", "Draft"),
        new(Domain.RequestStatuses.InApproval, "قيد الاعتماد", "In approval"),
        new(Domain.RequestStatuses.Approved, "معتمد", "Approved"),
        new(Domain.RequestStatuses.Rejected, "مرفوض", "Rejected"),
        new(Domain.RequestStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    /// <summary>Steps of the default program-request approval (the request page's step names, <c>portfolio.json → stages</c>).</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> RequestApprovalSteps =
    [
        new("draft", "مسودة", "Draft"),
        new("manager", "اعتماد المدير", "Manager approval"),
        new("pm-office", "مكتب إدارة المشاريع", "PM Office"),
        new("dg-office", "مكتب المدير العام", "DG Office"),
        new("budget", "الميزانية", "Budget"),
        new("approved", "معتمد", "Approved"),
        new("rejected", "مرفوض", "Rejected"),
        new("cancelled", "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ChangeTypes =
    [
        new(Domain.ChangeTypes.Details, "تفاصيل", "Details"),
        new(Domain.ChangeTypes.Postponement, "تأجيل", "Postponement"),
        new(Domain.ChangeTypes.Ownership, "ملكية", "Ownership"),
        new(Domain.ChangeTypes.Cancellation, "إلغاء", "Cancellation"),
        new(Domain.ChangeTypes.Merge, "دمج", "Merge"),
        new(Domain.ChangeTypes.Hold, "تعليق المشروع", "Put on hold"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ChangeStatuses =
    [
        new(Domain.ChangeStatuses.Draft, "مسودة", "Draft"),
        new(Domain.ChangeStatuses.UnderReview, "قيد المراجعة", "Under review"),
        new(Domain.ChangeStatuses.Approved, "معتمد — بانتظار التطبيق", "Approved — to apply"),
        new(Domain.ChangeStatuses.Rejected, "مرفوض", "Rejected"),
        new(Domain.ChangeStatuses.Cancelled, "ملغى", "Cancelled"),
        new(Domain.ChangeStatuses.Completed, "تم تطبيق التغييرات", "Changes implemented"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RiskBands =
    [
        new(Domain.RiskBands.Low, "منخفض", "Low"),
        new(Domain.RiskBands.Medium, "متوسط", "Medium"),
        new(Domain.RiskBands.High, "مرتفع", "High"),
        new(Domain.RiskBands.Critical, "حرج", "Critical"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RiskResponses =
    [
        new("accept", "قبول", "Accept"),
        new("avoid", "تجنب", "Avoid"),
        new("mitigate", "تخفيف", "Mitigate"),
        new("transfer", "نقل", "Transfer"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RaidTypes =
    [
        new(Domain.RaidTypes.Issue, "مشكلة", "Issue"),
        new(Domain.RaidTypes.Assumption, "افتراض", "Assumption"),
        new(Domain.RaidTypes.Dependency, "اعتمادية", "Dependency"),
        new(Domain.RaidTypes.Decision, "قرار", "Decision"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RaidPriorities =
    [
        new("high", "عالية", "High"),
        new("medium", "متوسطة", "Medium"),
        new("low", "منخفضة", "Low"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RaidStatuses =
    [
        new(Domain.RaidStatuses.Proposed, "مقترح", "Proposed"),
        new(Domain.RaidStatuses.Open, "مفتوح", "Open"),
        new(Domain.RaidStatuses.Resolved, "تم الحل", "Resolved"),
        new(Domain.RaidStatuses.Closed, "مغلق", "Closed"),
    ];

    /// <summary>Stakeholder interest and influence levels.</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> Levels =
    [
        new("low", "منخفض", "Low"),
        new("medium", "متوسط", "Medium"),
        new("high", "مرتفع", "High"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> KpiNatures =
    [
        new(Domain.KpiNatures.New, "جديد", "New"),
        new(Domain.KpiNatures.Existing, "بطاقة مؤشر قائمة", "Existing KPI card"),
    ];
}
