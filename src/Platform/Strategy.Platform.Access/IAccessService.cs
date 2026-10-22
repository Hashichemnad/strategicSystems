using System.Security.Claims;

namespace Strategy.Platform.Access;

/// <summary>
/// The single access entry point (CLAUDE.md §7). Every endpoint, job, API and MCP tool goes through it.
/// Phase 0 ships a placeholder (ADR 0005); the three-layer model arrives in Phase 2 behind this same contract.
/// </summary>
public interface IAccessService
{
    /// <summary>May <paramref name="principal"/> perform <paramref name="permission"/>, optionally on one record?</summary>
    Task<bool> CanAsync(
        ClaimsPrincipal principal,
        string permission,
        ResourceRef? resource = null,
        CancellationToken cancellationToken = default);

    /// <summary>Filters a list query to the records the principal may see for <paramref name="permission"/>, in the database.</summary>
    IQueryable<T> ApplyScope<T>(IQueryable<T> query, ClaimsPrincipal principal, string permission)
        where T : class;
}

/// <summary>Identifies one record for a record-level access check, e.g. <c>("sample.item", id)</c>.</summary>
public sealed record ResourceRef(string Type, Guid Id);
