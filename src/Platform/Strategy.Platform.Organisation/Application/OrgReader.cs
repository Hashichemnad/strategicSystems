using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>Read side of the structure: current and as-of trees, paths, unit details and history (ADR 0013).</summary>
internal sealed class OrgReader(OrgDbContext db, IAccessService access, IRequestContext context, IClock clock)
{
    private Task<IQueryable<OrgUnit>> VisibleUnitsAsync(CancellationToken cancellationToken) =>
        access.ApplyScopeAsync(db.Units.AsNoTracking(), context.User, OrgPermissions.UnitRead, OrgScopes.Units, cancellationToken);

    public async Task<IReadOnlyList<OrgTreeNodeDto>> GetTreeAsync(DateOnly? asOf, bool includeInactive, CancellationToken cancellationToken)
    {
        var visibleIds = (await VisibleUnitsAsync(cancellationToken).ConfigureAwait(false)).Select(u => u.Id);
        var counts = await db.Employees.AsNoTracking()
            .Where(e => e.Status != EmployeeStatus.Left)
            .GroupBy(e => e.UnitId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        List<OrgTreeNodeDto> nodes;
        if (asOf is null || asOf.Value >= clock.Today)
        {
            var units = await (await VisibleUnitsAsync(cancellationToken).ConfigureAwait(false)).Where(u => includeInactive || u.Active)
                .OrderBy(u => u.Depth).ThenBy(u => u.Code)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var heads = await EmployeeRefsAsync(units.Select(u => u.HeadEmployeeId), cancellationToken).ConfigureAwait(false);
            nodes = units.Select(u => new OrgTreeNodeDto(
                u.Id, u.ParentId, u.Code, u.NameAr, u.NameEn, u.LevelCode, u.Depth, u.Active,
                Lookup(heads, u.HeadEmployeeId), counts.GetValueOrDefault(u.Id))).ToList();
        }
        else
        {
            var date = asOf.Value;
            var versions = await db.UnitVersions.AsNoTracking()
                .Where(v => visibleIds.Contains(v.UnitId) && v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo > date))
                .Where(v => includeInactive || v.Active)
                .OrderBy(v => v.Depth).ThenBy(v => v.Code)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var heads = await EmployeeRefsAsync(versions.Select(v => v.HeadEmployeeId), cancellationToken).ConfigureAwait(false);

            // Employee counts are a current figure; on a past date they would mislead, so they are left out.
            nodes = versions.Select(v => new OrgTreeNodeDto(
                v.UnitId, v.ParentId, v.Code, v.NameAr, v.NameEn, v.LevelCode, v.Depth, v.Active,
                Lookup(heads, v.HeadEmployeeId), 0)).ToList();
        }

        // A unit whose parent is hidden (inactive / out of scope) becomes a root rather than disappearing.
        var ids = nodes.Select(n => n.Id).ToHashSet();
        return nodes.Select(n => n.ParentId is { } p && !ids.Contains(p) ? n with { ParentId = null } : n).ToList();
    }

    public async Task<Result<OrgUnitDto>> GetUnitAsync(Guid id, CancellationToken cancellationToken)
    {
        var unit = await (await VisibleUnitsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (unit is null)
        {
            return Error.NotFound();
        }

        var path = await PathItemsAsync(unit.Path, cancellationToken).ConfigureAwait(false);
        var refs = await EmployeeRefsAsync([unit.HeadEmployeeId, unit.DeputyEmployeeId], cancellationToken).ConfigureAwait(false);
        var currentFrom = await db.UnitVersions.AsNoTracking()
            .Where(v => v.UnitId == id && v.ValidTo == null)
            .Select(v => v.ValidFrom)
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        return new OrgUnitDto(
            unit.Id, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode, unit.ParentId, path,
            Lookup(refs, unit.HeadEmployeeId), Lookup(refs, unit.DeputyEmployeeId), unit.CostCentre, unit.Active,
            unit.EffectiveFrom, unit.EffectiveTo, currentFrom, unit.CreatedAt, unit.UpdatedAt, unit.Version);
    }

    public async Task<Result<IReadOnlyList<OrgUnitVersionDto>>> GetUnitHistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await (await VisibleUnitsAsync(cancellationToken).ConfigureAwait(false)).AnyAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var versions = await db.UnitVersions.AsNoTracking().Where(v => v.UnitId == id)
            .OrderByDescending(v => v.ValidFrom).ToListAsync(cancellationToken).ConfigureAwait(false);
        var refs = await EmployeeRefsAsync(versions.SelectMany(v => new[] { v.HeadEmployeeId, v.DeputyEmployeeId }), cancellationToken)
            .ConfigureAwait(false);
        var result = new List<OrgUnitVersionDto>();
        foreach (var version in versions)
        {
            var path = await PathAsOfAsync(version.Path, version.ValidFrom, cancellationToken).ConfigureAwait(false);
            result.Add(new OrgUnitVersionDto(
                version.ValidFrom, version.ValidTo, version.Code, version.NameAr, version.NameEn, version.LevelCode, path,
                Lookup(refs, version.HeadEmployeeId), Lookup(refs, version.DeputyEmployeeId), version.CostCentre, version.Active));
        }

        return result;
    }

    /// <summary>The unit's chain from the root, as the structure stood on <paramref name="asOf"/> (default today).</summary>
    public async Task<IReadOnlyList<OrgUnitPathItemDto>?> GetPathAsync(Guid unitId, DateOnly? asOf, CancellationToken cancellationToken)
    {
        if (!await (await VisibleUnitsAsync(cancellationToken).ConfigureAwait(false)).AnyAsync(u => u.Id == unitId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var date = asOf ?? clock.Today;
        var version = await db.UnitVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.UnitId == unitId && v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo > date), cancellationToken)
            .ConfigureAwait(false);
        return version is null ? [] : await PathAsOfAsync(version.Path, date, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrgUnitPathItemDto>> PathItemsAsync(string path, CancellationToken cancellationToken)
    {
        var ids = OrgMapping.PathIds(path);
        var units = await db.Units.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken).ConfigureAwait(false);
        return ids.Where(units.ContainsKey).Select(id => OrgMapping.PathItem(units[id])).ToList();
    }

    public async Task<Dictionary<Guid, EmployeeRefDto>> EmployeeRefsAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return await db.Employees.AsNoTracking().Where(e => wanted.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, OrgMapping.Ref, cancellationToken).ConfigureAwait(false);
    }

    public static EmployeeRefDto? Lookup(Dictionary<Guid, EmployeeRefDto> refs, Guid? id) =>
        id is { } key && refs.TryGetValue(key, out var value) ? value : null;

    private async Task<IReadOnlyList<OrgUnitPathItemDto>> PathAsOfAsync(string path, DateOnly date, CancellationToken cancellationToken)
    {
        var ids = OrgMapping.PathIds(path);
        var versions = await db.UnitVersions.AsNoTracking()
            .Where(v => ids.Contains(v.UnitId) && v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo > date))
            .ToDictionaryAsync(v => v.UnitId, cancellationToken).ConfigureAwait(false);
        return ids.Where(versions.ContainsKey).Select(id => OrgMapping.PathItem(versions[id])).ToList();
    }
}
