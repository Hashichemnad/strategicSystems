using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Cap.Application;

/// <summary>Permission, role, numbering, lookup and file codes (docs/design/cap.md §7).</summary>
public static class CapPermissions
{
    public const string SystemCode = "cap";

    public const string Read = "cap.remark.read";
    public const string SourceManage = "cap.source.manage";
    public const string Respond = "cap.remark.respond";
    public const string ActionUpdate = "cap.action.update";
    public const string Office = "cap.office.manage";
    public const string DashboardRead = "cap.dashboard.read";
    public const string Export = "cap.report.export";
    public const string SettingsManage = "cap.settings.manage";

    public const string AiExtract = "cap.ai.extract";
    public const string AiRootCause = "cap.ai.root-cause";
    public const string AiSuggest = "cap.ai.suggest";
    public const string AiQuality = "cap.ai.quality";
    public const string AiSummary = "cap.ai.summary";

    public const string RemarkOwnerRole = "remark-owner";
    public const string AssigneeRole = "action-assignee";
    public const string SourceViewerRole = "source-viewer";
    public const string ApproverRole = "approver";

    /// <summary>Approval-engine role key of the PM Office (ADR 0125).</summary>
    public const string OfficeRoleKey = "cap.cap-office";

    public const string SourceNumbering = "cap.source";
    public const string RemarkNumbering = "cap.remark";

    public const string SourceTypeLookup = "cap.source-type";
    public const string RemarkTypeLookup = "cap.remark-type";
    public const string CriterionLookup = "cap.criterion";
    public const string ActionTypeLookup = "cap.action-type";
    public const string PriorityLookup = "cap.priority";
}

/// <summary>Data scope of remarks (ADR 0015 / 0125): own = remark owner, unit = concerned department.</summary>
public static class CapScope
{
    public static readonly ScopeMap<Remark> Remark = new(Domain.Remark.EntityType, x => x.Id, ownerEmployee: x => x.OwnerEmployeeId, unit: x => x.DepartmentUnitId);
}

public static class CapFilePurposes
{
    public static readonly FilePurpose SourceDocument = new("cap.source-document", CapSource.EntityType, CapPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);

    public static readonly FilePurpose RootCause = new("cap.root-cause", Remark.EntityType, CapPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public static readonly FilePurpose Evidence = new("cap.action-evidence", CapAction.EntityType, CapPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);
}

/// <summary>Modes of the integrations that raise remarks (ADR 0123).</summary>
public static class RaiseModes
{
    public const string Off = "off";
    public const string Manual = "manual";
    public const string Automatic = "automatic";

    public static readonly IReadOnlyList<string> All = [Off, Manual, Automatic];
}

/// <summary>Settings → CAP (docs/design/cap.md §2.1).</summary>
public sealed record CapSettings : ISettingsArea
{
    public static string Key => "cap";

    public static readonly IReadOnlyList<ExpressionVariable> RuleVariables =
    [
        new("priority", ExpressionValueType.Text, "الأولوية", "Priority"),
        new("type", ExpressionValueType.Text, "نوع الملاحظة", "Remark type"),
        new("sourceType", ExpressionValueType.Text, "نوع المصدر", "Source type"),
        new("criterion", ExpressionValueType.Text, "المعيار", "Criterion"),
    ];

    public int DueDaysCritical { get; init; } = 30;

    public int DueDaysHigh { get; init; } = 60;

    public int DueDaysMedium { get; init; } = 90;

    public int DueDaysLow { get; init; } = 180;

    public bool CountWorkingDays { get; init; }

    public bool RejectionNeedsPmoConfirmation { get; init; }

    public string RootCauseRule { get; init; } =
        "priority == \"critical\" || priority == \"high\" || sourceType == \"internal-audit\" || sourceType == \"external-audit\"";

    public bool EffectivenessVerification { get; init; }

    public int EffectivenessMonths { get; init; } = 3;

    public int MonthlyReminderDay { get; init; } = 1;

    public int EscalatePmoWorkingDays { get; init; } = 5;

    public bool MirrorProjectProgress { get; init; }

    public string KpiMissMode { get; init; } = RaiseModes.Manual;

    public int KpiMissConsecutive { get; init; } = 1;

    public string RiskEventMode { get; init; } = RaiseModes.Manual;

    public string RiskControlMode { get; init; } = RaiseModes.Manual;

