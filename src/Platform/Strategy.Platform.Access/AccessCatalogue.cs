namespace Strategy.Platform.Access;

/// <summary>Permission codes of the access administration (<c>/api/v1/access</c>).</summary>
public static class AccessPermissions
{
    public const string SystemRead = "access.system.read";
    public const string SystemManage = "access.system.manage";
    public const string RoleRead = "access.role.read";
    public const string RoleManage = "access.role.manage";
    public const string AssignmentRead = "access.assignment.read";
    public const string AssignmentManage = "access.assignment.manage";
    public const string ReviewExport = "access.review.export";
}

internal sealed class AccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("access", SystemKind.Platform, "إدارة الصلاحيات", "Access control", 30);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(AccessPermissions.SystemRead, "عرض الأنظمة", "View systems", Scopable: false),
        new(AccessPermissions.SystemManage, "تفعيل الأنظمة وتحديد الوحدة المالكة", "Enable systems and set the owning unit", Scopable: false),
        new(AccessPermissions.RoleRead, "عرض الأدوار والصلاحيات", "View roles and permissions", Scopable: false),
        new(AccessPermissions.RoleManage, "إدارة الأدوار والصلاحيات", "Manage roles and permissions", Scopable: false),
        new(AccessPermissions.AssignmentRead, "عرض إسناد الأدوار والوصول", "View role assignments and system access", Scopable: false),
        new(AccessPermissions.AssignmentManage, "إسناد الأدوار ونقلها", "Assign and transfer roles", Scopable: false),
        new(AccessPermissions.ReviewExport, "تقرير مراجعة الصلاحيات", "Access review report", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول الصلاحيات", "Access administrator",
            [new(AccessPermissions.SystemRead), new(AccessPermissions.SystemManage), new(AccessPermissions.RoleRead), new(AccessPermissions.RoleManage),
             new(AccessPermissions.AssignmentRead), new(AccessPermissions.AssignmentManage), new(AccessPermissions.ReviewExport)],
            RequiresMfa: true, Locked: true),
        new("reviewer", "مراجع الصلاحيات", "Access reviewer",
            [new(AccessPermissions.SystemRead), new(AccessPermissions.RoleRead), new(AccessPermissions.AssignmentRead), new(AccessPermissions.ReviewExport)]),
    ];
}
