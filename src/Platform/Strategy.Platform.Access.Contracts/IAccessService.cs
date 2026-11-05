using System.Security.Claims;

namespace Strategy.Platform.Access;

/// <summary>
/// The single access entry point (CLAUDE.md §7). Every endpoint, job, API call and MCP tool goes through it.
/// Effective access = system enabled AND system access AND (system-role permission within its data scope OR a record-role
/// permission on the record). docs/design/access.md.
/// </summary>
public interface IAccessService
{
    /// <summary>
    /// Without <paramref name="resource"/>: may the principal perform <paramref name="permission"/> on at least some record
    /// (endpoint check, 403 when false). With a resource: on this record (handlers answer 404 when the record is outside the read
    /// scope, 403 when it is readable but the action is not allowed).
    /// </summary>
    Task<bool> CanAsync(
        ClaimsPrincipal principal,
        string permission,
        ResourceRef? resource = null,
        CancellationToken cancellationToken = default);

    /// <summary>Filters a list query to the records the principal may see for <paramref name="permission"/>, in the database.</summary>
    Task<IQueryable<T>> ApplyScopeAsync<T>(
        IQueryable<T> query,
        ClaimsPrincipal principal,
        string permission,
        ScopeMap<T> scope,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// The principal's effective scope for <paramref name="permission"/> (reporting read model, ADR 0141): whether it applies at all, to every
    /// record, to the records of a set of units, to own / team records, and on which resource types record-role grants add records.
    /// Same snapshot and rules as <see cref="ApplyScopeAsync{T}"/>.
    /// </summary>
    Task<EffectiveScope> GetScopeAsync(ClaimsPrincipal principal, string permission, CancellationToken cancellationToken = default);
}

/// <summary>
/// See <see cref="IAccessService.GetScopeAsync"/>. <c>UnitIds</c>: the expanded units of the Unit / UnitAndBelow grants.
/// <c>RecordGrantTypes</c>: resource types on which the principal holds active record roles that give the permission.
/// </summary>
public sealed record EffectiveScope(bool Allowed, bool All, IReadOnlySet<Guid> UnitIds, bool OwnOrTeam, IReadOnlyCollection<string> RecordGrantTypes)
{
    public static readonly EffectiveScope None = new(false, false, new HashSet<Guid>(), false, []);

    public static readonly EffectiveScope Everything = new(true, true, new HashSet<Guid>(), false, []);
}

/// <summary>
/// One record for a record-level check, e.g. <c>("sample.item", id)</c>, with the attributes data scopes test
/// (owner employee / user, unit). Build it with <see cref="ScopeMap{T}.RefOf"/>.
/// </summary>
public sealed record ResourceRef(string Type, Guid Id, Guid? OwnerEmployeeId = null, Guid? OwnerUserId = null, Guid? UnitId = null);
