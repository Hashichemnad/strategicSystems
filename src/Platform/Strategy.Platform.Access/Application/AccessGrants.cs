using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Access.Application;

/// <summary>
/// Programmatic grants for install-time and development seeding (and tests). Administrators use the access screens;
/// this never runs on behalf of an HTTP caller.
/// </summary>
public static class AccessGrants
{
    public const string User = PrincipalKinds.User;
    public const string Unit = PrincipalKinds.Unit;
    public const string Everyone = PrincipalKinds.Everyone;

    /// <summary>Assigns the role <c>&lt;system&gt;.&lt;code&gt;</c> (idempotent). Returns false when the role does not exist.</summary>
    public static async Task<bool> AssignRoleAsync(IServiceProvider services, string roleKey, string principalKind, Guid? principalId, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AccessDbContext>();
        var today = services.GetRequiredService<IClock>().Today;
        var separator = roleKey.IndexOf('.', StringComparison.Ordinal);
        var system = roleKey[..separator];
        var code = roleKey[(separator + 1)..];
        var role = await db.Roles.FirstOrDefaultAsync(r => r.SystemCode == system && r.Code == code, ct).ConfigureAwait(false);
        if (role is null)
        {
            return false;
        }

        var id = principalKind == PrincipalKinds.Everyone ? null : principalId;
        if (!await db.Assignments.AnyAsync(a => a.RoleId == role.Id && a.PrincipalKind == principalKind && a.PrincipalId == id && (a.ValidTo == null || a.ValidTo >= today), ct)
            .ConfigureAwait(false))
        {
            db.Assignments.Add(RoleAssignment.Create(role.Id, principalKind, id, today, null, null));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        services.GetRequiredService<AccessModelCache>().Invalidate();
        return true;
    }

    /// <summary>Gives the principal access to a module (idempotent).</summary>
    public static async Task GrantSystemAccessAsync(IServiceProvider services, string systemCode, string principalKind, Guid? principalId, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AccessDbContext>();
        var id = principalKind == PrincipalKinds.Everyone ? null : principalId;
        if (!await db.SystemAccess.AnyAsync(g => g.Active && g.SystemCode == systemCode && g.PrincipalKind == principalKind && g.PrincipalId == id, ct)
            .ConfigureAwait(false))
        {
            db.SystemAccess.Add(SystemAccessGrant.Create(systemCode, principalKind, id));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        services.GetRequiredService<AccessModelCache>().Invalidate();
    }
}