    public bool ReassignWithinUnit { get; init; } = true;

    public int DueDays(string priority) => priority switch
    {
        CapPriorities.Critical => DueDaysCritical,
        CapPriorities.High => DueDaysHigh,
        CapPriorities.Low => DueDaysLow,
        _ => DueDaysMedium,
    };

    public static List<FieldError> Validate(CapSettings value)
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

        Range("dueDaysCritical", value.DueDaysCritical, 1, 730);
        Range("dueDaysHigh", value.DueDaysHigh, 1, 730);
        Range("dueDaysMedium", value.DueDaysMedium, 1, 730);
        Range("dueDaysLow", value.DueDaysLow, 1, 730);
        Range("effectivenessMonths", value.EffectivenessMonths, 1, 36);
        Range("monthlyReminderDay", value.MonthlyReminderDay, 1, 28);
        Range("escalatePmoWorkingDays", value.EscalatePmoWorkingDays, 0, 60);
        Range("kpiMissConsecutive", value.KpiMissConsecutive, 1, 12);
        foreach (var (field, mode) in new[] { ("kpiMissMode", value.KpiMissMode), ("riskEventMode", value.RiskEventMode), ("riskControlMode", value.RiskControlMode) })
        {
            if (!RaiseModes.All.Contains(mode))
            {
                errors.Add(new FieldError(field, "validation.invalid", "validation.invalid"));
            }
        }

        if (!string.IsNullOrWhiteSpace(value.RootCauseRule))
        {
            var compiled = ExpressionEngine.Compile(value.RootCauseRule, RuleVariables.ToList(), ExpressionValueType.Boolean);
            if (compiled.Errors.Count > 0)
            {
                errors.Add(new FieldError("rootCauseRule", compiled.Errors[0].Code, compiled.Errors[0].MessageKey));
            }
        }

        return errors;
    }

    /// <summary>True when the root-cause rule requires an analysis for this remark (an empty rule never requires it).</summary>
    public bool RootCauseRequired(string priority, string type, string sourceType, string? criterion)
    {
        if (string.IsNullOrWhiteSpace(RootCauseRule))
        {
            return false;
        }

        var compiled = ExpressionEngine.Compile(RootCauseRule, RuleVariables.ToList(), ExpressionValueType.Boolean);
        if (compiled.Expression is null)
        {
            return false;
        }

        var result = compiled.Expression.Evaluate(new Dictionary<string, object?>
        {
            ["priority"] = priority,
            ["type"] = type,
            ["sourceType"] = sourceType,
            ["criterion"] = criterion ?? string.Empty,
        });
        return result.Value is true;
    }
}

/// <summary>Approval processes (docs/design/cap.md §6, ADR 0121): the PM Office is the only approver by default.</summary>
public static class CapProcessDefinitions
{
    private static readonly ExpressionVariable[] Variables =
    [
        new("priority", ExpressionValueType.Text, "أولوية الملاحظة", "Remark priority"),
        new("budgetRequired", ExpressionValueType.Boolean, "يتطلب ميزانية", "Budget required"),
        new("budgetAvailable", ExpressionValueType.Boolean, "الميزانية متوفرة", "Budget available"),
        new("estimatedCost", ExpressionValueType.Number, "التكلفة التقديرية", "Estimated cost"),
    ];

