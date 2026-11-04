using Microsoft.AspNetCore.Http;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Http.Access;

/// <summary>
/// Runs after authentication. Hands the authenticated user to the request context (audit stamps, access checks) and
/// refuses anonymous calls to every <c>/api</c> endpoint that is not marked <see cref="PublicEndpoint"/>
/// (401 <c>auth.required</c>), before any endpoint filter or handler runs.
/// </summary>
public sealed class AuthenticationRequirementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RequestContext requestContext)
    {
        requestContext.SetUser(context.User);

        // Wasel isolation (ADR 0110): a vendor principal never acts outside /api/wasel and an internal one never inside it, whatever scheme
        // authenticated it. The scheme selection already makes this so; this is the second wall.
        if (context.User.IsAuthenticated() && !context.User.IsSystem())
        {
            var waselPath = context.Request.Path.StartsWithSegments("/api/wasel", StringComparison.OrdinalIgnoreCase);
            var vendor = context.User.FindFirst(StrategyClaims.PrincipalType)?.Value == StrategyClaims.VendorPrincipal;
            if (vendor != waselPath && (vendor || context.Request.Path.StartsWithSegments("/api")))
            {
                await new Error("auth.principalRefused", "errors.forbidden", ErrorKind.Forbidden).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }
        }

        var endpoint = context.GetEndpoint();
        if (endpoint is not null
            && context.Request.Path.StartsWithSegments("/api")
            && endpoint.Metadata.GetMetadata<PublicEndpoint>() is null
            && !context.User.IsAuthenticated())
        {
            await new Error("auth.required", "errors.unauthorized", ErrorKind.Unauthorized)
                .ToProblem()
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
