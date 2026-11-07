using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk.Application;

/// <summary>Permission, role, numbering, lookup and file codes (docs/design/risk.md §7).</summary>
public static class RiskPermissions
{
    public const string SystemCode = "risk";

    public const string Read = "risk.risk.read";
    public const string Create = "risk.risk.create";
    public const string Edit = "risk.risk.edit";
    public const string Submit = "risk.risk.submit";
    public const string Office = "risk.risk.office";
    public const string CloseRequest = "risk.risk.close-request";
    public const string CommitteeDecide = "risk.committee.decide";
    public const string ActionUpdate = "risk.action.update";
    public const string ControlUpdate = "risk.control.update";
    public const string EventManage = "risk.event.manage";
    public const string DashboardRead = "risk.dashboard.read";
    public const string Export = "risk.report.export";
    public const string ProjectRisksRead = "risk.project-risks.read";
    public const string FrameworkManage = "risk.framework.manage";

    public const string AiIdentify = "risk.ai.identify";
    public const string AiQuality = "risk.ai.quality";
    public const string AiSuggest = "risk.ai.suggest";
    public const string AiDuplicates = "risk.ai.duplicates";
    public const string AiReview = "risk.ai.review";
    public const string AiReport = "risk.ai.report";

    public const string ApproverRole = "approver";
    public const string ActionOwnerRole = "action-owner";
    public const string ControlOwnerRole = "control-owner";

    /// <summary>Approval-engine role keys (ADR 0107).</summary>
    public const string RiskOfficeRoleKey = "risk.risk-office";
    public const string RiskCommitteeRoleKey = "risk.risk-committee";

    public const string RiskNumbering = "risk.risk";
    public const string ActionNumbering = "risk.action";
    public const string EventNumbering = "risk.event";

    public const string ResponseLookup = "risk.response";
    public const string ControlTypeLookup = "risk.control-type";
    public const string ControlNatureLookup = "risk.control-nature";
    public const string ControlFrequencyLookup = "risk.control-frequency";
    public const string ClosureReasonLookup = "risk.closure-reason";
    public const string CommitteeDecisionLookup = "risk.committee-decision";
    public const string DeterminedByLookup = "risk.determined-by";

    /// <summary>Category code under which project risks appear on dashboards (ADR 0103).</summary>
    public const string ProjectCategory = "project";
}

/// <summary>Data scopes (ADR 0015): own = risk owner, unit = owning unit (the register).</summary>
public static class RiskScope
{
    public static readonly ScopeMap<RiskRecord> Risk = new(RiskRecord.EntityType, x => x.Id, ownerEmployee: x => x.OwnerEmployeeId, unit: x => x.UnitId);
}

public static class RiskFilePurposes
{
    public static readonly FilePurpose RiskDocument = new("risk.document", RiskRecord.EntityType, RiskPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public static readonly FilePurpose ActionEvidence = new("risk.action-evidence", TreatmentAction.EntityType, RiskPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public static readonly FilePurpose ControlEvidence = new("risk.control-evidence", Control.EntityType, RiskPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);
}

/// <summary>Settings → Risk (docs/design/risk.md §2.2).</summary>
public sealed record RiskSettings : ISettingsArea
{
    public static string Key => "risk";

    public int TopCount { get; init; } = 10;

    public int ReviewReminderWorkingDays { get; init; } = 10;

    public int ActionReminderWorkingDays { get; init; } = 3;

    public int ActionEscalationWorkingDays { get; init; } = 2;

    public bool OpportunitiesEnabled { get; init; }

    public bool EscalateCritical { get; init; } = true;

    public bool EscalateOutsideAppetite { get; init; } = true;

    public bool KriWarningNotifies { get; init; }

    public bool IncludeProjectRisksByDefault { get; init; } = true;

    public string OrganisationCode { get; init; } = "ORG";

    public static List<FieldError> Validate(RiskSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        void Range(string field, int actual, int min, int max)
        {
            if (actual < min || actual > max)
            {
                errors.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = min, ["to"] = max }));
            }
        }