    private static ApprovalStepDefinition OfficeStep() =>
        new("pm-office", "مكتب إدارة المشاريع", "PM Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: CapPermissions.OfficeRoleKey), SlaHours: 24);

    public static readonly ApprovalProcessDefinition Validation = new(
        CapProcesses.Validation, CapPermissions.SystemCode, "اعتماد الإجراء التصحيحي", "Corrective action validation", CapAction.EntityType, CapPermissions.Read,
        "/cap/actions/{id}", Variables, [OfficeStep()], CapPermissions.ApproverRole,
        "يعتمد مكتب إدارة المشاريع كل إجراء أو يرفضه أو يعيده للتصحيح.", "The PM Office approves, rejects or returns each action.");

    public static readonly ApprovalProcessDefinition Completion = new(
        CapProcesses.Completion, CapPermissions.SystemCode, "اعتماد إنجاز الإجراء", "Action completion approval", CapAction.EntityType, CapPermissions.Read,
        "/cap/actions/{id}", Variables, [OfficeStep()], CapPermissions.ApproverRole,
        "يعتمد مكتب إدارة المشاريع إنجاز الإجراء بعد رفع الدليل.", "The PM Office approves the completion of an action with its evidence.");

    public static readonly ApprovalProcessDefinition Extension = new(
        CapProcesses.Extension, CapPermissions.SystemCode, "تمديد موعد الإجراء", "Action extension request", CapAction.EntityType, CapPermissions.Read,
        "/cap/actions/{id}", Variables, [OfficeStep()], CapPermissions.ApproverRole,
        "طلب تمديد تاريخ انتهاء الإجراء مع المبررات.", "A request to extend the action's end date, with the justification.");
}

/// <summary>Bilingual templates (editable in Settings → Notifications).</summary>
public static class CapNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "date", "actor", "text", "count"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, CapPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition RemarkAssigned = T("cap.remarkAssigned", "إسناد ملاحظة", "Remark assigned",
        "ملاحظة جديدة {{number}} بانتظار ردك", "New remark {{number}} needs your response",
        "أُسندت إليك الملاحظة «{{titleAr}}» ({{number}}). يرجى قبولها أو رفضها قبل {{date}}.",
        "The remark “{{titleEn}}” ({{number}}) was assigned to you. Please accept or reject it; it is due on {{date}}.");

    public static readonly NotificationTemplateDefinition RemarkRejected = T("cap.remarkRejected", "رفض ملاحظة", "Remark rejected",
        "رُفضت الملاحظة {{number}}", "Remark {{number}} rejected", "رفض {{actor}} الملاحظة «{{titleAr}}»: {{text}}", "{{actor}} rejected “{{titleEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition RejectionToConfirm = T("cap.rejectionToConfirm", "تأكيد رفض ملاحظة", "Remark rejection to confirm",
        "رفض الملاحظة {{number}} بانتظار تأكيدك", "Rejection of remark {{number}} waits for your confirmation",
        "رفض {{actor}} الملاحظة «{{titleAr}}»: {{text}}. يرجى تأكيد الرفض أو إعادتها.", "{{actor}} rejected “{{titleEn}}”: {{text}}. Please confirm or send it back.");

    public static readonly NotificationTemplateDefinition RemarkSentBack = T("cap.remarkSentBack", "إعادة ملاحظة", "Remark sent back",
        "أعاد مكتب إدارة المشاريع الملاحظة {{number}}", "The PM Office sent remark {{number}} back",
        "لم يؤكد مكتب إدارة المشاريع رفض الملاحظة «{{titleAr}}»: {{text}}", "The PM Office did not confirm the rejection of “{{titleEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition ActionsSubmitted = T("cap.actionsSubmitted", "إجراءات بانتظار الاعتماد", "Actions waiting for validation",
        "إجراءات الملاحظة {{number}} بانتظار الاعتماد", "Actions of remark {{number}} wait for validation",
        "قدّم {{actor}} {{count}} إجراء للملاحظة «{{titleAr}}» للاعتماد.", "{{actor}} submitted {{count}} action(s) of “{{titleEn}}” for validation.");

    public static readonly NotificationTemplateDefinition ActionValidated = T("cap.actionValidated", "قرار اعتماد الإجراء", "Action validation decision",
        "قرار مكتب إدارة المشاريع على الإجراء {{number}}", "PM Office decision on action {{number}}",
        "قرار مكتب إدارة المشاريع على الإجراء «{{titleAr}}» ({{number}}): {{text}}", "The PM Office decision on “{{titleEn}}” ({{number}}): {{text}}");

    public static readonly NotificationTemplateDefinition ActionAssigned = T("cap.actionAssigned", "إسناد إجراء تصحيحي", "Corrective action assigned",
        "أُسند إليك الإجراء {{number}}", "Action {{number}} assigned to you", "أُسند إليك الإجراء «{{titleAr}}» ({{number}}) بتاريخ انتهاء {{date}}.",
        "The action “{{titleEn}}” ({{number}}) was assigned to you, ending on {{date}}.");

