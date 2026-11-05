using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.AuditManagement.Application;

/// <summary>Permission, role, numbering, lookup, process and file codes (docs/design/audit.md §5, ADR 0132).</summary>
public static class AuditPermissions
{
    public const string SystemCode = "auditmgmt";

    public const string Read = "auditmgmt.finding.read";
    public const string Office = "auditmgmt.office.manage";
    public const string DashboardRead = "auditmgmt.dashboard.read";
    public const string Export = "auditmgmt.report.export";
    public const string SettingsManage = "auditmgmt.settings.manage";
    public const string Lifecycle = "auditmgmt.lifecycle.manage";
    public const string PlanRead = "auditmgmt.plan.read";
    public const string WorkPapers = "auditmgmt.workpaper.manage";

    public const string AiExtract = "auditmgmt.ai.extract";
    public const string AiEvidence = "auditmgmt.ai.evidence";
    public const string AiRepeat = "auditmgmt.ai.repeat";
    public const string AiSummary = "auditmgmt.ai.summary";

    public const string DepartmentReaderRole = "department-reader";
    public const string ApproverRole = "approver";

    public const string OfficeRoleKey = "auditmgmt.audit-office";
    public const string ChiefRoleKey = "auditmgmt.chief-audit-executive";
    public const string CommitteeRoleKey = "auditmgmt.audit-committee";

    public const string ReportNumbering = "auditmgmt.report";
    public const string FindingNumbering = "auditmgmt.finding";
    public const string RecommendationNumbering = "auditmgmt.recommendation";
    public const string FollowUpNumbering = "auditmgmt.follow-up";
    public const string EngagementNumbering = "auditmgmt.engagement";

    public const string ReportTypeLookup = "auditmgmt.report-type";
    public const string PriorityLookup = "auditmgmt.priority";
    public const string FindingTypeLookup = "auditmgmt.finding-type";
    public const string ResponseLookup = "auditmgmt.management-response";
    public const string EntityKindLookup = "auditmgmt.entity-kind";

    /// <summary>Layer 2 switch (Settings → Features, off by default, ADR 0136).</summary>
    public const string LifecycleFeature = "auditmgmt.lifecycle";

    public const string FollowUpReviewProcess = "auditmgmt.follow-up-review";
    public const string PlanProcess = "auditmgmt.plan";
}

/// <summary>Report type codes (lookup <c>auditmgmt.report-type</c>) the code reasons about.</summary>
public static class ReportTypes
{
    public const string InternalAudit = "internal-audit";
    public const string ExternalAudit = "external-audit";
    public const string SpecialReport = "special-report";
    public const string RevenueAudit = "revenue-audit";
    public const string Consultancy = "consultancy";
    public const string Other = "other";
}

/// <summary>Data scope of findings (ADR 0132): unit = responsible unit; department grants are unit record roles (ADR 0130).</summary>
public static class AuditScope
{
    public static readonly ScopeMap<Finding> Finding = new(Domain.Finding.EntityType, x => x.Id, unit: x => x.ResponsibleUnitId);
}

public static class AuditFilePurposes
{
    public static readonly FilePurpose ReportFile = new("auditmgmt.report-file", AuditReport.EntityType, AuditPermissions.SystemCode, FileKinds.Documents, 50 * 1024 * 1024);

    public static readonly FilePurpose Evidence = new("auditmgmt.evidence", FollowUp.EntityType, AuditPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);

    public static readonly FilePurpose WorkPaper = new("auditmgmt.work-paper", Domain.WorkPaper.EntityType, AuditPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);
}

public static class DepartmentScopes
{
    public const string Section = "section";
    public const string Department = "department";

    public static readonly IReadOnlyList<string> All = [Section, Department];
}

public static class CapObservationModes
{
    public const string Off = "off";
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All = [Off, Manual];
}

/// <summary>Settings → Audit management (docs/design/audit.md §2.1).</summary>
public sealed record AuditMgmtSettings : ISettingsArea
{
    public static string Key => "auditmgmt";

    public string DepartmentScope { get; init; } = DepartmentScopes.Section;

    public string DepartmentLevelCode { get; init; } = "department";

    public bool NotifyDepartments { get; init; }

    public bool FollowUpReview { get; init; }

    public int FollowUpReminderDays { get; init; } = 3;

    public int DueReminderDays { get; init; } = 7;

    public bool ReminderWorkingDays { get; init; } = true;

    public string CapObservations { get; init; } = CapObservationModes.Manual;

    public Guid? KpiClosureRateCardId { get; init; }

    public Guid? KpiOverdueCardId { get; init; }

    public Guid? KpiPlanCompletionCardId { get; init; }

    public Guid? KpiReportTimelinessCardId { get; init; }

    public Guid? KpiSatisfactionCardId { get; init; }

    public int ReportTimelinessDays { get; init; } = 30;

