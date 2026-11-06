using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling.Domain;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.Platform.Scheduling.Application;

/// <summary>
/// Health page (ADR 0151): the scheduler (started, schedules on), every declared job (last run and status, next fire time, failures in
/// 24 hours, paused) and the outbox (pending, dead, oldest pending age — notifications must leave within 5 minutes, CLAUDE.md §14).
/// </summary>
internal sealed class SchedulingHealthContributor(
    ISchedulerFactory factory, IServiceProvider services, SchedulingDbContext db, IClock clock, ScheduleSwitch schedules) : IAdminHealthContributor
{
    /// <summary>A message older than this still pending means notifications are late.</summary>
    public static readonly TimeSpan OutboxLate = TimeSpan.FromMinutes(5);

    public string Code => "scheduler";

    public string Group => AdminHealthGroups.Jobs;

    public int Order => 10;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var scheduler = await factory.GetScheduler(cancellationToken).ConfigureAwait(false);
        var running = scheduler.IsStarted && !scheduler.InStandbyMode && !scheduler.IsShutdown;
        var paused = await scheduler.GetPausedTriggerGroups(cancellationToken).ConfigureAwait(false);
        var since = now.AddHours(-24);
        var lastRuns = await db.JobRuns.AsNoTracking().GroupBy(r => r.JobKey)
            .Select(g => g.OrderByDescending(r => r.FiredAt).First()).ToListAsync(cancellationToken).ConfigureAwait(false);
        var failures = await db.JobRuns.AsNoTracking().Where(r => r.Status == JobRunStatuses.Failed && r.FiredAt >= since)
            .GroupBy(r => r.JobKey).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);

        var items = new List<AdminHealthItem>();
        foreach (var definition in services.GetServices<JobDefinition>().OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var triggerKey = new TriggerKey(definition.Key, JobScheduleSynchroniser.Group);
            var trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            var state = trigger is null ? TriggerState.None : await scheduler.GetTriggerState(triggerKey, cancellationToken).ConfigureAwait(false);
            var isPaused = state == TriggerState.Paused || paused.Contains(JobScheduleSynchroniser.Group);
            var last = lastRuns.Find(r => r.JobKey == definition.Key);
            var failed24 = failures.GetValueOrDefault(definition.Key);
            var next = trigger?.GetNextFireTimeUtc();
            var (status, key) = JobStatus(last?.Status, failed24, isPaused, next is not null, trigger is not null);
            items.Add(new AdminHealthItem(definition.Key, status, key,
                HealthParams.Of(("lastRunAt", last?.FiredAt), ("lastStatus", last?.Status), ("nextRunAt", next), ("failures24h", failed24),
                    ("cron", definition.Cron), ("paused", isPaused)),
                definition.NameAr, definition.NameEn));
        }

        var schedulerStatus = !running ? AdminHealthStatuses.Error
            : !schedules.RunSchedules ? AdminHealthStatuses.Warning
            : AdminHealthStatuses.Worst(items.Select(i => i.Status));
        var schedulerKey = !running ? "health.scheduler.stopped"
            : !schedules.RunSchedules ? "health.scheduler.schedulesOff"
            : schedulerStatus == AdminHealthStatuses.Ok ? "health.scheduler.ok" : "health.scheduler.attention";
        var schedulerCheck = new AdminHealthCheck("scheduler", AdminHealthGroups.Jobs, schedulerStatus, schedulerKey,
            HealthParams.Of(("jobs", items.Count), ("failing", items.Count(i => i.Status == AdminHealthStatuses.Error)),
                ("instance", scheduler.SchedulerInstanceId)),
            items);

        var pendingQuery = db.Outbox.AsNoTracking().Where(m => m.ProcessedAt == null && m.DeadAt == null);
        var pending = await pendingQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var oldest = await pendingQuery.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken).ConfigureAwait(false);
        var dead = await db.Outbox.CountAsync(m => m.DeadAt != null, cancellationToken).ConfigureAwait(false);
        var processed = await db.Outbox.CountAsync(m => m.ProcessedAt >= since, cancellationToken).ConfigureAwait(false);
        var late = oldest is { } first && now - first > OutboxLate;
        var outboxStatus = late ? AdminHealthStatuses.Error : dead > 0 ? AdminHealthStatuses.Warning : AdminHealthStatuses.Ok;
        var outboxKey = late ? "health.outbox.late" : dead > 0 ? "health.outbox.dead" : "health.outbox.ok";
        var outbox = new AdminHealthCheck("outbox", AdminHealthGroups.Jobs, outboxStatus, outboxKey,
            HealthParams.Of(("pending", pending), ("dead", dead), ("processed24h", processed), ("oldestPendingAt", oldest),
                ("oldestPendingMinutes", oldest is { } o ? (long)(now - o).TotalMinutes : null)));
        return [schedulerCheck, outbox];
    }

    /// <summary>Pure status rule for one job (unit-tested).</summary>
    public static (string Status, string MessageKey) JobStatus(string? lastStatus, int failures24h, bool paused, bool hasNextFire, bool scheduled)
    {
        if (!scheduled)
        {
            return (AdminHealthStatuses.Error, "health.job.notScheduled");
        }

        if (paused)
        {
            return (AdminHealthStatuses.Warning, "health.job.paused");
        }

        if (lastStatus == JobRunStatuses.Failed)
        {
            return (AdminHealthStatuses.Error, "health.job.lastFailed");
        }

        if (failures24h > 0)
        {
            return (AdminHealthStatuses.Warning, "health.job.recentFailures");
        }

        if (!hasNextFire)
        {
            return (AdminHealthStatuses.Warning, "health.job.noNextRun");
        }

        return lastStatus is null ? (AdminHealthStatuses.Ok, "health.job.notRunYet") : (AdminHealthStatuses.Ok, "health.job.ok");
    }
}
