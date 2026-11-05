using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Access.Application;

/// <summary>
/// The three-layer access model (SPEC §0.2, CLAUDE.md §7, docs/design/access.md, ADR 0015). Registered per scope: the
/// user's snapshot is resolved once per request (and cached for a minute across requests).
/// </summary>
internal sealed class AccessService(UserAccessLoader loader, AccessModelCache models, AccessDbContext db, IClock clock) : IAccessService
{
    /// <summary>Wasel vendor permissions (ADR 0111): only vendor principals hold them, only through record roles.</summary>
    public const string VendorPermissionPrefix = "wasel.";

    private readonly Dictionary<(Guid, Guid?), (AccessModel, UserAccess)> _perRequest = [];

    public async Task<bool> CanAsync(
        ClaimsPrincipal principal, string permission, ResourceRef? resource = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(permission);
        if (principal.IsSystem())
        {
            return true;
        }

        if (IsVendor(principal))
        {
            return await VendorCanAsync(principal, permission, resource, cancellationToken).ConfigureAwait(false);
        }

        if (!principal.IsInternal() || principal.UserId() is not { } userId || IsVendorPermission(permission))
        {
            return false;
        }

        var (model, access) = await SnapshotAsync(userId, principal.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        if (!SystemOpen(model, access, permission, out var info))
        {
            return false;
        }

        if (access.Permissions.TryGetValue(permission, out var scopes))
        {
            if (resource is null || !info.Scopable || scopes.Contains(DataScope.All) || scopes.Any(scope => InScope(scope, access, resource)))
            {
                return true;
            }
        }

        var recordRoles = model.RecordRolesFor(permission);
        if (recordRoles.Count == 0)
        {
            return false;
        }

        var grants = ActiveRecordRoles(access);
        if (resource is null)
        {
            // Endpoint check: some record role on some record could allow it. The handler checks the record itself.
            var types = recordRoles.Keys.ToList();
            var candidates = await grants.Where(g => types.Contains(g.ResourceType))
                .Select(g => new { g.ResourceType, g.RoleCode })
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return candidates.Any(c => recordRoles[c.ResourceType].Contains(c.RoleCode, StringComparer.Ordinal));
        }

        if (!recordRoles.TryGetValue(resource.Type, out var codes))
        {
            return false;
        }

        return await grants.AnyAsync(g => g.ResourceType == resource.Type && g.ResourceId == resource.Id && codes.Contains(g.RoleCode), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IQueryable<T>> ApplyScopeAsync<T>(
        IQueryable<T> query, ClaimsPrincipal principal, string permission, ScopeMap<T> scope, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scope);
        if (principal.IsSystem())
        {
            return query;
        }

        if (IsVendor(principal))
        {
            var vendorIds = await VendorRecordIdsAsync(principal, permission, scope.ResourceType, cancellationToken).ConfigureAwait(false);
            var vendorBuilder = new ScopePredicateBuilder<T>(scope);
            vendorBuilder.AddIds(vendorIds);
            return vendorBuilder.Build() is { } vendorPredicate ? query.Where(vendorPredicate) : query.Where(_ => false);
        }

        if (!principal.IsInternal() || principal.UserId() is not { } userId || IsVendorPermission(permission))
        {
            return query.Where(_ => false);
        }

        var (model, access) = await SnapshotAsync(userId, principal.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        if (!SystemOpen(model, access, permission, out var info))
        {
            return query.Where(_ => false);
        }

        var scopes = access.Permissions.GetValueOrDefault(permission);
        if (scopes is not null && (!info.Scopable || scopes.Contains(DataScope.All)))
        {
            return query;
        }

        var builder = new ScopePredicateBuilder<T>(scope);
        foreach (var granted in scopes ?? new HashSet<DataScope>())
        {
            builder.Add(granted, access);
        }

        if (model.RecordRolesFor(permission).TryGetValue(scope.ResourceType, out var codes))
        {
            var ids = await ActiveRecordRoles(access)
                .Where(g => g.ResourceType == scope.ResourceType && codes.Contains(g.RoleCode))
                .Select(g => g.ResourceId)
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            builder.AddIds(ids);
        }

        return builder.Build() is { } predicate ? query.Where(predicate) : query.Where(_ => false);
    }

    private static bool IsVendor(ClaimsPrincipal principal) =>
        principal.IsAuthenticated() && principal.FindFirst(StrategyClaims.PrincipalType)?.Value == StrategyClaims.VendorPrincipal;

    private static bool IsVendorPermission(string permission) => permission.StartsWith(VendorPermissionPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Vendor principals (ADR 0111): only <c>wasel.*</c> permissions of the catalogue, only while the Wasel system is on, only through record roles granted
    /// to the vendor's user id that are valid now. No system roles, no data scopes.
    /// </summary>
    private async Task<bool> VendorCanAsync(ClaimsPrincipal principal, string permission, ResourceRef? resource, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId || !IsVendorPermission(permission))
        {
            return false;
        }

        var model = await models.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!VendorSystemOpen(model, permission))
        {
            return false;
        }

        var recordRoles = model.RecordRolesFor(permission);
        if (recordRoles.Count == 0)
        {
            return false;
        }

        var grants = VendorGrants(userId);
        if (resource is null)
        {
            var types = recordRoles.Keys.ToList();
            var candidates = await grants.Where(g => types.Contains(g.ResourceType)).Select(g => new { g.ResourceType, g.RoleCode }).Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return candidates.Any(c => recordRoles[c.ResourceType].Contains(c.RoleCode, StringComparer.Ordinal));
        }

        return recordRoles.TryGetValue(resource.Type, out var codes)
            && await grants.AnyAsync(g => g.ResourceType == resource.Type && g.ResourceId == resource.Id && codes.Contains(g.RoleCode), cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<Guid>> VendorRecordIdsAsync(
        ClaimsPrincipal principal, string permission, string resourceType, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId || !IsVendorPermission(permission))
        {
            return [];
        }

        var model = await models.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!VendorSystemOpen(model, permission) || !model.RecordRolesFor(permission).TryGetValue(resourceType, out var codes))
        {
            return [];
        }

        return await VendorGrants(userId).Where(g => g.ResourceType == resourceType && codes.Contains(g.RoleCode)).Select(g => g.ResourceId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool VendorSystemOpen(AccessModel model, string permission) =>
        model.Permissions.TryGetValue(permission, out var info) && model.Systems.TryGetValue(info.SystemCode, out var system) && system.Enabled;

    private IQueryable<Domain.RecordRoleGrant> VendorGrants(Guid userId)
    {
        var now = clock.UtcNow;
        return db.RecordRoles.AsNoTracking().Where(g =>
            g.ValidFrom <= now && (g.ValidTo == null || g.ValidTo > now) && g.PrincipalKind == RecordPrincipalKinds.User && g.PrincipalId == userId);
    }

    /// <summary>The snapshot for a user (also used by the access API and the MFA policy).</summary>
    public async Task<(AccessModel Model, UserAccess Access)> SnapshotAsync(Guid userId, Guid? employeeId, CancellationToken cancellationToken)
    {
        if (_perRequest.TryGetValue((userId, employeeId), out var known))
        {
            return known;
        }

        var loaded = await loader.LoadAsync(userId, employeeId, cancellationToken).ConfigureAwait(false);
        _perRequest[(userId, employeeId)] = loaded;
        return loaded;
    }

    internal static bool SystemOpen(AccessModel model, UserAccess access, string permission, out PermissionInfo info)
    {
        // Unknown permissions are refused: every checked permission must be in the catalogue (fail closed).
        if (!model.Permissions.TryGetValue(permission, out info!))
        {
            return false;
        }

        return model.Systems.TryGetValue(info.SystemCode, out var system) && system.Enabled && access.Systems.Contains(info.SystemCode);
    }

    internal static bool InScope(DataScope scope, UserAccess access, ResourceRef resource) => scope switch
    {
        DataScope.All => true,
        DataScope.Own => IsOwner(resource, [access.EmployeeId ?? Guid.Empty], [access.UserId]),
        DataScope.Team => IsOwner(resource, access.TeamEmployeeIds, access.TeamUserIds),
        DataScope.Unit => resource.UnitId is { } unit && access.ScopeUnitIds.Contains(unit),
        DataScope.UnitAndBelow => resource.UnitId is { } unit && access.SubtreeUnitIds.Contains(unit),
        _ => false,
    };

    private static bool IsOwner(ResourceRef resource, IReadOnlyCollection<Guid> employees, IReadOnlyCollection<Guid> users) =>
        (resource.OwnerEmployeeId is { } employee && employee != Guid.Empty && employees.Contains(employee))
        || (resource.OwnerUserId is { } user && users.Contains(user));

    private IQueryable<Domain.RecordRoleGrant> ActiveRecordRoles(UserAccess access)
    {
        var now = clock.UtcNow;
        var employeeId = access.EmployeeId;
        var userId = access.UserId;
        var units = access.RecordUnitIds;
        return db.RecordRoles.AsNoTracking().Where(g =>
            g.ValidFrom <= now && (g.ValidTo == null || g.ValidTo > now)
            && ((g.PrincipalKind == RecordPrincipalKinds.Employee && employeeId != null && g.PrincipalId == employeeId)
                || (g.PrincipalKind == RecordPrincipalKinds.User && g.PrincipalId == userId)
                || (g.PrincipalKind == RecordPrincipalKinds.Unit && units.Contains(g.PrincipalId))));
    }
}

/// <summary>Builds the OR of the granted scopes from a module's <see cref="ScopeMap{T}"/>, as an EF-translatable predicate.</summary>
internal sealed class ScopePredicateBuilder<T>(ScopeMap<T> map)
    where T : class
{
    private readonly ParameterExpression _parameter = Expression.Parameter(typeof(T), "x");
    private readonly List<Expression> _parts = [];

    public void Add(DataScope scope, UserAccess access)
    {
        switch (scope)
        {
            case DataScope.Own:
                AddOwner(access.EmployeeId is { } employee ? [employee] : [], [access.UserId]);
                break;
            case DataScope.Team:
                AddOwner(access.TeamEmployeeIds, access.TeamUserIds);
                break;
            case DataScope.Unit:
                AddIn(map.Unit, access.ScopeUnitIds);
                break;
            case DataScope.UnitAndBelow:
                AddIn(map.Unit, access.SubtreeUnitIds);
                break;
        }
    }

    public void AddIds(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var body = Rebind(map.Id);
        var values = Parameterised(ids.ToList());
        _parts.Add(Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], values, body));
    }

    public Expression<Func<T, bool>>? Build() =>
        _parts.Count == 0 ? null : Expression.Lambda<Func<T, bool>>(_parts.Aggregate(Expression.OrElse), _parameter);

    private void AddOwner(IReadOnlyCollection<Guid> employees, IReadOnlyCollection<Guid> users)
    {
        AddIn(map.OwnerEmployee, employees);
        AddIn(map.OwnerUser, users);
    }

    private void AddIn(Expression<Func<T, Guid?>>? selector, IReadOnlyCollection<Guid> ids)
    {
        if (selector is null || ids.Count == 0)
        {
            return;
        }

        var body = Rebind(selector);
        var values = Parameterised(ids.Select(id => (Guid?)id).ToList());
        _parts.Add(Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid?)], values, body));
    }

    private Expression Rebind(LambdaExpression selector) =>
        new ParameterReplacer(selector.Parameters[0], _parameter).Visit(selector.Body);

    /// <summary>Member access on a closure-like holder, so EF Core sends the list as a query parameter.</summary>
    private static MemberExpression Parameterised<TValue>(List<TValue> values) =>
        Expression.Property(Expression.Constant(new ValueHolder<List<TValue>>(values)), nameof(ValueHolder<List<TValue>>.Value));

    private sealed class ValueHolder<TValue>(TValue value)
    {
        public TValue Value { get; } = value;
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
