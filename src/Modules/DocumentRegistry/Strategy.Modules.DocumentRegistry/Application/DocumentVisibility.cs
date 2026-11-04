using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>What a user is cleared for (computed once per request).</summary>
public sealed record Clearance(bool Manage, IReadOnlyList<string> AllCodes, IReadOnlyList<string> OwnerUnitCodes, IReadOnlyList<Guid> UnitPath, Guid? EmployeeId,
    bool CanRead);

/// <summary>
/// The one visibility rule of documents (ADR 0083): library, page, versions, compare, downloads, dashboard, search, Ask, MCP, AI and other systems
/// all read through <see cref="VisibleAsync"/>. Applied in the SQL query; outside it → 404.
/// </summary>
internal sealed class DocumentVisibility(
    DocRegDbContext db, IAccessService access, ISystemState systems, IRoleHolderQuery roles, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    private readonly Dictionary<Guid, Clearance> _cache = [];

    public async Task<Clearance> ClearanceAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var key = user.UserId() ?? Guid.Empty;
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var canRead = await systems.IsEnabledAsync(DocRegPermissions.SystemCode, ct).ConfigureAwait(false)
            && await access.CanAsync(user, DocRegPermissions.Read, cancellationToken: ct).ConfigureAwait(false);
        var manage = canRead && await access.CanAsync(user, DocRegPermissions.Manage, cancellationToken: ct).ConfigureAwait(false);
        var employee = user.EmployeeIdOf();
        var unit = employee is { } e ? (await employees.GetAsync(e, ct).ConfigureAwait(false))?.UnitId : null;
        var path = unit is { } u ? (await org.GetPathAsync(u, cancellationToken: ct).ConfigureAwait(false)).Select(p => p.Id).ToList() : [];
        var all = new List<string>();
        var ownerUnit = new List<string>();
        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        foreach (var rule in configuration.Sensitivities)
        {
            if (rule.AllEmployees || await HoldsAnyAsync(user, rule.RoleKeys, path, ct).ConfigureAwait(false))
            {
                all.Add(rule.Code);
            }
            else if (rule.OwnerUnit)
            {
                ownerUnit.Add(rule.Code);
            }
        }

        var clearance = new Clearance(manage, all, ownerUnit, path, employee, canRead);
        _cache[key] = clearance;
        return clearance;
    }

    /// <summary>Documents the user may see (any status for managers; active ones by clearance; their own at any status).</summary>
    public async Task<IQueryable<Document>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Documents : db.Documents.AsNoTracking();
        var c = await ClearanceAsync(user, ct).ConfigureAwait(false);
        if (!c.CanRead)
        {
            return source.Where(_ => false);
        }

        if (c.Manage)
        {
            return source;
        }

        var all = c.AllCodes.ToList();
        var ownerUnit = c.OwnerUnitCodes.ToList();
        var path = c.UnitPath.ToList();
        var me = c.EmployeeId;
        var versions = db.Versions.AsNoTracking();
        return source.Where(d =>
            ((d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired)
                && (d.ShareToAll || (d.Sensitivity != null && all.Contains(d.Sensitivity))
                    || (d.Sensitivity != null && ownerUnit.Contains(d.Sensitivity) && d.OwnerUnitId != null && path.Contains(d.OwnerUnitId.Value))))
            || (me != null && (d.OwnerEmployeeId == me || d.RequesterEmployeeId == me || versions.Any(v => v.DocumentId == d.Id && v.RequestedByEmployeeId == me))));
    }

    public async Task<Document?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);

    /// <summary>Versions of a visible document the user may see: published ones for everyone; in-review ones for the owner, requester and office.</summary>
    public async Task<IReadOnlyList<DocumentVersion>> VersionsAsync(Document document, ClaimsPrincipal user, CancellationToken ct)
    {
        var c = await ClearanceAsync(user, ct).ConfigureAwait(false);
        var list = await db.Versions.AsNoTracking().Where(v => v.DocumentId == document.Id).OrderByDescending(v => v.Major).ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var insider = c.Manage || (c.EmployeeId is { } me && (document.OwnerEmployeeId == me || document.RequesterEmployeeId == me));
        return list.Where(v => insider || v.Status is VersionStatuses.Current or VersionStatuses.Superseded
            || (c.EmployeeId is { } e && v.RequestedByEmployeeId == e)).ToList();
    }

    /// <summary>Owner, owner-unit member or office: may confirm validity, request a new version or withdrawal.</summary>
    public async Task<bool> IsOwnerSideAsync(Document document, ClaimsPrincipal user, CancellationToken ct)
    {
        var c = await ClearanceAsync(user, ct).ConfigureAwait(false);
        return c.Manage || (c.EmployeeId is { } me && document.OwnerEmployeeId == me)
            || (document.OwnerUnitId is { } unit && c.UnitPath.Count > 0 && c.UnitPath[^1] == unit);
    }

    private async Task<bool> HoldsAnyAsync(ClaimsPrincipal user, IReadOnlyList<string> keys, IReadOnlyList<Guid> path, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            var holders = await roles.GetHoldersAsync(key, ct).ConfigureAwait(false);
            if (holders.Exists && (holders.Everyone || (user.UserId() is { } id && holders.UserIds.Contains(id)) || path.Any(holders.UnitIds.Contains)))
            {
                return true;
            }
        }

        return false;
    }
}
