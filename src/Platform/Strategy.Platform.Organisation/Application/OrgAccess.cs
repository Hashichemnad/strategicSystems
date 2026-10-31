using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Organisation.Application;

/// <summary>How organisation records map to data scopes (ADR 0015): a unit is its own unit; an employee is owned by itself.</summary>
public static class OrgScopes
{
    public static readonly ScopeMap<OrgUnit> Units = new(OrgPermissions.UnitResource, u => u.Id, unit: u => (Guid?)u.Id);

    public static readonly ScopeMap<Employee> Employees =
        new(OrgPermissions.EmployeeResource, e => e.Id, ownerEmployee: e => (Guid?)e.Id, unit: e => (Guid?)e.UnitId);
}

public static class OrgLookups
{
    public const string Grade = "employee.grade";
}

/// <summary>Seeded catalogue of the organisation service: permissions with AR/EN labels and default roles (ADR 0016).</summary>
internal sealed class OrgAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("org", SystemKind.Platform, "الهيكل التنظيمي والموظفون", "Organisation and employees", 10);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(OrgPermissions.UnitRead, "عرض الوحدات التنظيمية", "View organisation units"),
        new(OrgPermissions.UnitManage, "إدارة الهيكل التنظيمي", "Manage the organisation structure", Scopable: false),
        new(OrgPermissions.EmployeeRead, "عرض الموظفين", "View employees"),
        new(OrgPermissions.EmployeeManage, "إدارة الموظفين", "Manage employees", Scopable: false),
        new(OrgPermissions.ImportManage, "استيراد الوحدات والموظفين", "Import units and employees", Scopable: false),
        new(OrgPermissions.DelegationManage, "إدارة تفويضات جميع الموظفين", "Manage everyone's delegations", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("viewer", "مستعرض الهيكل التنظيمي", "Organisation viewer",
            [new(OrgPermissions.UnitRead), new(OrgPermissions.EmployeeRead)], AssignToEveryone: true),
        new("administrator", "مسؤول الهيكل التنظيمي", "Organisation administrator",
            [new(OrgPermissions.UnitRead), new(OrgPermissions.UnitManage), new(OrgPermissions.EmployeeRead), new(OrgPermissions.EmployeeManage),
             new(OrgPermissions.ImportManage), new(OrgPermissions.DelegationManage)],
            RequiresMfa: true),
    ];
}

/// <summary>Grades entered as free text in Phase 1 become lookup values once (ADR 0014).</summary>
internal sealed class GradeBackfill(OrgDbContext db) : ILookupBackfillSource
{
    public string TypeCode => OrgLookups.Grade;

    public async Task<IReadOnlyList<string>> GetCodesInUseAsync(CancellationToken cancellationToken) =>
        await db.Employees.AsNoTracking().Where(e => e.Grade != null).Select(e => e.Grade!).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal static class OrgLookupDefinitions
{
    public static readonly LookupTypeDefinition Grade = new(
        OrgLookups.Grade, "org", "الدرجة الوظيفية", "Grade",
        [.. Enumerable.Range(1, 12).Select(i => new LookupSeed($"G{i:D2}", $"الدرجة {i}", $"Grade {i}"))]);

}
