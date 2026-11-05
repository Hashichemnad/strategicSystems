using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>Permission, role, numbering, lookup and file codes (docs/design/benchmarking.md §7).</summary>
public static class BenchPermissions
{
    public const string SystemCode = "bench";

    public const string Read = "bench.study.read";
    public const string Edit = "bench.study.edit";
    public const string Office = "bench.office.manage";
    public const string PlanRead = "bench.plan.read";
    public const string ComparatorRead = "bench.comparator.read";
    public const string ComparatorManage = "bench.comparator.manage";
    public const string PracticeRead = "bench.practice.read";
    public const string PracticeManage = "bench.practice.manage";
    public const string DashboardRead = "bench.dashboard.read";
    public const string SettingsManage = "bench.settings.manage";

    public const string AiComparators = "bench.ai.comparators";
    public const string AiQuestionnaire = "bench.ai.questionnaire";
    public const string AiAnalysis = "bench.ai.analysis";
    public const string AiSummary = "bench.ai.summary";
    public const string AiSearch = "bench.ai.search";

    public const string CoordinatorRole = "coordinator";
    public const string TopicOwnerRole = "topic-owner";
    public const string ParticipantRole = "participant";
    public const string ApproverRole = "approver";

    /// <summary>Approval-engine role key of the PM Office (ADR 0126).</summary>
    public const string OfficeRoleKey = "bench.bench-office";

    public const string StudyNumbering = "bench.study";

    public const string ComparisonTypeLookup = "bench.comparison-type";
    public const string PurposeLookup = "bench.purpose";
    public const string SectorLookup = "bench.sector";
}

/// <summary>Data scope of studies: own = responsible employee (coordinator), unit = responsible unit.</summary>
public static class BenchScope
{
    public static readonly ScopeMap<Study> Study = new(Domain.Study.EntityType, x => x.Id, ownerEmployee: x => x.ResponsibleEmployeeId, unit: x => x.ResponsibleUnitId);
}

public static class BenchFilePurposes
{
    public static readonly FilePurpose StudyDocument = new("bench.study-document", Study.EntityType, BenchPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);
}

/// <summary>Settings → Benchmarking (docs/design/benchmarking.md §4).</summary>
public sealed record BenchSettings : ISettingsArea
{
    public static string Key => "bench";

    public int SearchNoticeDays { get; init; } = 30;

    public bool CountWorkingDays { get; init; }

    public int ImpactFollowUpMonths { get; init; } = 6;

    public bool KpiBenchmarkUpdate { get; init; } = true;

    public static List<FieldError> Validate(BenchSettings value)
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

        Range("searchNoticeDays", value.SearchNoticeDays, 0, 180);
        Range("impactFollowUpMonths", value.ImpactFollowUpMonths, 0, 36);
        return errors;
    }
}

/// <summary>Approval processes (ADR 0126): the Plan on the request (Request Framework), Search / Analysis / Closure on the study.</summary>
public static class BenchProcesses
{
    public const string Plan = "bench.plan";
    public const string Search = "bench.search";
    public const string Analysis = "bench.analysis";
    public const string Closure = "bench.closure";

    private static readonly ExpressionVariable[] Variables =
    [
        new("targetYear", ExpressionValueType.Number, "سنة التنفيذ", "Target year"),
        new("comparisonType", ExpressionValueType.Text, "نوع المقارنة", "Comparison type"),
    ];

    private static ApprovalStepDefinition OfficeStep() =>
        new("pm-office", "مكتب الاستراتيجية وإدارة المشاريع", "Strategy & PM Office", new ApproverRule(ApproverRuleKinds.Role, RoleKey: BenchPermissions.OfficeRoleKey),
            SlaHours: 24);

    public static readonly ApprovalProcessDefinition PlanDefinition = new(
        Plan, BenchPermissions.SystemCode, "اعتماد طلب المقارنة المعيارية", "Benchmarking request approval", RequestResource.Type, RequestResource.ReadPermission,
        RequestResource.LinkTemplate, [], [OfficeStep()], RequestResource.ApproverRole,
        "يعتمد المكتب طلب المقارنة المعيارية فيدخل الخطة السنوية، أو يعيده للتصحيح أو يرفضه.",
        "The office approves the benchmarking request into the annual plan, returns it for correction or rejects it.");

