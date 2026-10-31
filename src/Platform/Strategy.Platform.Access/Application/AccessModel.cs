using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Access.Application;

/// <summary>The access configuration (systems, permissions, roles, assignments, record-role types), held in memory.</summary>
internal sealed class AccessModel
{
    public required IReadOnlyDictionary<string, SystemInfo> Systems { get; init; }

    public required IReadOnlyDictionary<string, PermissionInfo> Permissions { get; init; }

    public required IReadOnlyDictionary<Guid, RoleInfo> Roles { get; init; }

    public required IReadOnlyList<AssignmentInfo> Assignments { get; init; }

    public required IReadOnlyList<GrantInfo> SystemGrants { get; init; }

    /// <summary>Permission → (resource type → record-role codes that grant it).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> RecordRoleCodes { get; init; }

    public long Generation { get; init; }

    public IReadOnlyDictionary<string, string[]> RecordRolesFor(string permission) =>
        RecordRoleCodes.TryGetValue(permission, out var byType) ? byType : EmptyRecordRoles;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyRecordRoles = new Dictionary<string, string[]>();
}

internal sealed record SystemInfo(string Code, bool Platform, bool Enabled, Guid? OwningUnitId);

internal sealed record PermissionInfo(string Code, string SystemCode, bool Scopable);

internal sealed record RoleInfo(Guid Id, string SystemCode, string Code, bool Active, bool RequiresMfa, IReadOnlyDictionary<string, DataScope> Permissions)
{
    public string Key => $"{SystemCode}.{Code}";
}

internal sealed record AssignmentInfo(Guid Id, Guid RoleId, string PrincipalKind, Guid? PrincipalId, DateOnly ValidFrom, DateOnly? ValidTo)
{
    public bool ActiveOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || ValidTo >= date);
}

internal sealed record GrantInfo(string SystemCode, string PrincipalKind, Guid? PrincipalId);

/// <summary>
/// Loads <see cref="AccessModel"/> and keeps it for at most 60 s; any access change calls <see cref="Invalidate"/> so the next
/// check reloads it (several app instances converge within the minute).
/// </summary>
internal sealed class AccessModelCache(IServiceScopeFactory scopeFactory, IClock clock) : IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _generation;
    private AccessModel? _model;
    private DateTimeOffset _loadedAt;

    public long Generation => Interlocked.Read(ref _generation);

    public void Dispose() => _gate.Dispose();

    public void Invalidate() => Interlocked.Increment(ref _generation);

    public async Task<AccessModel> GetAsync(CancellationToken cancellationToken)
    {
        var model = _model;
        if (model is not null && model.Generation == Generation && clock.UtcNow - _loadedAt < MaxAge)
        {
            return model;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_model is { } current && current.Generation == Generation && clock.UtcNow - _loadedAt < MaxAge)
            {
                return current;
            }

            var generation = Generation;
            if (_model is not null && clock.UtcNow - _loadedAt >= MaxAge && _model.Generation == generation)
            {
                Invalidate(); // time-based refresh also drops per-user snapshots
                generation = Generation;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
            _model = await LoadAsync(db, generation, clock.Today, cancellationToken).ConfigureAwait(false);
            _loadedAt = clock.UtcNow;
            return _model;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static async Task<AccessModel> LoadAsync(AccessDbContext db, long generation, DateOnly today, CancellationToken cancellationToken)
    {
        var systems = await db.Systems.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var permissions = await db.Permissions.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).ToListAsync(cancellationToken).ConfigureAwait(false);
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.ValidTo == null || a.ValidTo >= today.AddDays(-1))
            .Select(a => new AssignmentInfo(a.Id, a.RoleId, a.PrincipalKind, a.PrincipalId, a.ValidFrom, a.ValidTo))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var grants = await db.SystemAccess.AsNoTracking().Where(g => g.Active)
            .Select(g => new GrantInfo(g.SystemCode, g.PrincipalKind, g.PrincipalId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var recordTypes = await db.RecordRoleTypes.AsNoTracking().Include(t => t.Permissions).ToListAsync(cancellationToken).ConfigureAwait(false);

        var recordRoleCodes = recordTypes
            .SelectMany(t => t.Permissions.Select(p => (p.PermissionCode, t.ResourceType, t.RoleCode)))
            .GroupBy(x => x.PermissionCode, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string[]>)g.GroupBy(x => x.ResourceType, StringComparer.Ordinal)
                    .ToDictionary(t => t.Key, t => t.Select(x => x.RoleCode).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal),
                StringComparer.Ordinal);

        return new AccessModel
        {
            Generation = generation,
            Systems = systems.ToDictionary(s => s.Code, s => new SystemInfo(s.Code, s.IsPlatform, s.Enabled, s.OwningUnitId), StringComparer.Ordinal),
            Permissions = permissions.ToDictionary(p => p.Code, p => new PermissionInfo(p.Code, p.SystemCode, p.Scopable), StringComparer.Ordinal),
            Roles = roles.ToDictionary(
                r => r.Id,
                r => new RoleInfo(r.Id, r.SystemCode, r.Code, r.Active, r.RequiresMfa, r.Permissions.ToDictionary(
                    p => p.PermissionCode,
                    p => DataScopes.TryParse(p.Scope, out var scope) ? scope : DataScope.Own,
                    StringComparer.Ordinal))),
            Assignments = assignments,
            SystemGrants = grants,
            RecordRoleCodes = recordRoleCodes,
        };
    }
}
