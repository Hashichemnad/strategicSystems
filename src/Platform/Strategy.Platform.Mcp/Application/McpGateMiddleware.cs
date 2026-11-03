using Microsoft.AspNetCore.Http;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Mcp.OAuth;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Mcp.Application;

/// <summary>
/// In front of <c>/mcp</c> (docs/design/mcp.md §1): server off → 404; no bearer token → 401 with the protected-resource metadata (so MCP
/// clients start OAuth); vendor principals → 403. A browser session is not accepted on <c>/mcp</c>, only tokens. Token scopes are enforced
/// earlier by the token-scope rule (scope <c>mcp</c>).
/// </summary>
internal sealed class McpGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISettingsStore settings)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!(await settings.GetAsync<McpSettings>(context.RequestAborted).ConfigureAwait(false)).Enabled)
        {
            await new Error("mcp.disabled", "mcp.errors.disabled", ErrorKind.Disabled).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (!context.User.IsAuthenticated() || !context.User.IsToken())
        {
            context.Response.Headers.WWWAuthenticate =
                $"Bearer resource_metadata=\"{AuthorizationEndpoints.BaseUrl(context.Request)}/.well-known/oauth-protected-resource/mcp\", scope=\"{McpScopes.Mcp}\"";
            await new Error("auth.required", "errors.unauthorized", ErrorKind.Unauthorized).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (!context.User.IsInternal())
        {
            await new Error("mcp.vendorRefused", "errors.forbidden", ErrorKind.Forbidden).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