    public static List<FieldError> Validate(AuditMgmtSettings value)
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

        Range("followUpReminderDays", value.FollowUpReminderDays, 0, 60);
        Range("dueReminderDays", value.DueReminderDays, 0, 90);
        Range("reportTimelinessDays", value.ReportTimelinessDays, 1, 365);
        if (!DepartmentScopes.All.Contains(value.DepartmentScope))
        {
            errors.Add(new FieldError("departmentScope", "validation.invalid", "validation.invalid"));
        }

        if (string.IsNullOrWhiteSpace(value.DepartmentLevelCode) || value.DepartmentLevelCode.Length > 50)
        {
            errors.Add(new FieldError("departmentLevelCode", "validation.required", "validation.required"));
        }

        if (!CapObservationModes.All.Contains(value.CapObservations))
        {
            errors.Add(new FieldError("capObservations", "validation.invalid", "validation.invalid"));
        }

        return errors;
    }
}

/// <summary>Approval processes (docs/design/audit.md §4.4 / §4.6): follow-up review (off unless the setting is on) and the annual plan.</summary>
public static class AuditProcessDefinitions
{
    public static readonly ApprovalProcessDefinition FollowUpReview = new(
        AuditPermissions.FollowUpReviewProcess, AuditPermissions.SystemCode, "مراجعة المتابعة", "Follow-up review", Recommendation.EntityType, AuditPermissions.Read,
        "/audit-management/recommendations/{id}", [],
        [new ApprovalStepDefinition("chief-audit-executive", "رئيس التدقيق الداخلي", "Chief Audit Executive",
            new ApproverRule(ApproverRuleKinds.Role, RoleKey: AuditPermissions.ChiefRoleKey), SlaHours: 24)],
        AuditPermissions.ApproverRole,
        "مراجعة داخلية اختيارية للمتابعة قبل تطبيقها على التوصية.", "Optional internal review of a follow-up before it applies to the recommendation.");

    public static readonly ApprovalProcessDefinition Plan = new(
        AuditPermissions.PlanProcess, AuditPermissions.SystemCode, "اعتماد خطة التدقيق السنوية", "Annual audit plan approval", AuditPlan.EntityType, AuditPermissions.PlanRead,
        "/audit-management/plans/{id}", [],
        [new ApprovalStepDefinition("audit-committee", "لجنة التدقيق", "Audit committee",
            new ApproverRule(ApproverRuleKinds.Role, RoleKey: AuditPermissions.CommitteeRoleKey), SlaHours: 120)],
        AuditPermissions.ApproverRole,
        "تعتمد لجنة التدقيق (أو المدير العام) خطة التدقيق السنوية المبنية على المخاطر.", "The audit committee (or the Director General) approves the risk-based annual plan.");
}

