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
