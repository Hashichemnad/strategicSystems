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
    public const string SettingsManage = "portfolio.settings.manage";
    public const string AiAssessment = "portfolio.ai.assessment";

    public const string RequesterRole = "requester";
    public const string ApproverRole = "approver";
    public const string ProcurementRole = "procurement";
    public const string OwnerRole = "owner";

    /// <summary>Role keys used by the default approval chain and the hand-off (ADR 0048, 0051).</summary>
    public const string PmOfficeRoleKey = "portfolio.pm-office";
    public const string DgOfficeRoleKey = "portfolio.dg-office";
    public const string BudgetSectionRoleKey = "portfolio.budget-section";
    public const string AdministrationRoleKey = "portfolio.administration";

    public const string RequestNumbering = "portfolio.request";
    public const string ProjectNumbering = "portfolio.project";

    public const string ObjectiveLookup = "portfolio.strategic-objective";
    public const string SourceLookup = "portfolio.suggestion-source";
    public const string ProgramTypeLookup = "portfolio.program-type";
    public const string DeliveryOptionLookup = "portfolio.delivery-option";
}

/// <summary>Data scopes (ADR 0015): own = requester (employee or user), unit = requesting unit.</summary>
public static class RequestScope
{
    public static readonly ScopeMap<ProgramRequest> Map = new(
        PortfolioPermissions.RequestResource, x => x.Id, ownerEmployee: x => x.RequesterEmployeeId, ownerUser: x => x.RequesterUserId, unit: x => x.UnitId);
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
        new(PortfolioPermissions.SettingsManage, "إدارة إعدادات المحافظ", "Manage portfolio settings", Scopable: false),
        new(PortfolioPermissions.AiAssessment, "تقييم المقترح بالذكاء الاصطناعي", "AI proposal assessment", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("requester", "مقدم طلب", "Requester",
            [new(PortfolioPermissions.RequestRead, DataScope.Own), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.Own),
                new(PortfolioPermissions.ProjectRead, DataScope.Own), new(PortfolioPermissions.AiAssessment)],
            AssignToEveryone: true),
        new("department-viewer", "مستعرض الإدارة", "Department viewer",
            [new(PortfolioPermissions.RequestRead, DataScope.UnitAndBelow), new(PortfolioPermissions.ProjectRead, DataScope.UnitAndBelow)]),
        new("viewer", "مستعرض المحفظة (القيادة)", "Portfolio viewer (leadership)",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All)]),
        new("pm-office", "مكتب إدارة المشاريع", "PM Office",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.All),
                new(PortfolioPermissions.RequestManage, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All), new(PortfolioPermissions.AiAssessment)]),
        new("administrator", "مسؤول نظام المحافظ", "Portfolio administrator",
            [new(PortfolioPermissions.RequestRead, DataScope.All), new(PortfolioPermissions.RequestCreate), new(PortfolioPermissions.RequestEdit, DataScope.All),
                new(PortfolioPermissions.RequestManage, DataScope.All), new(PortfolioPermissions.ProjectRead, DataScope.All), new(PortfolioPermissions.SettingsManage),
                new(PortfolioPermissions.AiAssessment)],
            RequiresMfa: true),
        new("dg-office", "مكتب المدير العام", "DG Office", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("budget-section", "قسم الميزانية", "Budget Section", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
        new("administration", "الإدارة الإدارية (المشتريات)", "Administration Dept (procurement)", [new(PortfolioPermissions.RequestRead, DataScope.Own)]),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.RequesterRole, "مقدم الطلب", "Requester",
            [PortfolioPermissions.RequestRead, PortfolioPermissions.RequestEdit]),
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.ApproverRole, "معتمد الطلب", "Request approver", [PortfolioPermissions.RequestRead]),
        new(PortfolioPermissions.RequestResource, PortfolioPermissions.ProcurementRole, "المشتريات (عروض الأسعار)", "Procurement (quotations)",
            [PortfolioPermissions.RequestRead, PortfolioPermissions.RequestQuotations]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.OwnerRole, "مالك المشروع", "Project owner", [PortfolioPermissions.ProjectRead]),
        new(PortfolioPermissions.ProjectResource, PortfolioPermissions.RequesterRole, "مقدم طلب المشروع", "Project requester", [PortfolioPermissions.ProjectRead]),
    ];

    public static readonly NumberingDefinition RequestNumbering = new(
        PortfolioPermissions.RequestNumbering, PortfolioPermissions.SystemCode, "رقم طلب البرنامج", "Program request number", "PR-{SEQ:4}");

    public static readonly NumberingDefinition ProjectNumbering = new(
        PortfolioPermissions.ProjectNumbering, PortfolioPermissions.SystemCode, "رقم المشروع / المبادرة", "Project / initiative number", "PROJ-{UNIT}-{YYYY}-{SEQ:3}");

    public static readonly IReadOnlyList<LookupTypeDefinition> Lookups =
    [
        new(PortfolioPermissions.ObjectiveLookup, PortfolioPermissions.SystemCode, "الأهداف الاستراتيجية", "Strategic objectives", []),
        new(PortfolioPermissions.SourceLookup, PortfolioPermissions.SystemCode, "مصادر الاقتراح", "Sources of suggestion",
        [
            new("idea", "فكرة", "Idea"),
            new("benchmarking", "مقارنة معيارية", "Benchmarking"),
            new("reports", "تقارير", "Reports"),
            new("global-practices", "ممارسات عالمية", "Global practices"),
            new("questionnaire", "استبيان", "Questionnaire"),
            new("meeting-outputs", "مخرجات اجتماع", "Meeting outputs"),
            new("leadership-guidance", "توجيهات القيادة", "Leadership guidance"),
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
