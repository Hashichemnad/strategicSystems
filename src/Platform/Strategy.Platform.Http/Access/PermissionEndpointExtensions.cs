using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Access;

/// <summary>Endpoint metadata: the permission an endpoint requires (e.g. <c>portfolio.project.submit</c>).</summary>
public sealed record RequiredPermission(string Permission);

/// <summary>Endpoint metadata: the endpoint is deliberately public (health, antiforgery token, login).</summary>
public sealed record PublicEndpoint;

/// <summary>
/// Endpoint metadata: any signed-in user may call it, because it only acts on the caller's own data
/// (profile, own tokens, own sessions). The handler checks ownership and answers 404 for anyone else's record.
/// </summary>
public sealed record SelfServiceEndpoint;

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Declares and enforces the endpoint's permission through <see cref="IAccessService"/> (403 when refused).
    /// Record-level checks (404 outside scope) happen in the handler after loading the record.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new RequiredPermission(permission));
        builder.AddEndpointFilter(async (context, next) =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<IAccessService>();
            var allowed = await access.CanAsync(context.HttpContext.User, permission, cancellationToken: context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            return allowed ? await next(context).ConfigureAwait(false) : Error.Forbidden().ToProblem();
        });
        return builder;
    }

    /// <summary>Marks an endpoint as self-service: signed-in callers only, ownership checked in the handler.</summary>
    public static TBuilder RequireSignedIn<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new SelfServiceEndpoint());

    /// <summary>Marks an endpoint as intentionally public. Every API endpoint must be public, self-service or permission-guarded.</summary>
    public static TBuilder AsPublic<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PublicEndpoint());
}
