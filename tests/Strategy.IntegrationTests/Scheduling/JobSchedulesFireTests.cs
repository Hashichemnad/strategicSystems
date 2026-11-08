using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Scheduling.Application;
using Strategy.Platform.Scheduling.Domain;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.IntegrationTests.Scheduling;

/// <summary>
/// Quartz's clock (<see cref="SystemTime"/>) is process-wide: this collection runs alone, never next to another test host.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QuartzClockCollection
{
    public const string Name = "quartz-clock";
}

/// <summary>
/// Every registered job fires <b>on its schedule</b> (Phase 18; the Phase 3–17 defect — the run listener vetoed every cron firing, so jobs ran only
/// when started by hand — must never return). A real host with <c>Scheduling:RunSchedules=true</c>; the application clock and Quartz's clock are the
/// same fake clock. For each job the fake time moves to its next cron instant (computed independently from the cron expression in the
/// installation's time zone), and the run must appear in <c>scheduling.job_run</c> as a schedule-fired run (no requesting user) that succeeded.
/// </summary>
[Collection(QuartzClockCollection.Name)]
public sealed class JobSchedulesFireTests(PostgresFixture postgres)
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

    /// <summary>Monday 1 March 2027, 00:00 in Dubai.</summary>
    private static readonly DateTimeOffset Start = new(2027, 3, 1, 0, 0, 0, TimeSpan.FromHours(4));

    [Fact]
    public async Task Every_registered_job_has_a_valid_cron_that_fires_at_least_once_a_year()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Scheduling:TimeZone"] = "Asia/Dubai" });
        var definitions = factory.Services.GetServices<JobDefinition>().ToList();
        definitions.Count.ShouldBeGreaterThanOrEqualTo(27);
        definitions.Select(d => d.Key).ShouldBeUnique();
        foreach (var definition in definitions)
        {
            CronExpression.IsValidExpression(definition.Cron).ShouldBeTrue(definition.Key);
            var next = Next(definition, Start);
            next.ShouldNotBeNull(definition.Key);
            (next.Value - Start).ShouldBeLessThanOrEqualTo(TimeSpan.FromDays(366), definition.Key);
            definition.NameAr.ShouldNotBeNullOrWhiteSpace();
            definition.NameEn.ShouldNotBeNullOrWhiteSpace();
            typeof(StrategyJob).IsAssignableFrom(definition.JobType).ShouldBeTrue(definition.Key);
        }
    }

    [Fact]
    public async Task Every_registered_job_runs_when_its_schedule_fires_with_the_fake_clock()
    {
        // Half a minute before the start, between two instants of every cron: no job is due while the stored triggers are checked.
        // Frozen exactly at the start, a job whose first instant is the start itself was due at once, and Quartz could fire it before its
        // trigger was checked, moving its next fire time on (a race seen in CI).
        var clock = new TravellingClock { Frozen = Start.AddSeconds(-30) };
        var quartzClock = SystemTime.UtcNow;
        var quartzLocal = SystemTime.Now;
        SystemTime.UtcNow = () => clock.UtcNow;
        SystemTime.Now = () => clock.UtcNow.ToLocalTime();
        try
        {
            await using var factory = await ApiFactory.CreateAsync(
                postgres,
                settings: new Dictionary<string, string?>
                {
                    ["Scheduling:RunSchedules"] = "true",
                    ["Scheduling:TimeZone"] = "Asia/Dubai",
                },
                configureServices: services =>
                {
                    services.AddSingleton<IClock>(clock);

                    // Look for due triggers every second of real time (default 30 s; Quartz allows no less), so each jump of the fake clock is noticed.
                    services.Configure<QuartzOptions>(options => options["quartz.scheduler.idleWaitTime"] = "1000");
                });
            var scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            var definitions = factory.Services.GetServices<JobDefinition>().ToList();

            // The stored triggers say what the cron expressions say, in the installation's time zone.
            var due = new List<(JobDefinition Definition, DateTimeOffset At)>();
            foreach (var definition in definitions)
            {
                var trigger = await EventuallyAsync(
                    async () => await scheduler.GetTrigger(new TriggerKey(definition.Key, JobScheduleSynchroniser.Group)) as ICronTrigger,
                    t => t is not null);
                trigger!.CronExpressionString.ShouldBe(definition.Cron);
                trigger.TimeZone.Id.ShouldBe(Dubai.Id);
                // Quartz schedules the first firing after the (frozen) time the host started, like the cron expression itself.
                var expected = Next(definition, clock.Frozen!.Value)!.Value;
                trigger.GetNextFireTimeUtc().ShouldBe(expected, definition.Key);
                due.Add((definition, expected));
            }

            // Walk the fake time forward from one cron instant to the next: each job must run on its own, fired by its schedule.
            var verified = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instant in due.Select(d => d.At).Distinct().Order())
            {
                if (due.Where(d => d.At == instant).All(d => verified.Contains(d.Definition.Key)))
                {
                    continue;
                }

                clock.Frozen = instant.AddSeconds(1);
                foreach (var (definition, _) in due.Where(d => d.At == instant && !verified.Contains(d.Definition.Key)))
                {
                    var run = await EventuallyAsync(() => LastScheduledRunAsync(factory, definition.Key, instant), r => r is { Status: not JobRunStatuses.Running });
                    run.ShouldNotBeNull($"{definition.Key} did not run when its schedule fired at {instant:O}");
                    run.TriggeredBy.ShouldBeNull();
                    run.Status.ShouldBe(JobRunStatuses.Succeeded, $"{definition.Key}: {run.Message}");
                    verified.Add(definition.Key);
                }
            }

            verified.Count.ShouldBe(definitions.Count);
        }
        finally
        {
            SystemTime.UtcNow = quartzClock;
            SystemTime.Now = quartzLocal;
        }
    }

    private static DateTimeOffset? Next(JobDefinition definition, DateTimeOffset after) =>
        new CronExpression(definition.Cron) { TimeZone = Dubai }.GetNextValidTimeAfter(after);

    private static async Task<JobRun?> LastScheduledRunAsync(ApiFactory factory, string key, DateTimeOffset notBefore)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        return await db.JobRuns.AsNoTracking()
            .Where(r => r.JobKey == key && r.TriggeredBy == null && r.FiredAt >= notBefore)
            .OrderByDescending(r => r.FiredAt).FirstOrDefaultAsync();
    }

    private static async Task<T?> EventuallyAsync<T>(Func<Task<T?>> read, Func<T?, bool> done, int timeoutSeconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(100);
        }
    }
}