    public static readonly NotificationTemplateDefinition BudgetRouted = T("cap.budgetRouted", "طلب برنامج من خطة العمل", "Program request from the action plan",
        "أُنشئ طلب برنامج {{text}} للإجراء {{number}}", "Program request {{text}} created for action {{number}}",
        "يحتاج الإجراء «{{titleAr}}» ({{number}}) إلى ميزانية غير متوفرة، فأُنشئت مسودة طلب البرنامج {{text}}. يرجى استكمالها وتقديمها.",
        "The action “{{titleEn}}” ({{number}}) needs budget that is not available, so the draft program request {{text}} was created. Please complete and submit it.");

    public static readonly NotificationTemplateDefinition MonthlyReminder = T("cap.monthlyReminder", "تذكير شهري بالإجراءات", "Monthly action reminder",
        "تحديث إجراءاتك التصحيحية", "Update your corrective actions",
        "لديك {{count}} إجراء مفتوح ({{text}}). يرجى تحديث نسبة الإنجاز.", "You have {{count}} open action(s) ({{text}}). Please update their achievement.");

    public static readonly NotificationTemplateDefinition ActionLate = T("cap.actionLate", "تأخر إجراء", "Action late", "تأخر الإجراء {{number}}", "Action {{number}} is late",
        "تجاوز الإجراء «{{titleAr}}» ({{number}}) تاريخ الانتهاء {{date}} دون اكتمال.", "The action “{{titleEn}}” ({{number}}) passed its end date {{date}} without completion.");

    public static readonly NotificationTemplateDefinition ActionEscalated = T("cap.actionEscalated", "تصعيد إجراء متأخر", "Late action escalated",
        "تصعيد: الإجراء {{number}} متأخر", "Escalation: action {{number}} is late",
        "ما زال الإجراء «{{titleAr}}» ({{number}}) متأخراً منذ {{date}}.", "The action “{{titleEn}}” ({{number}}) is still late since {{date}}.");

    public static readonly NotificationTemplateDefinition RemarkOverdue = T("cap.remarkOverdue", "تأخر ملاحظة", "Remark overdue", "تجاوزت الملاحظة {{number}} موعدها",
        "Remark {{number}} is overdue", "تجاوزت الملاحظة «{{titleAr}}» ({{number}}) تاريخ الاستحقاق {{date}}.",
        "The remark “{{titleEn}}” ({{number}}) passed its due date {{date}}.");

    public static readonly NotificationTemplateDefinition ExtensionRequested = T("cap.extensionRequested", "طلب تمديد", "Extension requested",
        "طلب تمديد للإجراء {{number}}", "Extension requested for action {{number}}", "طلب {{actor}} تمديد الإجراء «{{titleAr}}» حتى {{date}}: {{text}}",
        "{{actor}} asked to extend “{{titleEn}}” to {{date}}: {{text}}");

    public static readonly NotificationTemplateDefinition ExtensionDecided = T("cap.extensionDecided", "قرار طلب التمديد", "Extension decision",
        "قرار طلب تمديد الإجراء {{number}}", "Extension decision for action {{number}}", "قرار طلب تمديد الإجراء «{{titleAr}}»: {{text}}",
        "Decision on the extension of “{{titleEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition CompletionDecided = T("cap.completionDecided", "قرار إنجاز الإجراء", "Completion decision",
        "قرار إنجاز الإجراء {{number}}", "Completion decision for action {{number}}", "قرار مكتب إدارة المشاريع على إنجاز «{{titleAr}}»: {{text}}",
        "The PM Office decision on the completion of “{{titleEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition RemarkCompleted = T("cap.remarkCompleted", "اكتمال ملاحظة", "Remark completed",
        "اكتملت الملاحظة {{number}}", "Remark {{number}} completed", "اكتملت جميع إجراءات الملاحظة «{{titleAr}}» ({{number}}).",
        "All actions of the remark “{{titleEn}}” ({{number}}) are completed.");

    public static readonly NotificationTemplateDefinition VerificationDue = T("cap.verificationDue", "التحقق من الفعالية", "Effectiveness verification due",
        "التحقق من فعالية الملاحظة {{number}}", "Verify the effectiveness of remark {{number}}",
        "حان موعد التحقق من فعالية إجراءات الملاحظة «{{titleAr}}» ({{number}}) في {{date}}.",
        "The effectiveness of the actions of “{{titleEn}}” ({{number}}) is due for verification on {{date}}.");

