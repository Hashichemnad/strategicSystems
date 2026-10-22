using Strategy.Platform.Organisation.Domain;

namespace Strategy.Platform.Organisation.Application;

public sealed record OrgLevelDto(Guid Id, string Code, string? NameAr, string? NameEn, int Sort, bool Active, uint Version)
{
    public static OrgLevelDto From(OrgLevel level) =>
        new(level.Id, level.Code, level.NameAr, level.NameEn, level.Sort, level.Active, level.Version);
}

public sealed record OrgSettingsDto(bool EnforceLevelOrder, uint Version);

/// <summary>A short reference to an employee shown next to units, managers and delegations.</summary>
public sealed record EmployeeRefDto(Guid Id, string EmployeeNo, string? NameAr, string? NameEn);

/// <summary>A node of the organisation tree (flat list; the client builds the tree from <see cref="ParentId"/>).</summary>
public sealed record OrgTreeNodeDto(
    Guid Id,
    Guid? ParentId,
    string Code,
    string? NameAr,
    string? NameEn,
    string LevelCode,
    int Depth,
    bool Active,
    EmployeeRefDto? Head,
    int EmployeeCount);

public sealed record OrgUnitDto(
    Guid Id,
    string Code,
    string? NameAr,
    string? NameEn,
    string LevelCode,
    Guid? ParentId,
    IReadOnlyList<OrgUnitPathItemDto> Path,
    EmployeeRefDto? Head,
    EmployeeRefDto? Deputy,
    string? CostCentre,
    bool Active,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    DateOnly CurrentVersionFrom,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record OrgUnitPathItemDto(Guid Id, string Code, string? NameAr, string? NameEn, string LevelCode);

public sealed record OrgUnitVersionDto(
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string Code,
    string? NameAr,
    string? NameEn,
    string LevelCode,
    IReadOnlyList<OrgUnitPathItemDto> Path,
    EmployeeRefDto? Head,
    EmployeeRefDto? Deputy,
    string? CostCentre,
    bool Active);

public sealed record EmployeeListItemDto(
    Guid Id,
    string EmployeeNo,
    string? NameAr,
    string? NameEn,
    string Email,
    string? JobTitleAr,
    string? JobTitleEn,
    Guid UnitId,
    string UnitCode,
    string? UnitNameAr,
    string? UnitNameEn,
    string Status,
    bool HasPhoto);

public sealed record EmployeeDto(
    Guid Id,
    string EmployeeNo,
    string? NameAr,
    string? NameEn,
    string Email,
    string? Mobile,
    string? JobTitleAr,
    string? JobTitleEn,
    string? Grade,
    Guid UnitId,
    OrgUnitPathItemDto Unit,
    EmployeeRefDto? Manager,
    string Status,
    DateOnly? JoinDate,
    bool HasPhoto,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record EmployeeAssignmentDto(
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    OrgUnitPathItemDto Unit,
    EmployeeRefDto? Manager,
    string? JobTitleAr,
    string? JobTitleEn);

public sealed record DelegationDto(
    Guid Id,
    EmployeeRefDto Delegator,
    EmployeeRefDto Delegate,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<string> Scope,
    string? Reason,
    string Status,
    string? CancelReason,
    DateTimeOffset CreatedAt,
    uint Version);

internal static class OrgMapping
{
    public static EmployeeRefDto Ref(Employee employee) => new(employee.Id, employee.EmployeeNo, employee.NameAr, employee.NameEn);

    public static OrgUnitPathItemDto PathItem(OrgUnit unit) => new(unit.Id, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode);

    public static OrgUnitPathItemDto PathItem(OrgUnitVersion unit) => new(unit.UnitId, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode);

    /// <summary>Unit ids of a materialised path, root first.</summary>
    public static IReadOnlyList<Guid> PathIds(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}
