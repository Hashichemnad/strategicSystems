using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Permission, record-role, numbering, lookup and file-purpose codes (docs/design/portfolio.md §2, §10).</summary>
public static class PortfolioPermissions
{
    public const string SystemCode = "portfolio";
    public const string RequestResource = ProgramRequest.EntityType;
    public const string ProjectResource = Project.EntityType;

    public const string RequestRead = "portfolio.request.read";
    public const string RequestCreate = "portfolio.request.create";
    public const string RequestEdit = "portfolio.request.edit";
    public const string RequestManage = "portfolio.request.manage";
    public const string RequestQuotations = "portfolio.request.quotations";
    public const string ProjectRead = "portfolio.project.read";
    public const string ProjectEdit = "portfolio.project.edit";
    public const string ProjectContribute = "portfolio.project.contribute";
    public const string ProjectChange = "portfolio.project.change";
    public const string ProjectManage = "portfolio.project.manage";
    public const string ProjectLpo = "portfolio.project.lpo";
    public const string ProjectCosts = "portfolio.project.costs";
    public const string PortfolioManage = "portfolio.portfolio.manage";
    public const string AiAssist = "portfolio.ai.assist";
    public const string StoryResource = UserStory.EntityType;
    public const string MilestoneResource = Milestone.EntityType;
    public const string SettingsManage = "portfolio.settings.manage";
    public const string AiAssessment = "portfolio.ai.assessment";

    // Phase 11 — Wasel (ADR 0111) and Portfolio completion (ADR 0116).
    public const string VendorRead = "portfolio.vendor.read";
    public const string VendorManage = "portfolio.vendor.manage";
    public const string ProjectVendors = "portfolio.project.vendors";
    public const string CapacityRead = "portfolio.capacity.read";
    public const string ReviewRead = "portfolio.review.read";
    public const string ReviewManage = "portfolio.review.manage";
    public const string VendorNumbering = "portfolio.vendor";

    public const string RequesterRole = "requester";
    public const string ApproverRole = "approver";
    public const string ProcurementRole = "procurement";
    public const string OwnerRole = "owner";
    public const string PmRole = "pm";
    public const string TeamMemberRole = "team-member";

    /// <summary>Role keys used by the default approval chain and the hand-off (ADR 0048, 0051).</summary>
    public const string PmOfficeRoleKey = "portfolio.pm-office";
    public const string DgOfficeRoleKey = "portfolio.dg-office";
    public const string BudgetSectionRoleKey = "portfolio.budget-section";
    public const string AdministrationRoleKey = "portfolio.administration";
    public const string FinanceSectionRoleKey = "portfolio.finance-section";
    public const string GmOfficeRoleKey = "portfolio.gm-office";

    public const string RequestNumbering = "portfolio.request";
    public const string ProjectNumbering = "portfolio.project";

    public const string SourceLookup = "portfolio.suggestion-source";
    public const string ProgramTypeLookup = "portfolio.program-type";
    public const string DeliveryOptionLookup = "portfolio.delivery-option";
    public const string ThemeLookup = "portfolio.operational-theme";
    public const string RiskCategoryLookup = Contracts.PortfolioLookupTypes.RiskCategory;
    public const string StakeholderClassLookup = "portfolio.stakeholder-class";
    public const string LessonTypeLookup = "portfolio.lesson-type";
    public const string KpiIndexClassLookup = "portfolio.kpi-index-class";
}

/// <summary>Data scopes (ADR 0015): own = requester (employee or user), unit = requesting unit.</summary>
public static class RequestScope
{
    public static readonly ScopeMap<ProgramRequest> Map = new(
        PortfolioPermissions.RequestResource, x => x.Id, ownerEmployee: x => x.RequesterEmployeeId, ownerUser: x => x.RequesterUserId, unit: x => x.UnitId);
}

/// <summary>Files on a project / milestone (docs/design/portfolio.md §21).</summary>
public static class ProjectFilePurposes
{
    private const long MaxBytes = 20 * 1024 * 1024;

    private static readonly string[] Kinds =
        [FileKinds.Pdf, FileKinds.Docx, FileKinds.Pptx, FileKinds.Xlsx, FileKinds.Doc, FileKinds.Ppt, FileKinds.Xls, FileKinds.Png, FileKinds.Jpeg, FileKinds.Text];

