using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Domain;

namespace Strategy.Platform.Organisation.Infrastructure;

internal sealed class OrgDirectory(OrgDbContext db, IClock clock) : IOrgDirectory
{
    public async Task<UnitReference?> GetUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken).ConfigureAwait(false);
        return unit is null ? null : new UnitReference(unit.Id, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode, unit.ParentId, unit.Active);
    }

    public async Task<IReadOnlyList<UnitReference>> GetPathAsync(Guid unitId, DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var version = await VersionOn(unitId, date).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return [];
        }

        var ids = version.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        var versions = await db.UnitVersions.AsNoTracking()
            .Where(v => ids.Contains(v.UnitId) && v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo > date))
            .ToDictionaryAsync(v => v.UnitId, cancellationToken).ConfigureAwait(false);
        return ids.Where(versions.ContainsKey)
            .Select(id => versions[id])
            .Select(v => new UnitReference(v.UnitId, v.Code, v.NameAr, v.NameEn, v.LevelCode, v.ParentId, v.Active))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetSelfAndDescendantIdsAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var path = await db.Units.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Path).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return path is null
            ? []
            : await db.Units.AsNoTracking().Where(u => u.Path.StartsWith(path)).Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid?> GetHeadOfUnitAsync(Guid unitId, DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
        await VersionOn(unitId, asOf ?? clock.Today).Select(v => v.HeadEmployeeId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<OrgUnitVersion> VersionOn(Guid unitId, DateOnly date) =>
        db.UnitVersions.AsNoTracking().Where(v => v.UnitId == unitId && v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo > date));
}

internal sealed class EmployeeDirectory(OrgDbContext db, IClock clock) : IEmployeeDirectory
{
    public Task<EmployeeReference?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        FirstAsync(db.Employees.Where(e => e.Id == employeeId), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, EmployeeReference>> GetManyAsync(
        IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default) =>
        await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .Select(e => new EmployeeReference(e.Id, e.EmployeeNo, e.NameAr, e.NameEn, e.Email, e.UnitId, e.ManagerEmployeeId, e.Status))
            .ToDictionaryAsync(e => e.Id, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> SearchAsync(string search, CancellationToken cancellationToken = default)
    {
        var pattern = "%" + Kernel.Text.SearchText.Normalise(search).Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        return await db.Employees.AsNoTracking().Where(e => EF.Functions.ILike(e.NameSearch, pattern, "\\")).Select(e => e.Id).Take(500)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<EmployeeReference?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalised = Employee.NormaliseEmail(email);
        return FirstAsync(db.Employees.Where(e => e.Email == normalised), cancellationToken);
    }

    public Task<EmployeeReference?> FindByEmployeeNoAsync(string employeeNo, CancellationToken cancellationToken = default)
    {
        var trimmed = employeeNo.Trim();
        return FirstAsync(db.Employees.Where(e => e.EmployeeNo == trimmed), cancellationToken);
    }

    public async Task<Guid?> GetManagerAsync(Guid employeeId, DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        return await db.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.ValidFrom <= date && (a.ValidTo == null || a.ValidTo > date))
            .Select(a => a.ManagerEmployeeId)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EmployeeReference?> FirstAsync(IQueryable<Employee> query, CancellationToken cancellationToken) =>
        await query.AsNoTracking()
            .Select(e => new EmployeeReference(e.Id, e.EmployeeNo, e.NameAr, e.NameEn, e.Email, e.UnitId, e.ManagerEmployeeId, e.Status))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class DelegationQuery(OrgDbContext db) : IDelegationQuery
{
    public async Task<IReadOnlyList<Guid>> GetActiveDelegatesAsync(
        Guid employeeId, DateOnly date, string? scopeKey, CancellationToken cancellationToken = default)
    {
        var candidates = await db.Delegations.AsNoTracking()
            .Where(d => d.DelegatorEmployeeId == employeeId && d.Status == DelegationStatus.Active && d.FromDate <= date && d.ToDate >= date)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates.Where(d => d.Covers(date, scopeKey)).Select(d => d.DelegateEmployeeId).Distinct().ToList();
    }
}

/// <summary>Default directory source: nothing to sync until a customer connector is configured (ADR 0014).</summary>
internal sealed class NoDirectorySyncSource : IDirectorySyncSource
{
    public string Name => "none";

    public async IAsyncEnumerable<EmployeeReference> ReadEmployeesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
