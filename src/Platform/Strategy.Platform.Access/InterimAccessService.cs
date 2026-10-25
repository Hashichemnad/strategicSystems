using System.Security.Claims;

namespace Strategy.Platform.Access;

/// <summary>
/// Phase 1 access rules (ADR 0009) behind the final <see cref="IAccessService"/> contract. Phase 2 replaces it with
/// system access, system roles with data scope, and record roles.
/// <list type="bullet">
/// <item>Anonymous and vendor principals: refused. The in-process system principal (seeding, jobs): allowed.</item>
/// <item>Platform permissions (<c>org.*</c>, <c>identity.*</c>): <c>*.read</c> for any signed-in internal user,
/// everything else for system administrators only.</item>
/// <item>Any other system: signed-in internal users in Development / Testing only; refused elsewhere (fail closed).</item>
/// </list>
/// </summary>
public sealed class InterimAccessService(bool allowModulesForDevelopment) : IAccessService
{
    private static readonly string[] PlatformSystems = ["org.", "identity."];

    public bool AllowsModulesForDevelopment => allowModulesForDevelopment;

    public Task<bool> CanAsync(
        ClaimsPrincipal principal, string permission, ResourceRef? resource = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Can(principal, permission));

    public IQueryable<T> ApplyScope<T>(IQueryable<T> query, ClaimsPrincipal principal, string permission)
        where T : class =>
        Can(principal, permission) ? query : query.Where(_ => false);

    public bool Can(ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(permission);
        if (principal.IsSystem())
        {
            return true;
        }

        if (!principal.IsInternal())
        {
            return false;
        }

        if (PlatformSystems.Any(prefix => permission.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return permission.EndsWith(".read", StringComparison.Ordinal) || principal.IsSystemAdmin();
        }

        return allowModulesForDevelopment;
    }
}
