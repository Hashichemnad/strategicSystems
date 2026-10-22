using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>
/// Changes to the unit structure with their history (ADR 0013). Works on the tracked context and never saves:
/// handlers and the importer save once, so a change and all its versions commit together.
/// </summary>
internal sealed class OrgStructure(OrgDbContext db, IClock clock)
{
    public DateOnly Today => clock.Today;

    public async Task<Result<OrgUnit>> CreateAsync(UnitDetails details, Guid? parentId, DateOnly? effective, CancellationToken cancellationToken)
    {
        var effectiveFrom = effective ?? Today;
        if (effectiveFrom > Today)
        {
            return OrgErrors.EffectiveDateInFuture;
        }

        if (await CodeTakenAsync(details.Code, null, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.DuplicateUnitCode;
        }

        OrgUnit? parent = null;
        if (parentId is not null)
        {
            parent = await FindTrackedAsync(parentId.Value, cancellationToken).ConfigureAwait(false);
            if (parent is null)
            {
                return Error.Rule("org.unit.parentNotFound", "org.errors.parentNotFound");
            }

            if (!parent.Active)
            {
                return OrgErrors.ParentInactive;
            }
        }

        var check = await CheckReferencesAsync(details, parent, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var unit = OrgUnit.Create(details, parent, effectiveFrom);
        db.Units.Add(unit);
        db.UnitVersions.Add(OrgUnitVersion.Snapshot(unit, effectiveFrom));
        return unit;
    }

    public async Task<Result> UpdateAsync(OrgUnit unit, UnitDetails details, DateOnly? effective, CancellationToken cancellationToken)
    {
        if (await CodeTakenAsync(details.Code, unit.Id, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.DuplicateUnitCode;
        }

        var parent = unit.ParentId is null ? null : await FindTrackedAsync(unit.ParentId.Value, cancellationToken).ConfigureAwait(false);
        var check = await CheckReferencesAsync(details, parent, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check;
        }

        if (details.LevelCode != unit.LevelCode)
        {
            var childCheck = await CheckChildrenLevelAsync(unit.Id, details.LevelCode, cancellationToken).ConfigureAwait(false);
            if (childCheck.IsFailure)
            {
                return childCheck;
            }
        }

        return await RecordAsync(unit, effective, () =>
        {
            unit.UpdateDetails(details);
            return Result.Success();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-parents a unit and rebases the whole subtree, each with a new version at the effective date.</summary>
    public async Task<Result> MoveAsync(OrgUnit unit, Guid? newParentId, DateOnly? effective, CancellationToken cancellationToken)
    {
        if (newParentId == unit.ParentId)
        {
            return Error.Rule("org.unit.sameParent", "org.errors.sameParent");
        }

        OrgUnit? newParent = null;
        if (newParentId is not null)
        {
            newParent = await FindTrackedAsync(newParentId.Value, cancellationToken).ConfigureAwait(false);
            if (newParent is null)
            {
                return Error.Rule("org.unit.parentNotFound", "org.errors.parentNotFound");
            }

            if (!newParent.Active)
            {
                return OrgErrors.ParentInactive;
            }

            var levelCheck = await CheckLevelOrderAsync(newParent.LevelCode, unit.LevelCode, cancellationToken).ConfigureAwait(false);
            if (levelCheck.IsFailure)
            {
                return levelCheck;
            }
        }

        var effectiveDate = effective ?? Today;
        var oldPrefix = unit.Path;
        var oldDepth = unit.Depth;
        var descendants = await db.Units
            .Where(u => u.Path.StartsWith(oldPrefix) && u.Id != unit.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Validate the date against every affected unit before changing anything.
        var currents = await CurrentVersionsAsync([unit.Id, .. descendants.Select(d => d.Id)], cancellationToken).ConfigureAwait(false);
        foreach (var current in currents.Values)
        {
            var dateCheck = UnitHistory.CanApply(current, effectiveDate, Today);
            if (dateCheck.IsFailure)
            {
                return dateCheck;
            }
        }

        var moved = unit.MoveUnder(newParent);
        if (moved.IsFailure)
        {
            return moved;
        }

        foreach (var descendant in descendants)
        {
            descendant.RebasePath(oldPrefix, unit.Path, unit.Depth - oldDepth);
        }

        foreach (var affected in descendants.Prepend(unit))
        {
            var next = UnitHistory.Record(affected, currents[affected.Id], effectiveDate);
            if (next is not null)
            {
                db.UnitVersions.Add(next);
            }
        }

        return Result.Success();
    }

    public async Task<Result> DeactivateAsync(OrgUnit unit, DateOnly? effective, CancellationToken cancellationToken)
    {
        if (await db.Units.AnyAsync(u => u.ParentId == unit.Id && u.Active, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.UnitHasActiveChildren;
        }

        if (await db.Employees.AnyAsync(e => e.UnitId == unit.Id && e.Status != EmployeeStatus.Left, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.UnitHasActiveEmployees;
        }

        var effectiveDate = effective ?? Today;
        return await RecordAsync(unit, effectiveDate, () => unit.Deactivate(effectiveDate), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> ActivateAsync(OrgUnit unit, DateOnly? effective, CancellationToken cancellationToken)
    {
        if (unit.ParentId is not null)
        {
            var parent = await FindTrackedAsync(unit.ParentId.Value, cancellationToken).ConfigureAwait(false);
            if (parent is { Active: false })
            {
                return OrgErrors.ParentInactive;
            }
        }

        return await RecordAsync(unit, effective, unit.Activate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrgUnit?> FindTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        db.Units.Local.FirstOrDefault(u => u.Id == id)
        ?? await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

    private async Task<Result> RecordAsync(OrgUnit unit, DateOnly? effective, Func<Result> change, CancellationToken cancellationToken)
    {
        var effectiveDate = effective ?? Today;
        var currents = await CurrentVersionsAsync([unit.Id], cancellationToken).ConfigureAwait(false);
        var current = currents[unit.Id];
        var dateCheck = UnitHistory.CanApply(current, effectiveDate, Today);
        if (dateCheck.IsFailure)
        {
            return dateCheck;
        }

        var outcome = change();
        if (outcome.IsFailure)
        {
            return outcome;
        }

        var next = UnitHistory.Record(unit, current, effectiveDate);
        if (next is not null)
        {
            db.UnitVersions.Add(next);
        }

        return Result.Success();
    }

    private async Task<Dictionary<Guid, OrgUnitVersion>> CurrentVersionsAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken)
    {
        var local = db.UnitVersions.Local.Where(v => v.ValidTo is null && unitIds.Contains(v.UnitId)).ToList();
        var missing = unitIds.Except(local.Select(v => v.UnitId)).ToList();
        if (missing.Count > 0)
        {
            local.AddRange(await db.UnitVersions
                .Where(v => v.ValidTo == null && missing.Contains(v.UnitId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        return local.ToDictionary(v => v.UnitId);
    }

    private async Task<bool> CodeTakenAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = code.Trim();
        return db.Units.Local.Any(u => u.Code == trimmed && u.Id != exceptId)
            || await db.Units.AnyAsync(u => u.Code == trimmed && u.Id != exceptId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> CheckReferencesAsync(UnitDetails details, OrgUnit? parent, CancellationToken cancellationToken)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Code == details.LevelCode.Trim(), cancellationToken).ConfigureAwait(false);
        if (level is null || !level.Active)
        {
            return OrgErrors.UnknownLevel;
        }

        if (parent is not null)
        {
            var levelCheck = await CheckLevelOrderAsync(parent.LevelCode, level.Code, cancellationToken).ConfigureAwait(false);
            if (levelCheck.IsFailure)
            {
                return levelCheck;
            }
        }

        foreach (var employeeId in new[] { details.HeadEmployeeId, details.DeputyEmployeeId }.OfType<Guid>())
        {
            var exists = db.Employees.Local.Any(e => e.Id == employeeId)
                || await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                return OrgErrors.EmployeeNotFound;
            }
        }

        return Result.Success();
    }

    private async Task<Result> CheckChildrenLevelAsync(Guid unitId, string newLevelCode, CancellationToken cancellationToken)
    {
        var childLevels = await db.Units.Where(u => u.ParentId == unitId).Select(u => u.LevelCode).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var childLevel in childLevels)
        {
            var check = await CheckLevelOrderAsync(newLevelCode, childLevel, cancellationToken).ConfigureAwait(false);
            if (check.IsFailure)
            {
                return check;
            }
        }

        return Result.Success();
    }

    private async Task<Result> CheckLevelOrderAsync(string parentLevel, string childLevel, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.AsNoTracking().FirstAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.EnforceLevelOrder)
        {
            return Result.Success();
        }

        var sorts = await db.Levels.AsNoTracking()
            .Where(l => l.Code == parentLevel || l.Code == childLevel)
            .ToDictionaryAsync(l => l.Code, l => l.Sort, cancellationToken)
            .ConfigureAwait(false);
        return sorts.TryGetValue(parentLevel, out var parentSort) && sorts.TryGetValue(childLevel, out var childSort) && childSort > parentSort
            ? Result.Success()
            : OrgErrors.LevelOrder;
    }
}