        Range("topCount", value.TopCount, 3, 50);
        Range("reviewReminderWorkingDays", value.ReviewReminderWorkingDays, 0, 60);
        Range("actionReminderWorkingDays", value.ActionReminderWorkingDays, 0, 30);
        Range("actionEscalationWorkingDays", value.ActionEscalationWorkingDays, 0, 60);
        if (string.IsNullOrWhiteSpace(value.OrganisationCode) || value.OrganisationCode.Length > 20)
        {
            errors.Add(new FieldError("organisationCode", "validation.invalid", "validation.invalid"));
        }

        return errors;
    }
}

/// <summary>Approval processes (docs/design/risk.md §6, ADR 0102). Steps are configuration after seeding.</summary>
public static class RiskProcesses
{
    public const string Review = "risk.review";
    public const string PeriodicReview = "risk.periodic-review";
    public const string Closure = "risk.closure";

    private static readonly ExpressionVariable[] Variables =
    [
        new("inherentBand", ExpressionValueType.Text, "نطاق الخطر الكامن", "Inherent rating"),
        new("residualBand", ExpressionValueType.Text, "نطاق الخطر المتبقي", "Residual rating"),
        new("residualScore", ExpressionValueType.Number, "درجة الخطر المتبقي", "Residual score"),
        new("withinAppetite", ExpressionValueType.Boolean, "ضمن الرغبة في المخاطر", "Within appetite"),
        new("top10", ExpressionValueType.Boolean, "ضمن أهم المخاطر", "Top-10 risk"),
    ];

