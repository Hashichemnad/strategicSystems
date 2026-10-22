using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Infrastructure;

internal static class HealthChecks
{
    /// <summary><c>/health/live</c>: the process answers. <c>/health/ready</c>: every module database is reachable.</summary>
    public static void MapStrategyHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        }).AsPublic();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(PersistenceServiceCollectionExtensions.ReadyTag),
            ResponseWriter = WriteAsync,
        }).AsPublic();
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), durationMs = e.Value.Duration.TotalMilliseconds }),
        });
    }
}
