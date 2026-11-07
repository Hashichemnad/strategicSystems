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

/// <summary>A system's Members page (ADR 0166): its lists in order (administrators, users, extra roles).</summary>
/// <param name="System">The system.</param>
/// <param name="PlatformAdministrator">The caller holds <c>access.assignment.manage</c> (may also manage platform administrator roles).</param>
/// <param name="Lists">Administrators, users, then one list per extra role.</param>
public sealed record SystemMembersDto(SystemDto System, bool PlatformAdministrator, IReadOnlyList<SystemMemberListDto> Lists);

/// <param name="Key"><c>administrators</c>, <c>users</c> or <c>extra:&lt;roleCode&gt;</c>.</param>
/// <param name="Kind"><c>administrators</c>, <c>users</c> or <c>extra</c>.</param>
/// <param name="Roles">The roles an entry of this list holds.</param>
/// <param name="CanManage">False when the list holds a platform administrator role and the caller is a delegated administrator (ADR 0167).</param>
/// <param name="Members">One row per principal.</param>
public sealed record SystemMemberListDto(string Key, string Kind, IReadOnlyList<SystemMemberRoleDto> Roles, bool CanManage, IReadOnlyList<SystemMemberDto> Members);

public sealed record SystemMemberRoleDto(string Code, string? NameAr, string? NameEn);

/// <param name="Id">Pass to <c>…/members/{id}/remove</c>.</param>
/// <param name="Principal">User, unit (and below) or everyone.</param>
/// <param name="ValidFrom">First day of the list's roles (null: system access only).</param>
/// <param name="ValidTo">Last day (null: open-ended).</param>
/// <param name="Active">In force today.</param>
/// <param name="SystemAccess">The principal holds its own system-access grant (always true for platform-kind systems).</param>
/// <param name="RoleCodes">The list's roles the principal holds.</param>
public sealed record SystemMemberDto(
    Guid Id, PrincipalDto Principal, DateOnly? ValidFrom, DateOnly? ValidTo, bool Active, bool SystemAccess, IReadOnlyList<string> RoleCodes);

/// <summary>A system's member-role configuration (Settings, platform administrator) with the code defaults.</summary>
public sealed record SystemMemberRolesConfigDto(
    string SystemCode, IReadOnlyList<string> AdministratorRoles, IReadOnlyList<string> UserRoles, IReadOnlyList<string> ExtraRoles,
    SystemMemberRolesDefaultsDto Defaults, bool IsDefault, uint Version);

public sealed record SystemMemberRolesDefaultsDto(IReadOnlyList<string> AdministratorRoles, IReadOnlyList<string> UserRoles, IReadOnlyList<string> ExtraRoles);
