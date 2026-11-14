using Quartz;
using Quartz.Impl;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// Shuts Quartz schedulers down explicitly. A scheduler that outlives its host keeps its cluster manager checking in against a test database
/// that is gone, and keeps the test process alive after the last test ("Foreground threads were left running, forcing process exit").
/// Called after the host stopped: shuts down only schedulers that are still running, without waiting for jobs, and never throws (cleanup
/// must not fail a test or leave the rest of the host undisposed).
/// </summary>
public static class TestSchedulers
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>Every scheduler of one host's scheduler factory (taken while the host was alive; called before the host is disposed).</summary>
    public static async Task ShutdownAsync(ISchedulerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        IReadOnlyList<IScheduler> schedulers;
        try
        {
            schedulers = [.. await factory.GetAllSchedulers()];
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SchedulerException or InvalidOperationException)
        {
            return;
        }

        await ShutdownAsync(schedulers);
    }

    /// <summary>Every scheduler still registered in this process (end of the test run, before the database server stops).</summary>
    public static async Task ShutdownRemainingAsync() => await ShutdownAsync([.. SchedulerRepository.Instance.LookupAll()]);

    private static async Task ShutdownAsync(IReadOnlyList<IScheduler> schedulers)
    {
        foreach (var scheduler in schedulers.Where(s => s is { IsShutdown: false }))
        {
            try
            {
                await scheduler.Shutdown(waitForJobsToComplete: false).WaitAsync(Limit);
            }
#pragma warning disable CA1031 // Best effort: a scheduler whose store is unreachable still stops its own threads; nothing here may fail the run.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }
}