    private static ApprovalStepDefinition OfficeStep() =>
        new("risk-office", "مكتب المخاطر", "Risk Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: RiskPermissions.RiskOfficeRoleKey), SlaHours: 40);

    public static readonly ApprovalProcessDefinition ReviewDefinition = new(
        Review, RiskPermissions.SystemCode, "مراجعة تسجيل خطر", "Risk registration review", RiskRecord.EntityType, RiskPermissions.Read, "/risk/risks/{id}", Variables,
        [OfficeStep()], RiskPermissions.ApproverRole,
        "يراجع مكتب المخاطر الخطر الجديد ويعتمده أو يعيده للتصحيح أو يرفضه (مكرر / ليس خطراً).",
        "The Risk Office reviews a new risk and approves it, returns it for correction or rejects it (duplicate / not a risk).");

    public static readonly ApprovalProcessDefinition PeriodicReviewDefinition = new(
        PeriodicReview, RiskPermissions.SystemCode, "المراجعة الدورية للخطر", "Periodic risk review", RiskRecord.EntityType, RiskPermissions.Read, "/risk/risks/{id}?tab=reviews",
        Variables, [OfficeStep()], RiskPermissions.ApproverRole,
        "اعتماد إعادة تقييم الخطر وفعالية الضوابط.", "Approval of the risk reassessment and control effectiveness.");

    public static readonly ApprovalProcessDefinition ClosureDefinition = new(
        Closure, RiskPermissions.SystemCode, "إغلاق خطر", "Risk closure", RiskRecord.EntityType, RiskPermissions.Read, "/risk/risks/{id}", Variables,
        [
            OfficeStep(),
            new("risk-committee", "لجنة المخاطر", "Risk Committee", new ApproverRule(ApproverRuleKinds.Role, RoleKey: RiskPermissions.RiskCommitteeRoleKey),
                Condition: "residualBand == \"critical\"", SlaHours: 80),
        ],
        RiskPermissions.ApproverRole,
        "يعتمد مكتب المخاطر الإغلاق؛ وتعتمد لجنة المخاطر أيضاً إغلاق المخاطر الحرجة.", "The Risk Office approves the closure; the Risk Committee also approves closing Critical risks.");
}

/// <summary>Bilingual templates (editable in Settings → Notifications).</summary>
public static class RiskNotifications
{
    private static readonly string[] Common = ["name", "number", "riskAr", "riskEn", "date", "actor", "text", "band", "count"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, RiskPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition Approved = T("risk.approved", "اعتماد خطر", "Risk approved", "اعتُمد الخطر {{number}}", "Risk {{number}} approved",
        "اعتمد مكتب المخاطر الخطر «{{riskAr}}» ({{number}}). المراجعة القادمة في {{date}}.", "The Risk Office approved “{{riskEn}}” ({{number}}). Next review on {{date}}.");

    public static readonly NotificationTemplateDefinition Returned = T("risk.returned", "إعادة خطر للتصحيح", "Risk returned", "أعيد الخطر {{number}} للتصحيح",
        "Risk {{number}} returned for correction", "أعاد {{actor}} الخطر «{{riskAr}}» للتصحيح: {{text}}", "{{actor}} returned “{{riskEn}}” for correction: {{text}}");

    public static readonly NotificationTemplateDefinition Rejected = T("risk.rejected", "رفض خطر", "Risk rejected", "رُفض الخطر {{number}}", "Risk {{number}} rejected",
        "رفض {{actor}} الخطر «{{riskAr}}»: {{text}}", "{{actor}} rejected “{{riskEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition ReviewDue = T("risk.reviewDue", "تذكير بمراجعة خطر", "Risk review due", "مراجعة الخطر {{number}} مستحقة",
        "Review of risk {{number}} is due", "مراجعة الخطر «{{riskAr}}» ({{number}}) مستحقة في {{date}}.", "The review of “{{riskEn}}” ({{number}}) is due on {{date}}.");

    public static readonly NotificationTemplateDefinition ReviewOverdue = T("risk.reviewOverdue", "تأخر مراجعة خطر", "Risk review overdue", "تأخرت مراجعة الخطر {{number}}",
        "Review of risk {{number}} is overdue", "تجاوزت مراجعة الخطر «{{riskAr}}» ({{number}}) تاريخ الاستحقاق {{date}}.",
        "The review of “{{riskEn}}” ({{number}}) passed its due date {{date}}.");

    public static readonly NotificationTemplateDefinition ActionDue = T("risk.actionDue", "تذكير بإجراء معالجة", "Treatment action due", "الإجراء {{number}} مستحق قريباً",
        "Action {{number}} is due soon", "إجراء المعالجة «{{riskAr}}» ({{number}}) مستحق في {{date}}.", "The treatment action “{{riskEn}}” ({{number}}) is due on {{date}}.");

    public static readonly NotificationTemplateDefinition ActionOverdue = T("risk.actionOverdue", "تأخر إجراء معالجة", "Treatment action overdue", "تأخر الإجراء {{number}}",
        "Action {{number}} is overdue", "تجاوز إجراء المعالجة «{{riskAr}}» ({{number}}) تاريخ الانتهاء {{date}}.",
        "The treatment action “{{riskEn}}” ({{number}}) passed its planned end {{date}}.");

    public static readonly NotificationTemplateDefinition KriBreach = T("risk.kriBreach", "تجاوز مؤشر خطر رئيسي", "Key risk indicator breached",
        "تجاوز مؤشر الخطر الرئيسي للخطر {{number}}", "Key risk indicator breached for risk {{number}}",
        "تجاوزت نتيجة معتمدة للمؤشر {{text}} حد الخطر «{{riskAr}}» ({{number}}). يلزم إعادة تقييم الخطر.",
        "An approved result of {{text}} breached the threshold of “{{riskEn}}” ({{number}}). The risk needs reassessment.");

    public static readonly NotificationTemplateDefinition Escalated = T("risk.escalated", "تصعيد خطر", "Risk escalated", "صُعّد الخطر {{number}} ({{band}})",
        "Risk {{number}} escalated ({{band}})", "صُعّد الخطر «{{riskAr}}» ({{number}}) إلى القيادة / لجنة المخاطر: {{text}}.",
        "“{{riskEn}}” ({{number}}) was escalated to leadership / the Risk Committee: {{text}}.");

    public static readonly NotificationTemplateDefinition Closed = T("risk.closed", "إغلاق خطر", "Risk closed", "أُغلق الخطر {{number}}", "Risk {{number}} closed",
        "اعتُمد إغلاق الخطر «{{riskAr}}» ({{number}}).", "The closure of “{{riskEn}}” ({{number}}) was approved.");

    public static readonly NotificationTemplateDefinition Reassessment = T("risk.reassessment", "إعادة تقييم خطر", "Risk reassessment needed",
        "الخطر {{number}} يحتاج إلى إعادة تقييم", "Risk {{number}} needs reassessment", "يحتاج الخطر «{{riskAr}}» ({{number}}) إلى مراجعة غير مجدولة: {{text}}.",
        "“{{riskEn}}” ({{number}}) needs an unscheduled review: {{text}}.");

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
        [Approved, Returned, Rejected, ReviewDue, ReviewOverdue, ActionDue, ActionOverdue, KriBreach, Escalated, Closed, Reassessment];
}

/// <summary>The Risk Management catalogue (SPEC §3.7 defaults; editable in Settings → Roles).</summary>
internal sealed class RiskAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(RiskPermissions.SystemCode, SystemKind.Module, "إدارة المخاطر", "Risk Management", 30);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(RiskPermissions.Read, "عرض المخاطر", "View risks"),
        new(RiskPermissions.Create, "تسجيل المخاطر", "Register risks"),
        new(RiskPermissions.Edit, "تعديل المخاطر والضوابط والإجراءات", "Edit risks, controls and actions"),
        new(RiskPermissions.Submit, "تقديم المخاطر والمراجعات", "Submit risks and reviews"),
        new(RiskPermissions.Office, "إجراءات مكتب المخاطر (التقييمات وأهم المخاطر والتصعيد وإعادة الفتح)", "Risk Office actions (ratings, Top-10, escalation, reopen)"),
        new(RiskPermissions.CloseRequest, "طلب إغلاق خطر", "Request risk closure"),
        new(RiskPermissions.CommitteeDecide, "تسجيل قرارات لجنة المخاطر", "Record Risk Committee decisions"),
        new(RiskPermissions.ActionUpdate, "تحديث تقدم إجراءات المعالجة", "Update treatment action progress"),
        new(RiskPermissions.ControlUpdate, "تحديث فعالية الضوابط", "Update control effectiveness"),
        new(RiskPermissions.EventManage, "تسجيل أحداث المخاطر", "Log risk events"),
        new(RiskPermissions.DashboardRead, "عرض لوحة المخاطر", "View the risk dashboard", Scopable: false),
        new(RiskPermissions.Export, "تصدير تقارير المخاطر", "Export risk reports", Scopable: false),
        new(RiskPermissions.ProjectRisksRead, "عرض مخاطر المشاريع", "View project risks", Scopable: false),
        new(RiskPermissions.FrameworkManage, "إدارة إطار المخاطر", "Manage the risk framework", Scopable: false),
        new(RiskPermissions.AiIdentify, "مساعد تحديد المخاطر", "Risk identification assistant", Scopable: false),
        new(RiskPermissions.AiQuality, "فحص جودة صياغة الخطر", "Risk statement quality check", Scopable: false),
        new(RiskPermissions.AiSuggest, "اقتراح الضوابط والمعالجة", "Control and treatment suggestions", Scopable: false),
        new(RiskPermissions.AiDuplicates, "كشف المخاطر المكررة", "Duplicate detection", Scopable: false),
        new(RiskPermissions.AiReview, "مساعد المراجعة", "Review assistant", Scopable: false),
        new(RiskPermissions.AiReport, "كاتب تقرير المخاطر", "Risk report writer", Scopable: false),
        ResponsibilityPermissions.Definition(RiskPermissions.SystemCode),
    ];

