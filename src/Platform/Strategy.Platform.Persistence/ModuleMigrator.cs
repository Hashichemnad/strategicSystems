using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Strategy.Platform.Persistence;

/// <summary>A module DbContext registered for migration and health checks.</summary>
public sealed record ModuleDatabase(Type ContextType, string Schema);

/// <summary>
/// Reference data written after all migrations ran (permission catalogue, default roles, lookup types, numbering formats…).
/// Must be idempotent: it runs on every <c>--migrate</c> and every start with <c>Database:MigrateOnStartup</c>.
/// </summary>
public interface IDatabaseSeeder
{
    /// <summary>Lower runs first.</summary>
    int Order { get; }

    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>Applies pending migrations of every registered module, one schema at a time, then runs the seeders.</summary>
public sealed partial class ModuleMigrator(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ModuleDatabase> databases,
    ILogger<ModuleMigrator> logger)
{
    public async Task MigrateAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var database in databases)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            LogMigrated(logger, database.Schema, pending.Count);
        }

        await SeedAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when any module has a migration not yet applied (or its schema does not exist yet). Reads only.</summary>
    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var database in databases)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(database.ContextType);
            if (!await context.GetService<IHistoryRepository>().ExistsAsync(cancellationToken).ConfigureAwait(false) ||
                (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
            {
                return true;
            }
        }

        return false;
    }

    public async Task SeedAllAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDatabaseSeeder>().OrderBy(s => s.Order))
        {
            await using var seedScope = scopeFactory.CreateAsyncScope();
            await seeder.SeedAsync(seedScope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            LogSeeded(logger, seeder.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema {Schema}: applied {Count} pending migration(s)")]
    private static partial void LogMigrated(ILogger logger, string schema, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeder {Seeder} done")]
    private static partial void LogSeeded(ILogger logger, string seeder);
}