/// <summary>Bilingual templates (editable in Settings → Notifications).</summary>
public static class AuditNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "date", "actor", "text", "count"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, AuditPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition ReportIssued = T("auditmgmt.reportIssued", "إصدار تقرير تدقيق", "Audit report issued",
        "صدر تقرير التدقيق {{number}}", "Audit report {{number}} issued", "صدر تقرير التدقيق «{{titleAr}}» ({{number}}) وعدد الملاحظات {{count}}.",
        "The audit report “{{titleEn}}” ({{number}}) was issued with {{count}} finding(s).");

    public static readonly NotificationTemplateDefinition FindingCreated = T("auditmgmt.findingCreated", "ملاحظة تدقيق جديدة", "New audit finding",
        "ملاحظة تدقيق جديدة {{number}}", "New audit finding {{number}}", "سجّل مكتب التدقيق الملاحظة «{{titleAr}}» ({{number}}) على وحدتكم. للاطلاع فقط.",
        "The Audit Office recorded the finding “{{titleEn}}” ({{number}}) for your unit. For information only.");

    public static readonly NotificationTemplateDefinition FindingUpdated = T("auditmgmt.findingUpdated", "تحديث ملاحظة تدقيق", "Audit finding updated",
        "تحديث على الملاحظة {{number}}", "Update on finding {{number}}", "حدّث مكتب التدقيق الملاحظة «{{titleAr}}» ({{number}}): {{text}}",
        "The Audit Office updated the finding “{{titleEn}}” ({{number}}): {{text}}");

    public static readonly NotificationTemplateDefinition FindingClosed = T("auditmgmt.findingClosed", "إغلاق ملاحظة تدقيق", "Audit finding closed",
        "أُغلقت الملاحظة {{number}}", "Finding {{number}} closed", "أغلق مكتب التدقيق الملاحظة «{{titleAr}}» ({{number}}) — {{text}}.",
        "The Audit Office closed the finding “{{titleEn}}” ({{number}}) — {{text}}.");

    public static readonly NotificationTemplateDefinition FollowUpDue = T("auditmgmt.followUpDue", "موعد متابعة", "Follow-up due",
        "موعد متابعة التوصية {{number}} في {{date}}", "Follow-up of recommendation {{number}} due on {{date}}",
        "موعد المتابعة القادمة للتوصية «{{titleAr}}» ({{number}}) في {{date}}.", "The next follow-up of the recommendation “{{titleEn}}” ({{number}}) is due on {{date}}.");

    public static readonly NotificationTemplateDefinition DueSoon = T("auditmgmt.dueSoon", "اقتراب موعد تنفيذ توصية", "Recommendation due soon",
        "تاريخ تنفيذ التوصية {{number}} يقترب ({{date}})", "Recommendation {{number}} is due on {{date}}",
        "تاريخ تنفيذ التوصية «{{titleAr}}» ({{number}}) هو {{date}} ونسبة الإنجاز {{text}}.", "The recommendation “{{titleEn}}” ({{number}}) is due on {{date}}; achieved {{text}}.");

    public static readonly NotificationTemplateDefinition RecommendationLate = T("auditmgmt.recommendationLate", "توصية متأخرة", "Recommendation late",
        "التوصية {{number}} متأخرة", "Recommendation {{number}} is late", "تجاوزت التوصية «{{titleAr}}» ({{number}}) تاريخ التنفيذ {{date}} دون اكتمال.",
        "The recommendation “{{titleEn}}” ({{number}}) passed its due date {{date}} without completion.");

    public static readonly NotificationTemplateDefinition FollowUpReviewed = T("auditmgmt.followUpReviewed", "نتيجة مراجعة المتابعة", "Follow-up review decision",
        "قرار مراجعة المتابعة {{number}}", "Review decision on follow-up {{number}}", "قرار مراجعة المتابعة ({{number}}): {{text}}", "Review decision on follow-up {{number}}: {{text}}");

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
        [ReportIssued, FindingCreated, FindingUpdated, FindingClosed, FollowUpDue, DueSoon, RecommendationLate, FollowUpReviewed];
}

