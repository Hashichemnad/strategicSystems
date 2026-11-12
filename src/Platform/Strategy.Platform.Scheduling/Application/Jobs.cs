using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling.Domain;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.Platform.Scheduling;

/// <summary>
/// A scheduled job declared by a component or module: stable key, AR/EN name, default cron (installation time zone) and job type.
/// </summary>
public sealed record JobDefinition(string Key, string NameAr, string NameEn, string Cron, Type JobType, string? DescriptionAr = null, string? DescriptionEn = null);

/// <summary>
/// Base of every scheduled job: runs with channel <c>job</c> and the system principal, and returns a short result for the run log.
/// Jobs must be idempotent (CLAUDE.md §14).
/// </summary>
public abstract class StrategyJob(RequestContext requestContext) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        requestContext.Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        context.Result = await RunAsync(context.CancellationToken).ConfigureAwait(false);
    }

    /// <returns>A short, non-sensitive summary shown on the job dashboard.</returns>
    protected abstract Task<string?> RunAsync(CancellationToken cancellationToken);
}

public static class SchedulingServiceCollectionExtensions
{
    public static IServiceCollection AddScheduledJob(this IServiceCollection services, JobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        services.AddScoped(definition.JobType);
        return services;
    }

    public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IOutboxHandler
    {
        services.AddScoped<IOutboxHandler, THandler>();
        return services;
    }
}

/// <summary>Writes <c>scheduling.job_run</c> for every execution (start, end, status, message).</summary>
internal sealed class JobRunListener(IServiceScopeFactory scopes, IClock clock) : IJobListener
{
    public const string TriggeredByKey = "triggeredBy";
    private const string RunIdKey = "strategy.runId";

    public string Name => "strategy-job-runs";

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        var run = new JobRun
        {
            JobKey = context.JobDetail.Key.Name,
            FiredAt = clock.UtcNow,
            Instance = context.Scheduler.SchedulerInstanceId,
            // Only manual runs carry the key; GetString throws on a missing key, which would make Quartz skip every scheduled run.
            TriggeredBy = context.MergedJobDataMap.TryGetString(TriggeredByKey, out var by) && Guid.TryParse(by, out var user) ? user : null,
        };
        context.Put(RunIdKey, run.Id);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        db.JobRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Get(StandbySuspensionKey) is not true)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        db.JobRuns.Add(new JobRun
        {
            JobKey = context.JobDetail.Key.Name, FiredAt = clock.UtcNow, FinishedAt = clock.UtcNow, Status = JobRunStatuses.Suspended,
            Message = "standby", Instance = context.Scheduler.SchedulerInstanceId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Set by <see cref="ScheduleSwitchListener"/> when it vetoes a run because of the standby guard.</summary>
    public const string StandbySuspensionKey = "strategy.standby";

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        if (context.Get(RunIdKey) is not Guid id)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var run = await db.JobRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return;
        }

        run.FinishedAt = clock.UtcNow;
        run.Status = jobException is null ? JobRunStatuses.Succeeded : JobRunStatuses.Failed;
        var message = jobException?.InnerException?.Message ?? jobException?.Message ?? context.Result as string;
        run.Message = message is { Length: > 2000 } ? message[..2000] : message;
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }
}

/// <summary>Removes processed outbox messages and job runs older than 30 days (dead messages stay until re-queued).</summary>
internal sealed class SchedulingCleanupJob(RequestContext requestContext, SchedulingDbContext db, IClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "scheduling.cleanup", "تنظيف سجلات المهام والرسائل", "Clean up job runs and delivered messages", "0 40 3 * * ?", typeof(SchedulingCleanupJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddDays(-30);
        var outbox = await db.Outbox.Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var runs = await db.JobRuns.Where(r => r.FiredAt < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return $"outbox={outbox} runs={runs}";
    }
}

/// <summary><c>Scheduling:RunSchedules</c> (default true): false stops runs fired by the job schedules; runs started on request still execute.</summary>
internal sealed record ScheduleSwitch(bool RunSchedules);

/// <summary>
/// Vetoes schedule-fired runs when <see cref="ScheduleSwitch.RunSchedules"/> is off (integration-test hosts, which invoke jobs
/// explicitly and must not race a job firing every minute). Manual runs use Quartz's own trigger group and are never vetoed.
/// </summary>
internal sealed class ScheduleSwitchListener(ScheduleSwitch schedule, StandbyJobGate standby) : ITriggerListener
{
    public string Name => "strategy-schedule-switch";

    public async Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!schedule.RunSchedules && trigger.Key.Group == Application.JobScheduleSynchroniser.Group)
        {
            return true;
        }

        if (context.JobDetail?.Key?.Name is { } jobKey && await standby.IsSuspendedAsync(jobKey, cancellationToken).ConfigureAwait(false))
        {
            context.Put(JobRunListener.StandbySuspensionKey, true);
            return true;
        }

        return false;
    }

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Jobs of the systems suspended while a source system is primary (SPEC-ADDENDUM §G4, ADR 0206): job keys start with their system code
/// (<c>kpi.period-window</c>). Scheduled and manual runs are both vetoed; platform jobs keep running.
/// </summary>
public sealed class StandbyJobGate(IEnumerable<Strategy.Platform.Kernel.Integration.IStandbyGuard> guards)
{
    public async Task<bool> IsSuspendedAsync(string jobKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobKey);
        var system = jobKey.Split('.')[0];
        foreach (var guard in guards)
        {
            if (await guard.IsActiveAsync(cancellationToken).ConfigureAwait(false)
                && (await guard.SuspendedSystemsAsync(cancellationToken).ConfigureAwait(false)).Contains(system))
            {
                return true;
            }
        }

        return false;
    }
}