    public static readonly ApprovalProcessDefinition SearchDefinition = new(
        Search, BenchPermissions.SystemCode, "اعتماد نموذج البحث", "Search form approval", Study.EntityType, BenchPermissions.Read, "/bench/studies/{id}?tab=search",
        Variables, [OfficeStep()], BenchPermissions.ApproverRole, "اعتماد الجهات المرشحة للمقارنة.", "Approval of the candidate comparator entities.");

    public static readonly ApprovalProcessDefinition AnalysisDefinition = new(
        Analysis, BenchPermissions.SystemCode, "اعتماد نموذج التحليل", "Analysis approval", Study.EntityType, BenchPermissions.Read, "/bench/studies/{id}?tab=analysis",
        Variables, [OfficeStep()], BenchPermissions.ApproverRole,
        "اعتماد التحليل ينشئ مصدر ملاحظات في خطة العمل التصحيحية بملاحظة لكل بند من خطة التطوير.",
        "Approving the analysis creates a remarks source in the Corrective Action Plan with one remark per development-plan item.");

    public static readonly ApprovalProcessDefinition ClosureDefinition = new(
        Closure, BenchPermissions.SystemCode, "الاعتماد النهائي وإغلاق الدراسة", "Final approval and closure", Study.EntityType, BenchPermissions.Read, "/bench/studies/{id}",
        Variables, [OfficeStep()], BenchPermissions.ApproverRole, "تغلق الدراسة بعد اكتمال جميع إجراءاتها بالأدلة.",
        "The study closes once all its actions are completed with evidence.");
}

/// <summary>The built-in request type of the Plan stage (ADR 0126) — catalogue, My Requests, form builder extra fields.</summary>
public static class BenchRequestTypes
{
    public const string Request = "bench.request";
    public const string ApprovalStage = "pmo-approval";

    private static FormField F(string code, string type, string ar, string en, bool required = false) =>
        new() { Code = code, Type = type, LabelAr = ar, LabelEn = en, Required = required, System = true };

    public static readonly RequestTypeRegistration RequestType = new(
        Request, BenchPermissions.SystemCode, "طلب مقارنة معيارية", "Benchmarking request",
        "طلب دراسة مقارنة معيارية مع جهة محلية أو إقليمية أو دولية: الوحدة المسؤولة والربع المستهدف ونوع المقارنة والغرض والنطاق ومواضيع المقارنة.",
        "Request a benchmarking study with a local, regional or international entity: responsible unit, target quarter, comparison type, purpose, scope and comparison topics.",
        "BMREQ", "scale",
        new FormSchema
        {
            Sections =
            [
                new FormSection
                {
                    Code = "plan", TitleAr = "خطة المقارنة", TitleEn = "Plan", System = true,
                    Fields =
                    [
                        F("titleAr", FieldTypes.Text, "عنوان الدراسة (عربي)", "Study title (Arabic)", true) with { MaxLength = Study.TitleMaxLength, TitleOf = "ar" },
                        F("titleEn", FieldTypes.Text, "عنوان الدراسة (إنجليزي)", "Study title (English)", true) with { MaxLength = Study.TitleMaxLength, TitleOf = "en" },
                        F("responsibleUnit", FieldTypes.Unit, "الوحدة المسؤولة", "Responsible unit", true) with { RequestUnit = true },
                        F("section", FieldTypes.Unit, "القسم المعني", "Relevant section"),
                        F("responsibleEmployee", FieldTypes.Employee, "الموظف المسؤول (منسق المقارنة)", "Responsible employee (benchmarking coordinator)", true),
                        F("targetYear", FieldTypes.Number, "سنة التنفيذ", "Target year", true) with { Min = 2000, Max = 2100, Decimals = 0 },
                        F("targetQuarter", FieldTypes.Number, "الربع المستهدف (1–4)", "Target quarter (1–4)", true) with { Min = 1, Max = 4, Decimals = 0 },
                        F("comparisonType", FieldTypes.Picklist, "نوع المقارنة", "Comparison type", true) with { LookupType = BenchPermissions.ComparisonTypeLookup },
                        F("purpose", FieldTypes.Picklist, "الغرض من المقارنة", "Purpose of the comparison", true) with { LookupType = BenchPermissions.PurposeLookup },
                        F("scope", FieldTypes.Textarea, "نطاق المقارنة", "Scope", true) with { MaxLength = Study.TextMaxLength },
                    ],
                },
                new FormSection
                {
                    Code = "topics", TitleAr = "مواضيع المقارنة", TitleEn = "Comparison topics", System = true,
                    Fields =
                    [
                        F("topics", FieldTypes.Table, "المواضيع", "Topics", true) with
                        {
                            MinRows = 1,
                            MaxRows = 20,
                            Columns =
                            [
                                new FormField { Code = "topic", Type = FieldTypes.Text, LabelAr = "الموضوع", LabelEn = "Topic", Required = true, MaxLength = Study.TitleMaxLength },
                                new FormField { Code = "owner", Type = FieldTypes.Employee, LabelAr = "المسؤول", LabelEn = "Owner", Required = true },
                                new FormField { Code = "description", Type = FieldTypes.Textarea, LabelAr = "الوصف", LabelEn = "Description", MaxLength = 2000 },
                            ],
                        },
                    ],
                },
            ],
        },
        [
            new StageDefinition
            {
                Code = ApprovalStage, Kind = StageKinds.Approval, NameAr = "اعتماد المكتب", NameEn = "Office approval", ProcessKey = BenchProcesses.Plan, System = true,
            },
        ],
        Category: "benchmarking", Sort: 30);
}

