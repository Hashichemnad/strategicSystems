using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Integration.Clients;

/// <summary>
/// For integration clients (ADR 0191), after authentication and the token-scope rule: IP allow-list (403 <c>integration.ipRefused</c>),
/// rate limit per client per minute (429 <c>integration.rateLimited</c> with <c>Retry-After</c>), then one usage row per call. Refused
/// credentials of known clients (revoked, expired, ended credential) are logged too.
/// </summary>
internal sealed class IntegrationGateMiddleware(RequestDelegate next)
{
    public const string RowsKey = "integration.rows";

    public async Task InvokeAsync(HttpContext context, ClientRateLimiter limiter, IServiceScopeFactory scopes, IClock clock)
    {
        if (!context.User.IsIntegrationClient() || context.Items[IntegrationClientAuthority.CallContextKey] is not ClientCallContext call)
        {
            await next(context).ConfigureAwait(false);
            if (context.Items[IntegrationClientAuthority.RefusalKey] is ClientRefusal refusal)
            {
                await LogAsync(scopes, clock, context, refusal.Id, 0, refusal.Reason).ConfigureAwait(false);
            }

            return;
        }

        if (!IpAllowList.Allows(call.IpAllowList, context.Connection.RemoteIpAddress))
        {
            await new Error("integration.ipRefused", "integration.errors.ipRefused", ErrorKind.Forbidden).ToProblem().ExecuteAsync(context).ConfigureAwait(false);
            await LogAsync(scopes, clock, context, call.Id, 0, "ip").ConfigureAwait(false);
            return;
        }

        if (!limiter.TryAcquire(call.Id, call.RateLimitPerMinute, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var problem = new Error("integration.rateLimited", "integration.errors.rateLimited", ErrorKind.Forbidden).ToProblemDetails();
            problem.Status = StatusCodes.Status429TooManyRequests;
            await TypedResults.Problem(problem).ExecuteAsync(context).ConfigureAwait(false);
            await LogAsync(scopes, clock, context, call.Id, 0, "rate").ConfigureAwait(false);
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            await LogAsync(scopes, clock, context, call.Id, (int)watch.ElapsedMilliseconds, context.Response.StatusCode == 403 ? "scope" : null).ConfigureAwait(false);
        }
    }

    private static async Task LogAsync(IServiceScopeFactory scopes, IClock clock, HttpContext context, Guid client, int durationMs, string? refused)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var path = context.Request.Path.Value ?? string.Empty;
        db.Usage.Add(new ClientUsage
        {
            ClientRef = client,
            At = clock.UtcNow,
            Method = context.Request.Method,
            Path = path.Length > 500 ? path[..500] : path,
            StatusCode = context.Response.StatusCode,
            DurationMs = durationMs,
            Rows = context.Items[RowsKey] as int?,
            Ip = context.Connection.RemoteIpAddress?.ToString(),
            RefusedReason = refused,
        });
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