    public static readonly NotificationTemplateDefinition SuggestionRaised = T("cap.suggestion", "ملاحظة مقترحة من نظام آخر", "Remark suggested by another system",
        "اقتراح ملاحظة: {{titleAr}}", "Suggested remark: {{titleEn}}", "اقترح النظام ملاحظة: «{{titleAr}}» ({{text}}).", "A remark is suggested: “{{titleEn}}” ({{text}}).");

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
    [
        RemarkAssigned, RemarkRejected, RejectionToConfirm, RemarkSentBack, ActionsSubmitted, ActionValidated, ActionAssigned, BudgetRouted, MonthlyReminder, ActionLate,
        ActionEscalated, RemarkOverdue, ExtensionRequested, ExtensionDecided, CompletionDecided, RemarkCompleted, VerificationDue, SuggestionRaised,
    ];
}

/// <summary>The CAP catalogue (SPEC §9.7 defaults; editable in Settings → Roles).</summary>
internal sealed class CapAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(CapPermissions.SystemCode, SystemKind.Module, "خطة العمل التصحيحية", "Corrective Action Plan", 32);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(CapPermissions.Read, "عرض الملاحظات والإجراءات", "View remarks and actions"),
        new(CapPermissions.SourceManage, "إدارة مصادر الملاحظات والملاحظات", "Manage remark sources and remarks"),
        new(CapPermissions.Respond, "الرد على الملاحظات وتخطيط الإجراءات", "Respond to remarks and plan actions"),
        new(CapPermissions.ActionUpdate, "تحديث إنجاز الإجراءات", "Update action achievement"),
        new(CapPermissions.Office, "إجراءات مكتب إدارة المشاريع (الاعتماد والتأكيد والتحقق)", "PM Office actions (validation, confirmation, verification)"),
        new(CapPermissions.DashboardRead, "عرض لوحة خطة العمل", "View the CAP dashboard", Scopable: false),
        new(CapPermissions.Export, "تصدير تقارير خطة العمل", "Export CAP reports", Scopable: false),
        new(CapPermissions.SettingsManage, "إعدادات خطة العمل", "CAP settings", Scopable: false),
        new(CapPermissions.AiExtract, "استخراج الملاحظات من تقرير", "Remark extraction from a report", Scopable: false),
        new(CapPermissions.AiRootCause, "مساعد تحليل السبب الجذري", "Root cause assistant", Scopable: false),
        new(CapPermissions.AiSuggest, "اقتراح الإجراءات وكشف المكرر", "Action suggestions and duplicate check", Scopable: false),
        new(CapPermissions.AiQuality, "فحص جودة الإجراءات", "Action quality check", Scopable: false),
        new(CapPermissions.AiSummary, "ملخص خطة العمل الشهري", "Monthly CAP summary", Scopable: false),
        ResponsibilityPermissions.Definition(CapPermissions.SystemCode),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("cap-office", "مكتب إدارة المشاريع (خطة العمل)", "PM Office (CAP administrator)",
        [
            new(CapPermissions.Read, DataScope.All), new(CapPermissions.SourceManage, DataScope.All), new(CapPermissions.Respond, DataScope.All),
            new(CapPermissions.ActionUpdate, DataScope.All), new(CapPermissions.Office, DataScope.All), new(CapPermissions.DashboardRead), new(CapPermissions.Export),
            new(CapPermissions.SettingsManage), new(CapPermissions.AiExtract), new(CapPermissions.AiRootCause), new(CapPermissions.AiSuggest), new(CapPermissions.AiQuality),
            new(CapPermissions.AiSummary), new(ResponsibilityPermissions.Manage(CapPermissions.SystemCode), DataScope.All),
        ], RequiresMfa: true),
        new("department-manager", "مدير الإدارة (خطة العمل)", "Department manager (CAP)",
            [new(CapPermissions.Read, DataScope.UnitAndBelow), new(CapPermissions.DashboardRead), new(CapPermissions.Export)]),
        new("participant", "مشارك في خطة العمل", "CAP participant",
        [
            new(CapPermissions.Read, DataScope.Own), new(CapPermissions.Respond, DataScope.Own), new(CapPermissions.ActionUpdate, DataScope.Own),
            new(CapPermissions.AiRootCause), new(CapPermissions.AiSuggest),
        ], AssignToEveryone: true),
        new("viewer", "القيادة / مستعرض خطة العمل", "Leadership / CAP viewer",
            [new(CapPermissions.Read, DataScope.All), new(CapPermissions.DashboardRead), new(CapPermissions.Export), new(CapPermissions.AiSummary)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(Remark.EntityType, CapPermissions.RemarkOwnerRole, "مالك الملاحظة", "Remark owner", [CapPermissions.Read, CapPermissions.Respond]),
        new(Remark.EntityType, CapPermissions.AssigneeRole, "مسؤول إجراء تصحيحي", "Action assignee", [CapPermissions.Read, CapPermissions.ActionUpdate]),
        new(Remark.EntityType, CapPermissions.SourceViewerRole, "متابع من النظام المصدر", "Source-system viewer", [CapPermissions.Read]),
        new(CapAction.EntityType, CapPermissions.ApproverRole, "معتمد الإجراء", "Action approver", [CapPermissions.Read]),
    ];

    public static readonly NumberingDefinition SourceNumbering = new(CapPermissions.SourceNumbering, CapPermissions.SystemCode, "رقم مصدر الملاحظات", "Remark source number",
        "CAPS-{YYYY}-{SEQ:3}");

    public static readonly NumberingDefinition RemarkNumbering = new(CapPermissions.RemarkNumbering, CapPermissions.SystemCode, "رقم الملاحظة", "Remark number",
        "CAP-{UNIT}-{YYYY}-{SEQ:3}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(CapPermissions.SourceTypeLookup, CapPermissions.SystemCode, "نوع مصدر الملاحظات", "Remark source type",
        [
            new(CapSourceTypes.Workshop, "ورشة عمل", "Workshop"), new(CapSourceTypes.Benchmarking, "مقارنة معيارية", "Benchmarking"),
            new(CapSourceTypes.InternalAudit, "تدقيق داخلي", "Internal audit"), new(CapSourceTypes.ExternalAudit, "تدقيق خارجي", "External audit"),
            new(CapSourceTypes.ExcellenceAssessment, "تقييم التميز المؤسسي", "Excellence assessment (GEM)"),
            new(CapSourceTypes.KpiPerformance, "أداء مؤشرات الأداء", "KPI performance"), new(CapSourceTypes.RiskEvent, "حدث خطر", "Risk event"),
            new(CapSourceTypes.CustomerComplaint, "شكاوى / ملاحظات المتعاملين", "Customer complaint / feedback"),
            new(CapSourceTypes.EmployeeSurvey, "استبيان الموظفين", "Employee survey"), new(CapSourceTypes.ManagementReview, "مراجعة الإدارة", "Management review"),
            new(CapSourceTypes.Other, "أخرى", "Other"),
        ]),
        new(CapPermissions.RemarkTypeLookup, CapPermissions.SystemCode, "نوع الملاحظة", "Remark type",
        [
            new(CapRemarkTypes.Nonconformity, "عدم مطابقة", "Nonconformity"), new(CapRemarkTypes.Observation, "ملاحظة", "Observation"),
            new(CapRemarkTypes.Improvement, "فرصة تحسين", "Improvement opportunity"),
        ]),
        new(CapPermissions.CriterionLookup, CapPermissions.SystemCode, "معيار الملاحظة", "Remark criterion",
        [
            new("leadership", "القيادة", "Leadership"), new("strategy", "الاستراتيجية", "Strategy"), new("people", "الموارد البشرية", "People"),
            new("processes", "العمليات", "Processes"), new("services", "الخدمات", "Services"), new("innovation", "الابتكار", "Innovation"),
            new("results", "النتائج", "Results"), new("other", "أخرى", "Other"),
        ]),
        new(CapPermissions.ActionTypeLookup, CapPermissions.SystemCode, "نوع الإجراء", "Action type",
        [
            new(CapActionTypes.Correction, "تصحيح", "Correction"), new(CapActionTypes.Corrective, "إجراء تصحيحي", "Corrective action"),
            new(CapActionTypes.Preventive, "إجراء وقائي", "Preventive action"), new(CapActionTypes.Improvement, "تحسين", "Improvement"),
        ]),
        new(CapPermissions.PriorityLookup, CapPermissions.SystemCode, "أولوية الملاحظة", "Remark priority",
        [
            new(CapPriorities.Critical, "حرجة", "Critical"), new(CapPriorities.High, "عالية", "High"), new(CapPriorities.Medium, "متوسطة", "Medium"),
            new(CapPriorities.Low, "منخفضة", "Low"),
        ]),
    ];
}
