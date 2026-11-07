using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Access.Application;

/// <summary>
/// Synchronises the code-declared catalogue into <c>access.*</c> after migrations (docs/design/access.md):
/// systems and permissions follow the code; default roles and record-role types are inserted only when missing, so
/// administrators' changes survive upgrades; locked roles get back any seeded permission; Phase 1 administrators are migrated.
/// </summary>
internal sealed class AccessSeeder : IDatabaseSeeder
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AccessDbContext>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var clock = services.GetRequiredService<IClock>();
        var sources = services.GetServices<IAccessCatalogueSource>().ToList();

        await SyncSystemsAsync(db, configuration, sources, cancellationToken).ConfigureAwait(false);
        var added = await SyncPermissionsAsync(db, sources, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var newRoles = await SeedRolesAsync(db, sources, added, clock.Today, cancellationToken).ConfigureAwait(false);
        await SeedRecordRolesAsync(db, sources, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await MigrateAdministratorsAsync(db, clock.Today, cancellationToken).ConfigureAwait(false);
        await ExtendAdministratorsAsync(db, newRoles, clock.Today, cancellationToken).ConfigureAwait(false);
        services.GetRequiredService<AccessModelCache>().Invalidate();
    }

    private static async Task SyncSystemsAsync(AccessDbContext db, IConfiguration configuration, List<IAccessCatalogueSource> sources, CancellationToken ct)
    {
        var existing = await db.Systems.ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        foreach (var definition in sources.Select(s => s.System).DistinctBy(s => s.Code))
        {
            if (existing.TryGetValue(definition.Code, out var system))
            {
                system.SyncDefinition(definition);
            }
            else
            {
                // A module's first state comes from configuration (Modules:<Name>:Enabled); afterwards only Settings changes it.
                var enabled = configuration.GetValue($"Modules:{definition.Code}:Enabled", defaultValue: false);
                db.Systems.Add(AccessSystem.Create(definition, enabled));
            }
        }
    }

    /// <returns>Codes of permissions that did not exist before (new in this version).</returns>
    private static async Task<HashSet<string>> SyncPermissionsAsync(AccessDbContext db, List<IAccessCatalogueSource> sources, CancellationToken ct)
    {
        var declared = new Dictionary<string, (string System, PermissionDefinition Definition, int Sort)>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var sort = 0;
            foreach (var permission in MemberCatalogue.PermissionsOf(source))
            {
                var parts = permission.Code.Split('.');
                if (parts.Length != 3 || parts[0] != source.System.Code)
                {
                    throw new InvalidOperationException($"Permission '{permission.Code}' must be '{source.System.Code}.<object>.<action>'.");
                }

                declared[permission.Code] = (source.System.Code, permission, sort += 10);
            }
        }

        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, (system, definition, sort)) in declared)
        {
            if (!existing.TryGetValue(code, out var permission))
            {
                permission = new Permission { Code = code };
                db.Permissions.Add(permission);
                added.Add(code);
            }

            var parts = code.Split('.');
            permission.SystemCode = system;
            permission.ObjectCode = parts[1];
            permission.Action = parts[2];
            permission.LabelAr = definition.LabelAr;
            permission.LabelEn = definition.LabelEn;
            permission.Scopable = definition.Scopable;
            permission.Sort = sort;
        }

        // Permissions removed from code disappear with their role links (cascade); they can no longer be checked anyway.
        db.Permissions.RemoveRange(existing.Values.Where(p => !declared.ContainsKey(p.Code)));
        return added;
    }

    /// <summary>
    /// Inserts missing default roles. A permission that is new in this version joins the existing default roles that declare it
    /// (administrators' earlier edits stay; only new permissions are added once).
    /// </summary>
    /// <returns>The roles created now.</returns>
    private static async Task<List<Role>> SeedRolesAsync(
        AccessDbContext db, List<IAccessCatalogueSource> sources, HashSet<string> newPermissions, DateOnly today, CancellationToken ct)
    {
        var roles = await db.Roles.Include(r => r.Permissions).ToListAsync(ct).ConfigureAwait(false);
        var created = new List<Role>();
        foreach (var source in sources)
        {
            foreach (var seed in MemberCatalogue.RolesOf(source))
            {
                var role = roles.Find(r => r.SystemCode == source.System.Code && r.Code == seed.Code);
                if (role is null)
                {
                    role = Role.Create(source.System.Code, seed.Code, seed.NameAr, seed.NameEn, null, null, seed.RequiresMfa, builtin: true, locked: seed.Locked);
                    role.SetPermissions(seed.Permissions.Select(p => (p.Permission, p.Scope.ToCode())).ToList());
                    db.Roles.Add(role);
                    roles.Add(role);
                    created.Add(role);
                    if (seed.AssignToEveryone)
                    {
                        db.Assignments.Add(RoleAssignment.Create(role.Id, PrincipalKinds.Everyone, null, today, null, null));
                    }
                }
                else
                {
                    foreach (var grant in seed.Permissions.Where(g => role.Locked || (role.Builtin && newPermissions.Contains(g.Permission))))
                    {
                        role.EnsurePermission(grant.Permission, grant.Scope.ToCode());
                    }
                }
            }
        }

        return created;
    }

    /// <summary>A platform administrator role created by this version (e.g. Audit administrator) goes to the current administrators.</summary>
    private static async Task ExtendAdministratorsAsync(AccessDbContext db, List<Role> created, DateOnly today, CancellationToken ct)
    {
        var newAdminRoles = created.Where(r => AdministratorRoles.All.Contains((r.SystemCode, r.Code))).ToList();
        var accessAdmin = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.SystemCode == "access" && r.Code == "administrator", ct).ConfigureAwait(false);
        if (newAdminRoles.Count == 0 || accessAdmin is null || newAdminRoles.Any(r => r.Id == accessAdmin.Id))
        {
            return;
        }

        var holders = await db.Assignments.AsNoTracking()
            .Where(a => a.RoleId == accessAdmin.Id && a.PrincipalKind == PrincipalKinds.User && (a.ValidTo == null || a.ValidTo >= today))
            .Select(a => a.PrincipalId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        foreach (var role in newAdminRoles)
        {
            foreach (var userId in holders)
            {
                db.Assignments.Add(RoleAssignment.Create(role.Id, PrincipalKinds.User, userId, today, null, null));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedRecordRolesAsync(AccessDbContext db, List<IAccessCatalogueSource> sources, CancellationToken ct)
    {
        var types = await db.RecordRoleTypes.Include(t => t.Permissions).ToListAsync(ct).ConfigureAwait(false);
        foreach (var source in sources)
        {
            foreach (var definition in source.RecordRoles)
            {
                var type = types.Find(t => t.ResourceType == definition.ResourceType && t.RoleCode == definition.RoleCode);
                if (type is null)
                {
                    db.RecordRoleTypes.Add(RecordRoleType.Create(source.System.Code, definition));
                }
                else
                {
                    type.SyncNames(definition);
                }
            }
        }
    }

    /// <summary>ADR 0016: users who had the Phase 1 flag get the platform administrator roles.</summary>
    private static async Task MigrateAdministratorsAsync(AccessDbContext db, DateOnly today, CancellationToken ct)
    {
        var pending = await db.PendingAdministratorGrants.ToListAsync(ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var grant in pending)
        {
            await AdministratorRoles.GrantAsync(db, grant.UserId, today, ct).ConfigureAwait(false);
        }

        db.PendingAdministratorGrants.RemoveRange(pending);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// The synthesised delegated-administration permission (ADR 0167): every catalogue with member roles gets
/// <c>&lt;system&gt;.members.manage</c>, granted to its seeded administrator roles (new permissions join builtin roles once).
/// </summary>
internal static class MemberCatalogue
{
    public const string LabelAr = "إدارة أعضاء النظام";
    public const string LabelEn = "Manage the system's members";

    public static IEnumerable<PermissionDefinition> PermissionsOf(IAccessCatalogueSource source) =>
        source.MemberRoles is null
            ? source.Permissions
            : source.Permissions.Append(new PermissionDefinition(SystemMemberRoles.ManagePermission(source.System.Code), LabelAr, LabelEn, Scopable: false));

    public static IEnumerable<RoleSeed> RolesOf(IAccessCatalogueSource source)
    {
        if (source.MemberRoles is not { } members)
        {
            return source.DefaultRoles;
        }

        var manage = new PermissionGrant(SystemMemberRoles.ManagePermission(source.System.Code));
        return source.DefaultRoles.Select(r => members.AdministratorRoles.Contains(r.Code, StringComparer.Ordinal)
            ? r with { Permissions = [.. r.Permissions, manage] }
            : r);
    }
}

/// <summary>The seeded platform administrator roles (ADR 0016).</summary>
internal static class AdministratorRoles
{
    public static bool Contains(string system, string code) => All.Contains((system, code));

    public static readonly IReadOnlyList<(string System, string Code)> All =
    [
        ("org", "administrator"), ("identity", "administrator"), ("access", "administrator"), ("settings", "administrator"),
        ("audit", "administrator"), ("workflow", "administrator"),
        ("calendar", "administrator"), ("ai", "administrator"), ("mcp", "administrator"), ("tasks", "administrator"), ("teams", "administrator"),
        ("platform", "administrator"),
    ];

    public static async Task GrantAsync(AccessDbContext db, Guid userId, DateOnly today, CancellationToken ct)
    {
        foreach (var (system, code) in All)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.SystemCode == system && r.Code == code, ct).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }

            var held = await db.Assignments.AnyAsync(
                a => a.RoleId == role.Id && a.PrincipalKind == PrincipalKinds.User && a.PrincipalId == userId && (a.ValidTo == null || a.ValidTo >= today), ct)
                .ConfigureAwait(false);
            if (!held)
            {
                db.Assignments.Add(RoleAssignment.Create(role.Id, PrincipalKinds.User, userId, today, null, null));
            }
        }
    }
}