/// <summary>Bilingual templates (editable in Settings → Notifications).</summary>
public static class BenchNotifications
{
    private static readonly string[] Common = ["name", "number", "titleAr", "titleEn", "date", "actor", "text", "count"];

    private static NotificationTemplateDefinition T(string key, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn) =>
        new(key, BenchPermissions.SystemCode, nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{{{{button}}}}", $"Hello {{{{name}}}},\n{lineEn}\n{{{{button}}}}", "فتح", "Open", Common);

    public static readonly NotificationTemplateDefinition PlanApproved = T("bench.planApproved", "اعتماد طلب مقارنة", "Benchmarking request approved",
        "اعتُمدت الدراسة {{number}} في الخطة السنوية", "Study {{number}} approved into the annual plan",
        "اعتُمدت دراسة المقارنة «{{titleAr}}» ({{number}}) للربع {{text}}. سيصلك تذكير بتعبئة نموذج البحث قبل بداية الربع.",
        "The benchmarking study “{{titleEn}}” ({{number}}) is approved for {{text}}. You will be reminded to fill the search form before the quarter starts.");

    public static readonly NotificationTemplateDefinition PlanCirculated = T("bench.planCirculated", "تعميم خطة المقارنات", "Benchmarking plan circulated",
        "خطة المقارنات المعيارية لعام {{text}}", "Benchmarking plan {{text}}", "عُممت خطة المقارنات المعيارية لعام {{text}} ({{count}} دراسة).",
        "The benchmarking plan for {{text}} was circulated ({{count}} studies).");

    public static readonly NotificationTemplateDefinition SearchDue = T("bench.searchDue", "تعبئة نموذج البحث", "Fill the search form",
        "يرجى تعبئة نموذج البحث للدراسة {{number}}", "Please fill the search form of study {{number}}",
        "يبدأ الربع المستهدف للدراسة «{{titleAr}}» ({{number}}) في {{date}}. يرجى تعبئة نموذج البحث بالجهات المرشحة.",
        "The target quarter of “{{titleEn}}” ({{number}}) starts on {{date}}. Please fill the search form with the candidate entities.");

    public static readonly NotificationTemplateDefinition WaitingOffice = T("bench.waitingOffice", "بانتظار اعتماد المكتب", "Waiting for the office",
        "الدراسة {{number}} بانتظار الاعتماد ({{text}})", "Study {{number}} waits for approval ({{text}})", "قدّم {{actor}} {{text}} للدراسة «{{titleAr}}».",
        "{{actor}} submitted the {{text}} of “{{titleEn}}”.");

    public static readonly NotificationTemplateDefinition Decided = T("bench.decided", "قرار المكتب", "Office decision", "قرار المكتب على الدراسة {{number}}",
        "Office decision on study {{number}}", "قرار المكتب على الدراسة «{{titleAr}}»: {{text}}", "The office decision on “{{titleEn}}”: {{text}}");

    public static readonly NotificationTemplateDefinition ReadyForClosure = T("bench.readyForClosure", "جاهزية إغلاق الدراسة", "Study ready for closure",
        "اكتملت إجراءات الدراسة {{number}}", "All actions of study {{number}} are completed", "اكتملت جميع إجراءات الدراسة «{{titleAr}}» بالأدلة. يمكن طلب الإغلاق.",
        "All actions of “{{titleEn}}” are completed with evidence. Closure can be requested.");

    public static readonly NotificationTemplateDefinition Closed = T("bench.closed", "إغلاق دراسة", "Study closed", "أُغلقت الدراسة {{number}}", "Study {{number}} closed",
        "اكتملت الدراسة «{{titleAr}}» ({{number}}) وأُغلقت.", "The study “{{titleEn}}” ({{number}}) is completed and closed.");

    public static readonly NotificationTemplateDefinition ImpactDue = T("bench.impactDue", "متابعة أثر المقارنة", "Benchmarking impact follow-up",
        "متابعة أثر الدراسة {{number}}", "Impact follow-up of study {{number}}", "حان موعد متابعة أثر الدراسة «{{titleAr}}»: هل نُفذت التحسينات وتحركت المؤشرات؟",
        "The impact follow-up of “{{titleEn}}” is due: were the improvements implemented and did the indicators move?");

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } = [PlanApproved, PlanCirculated, SearchDue, WaitingOffice, Decided, ReadyForClosure, Closed, ImpactDue];
}

