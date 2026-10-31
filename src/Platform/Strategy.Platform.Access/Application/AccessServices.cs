using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Access.Application;

internal sealed class SystemState(AccessModelCache models) : ISystemState
{
    public async Task<bool> IsEnabledAsync(string systemCode, CancellationToken cancellationToken = default)
    {
        var model = await models.GetAsync(cancellationToken).ConfigureAwait(false);
        return model.Systems.TryGetValue(systemCode, out var system) && system.Enabled;
    }
}

internal sealed class RecordRoleService(AccessDbContext db, AccessModelCache models, IClock clock) : IRecordRoleService
{
    public async Task GrantAsync(
        string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode,
        DateTimeOffset? validFrom = null, DateTimeOffset? validTo = null, CancellationToken cancellationToken = default)
    {
        if (principalKind is not (RecordPrincipalKinds.Employee or RecordPrincipalKinds.User))
        {
            throw new ArgumentException($"Unknown principal kind '{principalKind}'.", nameof(principalKind));
        }

        if (!await db.RecordRoleTypes.AnyAsync(t => t.ResourceType == resourceType && t.RoleCode == roleCode, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Record role '{resourceType}/{roleCode}' is not declared in an access catalogue.");
        }

        var now = clock.UtcNow;
        var active = await db.RecordRoles.AnyAsync(
            g => g.ResourceType == resourceType && g.ResourceId == resourceId && g.PrincipalKind == principalKind
                && g.PrincipalId == principalId && g.RoleCode == roleCode && (g.ValidTo == null || g.ValidTo > now),
            cancellationToken).ConfigureAwait(false);
        if (!active)
        {
            db.RecordRoles.Add(RecordRoleGrant.Create(resourceType, resourceId, principalKind, principalId, roleCode, validFrom ?? now, validTo));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RevokeAsync(
        string resourceType, Guid resourceId, string principalKind, Guid principalId, string roleCode, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var grants = await db.RecordRoles
            .Where(g => g.ResourceType == resourceType && g.ResourceId == resourceId && g.PrincipalKind == principalKind
                && g.PrincipalId == principalId && g.RoleCode == roleCode && (g.ValidTo == null || g.ValidTo > now))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var grant in grants)
        {
            grant.End(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        models.Invalidate();
    }

    public async Task<IReadOnlyList<RecordRoleHolder>> GetHoldersAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return await db.RecordRoles.AsNoTracking()
            .Where(g => g.ResourceType == resourceType && g.ResourceId == resourceId && (g.ValidTo == null || g.ValidTo > now))
            .OrderBy(g => g.RoleCode).ThenBy(g => g.ValidFrom)
            .Select(g => new RecordRoleHolder(g.RoleCode, g.PrincipalKind, g.PrincipalId, g.ValidFrom, g.ValidTo))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class UserRoleQuery(
    UserAccessLoader loader, AccessDbContext db, AccessModelCache models, IUserDirectory users, IClock clock) : IUserRoleQuery
{
    public async Task<bool> RequiresMfaAsync(Guid userId, Guid? employeeId, CancellationToken cancellationToken = default) =>
        (await loader.LoadAsync(userId, employeeId, cancellationToken).ConfigureAwait(false)).Access.RequiresMfa;

    public async Task<bool> IsLastAccessAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var holders = await AccessAdministratorHoldersAsync(cancellationToken).ConfigureAwait(false);
        return holders is not null && holders.Contains(userId) && holders.Count == 1;
    }

    /// <summary>Active users directly holding the access administrator role; null when a unit / everyone assignment also grants it.</summary>
    internal async Task<IReadOnlyList<Guid>?> AccessAdministratorHoldersAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.SystemCode == "access" && r.Code == "administrator", cancellationToken)
            .ConfigureAwait(false);
        if (role is null)
        {
            return null;
        }

        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.RoleId == role.Id && a.ValidFrom <= today && (a.ValidTo == null || a.ValidTo >= today))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (assignments.Any(a => a.PrincipalKind != PrincipalKinds.User))
        {
            return null;
        }

        var ids = assignments.Select(a => a.PrincipalId!.Value).Distinct().ToList();
        var found = await users.GetManyAsync(ids, cancellationToken).ConfigureAwait(false);
        return ids.Where(id => found.TryGetValue(id, out var user) && user.Active).ToList();
    }

    public async Task GrantPlatformAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await AdministratorRoles.GrantAsync(db, userId, clock.Today, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        models.Invalidate();
    }
}
