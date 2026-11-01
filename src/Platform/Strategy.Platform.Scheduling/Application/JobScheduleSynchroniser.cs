using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;

namespace Strategy.Platform.Scheduling.Application;

/// <summary>
/// Puts every declared job and its cron trigger into the job store at start-up. A job paused by an administrator stays paused
/// (the existing trigger state is kept when the schedule did not change).
/// </summary>
internal sealed class JobScheduleSynchroniser(ISchedulerFactory factory, IServiceProvider services, SchedulingOptions options) : IHostedService
{
    public const string Group = "strategy";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken).ConfigureAwait(false);
        foreach (var definition in services.GetServices<JobDefinition>())
        {
            var jobKey = new JobKey(definition.Key, Group);
            var triggerKey = new TriggerKey(definition.Key, Group);
            var job = JobBuilder.Create(definition.JobType).WithIdentity(jobKey).WithDescription(definition.NameEn).StoreDurably().Build();
            var trigger = TriggerBuilder.Create().WithIdentity(triggerKey).ForJob(jobKey)
                .WithCronSchedule(definition.Cron, cron => cron.InTimeZone(options.TimeZone).WithMisfireHandlingInstructionDoNothing()).Build();

            var existing = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false) as ICronTrigger;
            await scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
            }
            else if (existing.CronExpressionString != definition.Cron || existing.TimeZone.Id != options.TimeZone.Id)
            {
                await scheduler.RescheduleJob(triggerKey, trigger, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