    public static readonly FilePurpose Document = new("portfolio.project-document", PortfolioPermissions.ProjectResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose Lpo = new("portfolio.lpo", PortfolioPermissions.ProjectResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose Closure = new("portfolio.closure", PortfolioPermissions.ProjectResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose Evidence = new("portfolio.milestone-evidence", PortfolioPermissions.MilestoneResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly IReadOnlyList<FilePurpose> OnProject = [Document, Lpo, Closure];

    public static FilePurpose? ByKind(string kind) => kind switch
    {
        "document" => Document,
        "lpo" => Lpo,
        "closure" => Closure,
        _ => null,
    };

    public static string KindOf(string purpose) => purpose == Lpo.Key ? "lpo" : purpose == Closure.Key ? "closure" : "document";
}

public static class ProjectScope
{
    public static readonly ScopeMap<Project> Map = new(
        PortfolioPermissions.ProjectResource, x => x.Id, ownerEmployee: x => x.RequesterEmployeeId, ownerUser: x => x.RequesterUserId, unit: x => x.UnitId);
}

/// <summary>Files on a request (docs/design/portfolio.md §3.1).</summary>
public static class RequestFilePurposes
{
    private const long MaxBytes = 20 * 1024 * 1024;

    private static readonly string[] Kinds =
        [FileKinds.Pdf, FileKinds.Docx, FileKinds.Pptx, FileKinds.Xlsx, FileKinds.Doc, FileKinds.Ppt, FileKinds.Xls, FileKinds.Png, FileKinds.Jpeg, FileKinds.Text];

    public static readonly FilePurpose Document = new("portfolio.request-document", PortfolioPermissions.RequestResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose CostEstimation = new("portfolio.cost-estimation", PortfolioPermissions.RequestResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose ProjectScope = new("portfolio.project-scope", PortfolioPermissions.RequestResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose Quotation = new("portfolio.quotation", PortfolioPermissions.RequestResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    /// <summary>The Project Scope template of Settings → Portfolio (one file, owner = the installation).</summary>
    public static readonly FilePurpose Template = new("portfolio.template", "portfolio.settings", PortfolioPermissions.SystemCode, Kinds, MaxBytes, SingleFile: true);

    public static readonly IReadOnlyList<FilePurpose> OnRequest = [Document, CostEstimation, ProjectScope, Quotation];

    /// <summary>URL kind → purpose.</summary>
    public static FilePurpose? ByKind(string kind) => kind switch
    {
        "document" => Document,
        CostPaths.CostEstimation => CostEstimation,
        CostPaths.ProjectScope => ProjectScope,
        "quotation" => Quotation,
        _ => null,
    };

    public static string KindOf(string purpose) =>
        purpose == CostEstimation.Key ? CostPaths.CostEstimation
        : purpose == ProjectScope.Key ? CostPaths.ProjectScope
        : purpose == Quotation.Key ? "quotation"
        : "document";
}

/// <summary>The Portfolio system's catalogue (SPEC §1.9; every cell editable in Settings → Roles).</summary>
internal sealed class PortfolioAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(PortfolioPermissions.SystemCode, SystemKind.Module, "إدارة المحافظ والمشاريع", "Portfolio management", 100);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(PortfolioPermissions.RequestRead, "عرض طلبات البرامج", "View program requests"),
        new(PortfolioPermissions.RequestCreate, "إنشاء طلب برنامج", "Create program requests", Scopable: false),
        new(PortfolioPermissions.RequestEdit, "تعديل الطلبات وتقديمها", "Edit and submit program requests"),
        new(PortfolioPermissions.RequestManage, "إدارة الطلبات (التصنيف، الإلغاء، التقييم)", "Manage requests (classification, cancel, assessment)"),
        new(PortfolioPermissions.RequestQuotations, "رفع عروض الأسعار", "Upload quotations"),
        new(PortfolioPermissions.ProjectRead, "عرض المشاريع والمبادرات", "View projects and initiatives"),
        new(PortfolioPermissions.ProjectEdit, "إدارة المشروع (الميثاق، التخطيط، المراحل، التحديث الشهري)", "Run the project (charter, planning, milestones, monthly update)"),
        new(PortfolioPermissions.ProjectContribute, "المساهمة في المشروع (التحديات، الاجتماعات، الدروس)", "Contribute to the project (challenges, meetings, lessons)"),
        new(PortfolioPermissions.ProjectChange, "طلبات التغيير على المشروع", "Raise project change requests"),
        new(PortfolioPermissions.ProjectManage, "إدارة المشاريع (تعيين مدير، تعليق، إلغاء، فتح التحديث)", "Manage projects (assign PM, hold, cancel, open the update window)"),
        new(PortfolioPermissions.ProjectLpo, "رفع أمر الشراء والعقود", "Upload the LPO and contracts"),
        new(PortfolioPermissions.ProjectCosts, "عرض تكاليف وميزانية المشروع", "View project costs and budget"),
        new(PortfolioPermissions.PortfolioManage, "إدارة المحافظ والبرامج", "Manage portfolios and programmes", Scopable: false),
        new(PortfolioPermissions.AiAssist, "مساعدات الذكاء الاصطناعي للمشاريع", "AI assists for projects", Scopable: false),
        new(PortfolioPermissions.SettingsManage, "إدارة إعدادات المحافظ", "Manage portfolio settings", Scopable: false),
        new(PortfolioPermissions.AiAssessment, "تقييم المقترح بالذكاء الاصطناعي", "AI proposal assessment", Scopable: false),
        new(PortfolioPermissions.VendorRead, "عرض سجل الموردين", "View the vendor register", Scopable: false),
        new(PortfolioPermissions.VendorManage, "إدارة سجل الموردين وحسابات واصل", "Manage the vendor register and Wasel accounts", Scopable: false),
        new(PortfolioPermissions.ProjectVendors, "إدارة الموردين في المشروع (واصل)", "Manage vendors on the project (Wasel)"),
        new(PortfolioPermissions.CapacityRead, "عرض سعة الموظفين", "View employee capacity", Scopable: false),
        new(PortfolioPermissions.ReviewRead, "عرض المراجعات الربعية للمحفظة", "View quarterly portfolio reviews", Scopable: false),
        new(PortfolioPermissions.ReviewManage, "إجراء المراجعات الربعية للمحفظة", "Run quarterly portfolio reviews", Scopable: false),
        ResponsibilityPermissions.Definition(PortfolioPermissions.SystemCode),
    ];

    /// <summary>Members page lists (ADR 0166, docs/design/members-responsibilities.md §1).</summary>
    public SystemMemberRoles MemberRoles => new(["administrator"], ["requester"], ["pm-office", "viewer", "department-viewer"]);

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("requester", "مقدم طلب", "Requester",
            [new(PortfolioPermissions.RequestRead, DataScope.Own), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.Own),
                new(PortfolioPermissions.ProjectRead, DataScope.Own), new(PortfolioPermissions.AiAssessment), new(PortfolioPermissions.ProjectCosts, DataScope.Own),
                new(PortfolioPermissions.AiAssist)],
            AssignToEveryone: true),
        new("department-viewer", "مستعرض الإدارة", "Department viewer",
            [new(PortfolioPermissions.RequestRead, DataScope.UnitAndBelow), new(PortfolioPermissions.ProjectRead, DataScope.UnitAndBelow),
                new(PortfolioPermissions.ProjectCosts, DataScope.UnitAndBelow)]),
        new("viewer", "مستعرض المحفظة (القيادة)", "Portfolio viewer (leadership)",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All), new(PortfolioPermissions.ProjectCosts, DataScope.All),
                new(PortfolioPermissions.VendorRead), new(PortfolioPermissions.CapacityRead), new(PortfolioPermissions.ReviewRead)]),
        new("pm-office", "مكتب إدارة المشاريع", "PM Office",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.All),
                new(PortfolioPermissions.RequestManage, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All), new(PortfolioPermissions.AiAssessment),
                .. PmOfficeProjectGrants]),
        new("administrator", "مسؤول نظام المحافظ", "Portfolio administrator",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.All),
                new(PortfolioPermissions.RequestManage, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All), new(PortfolioPermissions.SettingsManage),
                new(PortfolioPermissions.AiAssessment), .. PmOfficeProjectGrants],
            RequiresMfa: true),
        new("dg-office", "مكتب المدير العام", "DG Office", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("budget-section", "قسم الميزانية", "Budget Section", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("administration", "الإدارة الإدارية (المشتريات)", "Administration Dept (procurement)", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("finance-section", "قسم الخدمات المالية", "Finance Service Section", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("gm-office", "مكتب المدير العام (طلبات التغيير)", "GM Office (change requests)", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
    ];

    private static readonly PermissionGrant[] PmOfficeProjectGrants =
    [
        new(PortfolioPermissions.ProjectEdit, DataScope.All), new(PortfolioPermissions.ProjectContribute, DataScope.All), new(PortfolioPermissions.ProjectChange, DataScope.All),
        new(PortfolioPermissions.ProjectManage, DataScope.All), new(PortfolioPermissions.ProjectCosts, DataScope.All), new(PortfolioPermissions.PortfolioManage),
        new(PortfolioPermissions.AiAssist), new(PortfolioPermissions.VendorRead), new(PortfolioPermissions.VendorManage),
        new(PortfolioPermissions.ProjectVendors, DataScope.All), new(PortfolioPermissions.CapacityRead), new(PortfolioPermissions.ReviewRead),
        new(PortfolioPermissions.ReviewManage), new(ResponsibilityPermissions.Manage(PortfolioPermissions.SystemCode), DataScope.All),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.RequesterRole, "مقدم الطلب", "Requester",
            [PortfolioPermissions.RequestRead, PortfolioPermissions.RequestEdit]),
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.ApproverRole, "معتمد الطلب", "Request approver", [PortfolioPermissions.RequestRead]),
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.ProcurementRole, "المشتريات (عروض الأسعار)", "Procurement (quotations)",
            [PortfolioPermissions.RequestRead, PortfolioPermissions.RequestQuotations]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.OwnerRole, "مالك المشروع", "Project owner",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectContribute, PortfolioPermissions.ProjectChange, PortfolioPermissions.ProjectCosts]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.PmRole, "مدير المشروع", "Project manager",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectEdit, PortfolioPermissions.ProjectContribute, PortfolioPermissions.ProjectChange,
                PortfolioPermissions.ProjectCosts, PortfolioPermissions.ProjectVendors, PortfolioPermissions.VendorRead]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.TeamMemberRole, "عضو فريق المشروع", "Project team member",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectContribute]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.ApproverRole, "معتمد المشروع", "Project approver",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectCosts]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.ProcurementRole, "المشتريات (أمر الشراء)", "Procurement (LPO)",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectLpo, PortfolioPermissions.ProjectCosts]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.RequesterRole, "مقدم طلب المشروع", "Project requester",
            [PortfolioPermissions.ProjectRead, PortfolioPermissions.ProjectCosts]),
    ];

    public static readonly NumberingDefinition VendorNumbering = new(
        PortfolioPermissions.VendorNumbering, PortfolioPermissions.SystemCode, "رقم المورد", "Vendor number", "VND-{SEQ:4}");

    public static readonly NumberingDefinition RequestNumbering = new(
        PortfolioPermissions.RequestNumbering, PortfolioPermissions.SystemCode, "رقم طلب البرنامج", "Program request number", "PR-{SEQ:4}");

    public static readonly NumberingDefinition ProjectNumbering = new(
        PortfolioPermissions.ProjectNumbering, PortfolioPermissions.SystemCode, "رقم المشروع / المبادرة", "Project / initiative number", "PROJ-{UNIT}-{YYYY}-{SEQ:3}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(PortfolioPermissions.ThemeLookup, PortfolioPermissions.SystemCode, "المحاور التشغيلية", "Operational themes",
        [
            new("innovative", "مبتكر", "Innovative"),
            new("digital-transformation", "التحول الرقمي", "Digital transformation"),
            new("customer-experience", "تجربة المتعاملين", "Customer experience"),
            new("sustainability", "الاستدامة", "Sustainability"),
        ]),
        new(PortfolioPermissions.RiskCategoryLookup, PortfolioPermissions.SystemCode, "فئات مخاطر المشاريع", "Project risk categories",
        [
            new("schedule", "الجدول الزمني", "Schedule"),
            new("cost", "التكلفة", "Cost"),
            new("scope", "النطاق", "Scope"),
            new("quality", "الجودة", "Quality"),
            new("resources", "الموارد", "Resources"),
            new("vendor", "المورّد", "Vendor"),
            new("technical", "تقنية", "Technical"),
            new(OtherCode.Value, "أخرى", "Other"),
        ]),
        new(PortfolioPermissions.StakeholderClassLookup, PortfolioPermissions.SystemCode, "تصنيف أصحاب المصلحة", "Stakeholder classifications",
        [
            new("internal", "داخلي", "Internal"),
            new("external", "خارجي", "External"),
            new("government", "جهة حكومية", "Government entity"),
            new("contractor", "مقاول / مورّد", "Contractor / supplier"),
            new("customer", "متعامل", "Customer"),
        ]),
        new(PortfolioPermissions.LessonTypeLookup, PortfolioPermissions.SystemCode, "أنواع الدروس المستفادة", "Lesson types",
        [
            new("positive", "إيجابي", "Positive"),
            new("improvement", "فرصة تحسين", "Improvement opportunity"),
            new("risk", "خطر", "Risk"),
        ]),
        new(PortfolioPermissions.KpiIndexClassLookup, PortfolioPermissions.SystemCode, "تصنيف مؤشرات المشاريع", "Project KPI classifications",
        [
            new("outcome", "نتائج", "Outcome"),
            new("output", "مخرجات", "Output"),
            new("operational", "تشغيلي", "Operational"),
        ]),
        new(PortfolioPermissions.SourceLookup, PortfolioPermissions.SystemCode, "مصادر الاقتراح", "Sources of suggestion",
        [
            new("idea", "فكرة", "Idea"),
            new("benchmarking", "مقارنة معيارية", "Benchmarking"),
            new("reports", "تقارير", "Reports"),
            new("global-practices", "ممارسات عالمية", "Global practices"),
            new("questionnaire", "استبيان", "Questionnaire"),
            new("meeting-outputs", "مخرجات اجتماع", "Meeting outputs"),
            new("leadership-guidance", "توجيهات القيادة", "Leadership guidance"),
            new(Contracts.PortfolioSuggestionSources.ActionPlan, "خطة العمل التصحيحية", "Action Plan"),
            new(OtherCode.Value, "أخرى", "Others"),
        ]),
        new(PortfolioPermissions.ProgramTypeLookup, PortfolioPermissions.SystemCode, "أنواع البرامج", "Program types",
        [
            new("digital", "رقمي", "Digital"),
            new("non-digital", "غير رقمي", "Non-digital"),
        ]),
        new(PortfolioPermissions.DeliveryOptionLookup, PortfolioPermissions.SystemCode, "خيارات التنفيذ", "Delivery options",
        [
            new("contracting", "التعاقد مع شركة", "Contracting with a company"),
            new("internal", "التنفيذ الداخلي", "Internal implementation"),
            new("outsourcing-employee", "الاستعانة بموظف خارجي", "Outsourcing an employee"),
            new("ppp", "شراكة بين القطاعين العام والخاص", "Public-private partnership"),
            new(OtherCode.Value, "أخرى", "Other"),
        ]),
    ];
}

