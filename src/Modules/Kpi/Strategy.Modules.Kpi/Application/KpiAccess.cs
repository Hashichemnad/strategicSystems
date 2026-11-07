using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Permission, record-role, numbering, lookup and file codes (docs/design/kpi.md §8).</summary>
public static class KpiPermissions
{
    public const string SystemCode = "kpi";

    public const string CardRead = "kpi.card.read";
    public const string CardManage = "kpi.card.manage";
    public const string StrategyManage = "kpi.strategy.manage";
    public const string PlanManage = "kpi.plan.manage";
    public const string ResultRead = "kpi.result.read";
    public const string ResultSubmit = "kpi.result.submit";
    public const string ResultReview = "kpi.result.review";
    public const string ResultReopen = "kpi.result.reopen";
    public const string ScorecardRead = "kpi.scorecard.read";
    public const string DashboardRead = "kpi.dashboard.read";
    public const string Export = "kpi.report.export";
    public const string SettingsManage = "kpi.settings.manage";

    public const string AiEvidence = "kpi.ai.evidence";
    public const string AiAnomalies = "kpi.ai.anomalies";
    public const string AiCausality = "kpi.ai.causality";
    public const string AiBuilder = "kpi.ai.builder";
    public const string AiTargets = "kpi.ai.targets";
    public const string AiNarrative = "kpi.ai.narrative";

    /// <summary>Record role the approval engine grants to approvers of a result / change while their task is open.</summary>
    public const string ApproverRole = "approver";

    /// <summary>Approval-engine role key of the Strategy Office / PMO (default approver of <c>kpi.result</c>).</summary>
    public const string StrategyOfficeRoleKey = "kpi.strategy-office";

    public const string CardNumbering = "kpi.card";

    public const string TypeLookup = "kpi.type";
    public const string IndexClassificationLookup = "kpi.index-classification";
    public const string EffectivenessLookup = "kpi.effectiveness";
    public const string SourceLookup = "kpi.source";
    public const string UnitLookup = "kpi.unit";
    public const string MeasuringMethodLookup = "kpi.measuring-method";
    public const string NationalGoalLookup = "kpi.national-goal";
    public const string VisionLinkLookup = "kpi.vision-link";
    public const string GemCriterionLookup = "kpi.gem-criterion";

    public const string ChangeControlFeature = "kpi.change-control";
}

/// <summary>Data scopes (ADR 0015): own = responsible person, unit = responsible unit.</summary>
public static class KpiScope
{
    public static readonly ScopeMap<KpiCard> Card = new(
        KpiCard.EntityType, x => x.Id, ownerEmployee: x => x.ResponsibleEmployeeId, unit: x => x.ResponsibleUnitId);

    public static readonly ScopeMap<KpiResult> Result = new(
        KpiResult.EntityType, x => x.Id, ownerEmployee: x => x.OwnerEmployeeId, unit: x => x.OwnerUnitId);

    public static readonly ScopeMap<AnnualPlan> Plan = new(
        KpiCard.EntityType, x => x.CardId, ownerEmployee: x => x.ResponsibleEmployeeId, unit: x => x.ResponsibleUnitId);
}

