using Microsoft.Extensions.Caching.Memory;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Access.Application;

/// <summary>What one user holds right now: accessible systems, permissions with their scopes, and the org facts scopes need.</summary>
internal sealed class UserAccess
{
    public required Guid UserId { get; init; }

    public required Guid? EmployeeId { get; init; }

    /// <summary>The unit the employee is placed in.</summary>
    public required Guid? UnitId { get; init; }

    /// <summary>The "unit" scope: the placement unit and every active unit the employee heads (ADR 0015).</summary>
    public required IReadOnlyList<Guid> ScopeUnitIds { get; init; }

    /// <summary>Enabled systems the user may open (platform systems always, modules through a system-access grant).</summary>
    public required IReadOnlySet<string> Systems { get; init; }

    /// <summary>Permission → scopes granted by the user's active roles (only roles of enabled systems).</summary>
    public required IReadOnlyDictionary<string, IReadOnlySet<DataScope>> Permissions { get; init; }

    public required IReadOnlyList<RoleInfo> Roles { get; init; }

    /// <summary>The "unit and below" scope: every <see cref="ScopeUnitIds"/> unit and every unit below them.</summary>
    public required IReadOnlyList<Guid> SubtreeUnitIds { get; init; }

    /// <summary>The user's employee and their direct reports.</summary>
    public required IReadOnlyList<Guid> TeamEmployeeIds { get; init; }

    /// <summary>The user and the users of their direct reports.</summary>
    public required IReadOnlyList<Guid> TeamUserIds { get; init; }

    public bool RequiresMfa => Roles.Any(r => r.RequiresMfa);
}

/// <summary>Builds <see cref="UserAccess"/> from the model and the organisation; cached per user and model generation.</summary>
internal sealed class UserAccessLoader(
    AccessModelCache models,
    IOrgDirectory org,
    IEmployeeDirectory employees,
    IUserDirectory users,
    IMemoryCache cache,
    IClock clock)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public async Task<(AccessModel Model, UserAccess Access)> LoadAsync(Guid userId, Guid? employeeId, CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(cancellationToken).ConfigureAwait(false);
        var key = (nameof(UserAccess), userId, employeeId, model.Generation);
        if (cache.TryGetValue(key, out UserAccess? cached) && cached is not null)
        {
            return (model, cached);
        }

        var access = await BuildAsync(model, userId, employeeId, cancellationToken).ConfigureAwait(false);
        cache.Set(key, access, CacheFor);
        return (model, access);
    }

    private async Task<UserAccess> BuildAsync(AccessModel model, Guid userId, Guid? employeeId, CancellationToken cancellationToken)
    {
        Guid? unitId = null;
        IReadOnlyList<Guid> ancestors = [];
        IReadOnlyList<Guid> scopeUnits = [];
        var subtree = new List<Guid>();
        var teamEmployees = new List<Guid>();
        var teamUsers = new List<Guid> { userId };
        if (employeeId is { } employee && await employees.GetAsync(employee, cancellationToken).ConfigureAwait(false) is { } reference
            && reference.Status != "left")
        {
            unitId = reference.UnitId;
            ancestors = (await org.GetPathAsync(reference.UnitId, cancellationToken: cancellationToken).ConfigureAwait(false)).Select(u => u.Id).ToList();
            var headed = await org.GetUnitsHeadedByAsync(employee, cancellationToken).ConfigureAwait(false);
            scopeUnits = [.. new[] { reference.UnitId }.Concat(headed).Distinct()];
            foreach (var unit in scopeUnits)
            {
                subtree.AddRange(await org.GetSelfAndDescendantIdsAsync(unit, cancellationToken).ConfigureAwait(false));
            }
            var reports = await employees.GetDirectReportIdsAsync(employee, cancellationToken).ConfigureAwait(false);
            teamEmployees.Add(employee);
            teamEmployees.AddRange(reports);
            if (reports.Count > 0)
            {
                teamUsers.AddRange((await users.GetByEmployeesAsync(reports, cancellationToken).ConfigureAwait(false)).Select(u => u.Id));
            }
        }

        bool Matches(string kind, Guid? principalId) => kind switch
        {
            PrincipalKinds.Everyone => true,
            PrincipalKinds.User => principalId == userId,
            PrincipalKinds.Unit => principalId is { } unit && ancestors.Contains(unit),
            _ => false,
        };

        var today = clock.Today;
        var enabledSystems = model.Systems.Values.Where(s => s.Enabled).ToDictionary(s => s.Code, StringComparer.Ordinal);
        var systems = new HashSet<string>(enabledSystems.Values.Where(s => s.Platform).Select(s => s.Code), StringComparer.Ordinal);
        foreach (var grant in model.SystemGrants.Where(g => enabledSystems.ContainsKey(g.SystemCode) && Matches(g.PrincipalKind, g.PrincipalId)))
        {
            systems.Add(grant.SystemCode);
        }

        var roles = model.Assignments
            .Where(a => a.ActiveOn(today) && Matches(a.PrincipalKind, a.PrincipalId))
            .Select(a => model.Roles.GetValueOrDefault(a.RoleId))
            .OfType<RoleInfo>()
            .Where(r => r.Active && enabledSystems.ContainsKey(r.SystemCode))
            .DistinctBy(r => r.Id)
            .ToList();

        var permissions = new Dictionary<string, HashSet<DataScope>>(StringComparer.Ordinal);
        foreach (var (permission, scope) in roles.SelectMany(r => r.Permissions.Select(p => (p.Key, p.Value))))
        {
            if (!permissions.TryGetValue(permission, out var scopes))
            {
                permissions[permission] = scopes = [];
            }

            scopes.Add(scope);
        }

        return new UserAccess
        {
            UserId = userId,
            EmployeeId = employeeId,
            UnitId = unitId,
            Systems = systems,
            Permissions = permissions.ToDictionary(p => p.Key, p => (IReadOnlySet<DataScope>)p.Value, StringComparer.Ordinal),
            Roles = roles,
            ScopeUnitIds = scopeUnits,
            SubtreeUnitIds = [.. subtree.Distinct()],
            TeamEmployeeIds = teamEmployees,
            TeamUserIds = teamUsers.Distinct().ToList(),
        };
    }
}
