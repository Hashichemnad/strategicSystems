using Strategy.Platform.Access.Domain;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Access.Application;

/// <summary>Display names for users (employee name, user name) and units in access screens and the review report.</summary>
internal sealed class PrincipalNames(IUserDirectory users, IEmployeeDirectory employees, IOrgDirectory org)
{
    public async Task<IReadOnlyDictionary<Guid, UnitReference>> UnitsAsync(IEnumerable<Guid> unitIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UnitReference>();
        foreach (var id in unitIds.Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = unit;
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<(string Kind, Guid? Id), PrincipalDto>> PrincipalsAsync(
        IEnumerable<(string Kind, Guid? Id)> principals, CancellationToken ct)
    {
        var list = principals.Distinct().ToList();
        var userIds = list.Where(p => p.Kind == PrincipalKinds.User && p.Id is not null).Select(p => p.Id!.Value).ToList();
        var userMap = userIds.Count == 0 ? new Dictionary<Guid, UserReference>() : await users.GetManyAsync(userIds, ct).ConfigureAwait(false);
        var employeeIds = userMap.Values.Select(u => u.EmployeeId).OfType<Guid>().ToList();
        var employeeMap = employeeIds.Count == 0
            ? new Dictionary<Guid, EmployeeReference>()
            : await employees.GetManyAsync(employeeIds, ct).ConfigureAwait(false);
        var units = await UnitsAsync(list.Where(p => p.Kind == PrincipalKinds.Unit && p.Id is not null).Select(p => p.Id!.Value), ct).ConfigureAwait(false);

        var result = new Dictionary<(string, Guid?), PrincipalDto>();
        foreach (var (kind, id) in list)
        {
            result[(kind, id)] = kind switch
            {
                PrincipalKinds.User when id is { } userId && userMap.TryGetValue(userId, out var user) =>
                    user.EmployeeId is { } employeeId && employeeMap.TryGetValue(employeeId, out var employee)
                        ? new PrincipalDto(kind, id, employee.NameAr, employee.NameEn, user.UserName)
                        : new PrincipalDto(kind, id, null, null, user.UserName),
                PrincipalKinds.Unit when id is { } unitId && units.TryGetValue(unitId, out var unit) =>
                    new PrincipalDto(kind, id, unit.NameAr, unit.NameEn, unit.Code),
                _ => new PrincipalDto(kind, id, null, null, null),
            };
        }

        return result;
    }
}
