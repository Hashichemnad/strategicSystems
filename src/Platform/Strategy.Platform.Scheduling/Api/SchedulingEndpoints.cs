using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Scheduling.Application;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Scheduling.Api;

internal static class SchedulingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var jobs = group.MapGroup("/jobs");
        jobs.MapGet("/", async (JobAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListJobs").RequirePermission(SettingsPermissions.JobView);
        jobs.MapGet("/{key}/runs", async (string key, JobAdministration a, CancellationToken ct) => (await a.RunsAsync(key, ct)).ToOk())
            .WithName("ListJobRuns").RequirePermission(SettingsPermissions.JobView);
        jobs.MapPost("/{key}/run", async (string key, JobAdministration a, CancellationToken ct) => NoContent(await a.RunNowAsync(key, ct)))
            .WithName("RunJobNow").RequirePermission(SettingsPermissions.JobManage);
        jobs.MapPost("/{key}/pause", async (string key, JobAdministration a, CancellationToken ct) => NoContent(await a.SetPausedAsync(key, true, ct)))
            .WithName("PauseJob").RequirePermission(SettingsPermissions.JobManage);
        jobs.MapPost("/{key}/resume", async (string key, JobAdministration a, CancellationToken ct) => NoContent(await a.SetPausedAsync(key, false, ct)))
            .WithName("ResumeJob").RequirePermission(SettingsPermissions.JobManage);

        var outbox = group.MapGroup("/outbox");
        outbox.MapGet("/", async (JobAdministration a, CancellationToken ct) => TypedResults.Ok(await a.OutboxAsync(ct)))
            .WithName("GetOutboxStatus").RequirePermission(SettingsPermissions.JobView);
        outbox.MapPost("/{id:guid}/requeue", async (Guid id, JobAdministration a, CancellationToken ct) => NoContent(await a.RequeueAsync(id, ct)))
            .WithName("RequeueOutboxMessage").RequirePermission(SettingsPermissions.JobManage);
    }

    private static Results<NoContent, ProblemHttpResult> NoContent(Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
}
