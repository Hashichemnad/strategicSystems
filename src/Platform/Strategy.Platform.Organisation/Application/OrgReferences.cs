using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Integration;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>Units and employees carry external references although they are not linkable types (ADR 0164).</summary>
internal static class OrgReferences
{
    public static readonly ReferenceableTypeDefinition Unit = new(
        OrgPermissions.UnitResource, "org", "وحدة تنظيمية", "Organisation unit", OrgPermissions.UnitRead, "/org/units?unit={id}");

    public static readonly ReferenceableTypeDefinition Employee = new(
        OrgPermissions.EmployeeResource, "org", "موظف", "Employee", OrgPermissions.EmployeeRead, "/org/employees/{id}");
}

/// <summary>Units the user may read (data scope applied in the query).</summary>
internal sealed class UnitReferenceSource(OrgDbContext db, IAccessService access) : IReferenceableRecordSource
{
    public string ResourceType => OrgPermissions.UnitResource;

    public async Task<IReadOnlyDictionary<Guid, ReferencedRecord>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var scoped = await access.ApplyScopeAsync(db.Units.AsNoTracking(), user, OrgPermissions.UnitRead, OrgScopes.Units, cancellationToken).ConfigureAwait(false);
        return await scoped.Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new ReferencedRecord(u.Id, u.Code, u.NameAr, u.NameEn), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Employees the user may read (data scope applied in the query).</summary>
internal sealed class EmployeeReferenceSource(OrgDbContext db, IAccessService access) : IReferenceableRecordSource
{
    public string ResourceType => OrgPermissions.EmployeeResource;

    public async Task<IReadOnlyDictionary<Guid, ReferencedRecord>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var scoped = await access.ApplyScopeAsync(db.Employees.AsNoTracking(), user, OrgPermissions.EmployeeRead, OrgScopes.Employees, cancellationToken)
            .ConfigureAwait(false);
        return await scoped.Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new ReferencedRecord(e.Id, e.EmployeeNo, e.NameAr, e.NameEn), cancellationToken).ConfigureAwait(false);
    }
}