public static class KpiFilePurposes
{
    public static readonly FilePurpose Evidence = new("kpi.evidence", KpiResult.EntityType, KpiPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public static readonly FilePurpose CardDocument = new("kpi.card-document", KpiCard.EntityType, KpiPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);
}

/// <summary>The Performance Management catalogue (SPEC §2.6 defaults; editable in Settings → Roles).</summary>
internal sealed class KpiAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(KpiPermissions.SystemCode, SystemKind.Module, "إدارة الأداء المؤسسي", "Performance Management", 20);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(KpiPermissions.CardRead, "عرض بطاقات المؤشرات", "View KPI cards"),
        new(KpiPermissions.CardManage, "إدارة بطاقات المؤشرات", "Manage KPI cards"),
        new(KpiPermissions.StrategyManage, "إدارة الهيكل الاستراتيجي", "Manage the strategy hierarchy", Scopable: false),
        new(KpiPermissions.PlanManage, "إدارة سنوات القياس والمستهدفات والفترات", "Manage measuring years, targets and periods", Scopable: false),
        new(KpiPermissions.ResultRead, "عرض نتائج المؤشرات", "View KPI results"),
        new(KpiPermissions.ResultSubmit, "إدخال النتائج وتقديمها", "Enter and submit results"),
        new(KpiPermissions.ResultReview, "مراجعة النتائج والتحقق منها", "Review and verify results"),
        new(KpiPermissions.ResultReopen, "إعادة فتح النتائج وإقفال الفترات", "Reopen results and lock periods"),
        new(KpiPermissions.ScorecardRead, "عرض بطاقات الأداء والخريطة الاستراتيجية", "View scorecards and the strategy map"),
        new(KpiPermissions.DashboardRead, "عرض لوحة الأداء", "View the performance dashboard", Scopable: false),
        new(KpiPermissions.Export, "تصدير تقارير الأداء", "Export performance reports", Scopable: false),
        new(KpiPermissions.SettingsManage, "إدارة إعدادات الأداء", "Manage performance settings", Scopable: false),
        new(KpiPermissions.AiEvidence, "مدقق الأدلة بالذكاء الاصطناعي", "AI evidence checker", Scopable: false),
        new(KpiPermissions.AiAnomalies, "تنبيهات القيم غير المعتادة", "Anomaly flags", Scopable: false),
        new(KpiPermissions.AiCausality, "مساعد الأسباب والإجراءات التصحيحية", "Causality and corrective action assistant", Scopable: false),
        new(KpiPermissions.AiBuilder, "منشئ بطاقة المؤشر", "KPI card builder", Scopable: false),
        new(KpiPermissions.AiTargets, "اقتراح المستهدفات", "Target suggestion", Scopable: false),
        new(KpiPermissions.AiNarrative, "تقرير الأداء السردي", "Performance narrative", Scopable: false),
        ResponsibilityPermissions.Definition(KpiPermissions.SystemCode),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("owner", "مسؤول المؤشر", "KPI owner",
            [new(KpiPermissions.CardRead, DataScope.Own), new(KpiPermissions.ResultRead, DataScope.Own), new(KpiPermissions.ResultSubmit, DataScope.Own),
                new(KpiPermissions.AiEvidence), new(KpiPermissions.AiCausality), new(KpiPermissions.AiAnomalies)],
            AssignToEveryone: true),
        new("department-manager", "مدير الإدارة (الأداء)", "Department manager (performance)",
            [new(KpiPermissions.CardRead, DataScope.UnitAndBelow), new(KpiPermissions.ResultRead, DataScope.UnitAndBelow),
                new(KpiPermissions.ScorecardRead, DataScope.UnitAndBelow), new(KpiPermissions.DashboardRead), new(KpiPermissions.Export),
                new(KpiPermissions.AiAnomalies), new(KpiPermissions.AiNarrative)]),
        new("strategy-office", "مكتب الاستراتيجية / مكتب إدارة المشاريع", "Strategy Office / PMO",
            [new(KpiPermissions.CardRead, DataScope.All), new(KpiPermissions.CardManage, DataScope.All), new(KpiPermissions.StrategyManage),
                new(KpiPermissions.PlanManage), new(KpiPermissions.ResultRead, DataScope.All), new(KpiPermissions.ResultSubmit, DataScope.All),
                new(KpiPermissions.ResultReview, DataScope.All), new(KpiPermissions.ResultReopen, DataScope.All), new(KpiPermissions.ScorecardRead, DataScope.All),
                new(KpiPermissions.DashboardRead), new(KpiPermissions.Export), new(KpiPermissions.AiEvidence), new(KpiPermissions.AiAnomalies),
                new(KpiPermissions.AiCausality), new(KpiPermissions.AiBuilder), new(KpiPermissions.AiTargets), new(KpiPermissions.AiNarrative),
                new(ResponsibilityPermissions.Manage(KpiPermissions.SystemCode), DataScope.All)],
            RequiresMfa: true),
        new("executive", "مستعرض تنفيذي (الأداء)", "Executive viewer (performance)",
            [new(KpiPermissions.CardRead, DataScope.All), new(KpiPermissions.ResultRead, DataScope.All), new(KpiPermissions.ScorecardRead, DataScope.All),
                new(KpiPermissions.DashboardRead), new(KpiPermissions.Export), new(KpiPermissions.AiNarrative)]),
        new("administrator", "مسؤول نظام الأداء", "Performance administrator",
            [new(KpiPermissions.CardRead, DataScope.All), new(KpiPermissions.CardManage, DataScope.All), new(KpiPermissions.StrategyManage),
                new(KpiPermissions.PlanManage), new(KpiPermissions.ResultRead, DataScope.All), new(KpiPermissions.ScorecardRead, DataScope.All),
                new(KpiPermissions.DashboardRead), new(KpiPermissions.Export), new(KpiPermissions.SettingsManage), new(KpiPermissions.AiBuilder),
                new(ResponsibilityPermissions.Manage(KpiPermissions.SystemCode), DataScope.All)],
            RequiresMfa: true),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(KpiResult.EntityType, KpiPermissions.ApproverRole, "معتمد النتيجة", "Result approver", [KpiPermissions.ResultRead, KpiPermissions.CardRead]),
        new(KpiCard.EntityType, KpiPermissions.ApproverRole, "معتمد تعديل التعريف", "Definition change approver", [KpiPermissions.CardRead]),
    ];

    public static readonly NumberingDefinition CardNumbering = new(KpiPermissions.CardNumbering, KpiPermissions.SystemCode, "رمز المؤشر", "KPI code", "KPI-{SEQ:4}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(KpiPermissions.TypeLookup, KpiPermissions.SystemCode, "نوع المؤشر", "KPI type",
        [
            new("strategic", "استراتيجي", "Strategic"), new("regulatory", "تنظيمي", "Regulatory"), new("operational", "تشغيلي", "Operational"),
            new("sectors", "قطاعي", "Sectors"), new("competitive", "تنافسي", "Competitive"),
        ]),
        new(KpiPermissions.IndexClassificationLookup, KpiPermissions.SystemCode, "تصنيف المؤشر", "Index classification",
        [
            new("outcome", "مؤشر نتيجة", "Outcome"), new("output", "مؤشر مخرجات", "Output"), new("process", "مؤشر عمليات", "Process"), new("input", "مؤشر مدخلات", "Input"),
        ]),
        new(KpiPermissions.EffectivenessLookup, KpiPermissions.SystemCode, "الفعالية / الكفاءة", "Effectiveness / efficiency",
        [
            new("effectiveness", "فعالية", "Effectiveness"), new("efficiency", "كفاءة", "Efficiency"),
        ]),
        new(KpiPermissions.SourceLookup, KpiPermissions.SystemCode, "مصدر المؤشر", "KPI source",
        [
            new("internal-system", "نظام داخلي", "Internal system"), new("survey", "استبيان", "Survey"), new("manual-record", "سجل يدوي", "Manual record"),
            new("external-report", "تقرير خارجي", "External report"),
        ]),
        new(KpiPermissions.UnitLookup, KpiPermissions.SystemCode, "وحدة القياس", "Measuring unit",
        [
            new("number", "عدد", "Number"), new(Units.Percentage, "نسبة مئوية", "Percentage"), new("aed", "درهم", "AED"), new("days", "أيام", "Days"),
            new("hours", "ساعات", "Hours"), new("score", "درجة", "Score"),
        ]),
        new(KpiPermissions.MeasuringMethodLookup, KpiPermissions.SystemCode, "طريقة القياس", "Measuring method",
        [
            new("cumulative", "تراكمي", "Cumulative"), new("average", "متوسط", "Average"),
        ]),
        new(KpiPermissions.NationalGoalLookup, KpiPermissions.SystemCode, "الهدف الوطني", "National goal", []),
        new(KpiPermissions.VisionLinkLookup, KpiPermissions.SystemCode, "الارتباط بالرؤية", "Link to the vision", []),
        new(KpiPermissions.GemCriterionLookup, KpiPermissions.SystemCode, "معايير التميز", "Excellence (GEM) criteria", []),
    ];

    public static readonly FeatureDefinition ChangeControl = new(
        KpiPermissions.ChangeControlFeature, KpiPermissions.SystemCode, "ضبط تغيير تعريف المؤشر", "KPI definition change control", DefaultEnabled: true);
}