/// <summary>Wasel vendor permissions (ADR 0111): no internal roles; given only by the vendor record roles on a project.</summary>
public static class WaselPermissions
{
    public const string SystemCode = "wasel";
    public const string ProjectRead = "wasel.project.read";
    public const string PlanPrepare = "wasel.plan.prepare";
    public const string PlanSubmit = "wasel.plan.submit";
    public const string UpdatePrepare = "wasel.update.prepare";
    public const string UpdateSubmit = "wasel.update.submit";
    public const string DeliverableUpload = "wasel.deliverable.upload";
    public const string ItemPropose = "wasel.item.propose";
    public const string CommentWrite = "wasel.comment.write";
    public const string MeetingConfirm = "wasel.meeting.confirm";

    public static readonly IReadOnlyList<string> ContributorPermissions =
        [ProjectRead, PlanPrepare, UpdatePrepare, DeliverableUpload, ItemPropose, CommentWrite, MeetingConfirm];

    public static readonly IReadOnlyList<string> LeadPermissions = [.. ContributorPermissions, PlanSubmit, UpdateSubmit];
}

internal sealed class WaselAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(WaselPermissions.SystemCode, SystemKind.Module, "واصل — بوابة الموردين", "Wasel — vendor portal", 101);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(WaselPermissions.ProjectRead, "المورد: عرض المشروع المسند", "Vendor: view the assigned project"),
        new(WaselPermissions.PlanPrepare, "المورد: إعداد الخطة المقترحة", "Vendor: prepare the proposed plan"),
        new(WaselPermissions.PlanSubmit, "المورد: تقديم الخطة المقترحة", "Vendor: submit the proposed plan"),
        new(WaselPermissions.UpdatePrepare, "المورد: إعداد تحديث المراحل", "Vendor: prepare milestone updates"),
        new(WaselPermissions.UpdateSubmit, "المورد: تقديم تحديث المراحل", "Vendor: submit milestone updates"),
        new(WaselPermissions.DeliverableUpload, "المورد: رفع المخرجات", "Vendor: upload deliverables"),
        new(WaselPermissions.ItemPropose, "المورد: اقتراح مشكلات وتحديات ومخاطر", "Vendor: propose issues, challenges and risks"),
        new(WaselPermissions.CommentWrite, "المورد: التعليق", "Vendor: comment"),
        new(WaselPermissions.MeetingConfirm, "المورد: تأكيد الاجتماعات", "Vendor: confirm meetings"),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(PortfolioPermissions.ProjectResource, VendorRoles.Lead, "قائد مشروع المورد", "Vendor project lead", WaselPermissions.LeadPermissions),
        new(PortfolioPermissions.ProjectResource, VendorRoles.Contributor, "مساهم من المورد", "Vendor contributor", WaselPermissions.ContributorPermissions),
    ];
}
