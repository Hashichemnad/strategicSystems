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

    /// <summary>Marks an endpoint as intentionally public. Every API endpoint must be either public or permission-guarded.</summary>
    public static TBuilder AsPublic<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new PublicEndpoint());
}
