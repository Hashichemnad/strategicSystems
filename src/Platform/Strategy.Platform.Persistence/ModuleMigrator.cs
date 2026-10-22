using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Strategy.Platform.Persistence;

/// <summary>A module DbContext registered for migration and health checks.</summary>
public sealed record ModuleDatabase(Type ContextType, string Schema);

/// <summary>Applies pending migrations of every registered module, one schema at a time.</summary>
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
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema {Schema}: applied {Count} pending migration(s)")]
    private static partial void LogMigrated(ILogger logger, string schema, int count);
}
