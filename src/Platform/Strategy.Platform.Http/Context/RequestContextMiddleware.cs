using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Kernel.Context;

namespace Strategy.Platform.Http.Context;

/// <summary>
/// Establishes the correlation id (<c>X-Correlation-Id</c>, echoed on the response) and the channel of the request,
/// and pushes both into the log scope and the current trace.
/// </summary>
public sealed partial class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string ClientHeader = "X-Client";

    public async Task InvokeAsync(HttpContext httpContext, RequestContext requestContext)
    {
        var correlationId = httpContext.Request.Headers[CorrelationHeader].ToString();
        if (!SafeCorrelationId().IsMatch(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString("N");
        }

        var channel = DetectChannel(httpContext.Request);
        requestContext.Initialise(
            correlationId,
            channel,
            httpContext.User,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());

        httpContext.Response.Headers[CorrelationHeader] = correlationId;
        Activity.Current?.SetTag("strategy.correlation_id", correlationId);
        Activity.Current?.SetTag("strategy.channel", channel);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["Channel"] = channel }))
        {
            await next(httpContext).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The channel labels the audit trail; it is not a security decision (the <c>X-Client</c> header is set by our SPA).
    /// Wasel and MCP are told apart by route; everything else on the API without the SPA header is <c>api</c>.
    /// </summary>
    public static string DetectChannel(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return Channels.Mcp;
        }

        if (request.Path.StartsWithSegments("/api/wasel", StringComparison.OrdinalIgnoreCase))
        {
            return Channels.Wasel;
        }

        return string.Equals(request.Headers[ClientHeader], "spa", StringComparison.OrdinalIgnoreCase)
            ? Channels.Ui
            : Channels.Api;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex SafeCorrelationId();
}
