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
}

public static class AccessCatalogueServiceCollectionExtensions
{
    public static IServiceCollection AddAccessCatalogue(this IServiceCollection services, IAccessCatalogueSource source)
    {
        services.AddSingleton(source);
        return services;
    }
}
