using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling.Domain;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.Platform.Scheduling.Application;

public sealed record JobDto(
    string Key, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string Cron, string TimeZone, bool Paused,
    DateTimeOffset? NextRunAt, DateTimeOffset? PreviousRunAt, JobRunDto? LastRun, int FailuresLast7Days);

public sealed record JobRunDto(Guid Id, string JobKey, DateTimeOffset FiredAt, DateTimeOffset? FinishedAt, string Status, string? Message, Guid? TriggeredBy,
    string? Instance);

public sealed record OutboxStatusDto(int Pending, int Dead, int ProcessedLast24Hours, DateTimeOffset? OldestPendingAt, IReadOnlyList<OutboxMessageDto> DeadMessages);

public sealed record OutboxMessageDto(Guid Id, string Type, DateTimeOffset CreatedAt, int Attempts, string? LastError, DateTimeOffset? DeadAt);

/// <summary>Job dashboard: declared jobs with their Quartz state and recent runs; run now, pause, resume; outbox status and re-queue.</summary>
internal sealed class JobAdministration(
    ISchedulerFactory factory, IServiceProvider services, SchedulingDbContext db, IClock clock, IRequestContext context, IAuditLog audit,
    OutboxSignal signal, SchedulingOptions options)
{
    public async Task<IReadOnlyList<JobDto>> ListAsync(CancellationToken ct)
    {
        var scheduler = await factory.GetScheduler(ct).ConfigureAwait(false);
        var paused = await scheduler.GetPausedTriggerGroups(ct).ConfigureAwait(false);
        var since = clock.UtcNow.AddDays(-7);
        var lastRuns = await db.JobRuns.AsNoTracking().GroupBy(r => r.JobKey)
            .Select(g => g.OrderByDescending(r => r.FiredAt).First()).ToListAsync(ct).ConfigureAwait(false);
        var failures = await db.JobRuns.AsNoTracking().Where(r => r.Status == JobRunStatuses.Failed && r.FiredAt >= since)
            .GroupBy(r => r.JobKey).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var result = new List<JobDto>();
        foreach (var definition in services.GetServices<JobDefinition>().OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            var triggerKey = new TriggerKey(definition.Key, JobScheduleSynchroniser.Group);
            var trigger = await scheduler.GetTrigger(triggerKey, ct).ConfigureAwait(false);
            var state = trigger is null ? TriggerState.None : await scheduler.GetTriggerState(triggerKey, ct).ConfigureAwait(false);
            var last = lastRuns.Find(r => r.JobKey == definition.Key);
            result.Add(new JobDto(definition.Key, definition.NameAr, definition.NameEn, definition.DescriptionAr, definition.DescriptionEn, definition.Cron,
                options.TimeZone.Id, state == TriggerState.Paused || paused.Contains(JobScheduleSynchroniser.Group), trigger?.GetNextFireTimeUtc(),
                trigger?.GetPreviousFireTimeUtc(), last is null ? null : ToDto(last), failures.GetValueOrDefault(definition.Key)));
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<JobRunDto>>> RunsAsync(string key, CancellationToken ct)
    {
        if (!Known(key))
        {
            return Error.NotFound();
        }

        return await db.JobRuns.AsNoTracking().Where(r => r.JobKey == key).OrderByDescending(r => r.FiredAt).Take(50).Select(r => ToDto(r))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result> RunNowAsync(string key, CancellationToken ct)
    {
        if (!Known(key))
        {
            return Error.NotFound();
        }

        var scheduler = await factory.GetScheduler(ct).ConfigureAwait(false);
        await scheduler.TriggerJob(new JobKey(key, JobScheduleSynchroniser.Group),
            new JobDataMap { [JobRunListener.TriggeredByKey] = context.UserId?.ToString("D") ?? string.Empty }, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent(AuditActions.JobRunRequested, true, "settings", "scheduling.job", key), ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetPausedAsync(string key, bool paused, CancellationToken ct)
    {
        if (!Known(key))
        {
            return Error.NotFound();
        }

        var scheduler = await factory.GetScheduler(ct).ConfigureAwait(false);
        var triggerKey = new TriggerKey(key, JobScheduleSynchroniser.Group);
        if (paused)
        {
            await scheduler.PauseTrigger(triggerKey, ct).ConfigureAwait(false);
        }
        else
        {
            await scheduler.ResumeTrigger(triggerKey, ct).ConfigureAwait(false);
        }

        await audit.WriteAsync(new AuditEvent(paused ? "job.pause" : "job.resume", true, "settings", "scheduling.job", key), ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<OutboxStatusDto> OutboxAsync(CancellationToken ct)
    {
        var since = clock.UtcNow.AddHours(-24);
        var pending = db.Outbox.AsNoTracking().Where(m => m.ProcessedAt == null && m.DeadAt == null);
        var dead = await db.Outbox.AsNoTracking().Where(m => m.DeadAt != null).OrderByDescending(m => m.DeadAt).Take(100)
            .Select(m => new OutboxMessageDto(m.Id, m.Type, m.CreatedAt, m.Attempts, m.LastError, m.DeadAt)).ToListAsync(ct).ConfigureAwait(false);
        return new OutboxStatusDto(
            await pending.CountAsync(ct).ConfigureAwait(false),
            await db.Outbox.CountAsync(m => m.DeadAt != null, ct).ConfigureAwait(false),
            await db.Outbox.CountAsync(m => m.ProcessedAt >= since, ct).ConfigureAwait(false),
            await pending.MinAsync(m => (DateTimeOffset?)m.CreatedAt, ct).ConfigureAwait(false),
            dead);
    }

    public async Task<Result> RequeueAsync(Guid id, CancellationToken ct)
    {
        var message = await db.Outbox.FirstOrDefaultAsync(m => m.Id == id && m.DeadAt != null, ct).ConfigureAwait(false);
        if (message is null)
        {
            return Error.NotFound();
        }

        message.DeadAt = null;
        message.Attempts = 0;
        message.AvailableAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("outbox.requeue", true, "settings", "scheduling.outbox_message", id.ToString("D"), new { message.Type }), ct)
            .ConfigureAwait(false);
        signal.Notify();
        return Result.Success();
    }

    private bool Known(string key) => services.GetServices<JobDefinition>().Any(d => d.Key == key);

    private static JobRunDto ToDto(JobRun r) => new(r.Id, r.JobKey, r.FiredAt, r.FinishedAt, r.Status, r.Message, r.TriggeredBy, r.Instance);
}
