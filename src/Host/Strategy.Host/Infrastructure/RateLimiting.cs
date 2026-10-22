using System.Globalization;
using System.Threading.RateLimiting;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Host.Infrastructure;

internal sealed class RateLimitingOptions
{
    public int PermitLimit { get; set; } = 300;

    public int WindowSeconds { get; set; } = 60;
}

internal static class RateLimiting
{
    /// <summary>Fixed window per client IP on <c>/api</c> and <c>/mcp</c>; static files and health checks are not limited.</summary>
    public static IServiceCollection AddStrategyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/mcp"))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                });
            });
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new Error("request.rateLimited", "errors.tooManyRequests", ErrorKind.Unexpected).ToProblemDetails();
                problem.Status = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem })
                    .ConfigureAwait(false);
            };
        });
        return services;
    }
}