/// <summary>The Benchmarking catalogue (SPEC §8 defaults; editable in Settings → Roles).</summary>
internal sealed class BenchAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(BenchPermissions.SystemCode, SystemKind.Module, "المقارنات المعيارية", "Benchmarking", 34);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(BenchPermissions.Read, "عرض دراسات المقارنة", "View benchmarking studies"),
        new(BenchPermissions.Edit, "تعبئة نماذج المقارنة (البحث والزيارة والتحليل)", "Fill the benchmarking forms (search, visit, analysis)"),
        new(BenchPermissions.Office, "إجراءات المكتب (التعميم والإلغاء والإغلاق)", "Office actions (circulation, cancellation, closure)"),
        new(BenchPermissions.PlanRead, "عرض خطة المقارنات السنوية", "View the annual benchmarking plan", Scopable: false),
        new(BenchPermissions.ComparatorRead, "عرض سجل الجهات المقارن بها", "View the comparator register", Scopable: false),
        new(BenchPermissions.ComparatorManage, "إدارة سجل الجهات المقارن بها", "Manage the comparator register", Scopable: false),
        new(BenchPermissions.PracticeRead, "عرض مكتبة أفضل الممارسات", "View the best-practice library", Scopable: false),
        new(BenchPermissions.PracticeManage, "إدارة مكتبة أفضل الممارسات", "Manage the best-practice library", Scopable: false),
        new(BenchPermissions.DashboardRead, "عرض لوحة المقارنات", "View the benchmarking dashboard", Scopable: false),
        new(BenchPermissions.SettingsManage, "إعدادات المقارنات", "Benchmarking settings", Scopable: false),
        new(BenchPermissions.AiComparators, "اقتراح الجهات المقارن بها", "Comparator suggestions", Scopable: false),
        new(BenchPermissions.AiQuestionnaire, "صياغة الاستبيان المسبق", "Questionnaire drafting", Scopable: false),
        new(BenchPermissions.AiAnalysis, "تحويل ملاحظات الزيارة إلى مقارنات", "Visit notes to comparisons and gaps", Scopable: false),
        new(BenchPermissions.AiSummary, "ملخص الدراسة", "Study summary", Scopable: false),
        new(BenchPermissions.AiSearch, "البحث في أفضل الممارسات بلغة طبيعية", "Natural-language best-practice search", Scopable: false),
    ];

    private static readonly PermissionGrant[] Ai =
    [
        new(BenchPermissions.AiComparators), new(BenchPermissions.AiQuestionnaire), new(BenchPermissions.AiAnalysis), new(BenchPermissions.AiSummary),
        new(BenchPermissions.AiSearch),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("bench-office", "مكتب الاستراتيجية (المقارنات)", "Strategy & PM Office (benchmarking)",
        [
            new(BenchPermissions.Read, DataScope.All), new(BenchPermissions.Edit, DataScope.All), new(BenchPermissions.Office, DataScope.All), new(BenchPermissions.PlanRead),
            new(BenchPermissions.ComparatorRead), new(BenchPermissions.ComparatorManage), new(BenchPermissions.PracticeRead), new(BenchPermissions.PracticeManage),
            new(BenchPermissions.DashboardRead), new(BenchPermissions.SettingsManage), .. Ai,
        ], RequiresMfa: true),
        new("coordinator", "منسق المقارنات في الوحدة", "Unit benchmarking coordinator",
        [
            new(BenchPermissions.Read, DataScope.UnitAndBelow), new(BenchPermissions.Edit, DataScope.UnitAndBelow), new(BenchPermissions.PlanRead),
            new(BenchPermissions.ComparatorRead), new(BenchPermissions.ComparatorManage), new(BenchPermissions.PracticeRead), new(BenchPermissions.DashboardRead), .. Ai,
        ]),
        new("participant", "مشارك في المقارنات", "Benchmarking participant",
        [
            new(BenchPermissions.Read, DataScope.Own), new(BenchPermissions.Edit, DataScope.Own), new(BenchPermissions.PlanRead), new(BenchPermissions.ComparatorRead),
            new(BenchPermissions.PracticeRead), new(BenchPermissions.AiSearch), new(BenchPermissions.AiQuestionnaire), new(BenchPermissions.AiAnalysis),
        ], AssignToEveryone: true),
        new("viewer", "مستعرض المقارنات", "Benchmarking viewer",
        [
            new(BenchPermissions.Read, DataScope.UnitAndBelow), new(BenchPermissions.PlanRead), new(BenchPermissions.ComparatorRead), new(BenchPermissions.PracticeRead),
            new(BenchPermissions.DashboardRead),
        ]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(Study.EntityType, BenchPermissions.CoordinatorRole, "منسق الدراسة", "Study coordinator", [BenchPermissions.Read, BenchPermissions.Edit]),
        new(Study.EntityType, BenchPermissions.TopicOwnerRole, "مسؤول موضوع مقارنة", "Comparison topic owner", [BenchPermissions.Read]),
        new(Study.EntityType, BenchPermissions.ParticipantRole, "مشارك في الزيارة", "Visit participant", [BenchPermissions.Read]),
        new(Study.EntityType, BenchPermissions.ApproverRole, "معتمد الدراسة", "Study approver", [BenchPermissions.Read]),
    ];

    public static readonly NumberingDefinition StudyNumbering = new(BenchPermissions.StudyNumbering, BenchPermissions.SystemCode, "رقم دراسة المقارنة", "Benchmarking study number",
        "BMS-{SEQ:4}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(BenchPermissions.ComparisonTypeLookup, BenchPermissions.SystemCode, "نوع المقارنة", "Comparison type",
        [
            new("internal", "داخلية", "Internal"), new("competitive", "تنافسية", "Competitive"), new("functional", "وظيفية", "Functional"), new("generic", "عامة", "Generic"),
        ]),
        new(BenchPermissions.PurposeLookup, BenchPermissions.SystemCode, "الغرض من المقارنة", "Purpose of the comparison",
        [
            new("policy-development", "تطوير السياسات", "Policy development"), new("process-improvement", "تحسين العمليات", "Process improvement"),
            new("human-skills", "تنمية المهارات البشرية", "Human skills development"), new("service-quality", "جودة الخدمات", "Service quality"),
            new("technology", "التقنية", "Technology"), new("other", "أخرى", "Other"),
        ]),
        new(BenchPermissions.SectorLookup, BenchPermissions.SystemCode, "قطاع الجهة", "Entity sector",
        [
            new("government", "حكومي", "Government"), new("semi-government", "شبه حكومي", "Semi-government"), new("private", "خاص", "Private"),
            new("international-organisation", "منظمة دولية", "International organisation"), new("academic", "أكاديمي", "Academic"), new("other", "أخرى", "Other"),
        ]),
    ];
}