    private static readonly string[] Ai =
        [RiskPermissions.AiIdentify, RiskPermissions.AiQuality, RiskPermissions.AiSuggest, RiskPermissions.AiDuplicates, RiskPermissions.AiReview];

    /// <summary>Members page lists (ADR 0166, docs/design/members-responsibilities.md §1).</summary>
    public SystemMemberRoles MemberRoles => new(["risk-office"], ["owner"], ["coordinator", "viewer", "risk-committee", "internal-audit"]);

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("risk-office", "مكتب المخاطر", "Risk Office",
        [
            new(RiskPermissions.Read, DataScope.All), new(RiskPermissions.Create, DataScope.All), new(RiskPermissions.Edit, DataScope.All),
            new(RiskPermissions.Submit, DataScope.All), new(RiskPermissions.Office, DataScope.All), new(RiskPermissions.CloseRequest, DataScope.All),
            new(RiskPermissions.ActionUpdate, DataScope.All), new(RiskPermissions.ControlUpdate, DataScope.All), new(RiskPermissions.EventManage, DataScope.All),
            new(RiskPermissions.DashboardRead), new(RiskPermissions.Export), new(RiskPermissions.ProjectRisksRead), new(RiskPermissions.FrameworkManage),
            .. Ai.Select(a => new PermissionGrant(a)), new(RiskPermissions.AiReport),
            new(ResponsibilityPermissions.Manage(RiskPermissions.SystemCode), DataScope.All),
        ], RequiresMfa: true),
        new("coordinator", "منسق المخاطر في الإدارة", "Department risk coordinator",
        [
            new(RiskPermissions.Read, DataScope.UnitAndBelow), new(RiskPermissions.Create, DataScope.UnitAndBelow), new(RiskPermissions.Edit, DataScope.UnitAndBelow),
            new(RiskPermissions.Submit, DataScope.UnitAndBelow), new(RiskPermissions.CloseRequest, DataScope.UnitAndBelow),
            new(RiskPermissions.ActionUpdate, DataScope.UnitAndBelow), new(RiskPermissions.ControlUpdate, DataScope.UnitAndBelow),
            new(RiskPermissions.EventManage, DataScope.UnitAndBelow), new(RiskPermissions.DashboardRead), new(RiskPermissions.Export),
            new(RiskPermissions.ProjectRisksRead), .. Ai.Select(a => new PermissionGrant(a)),
        ]),
        new("owner", "مالك الخطر", "Risk owner",
        [
            new(RiskPermissions.Read, DataScope.Own), new(RiskPermissions.Edit, DataScope.Own), new(RiskPermissions.Submit, DataScope.Own),
            new(RiskPermissions.CloseRequest, DataScope.Own), new(RiskPermissions.ActionUpdate, DataScope.Own), new(RiskPermissions.ControlUpdate, DataScope.Own),
            new(RiskPermissions.AiQuality), new(RiskPermissions.AiSuggest), new(RiskPermissions.AiReview),
        ], AssignToEveryone: true),
        new("viewer", "مستعرض مخاطر الإدارة", "Department risk viewer",
            [new(RiskPermissions.Read, DataScope.UnitAndBelow), new(RiskPermissions.DashboardRead)]),
        new("risk-committee", "لجنة المخاطر / القيادة", "Risk Committee / leadership",
        [
            new(RiskPermissions.Read, DataScope.All), new(RiskPermissions.CommitteeDecide, DataScope.All), new(RiskPermissions.DashboardRead),
            new(RiskPermissions.Export), new(RiskPermissions.ProjectRisksRead), new(RiskPermissions.AiReport),
        ]),
        new("internal-audit", "التدقيق الداخلي (مستعرض)", "Internal Audit (viewer)",
            [new(RiskPermissions.Read, DataScope.All), new(RiskPermissions.DashboardRead), new(RiskPermissions.Export), new(RiskPermissions.ProjectRisksRead)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(RiskRecord.EntityType, RiskPermissions.ApproverRole, "معتمد الخطر", "Risk approver", [RiskPermissions.Read]),
        new(RiskRecord.EntityType, RiskPermissions.ActionOwnerRole, "مسؤول إجراء معالجة", "Treatment action owner", [RiskPermissions.Read, RiskPermissions.ActionUpdate]),
        new(RiskRecord.EntityType, RiskPermissions.ControlOwnerRole, "مسؤول ضابط", "Control owner", [RiskPermissions.Read, RiskPermissions.ControlUpdate]),
    ];

    public static readonly NumberingDefinition RiskNumbering = new(RiskPermissions.RiskNumbering, RiskPermissions.SystemCode, "رقم الخطر", "Risk number", "{UNIT}{YY}-R{SEQ:3}");

    public static readonly NumberingDefinition ActionNumbering = new(RiskPermissions.ActionNumbering, RiskPermissions.SystemCode, "رقم إجراء المعالجة", "Treatment action number", "ORA-{SEQ:4}");

    public static readonly NumberingDefinition EventNumbering = new(RiskPermissions.EventNumbering, RiskPermissions.SystemCode, "رقم حدث الخطر", "Risk event number", "RE-{YYYY}-{SEQ:3}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(RiskPermissions.ResponseLookup, RiskPermissions.SystemCode, "استراتيجية الاستجابة", "Response strategy",
        [
            new("accept", "قبول", "Accept"), new("avoid", "تجنب", "Avoid"), new("reduce", "تخفيف", "Reduce (mitigate)"), new("transfer", "نقل", "Transfer"),
            new("exploit", "استغلال", "Exploit"), new("enhance", "تعزيز", "Enhance"),
        ]),
        new(RiskPermissions.ControlTypeLookup, RiskPermissions.SystemCode, "نوع الضابط", "Control type",
        [
            new("preventive", "وقائي", "Preventive"), new("detective", "كاشف", "Detective"), new("corrective", "تصحيحي", "Corrective"), new("directive", "توجيهي", "Directive"),
        ]),
        new(RiskPermissions.ControlNatureLookup, RiskPermissions.SystemCode, "طبيعة الضابط", "Control nature",
            [new("manual", "يدوي", "Manual"), new("automated", "آلي", "Automated"), new("hybrid", "مختلط", "Hybrid")]),
        new(RiskPermissions.ControlFrequencyLookup, RiskPermissions.SystemCode, "تكرار الضابط", "Control frequency",
        [
            new("continuous", "مستمر", "Continuous"), new("daily", "يومي", "Daily"), new("weekly", "أسبوعي", "Weekly"), new("monthly", "شهري", "Monthly"),
            new("quarterly", "ربع سنوي", "Quarterly"), new("annually", "سنوي", "Annually"), new("ad-hoc", "عند الحاجة", "Ad hoc"),
        ]),
        new(RiskPermissions.ClosureReasonLookup, RiskPermissions.SystemCode, "سبب إغلاق الخطر", "Risk closure reason",
        [
            new("no-longer-relevant", "لم يعد قائماً", "No longer relevant"), new("fully-treated", "عولج بالكامل", "Fully treated"),
            new("transferred", "نُقل", "Transferred"), new("other", "أخرى", "Other"),
        ]),
        new(RiskPermissions.CommitteeDecisionLookup, RiskPermissions.SystemCode, "قرار لجنة المخاطر", "Risk Committee decision",
        [
            new("accept", "قبول الخطر", "Accept the risk"), new("additional-treatment", "معالجة إضافية", "Additional treatment"),
            new("transfer", "نقل الخطر", "Transfer"), new("other", "أخرى", "Other"),
        ]),
        new(RiskPermissions.DeterminedByLookup, RiskPermissions.SystemCode, "مصدر تحديد الخطر", "Risk determined by",
        [
            new("risk-workshop", "ورشة مخاطر", "Risk workshop"), new("management", "الإدارة", "Management"), new("audit", "التدقيق", "Audit"),
            new("project", "مشروع", "Project"), new("other", "أخرى", "Other"),
        ]),
    ];
}
