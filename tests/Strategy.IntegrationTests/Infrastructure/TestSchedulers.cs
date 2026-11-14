using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// Shuts Quartz schedulers down explicitly. A scheduler that outlives its host keeps its cluster manager checking in against a test database
/// that is gone, and keeps the test process alive after the last test ("Foreground threads were left running, forcing process exit").
/// Shutdown does not wait for running jobs: the tests that run jobs await them themselves.
/// </summary>
public static class TestSchedulers
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>Every scheduler of one host (before the host itself is disposed).</summary>
    public static async Task ShutdownAsync(IServiceProvider services)
    {
        if (services.GetService<ISchedulerFactory>() is not { } factory)
        {
            return;
        }

        IReadOnlyList<IScheduler> schedulers;
        try
        {
            schedulers = [.. await factory.GetAllSchedulers()];
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await ShutdownAsync(schedulers);
    }

    /// <summary>Every scheduler still registered in this process (end of the test run, before the database server stops).</summary>
    public static async Task ShutdownRemainingAsync() => await ShutdownAsync([.. SchedulerRepository.Instance.LookupAll()]);

    private static async Task ShutdownAsync(IReadOnlyList<IScheduler> schedulers)
    {
        foreach (var scheduler in schedulers.Where(s => !s.IsShutdown))
        {
            try
            {
                await scheduler.Shutdown(waitForJobsToComplete: false).WaitAsync(Limit);
            }
            catch (Exception ex) when (ex is SchedulerException or TimeoutException or ObjectDisposedException or InvalidOperationException)
            {
                // Best effort: a scheduler whose store is unreachable still stops its own threads; a timeout must not fail the run.
            }
        }
    }
}
