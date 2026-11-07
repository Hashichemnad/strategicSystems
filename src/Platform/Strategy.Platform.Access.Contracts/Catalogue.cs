using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Access;

/// <summary>Platform areas are always on and need no system-access grant; modules can be disabled and need one.</summary>
public enum SystemKind
{
    Platform = 1,
    Module = 2,
}

/// <summary>A system (permission prefix) in the registry, with its seeded AR/EN name.</summary>
public sealed record SystemDefinition(string Code, SystemKind Kind, string NameAr, string NameEn, int Sort);

/// <summary>
/// A permission in the seeded catalogue (<c>&lt;system&gt;.&lt;object&gt;.&lt;action&gt;</c>) with AR/EN labels.
/// <paramref name="Scopable"/> false: a data scope does not apply (create, administration) and any grant behaves as All.
/// </summary>
public sealed record PermissionDefinition(string Code, string LabelAr, string LabelEn, bool Scopable = true);

/// <summary>A permission granted by a default role, with its data scope.</summary>
public sealed record PermissionGrant(string Permission, DataScope Scope = DataScope.All);

/// <summary>A default role, seeded once (administrators may edit it afterwards). Codes are unique within the system.</summary>
public sealed record RoleSeed(
    string Code,
    string NameAr,
    string NameEn,
    IReadOnlyList<PermissionGrant> Permissions,
    bool RequiresMfa = false,
    bool Locked = false,
    bool AssignToEveryone = false);

/// <summary>A role a person can hold on one record (e.g. Project Manager), with the permissions it gives on that record.</summary>
public sealed record RecordRoleDefinition(string ResourceType, string RoleCode, string NameAr, string NameEn, IReadOnlyList<string> Permissions);

/// <summary>Declared in code by each platform component and module; synchronised into <c>access.*</c> at start-up.</summary>
public interface IAccessCatalogueSource
{
    SystemDefinition System { get; }

    IEnumerable<PermissionDefinition> Permissions { get; }

    IEnumerable<RoleSeed> DefaultRoles => [];

    IEnumerable<RecordRoleDefinition> RecordRoles => [];

    /// <summary>
    /// Default roles of the system's Members page (SPEC-ADDENDUM §D1, ADR 0166): administrators, users and extra lists, as codes of
    /// <see cref="DefaultRoles"/>. Null: the system has no Members page. The platform administrator can override them in Settings.
    /// </summary>
    SystemMemberRoles? MemberRoles => null;
}

/// <summary>
/// Which roles of a system the Members lists mean (ADR 0166): <paramref name="AdministratorRoles"/> (system administrators, people only),
/// <paramref name="UserRoles"/> (users: people, units with sub-units, everyone; plus system access) and one extra list per
/// <paramref name="ExtraRoles"/> entry. Codes are role codes within the system; the three lists are disjoint.
/// </summary>
public sealed record SystemMemberRoles(IReadOnlyList<string> AdministratorRoles, IReadOnlyList<string> UserRoles, IReadOnlyList<string> ExtraRoles)
{
    /// <summary>The delegated-administration permission of a system (ADR 0167), e.g. <c>portfolio.members.manage</c>.</summary>
    public static string ManagePermission(string systemCode) => $"{systemCode}.members.manage";

    /// <summary>Every role code of the three lists.</summary>
    public IEnumerable<string> AllRoles => AdministratorRoles.Concat(UserRoles).Concat(ExtraRoles);
}

public static class AccessCatalogueServiceCollectionExtensions
{
    public static IServiceCollection AddAccessCatalogue(this IServiceCollection services, IAccessCatalogueSource source)
    {
        services.AddSingleton(source);
        return services;
    }
}