/// <summary>The Audit Management catalogue (SPEC §10.6 defaults; editable in Settings → Roles).</summary>
internal sealed class AuditAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(AuditPermissions.SystemCode, SystemKind.Module, "إدارة التدقيق", "Audit Management", 36);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(AuditPermissions.Read, "عرض تقارير وملاحظات التدقيق", "View audit reports and findings"),
        new(AuditPermissions.Office, "إجراءات مكتب التدقيق (التسجيل والمتابعة والإغلاق)", "Audit Office actions (record, follow up, close)"),
        new(AuditPermissions.DashboardRead, "عرض لوحة التدقيق", "View the audit dashboard", Scopable: false),
        new(AuditPermissions.Export, "تصدير تقارير التدقيق", "Export audit reports", Scopable: false),
        new(AuditPermissions.SettingsManage, "إعدادات إدارة التدقيق", "Audit management settings", Scopable: false),
        new(AuditPermissions.Lifecycle, "دورة التدقيق (المجال والخطة والمهام)", "Audit lifecycle (universe, plan, engagements)", Scopable: false),
        new(AuditPermissions.PlanRead, "عرض خطة التدقيق السنوية", "View the annual audit plan"),
        new(AuditPermissions.WorkPapers, "برامج وأوراق عمل التدقيق", "Audit programmes and working papers", Scopable: false),
        new(AuditPermissions.AiExtract, "استخراج الملاحظات من تقرير التدقيق", "Findings extraction from an audit report", Scopable: false),
        new(AuditPermissions.AiEvidence, "مراجعة الأدلة وصياغة تعليق المتابعة", "Evidence review and follow-up comment draft", Scopable: false),
        new(AuditPermissions.AiRepeat, "كشف الملاحظات المتكررة", "Repeat-finding detection", Scopable: false),
        new(AuditPermissions.AiSummary, "ملخص لجنة التدقيق", "Audit committee summary", Scopable: false),
    ];

    private static readonly PermissionGrant[] OfficeGrants =
    [
        new(AuditPermissions.Read, DataScope.All), new(AuditPermissions.Office, DataScope.All), new(AuditPermissions.DashboardRead), new(AuditPermissions.Export),
        new(AuditPermissions.SettingsManage), new(AuditPermissions.Lifecycle), new(AuditPermissions.PlanRead, DataScope.All), new(AuditPermissions.WorkPapers),
        new(AuditPermissions.AiExtract), new(AuditPermissions.AiEvidence), new(AuditPermissions.AiRepeat), new(AuditPermissions.AiSummary),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("audit-office", "مكتب التدقيق الداخلي", "Audit Office", OfficeGrants, RequiresMfa: true),
        new("chief-audit-executive", "رئيس التدقيق الداخلي", "Chief Audit Executive", OfficeGrants, RequiresMfa: true),
        new("department-user", "مستخدم الإدارة (اطلاع على ملاحظات التدقيق)", "Department user (audit findings, read-only)",
            [new(AuditPermissions.Read, DataScope.UnitAndBelow)], AssignToEveryone: true),
        new("audit-committee", "لجنة التدقيق / القيادة", "Audit committee / leadership",
        [
            new(AuditPermissions.Read, DataScope.All), new(AuditPermissions.DashboardRead), new(AuditPermissions.Export), new(AuditPermissions.PlanRead, DataScope.All),
            new(AuditPermissions.AiSummary),
        ]),
        new("viewer", "مستعرض التدقيق", "Audit viewer", [new(AuditPermissions.Read, DataScope.All)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(Finding.EntityType, AuditPermissions.DepartmentReaderRole, "الإدارة المعنية (اطلاع)", "Concerned department (read)", [AuditPermissions.Read]),
        new(Recommendation.EntityType, AuditPermissions.ApproverRole, "مراجع المتابعة", "Follow-up reviewer", [AuditPermissions.Read]),
        new(AuditPlan.EntityType, AuditPermissions.ApproverRole, "معتمد خطة التدقيق", "Audit plan approver", [AuditPermissions.PlanRead]),
    ];

    public static readonly IReadOnlyList<NumberingDefinition> Numbering =
    [
        new(AuditPermissions.ReportNumbering, AuditPermissions.SystemCode, "رقم تقرير التدقيق", "Audit report number", "AUD-{YYYY}-{SEQ:3}"),
        new(AuditPermissions.FindingNumbering, AuditPermissions.SystemCode, "رقم ملاحظة التدقيق", "Audit finding number", "AR-{SEQ:4}"),
        new(AuditPermissions.RecommendationNumbering, AuditPermissions.SystemCode, "رقم التوصية", "Recommendation number", "AA-{SEQ:4}"),
        new(AuditPermissions.FollowUpNumbering, AuditPermissions.SystemCode, "رقم المتابعة", "Follow-up number", "AAQ-{SEQ:4}"),
        new(AuditPermissions.EngagementNumbering, AuditPermissions.SystemCode, "رقم مهمة التدقيق", "Audit engagement number", "ENG-{YYYY}-{SEQ:3}"),
    ];

    public static readonly FeatureDefinition LifecycleFeature = new(AuditPermissions.LifecycleFeature, AuditPermissions.SystemCode, "دورة التدقيق الكاملة (المرحلة الثانية)",
        "Full audit lifecycle (layer 2)", DefaultEnabled: false);

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(AuditPermissions.ReportTypeLookup, AuditPermissions.SystemCode, "نوع تقرير التدقيق", "Audit report type",
        [
            new(ReportTypes.InternalAudit, "تدقيق داخلي", "Internal audit"), new(ReportTypes.ExternalAudit, "تدقيق خارجي (جهاز الرقابة)", "External audit (e.g. State Audit Institution)"),
            new(ReportTypes.SpecialReport, "تقرير خاص", "Special report"), new(ReportTypes.RevenueAudit, "تدقيق الإيرادات", "Revenue audit"),
            new(ReportTypes.Consultancy, "استشارة", "Consultancy"), new(ReportTypes.Other, "أخرى", "Other"),
        ]),
        new(AuditPermissions.PriorityLookup, AuditPermissions.SystemCode, "أولوية / تصنيف خطورة الملاحظة", "Finding priority / risk rating",
        [
            new(AuditPriorities.Low, "منخفضة", "Low"), new(AuditPriorities.Medium, "متوسطة", "Medium"), new(AuditPriorities.High, "عالية", "High"),
            new(AuditPriorities.Critical, "حرجة", "Critical"),
        ]),
        new(AuditPermissions.FindingTypeLookup, AuditPermissions.SystemCode, "نوع الملاحظة", "Finding type",
            [new(FindingTypes.Finding, "ملاحظة تدقيق رسمية", "Formal finding"), new(FindingTypes.Improvement, "فرصة تحسين", "Improvement observation")]),
        new(AuditPermissions.ResponseLookup, AuditPermissions.SystemCode, "رد الإدارة", "Management response",
            [new("agree", "موافق", "Agree"), new("partially-agree", "موافق جزئياً", "Partially agree"), new("disagree", "غير موافق", "Disagree")]),
        new(AuditPermissions.EntityKindLookup, AuditPermissions.SystemCode, "نوع الكيان القابل للتدقيق", "Auditable entity kind",
        [
            new("unit", "وحدة تنظيمية", "Organisation unit"), new("process", "عملية", "Process"), new("service", "خدمة", "Service"), new("system", "نظام", "System"),
            new("project", "مشروع", "Project"), new("risk", "خطر", "Risk"),
        ]),
    ];
}
