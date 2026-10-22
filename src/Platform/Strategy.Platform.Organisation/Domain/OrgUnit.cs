using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Platform.Organisation.Domain;

/// <summary>The editable details of a unit; structure (parent) changes go through <see cref="OrgUnit.MoveUnder"/>.</summary>
public sealed record UnitDetails(
    string Code,
    string? NameAr,
    string? NameEn,
    string LevelCode,
    Guid? HeadEmployeeId,
    Guid? DeputyEmployeeId,
    string? CostCentre);

/// <summary>
/// An organisation unit: current state. Each change also closes / opens an <see cref="OrgUnitVersion"/> at its
/// effective date, so historical records keep showing the structure of their time (ADR 0013).
/// </summary>
public sealed class OrgUnit : AuditableEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int CostCentreMaxLength = 50;

    private OrgUnit()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string LevelCode { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    /// <summary>Ids from the root down to this unit: <c>/root/…/this/</c>. "Unit and below" = path prefix.</summary>
    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public Guid? DeputyEmployeeId { get; private set; }

    public string? CostCentre { get; private set; }

    public bool Active { get; private set; } = true;

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    public string NameSearch { get; private set; } = string.Empty;

    public static OrgUnit Create(UnitDetails details, OrgUnit? parent, DateOnly effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(details);
        var unit = new OrgUnit { EffectiveFrom = effectiveFrom };
        unit.Apply(details);
        unit.ParentId = parent?.Id;
        unit.Path = PathOf(parent?.Path, unit.Id);
        unit.Depth = parent is null ? 0 : parent.Depth + 1;
        return unit;
    }

    public void UpdateDetails(UnitDetails details) => Apply(details);

    /// <summary>
    /// Re-parents this unit. The caller rebases every descendant with <see cref="RebasePath"/> in the same transaction.
    /// Refuses moving a unit under itself or one of its descendants.
    /// </summary>
    public Result MoveUnder(OrgUnit? newParent)
    {
        if (newParent is not null && newParent.Path.StartsWith(Path, StringComparison.Ordinal))
        {
            return OrgErrors.UnitCycle;
        }

        ParentId = newParent?.Id;
        Path = PathOf(newParent?.Path, Id);
        Depth = newParent is null ? 0 : newParent.Depth + 1;
        return Result.Success();
    }

    /// <summary>Moves a descendant along with its ancestor: replaces the old path prefix with the new one.</summary>
    public void RebasePath(string oldPrefix, string newPrefix, int depthDelta)
    {
        ArgumentNullException.ThrowIfNull(oldPrefix);
        if (!Path.StartsWith(oldPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unit is not below the moved unit.");
        }

        Path = string.Concat(newPrefix, Path.AsSpan(oldPrefix.Length));
        Depth += depthDelta;
    }

    public Result Deactivate(DateOnly effective)
    {
        if (!Active)
        {
            return OrgErrors.UnitAlreadyInactive;
        }

        Active = false;
        EffectiveTo = effective;
        return Result.Success();
    }

    public Result Activate()
    {
        if (Active)
        {
            return OrgErrors.UnitAlreadyActive;
        }

        Active = true;
        EffectiveTo = null;
        return Result.Success();
    }

    public bool IsAncestorOf(OrgUnit other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Id != Id && other.Path.StartsWith(Path, StringComparison.Ordinal);
    }

    public static string PathOf(string? parentPath, Guid id) => (parentPath ?? "/") + id.ToString("N") + "/";

    private void Apply(UnitDetails details)
    {
        Code = details.Code.Trim();
        NameAr = Text.Clean(details.NameAr);
        NameEn = Text.Clean(details.NameEn);
        LevelCode = details.LevelCode.Trim();
        HeadEmployeeId = details.HeadEmployeeId;
        DeputyEmployeeId = details.DeputyEmployeeId;
        CostCentre = Text.Clean(details.CostCentre);
        NameSearch = SearchText.Normalise(Code, NameAr, NameEn);
    }
}

/// <summary>One effective-dated state of a unit: valid on <c>[ValidFrom, ValidTo)</c>; <c>ValidTo = null</c> is current.</summary>
public sealed class OrgUnitVersion : AuditableEntity
{
    private OrgUnitVersion()
    {
    }

    public Guid UnitId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string LevelCode { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public Guid? HeadEmployeeId { get; private set; }

    public Guid? DeputyEmployeeId { get; private set; }

    public string? CostCentre { get; private set; }

    public bool Active { get; private set; }

    public bool IsValidOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || ValidTo > date);

    public static OrgUnitVersion Snapshot(OrgUnit unit, DateOnly validFrom)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var version = new OrgUnitVersion { UnitId = unit.Id, ValidFrom = validFrom };
        version.CopyFrom(unit);
        return version;
    }

    internal void CopyFrom(OrgUnit unit)
    {
        Code = unit.Code;
        NameAr = unit.NameAr;
        NameEn = unit.NameEn;
        LevelCode = unit.LevelCode;
        ParentId = unit.ParentId;
        Path = unit.Path;
        Depth = unit.Depth;
        HeadEmployeeId = unit.HeadEmployeeId;
        DeputyEmployeeId = unit.DeputyEmployeeId;
        CostCentre = unit.CostCentre;
        Active = unit.Active;
    }

    internal void Close(DateOnly validTo) => ValidTo = validTo;
}

/// <summary>Keeps the version history in step with a change to the current unit (ADR 0013).</summary>
public static class UnitHistory
{
    /// <summary>Checks that a change may take effect on <paramref name="effective"/>, given the unit's current version.</summary>
    public static Result CanApply(OrgUnitVersion current, DateOnly effective, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (effective > today)
        {
            return OrgErrors.EffectiveDateInFuture;
        }

        return effective < current.ValidFrom ? OrgErrors.EffectiveDateTooEarly(current.ValidFrom) : Result.Success();
    }

    /// <summary>
    /// Records the unit's new state from <paramref name="effective"/>: closes the current version and returns a new one,
    /// or, for a change on the current version's start date, rewrites that version and returns null.
    /// </summary>
    public static OrgUnitVersion? Record(OrgUnit unit, OrgUnitVersion current, DateOnly effective)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(current);
        if (effective == current.ValidFrom)
        {
            current.CopyFrom(unit);
            return null;
        }

        current.Close(effective);
        return OrgUnitVersion.Snapshot(unit, effective);
    }
}
