namespace Strategy.Platform.Access.Application;

/// <summary>What the signed-in user can open and do; the UI uses it to show menus and actions (the server still checks).</summary>
public sealed record AccessMeDto(IReadOnlyList<string> Systems, IReadOnlyList<string> Permissions, IReadOnlyList<string> Roles);

public sealed record SystemDto(
    string Code, string Kind, string NameAr, string NameEn, bool Enabled, Guid? OwningUnitId, string? OwningUnitNameAr, string? OwningUnitNameEn,
    int Sort, uint Version);

public sealed record PermissionDto(string Code, string SystemCode, string ObjectCode, string Action, string LabelAr, string LabelEn, bool Scopable);

public sealed record RolePermissionDto(string Permission, string Scope);

public sealed record RoleDto(
    Guid Id, string SystemCode, string Code, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn,
    bool Builtin, bool Active, bool RequiresMfa, bool Locked, IReadOnlyList<RolePermissionDto> Permissions, int ActiveAssignments, uint Version);

public sealed record RecordRoleTypeDto(Guid Id, string SystemCode, string ResourceType, string RoleCode, string NameAr, string NameEn, IReadOnlyList<string> Permissions, uint Version);

public sealed record PermissionMatrixDto(SystemDto System, IReadOnlyList<PermissionDto> Permissions, IReadOnlyList<RoleDto> Roles, IReadOnlyList<RecordRoleTypeDto> RecordRoles);

public sealed record PrincipalDto(string Kind, Guid? Id, string? NameAr, string? NameEn, string? Detail);

public sealed record AssignmentDto(
    Guid Id, Guid RoleId, string SystemCode, string RoleCode, string? RoleNameAr, string? RoleNameEn, PrincipalDto Principal,
    DateOnly ValidFrom, DateOnly? ValidTo, string? Note, bool Active, uint Version);

public sealed record SystemAccessDto(Guid Id, string SystemCode, PrincipalDto Principal, bool Active);

public sealed record TransferDto(Guid Id, string RoleKeys, string From, string To, int MovedCount, string? Reason, DateTimeOffset CreatedAt);

public sealed record EffectivePermissionDto(string Permission, IReadOnlyList<string> Scopes);

public sealed record EffectiveRecordRoleDto(string ResourceType, Guid ResourceId, string RoleCode, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo);

public sealed record EffectiveAccessDto(
    Guid UserId, IReadOnlyList<string> Systems, IReadOnlyList<string> Roles, IReadOnlyList<EffectivePermissionDto> Permissions,
    IReadOnlyList<EffectiveRecordRoleDto> RecordRoles, bool RequiresMfa);

public sealed record AccessReviewRowDto(
    Guid UserId, string UserName, bool UserActive, string? EmployeeNameAr, string? EmployeeNameEn, string? UnitCode,
    string SystemCode, bool SystemAccess, string RoleCode, string? RoleNameAr, string? RoleNameEn, string Source, DateOnly ValidFrom, DateOnly? ValidTo,
    IReadOnlyList<RolePermissionDto> Permissions);
