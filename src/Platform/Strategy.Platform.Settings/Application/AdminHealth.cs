using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings.Infrastructure;

namespace Strategy.Platform.Settings.Application;

/// <summary>The Health page (ADR 0151): overall status (worst of all checks), when it was checked and every check.</summary>
public sealed record AdminHealthReport(string Status, DateTimeOffset CheckedAt, IReadOnlyList<AdminHealthCheck> Checks);

/// <summary>
/// Runs every <see cref="IAdminHealthContributor"/> in parallel, each in its own scope with a time-out (<c>Health:TimeoutSeconds</c>,
/// default 10). A contributor that throws or times out is reported as an error under its own code; the page always answers.
/// </summary>
internal sealed partial class AdminHealthService(IServiceScopeFactory scopes, IServiceProvider services, IClock clock, ILogger<AdminHealthService> logger)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public async Task<AdminHealthReport> CheckAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var contributors = services.GetServices<IAdminHealthContributor>().OrderBy(c => c.Order).ThenBy(c => c.Code, StringComparer.Ordinal).ToList();
        var results = await Task.WhenAll(contributors.Select(c => RunAsync(c, timeout, cancellationToken))).ConfigureAwait(false);
        var checks = results.SelectMany(r => r).ToList();
        return new AdminHealthReport(AdminHealthStatuses.Worst(checks.Select(c => c.Status)), clock.UtcNow, checks);
    }

    private async Task<IReadOnlyList<AdminHealthCheck>> RunAsync(IAdminHealthContributor contributor, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scoped = (IAdminHealthContributor)scope.ServiceProvider.GetRequiredService(contributor.GetType());
            return await scoped.CheckAsync(limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [new AdminHealthCheck(contributor.Code, contributor.Group, AdminHealthStatuses.Error, "health.common.timedOut",
                HealthParams.Of(("seconds", (int)timeout.TotalSeconds)))];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogContributorFailed(logger, contributor.Code, exception);
            return [new AdminHealthCheck(contributor.Code, contributor.Group, AdminHealthStatuses.Error, "health.common.failed",
                HealthParams.Of(("error", exception.GetType().Name)))];
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health check {Code} failed")]
    private static partial void LogContributorFailed(ILogger logger, string code, Exception exception);
}

/// <summary>Database reachable (latency, server version, size) and pending migrations per module schema.</summary>
internal sealed class DatabaseHealthContributor(SettingsDbContext db, ModuleMigrator migrator) : IAdminHealthContributor
{
    /// <summary>Above this round trip the database check is a warning.</summary>
    public static readonly TimeSpan SlowQuery = TimeSpan.FromMilliseconds(500);

    public string Code => "database";

    public string Group => AdminHealthGroups.Platform;

    public int Order => 0;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        AdminHealthCheck database;
        var watch = Stopwatch.StartNew();
        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_setting('server_version'), pg_database_size(current_database())";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var info = new DatabaseInfo(reader.GetString(0), reader.GetInt64(1));
            watch.Stop();
            database = new AdminHealthCheck("database", AdminHealthGroups.Platform,
                watch.Elapsed > SlowQuery ? AdminHealthStatuses.Warning : AdminHealthStatuses.Ok,
                watch.Elapsed > SlowQuery ? "health.database.slow" : "health.database.ok",
                HealthParams.Of(("latencyMs", (long)watch.Elapsed.TotalMilliseconds), ("version", info.Version), ("sizeBytes", info.SizeBytes)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [new AdminHealthCheck("database", AdminHealthGroups.Platform, AdminHealthStatuses.Error, "health.database.unreachable",
                HealthParams.Of(("error", exception.GetType().Name)))];
        }

        var pending = await migrator.PendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var items = pending.Select(p => new AdminHealthItem(p.Schema, p.Pending == 0 ? AdminHealthStatuses.Ok : AdminHealthStatuses.Error,
            p.Pending == 0 ? "health.migrations.itemOk" : "health.migrations.itemPending", HealthParams.Of(("count", p.Pending)))).ToList();
        var total = pending.Sum(p => p.Pending);
        var migrations = new AdminHealthCheck("migrations", AdminHealthGroups.Platform,
            total == 0 ? AdminHealthStatuses.Ok : AdminHealthStatuses.Error,
            total == 0 ? "health.migrations.ok" : "health.migrations.pending",
            HealthParams.Of(("count", total), ("schemas", pending.Count)), items);
        return [database, migrations];
    }

    private sealed record DatabaseInfo(string Version, long SizeBytes);
}
