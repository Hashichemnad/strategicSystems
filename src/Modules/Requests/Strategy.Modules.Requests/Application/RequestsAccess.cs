using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Requests.Application;

/// <summary>Permission, role, numbering, lookup and file codes (docs/design/requests.md §2.9).</summary>
public static class RequestPermissions
{
    public const string SystemCode = "requests";

    public const string Read = "requests.request.read";
    public const string Submit = "requests.request.submit";
    public const string Process = "requests.request.process";
    public const string Manage = "requests.request.manage";
    public const string TypeManage = "requests.type.manage";
    public const string AiExtract = "requests.ai.extract";

    /// <summary>Record role the approval engine grants to approvers of a request while their task is open.</summary>
    public const string ApproverRole = "approver";

    /// <summary>Default approver / processor of custom types (role key).</summary>
    public const string OfficeRoleKey = "requests.office";

    public const string Numbering = "requests.request";
    public const string CategoryLookup = "requests.category";

    /// <summary>Process key of a custom type (ADR 0081).</summary>
    public static string ProcessKeyOf(string typeCode) => $"requests.type.{typeCode}";

    /// <summary>The office role of a system (default approver / processor of its built-in types).</summary>
    public static string OfficeRoleOf(string systemCode) => $"{systemCode}.office";

    /// <summary>The processing permission of a system's built-in types.</summary>
    public static string ProcessPermissionOf(string systemCode) => $"{systemCode}.request.process";
}

public static class RequestScope
{
    public static readonly ScopeMap<Request> Map = new(Request.EntityType, x => x.Id, ownerEmployee: x => x.RequesterEmployeeId, ownerUser: x => x.RequesterUserId,
        unit: x => x.UnitId);
}

public static class RequestFilePurposes
{
    public static readonly FilePurpose Attachment = new("requests.attachment", Request.EntityType, RequestPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);
}

internal sealed class RequestsAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(RequestPermissions.SystemCode, SystemKind.Module, "الطلبات", "Requests", 40);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(RequestPermissions.Read, "عرض الطلبات", "View requests"),
        new(RequestPermissions.Submit, "تقديم الطلبات من الدليل", "Submit requests from the catalogue", Scopable: false),
        new(RequestPermissions.Process, "معالجة الطلبات", "Process requests", Scopable: false),
        new(RequestPermissions.Manage, "إدارة جميع الطلبات (إلغاء)", "Manage all requests (cancel)", Scopable: false),
        new(RequestPermissions.TypeManage, "إدارة أنواع الطلبات والنماذج", "Manage request types and forms", Scopable: false),
        new(RequestPermissions.AiExtract, "تعبئة النموذج من مستند بالذكاء الاصطناعي", "Fill a form from a document with AI", Scopable: false),
    ];

    /// <summary>Members page lists (ADR 0166, docs/design/members-responsibilities.md §1).</summary>
    public SystemMemberRoles MemberRoles => new(["office"], ["requester"], ["type-administrator"]);

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("requester", "مقدم الطلبات", "Requester",
            [new(RequestPermissions.Read, DataScope.Own), new(RequestPermissions.Submit), new(RequestPermissions.AiExtract)],
            AssignToEveryone: true),
        new("office", "مكتب الاستراتيجية / مكتب إدارة المشاريع (الطلبات)", "Strategy Office / PM Office (requests)",
            [new(RequestPermissions.Read, DataScope.All), new(RequestPermissions.Submit), new(RequestPermissions.Process), new(RequestPermissions.Manage),
                new(RequestPermissions.AiExtract)],
            RequiresMfa: true),
        new("type-administrator", "مسؤول أنواع الطلبات", "Request types administrator",
            [new(RequestPermissions.Read, DataScope.All), new(RequestPermissions.TypeManage)], RequiresMfa: true),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(Request.EntityType, RequestPermissions.ApproverRole, "معتمد الطلب", "Request approver", [RequestPermissions.Read]),
    ];

    public static readonly NumberingDefinition Numbering = new(RequestPermissions.Numbering, RequestPermissions.SystemCode, "رقم الطلب", "Request number",
        "{TYPE}-{YYYY}-{SEQ:4}");

    public static readonly LookupTypeDefinition Categories = new(RequestPermissions.CategoryLookup, RequestPermissions.SystemCode, "فئة الطلب", "Request category",
    [
        new("documents", "الوثائق", "Documents"), new("intellectual-property", "الملكية الفكرية", "Intellectual property"),
        new("services", "الخدمات", "Services"), new("general", "عام", "General"),
    ]);
}
