using System.Security.Claims;

namespace Strategy.Platform.Access;

/// <summary>
/// Phase 0 placeholder (ADR 0005). Allows everything only in Development / Testing, where there is no login yet;
/// in any other environment it denies everything, so a misconfigured install fails closed.
/// Replaced by the real access service in Phase 2.
/// </summary>
public sealed class PlaceholderAccessService(bool allowAll) : IAccessService
{
    public bool AllowsAll => allowAll;

    public Task<bool> CanAsync(
        ClaimsPrincipal principal, string permission, ResourceRef? resource = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(allowAll);

    public IQueryable<T> ApplyScope<T>(IQueryable<T> query, ClaimsPrincipal principal, string permission)
        where T : class =>
        allowAll ? query : query.Where(_ => false);
}
