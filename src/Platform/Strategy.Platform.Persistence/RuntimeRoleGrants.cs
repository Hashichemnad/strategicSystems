using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Strategy.Platform.Persistence;

/// <summary>A table the application may only read and append to (e.g. <c>audit.entry</c>, CLAUDE.md §8).</summary>
public sealed record AppendOnlyTable(string Schema, string Table);

/// <summary>
/// Two-role installs (ADR 0018): migrations run as the owner (<c>ConnectionStrings:StrategyMigrations</c>), the application as a runtime
/// role (<c>ConnectionStrings:Strategy</c>). After migrations this seeder gives the runtime role read / write on every module schema and
/// only SELECT + INSERT on append-only tables. With a single role nothing changes here; the triggers on those tables still refuse
/// UPDATE, DELETE and TRUNCATE.
/// </summary>
internal sealed partial class RuntimeRoleGrants : IDatabaseSeeder
{
    public int Order => 0;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var runtime = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        var runtimeRole = runtime is null ? null : new NpgsqlConnectionStringBuilder(runtime).Username;
        var ownerRole = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Username;
        if (string.IsNullOrEmpty(runtimeRole) || string.Equals(runtimeRole, ownerRole, StringComparison.Ordinal))
        {
            return;
        }

        var role = Quote(runtimeRole);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var schema in services.GetServices<ModuleDatabase>().Select(d => d.Schema).Concat(ExtraSchemas(services)).Distinct(StringComparer.Ordinal))
        {
            var s = Quote(schema);
            await ExecuteAsync(connection, $"""
                GRANT USAGE ON SCHEMA {s} TO {role};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {s} TO {role};
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {s} TO {role};
                """, cancellationToken).ConfigureAwait(false);
        }

        foreach (var table in services.GetServices<AppendOnlyTable>())
        {
            var name = $"{Quote(table.Schema)}.{Quote(table.Table)}";
            await ExecuteAsync(connection, $"REVOKE UPDATE, DELETE, TRUNCATE ON {name} FROM {role}; GRANT SELECT, INSERT ON {name} TO {role};",
                cancellationToken).ConfigureAwait(false);
        }

        var logger = services.GetRequiredService<ILogger<RuntimeRoleGrants>>();
        LogGranted(logger, runtimeRole);
    }

    private static IEnumerable<string> ExtraSchemas(IServiceProvider services) =>
        services.GetServices<ExtraSchema>().Select(s => s.Name);

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime role {Role} granted; append-only tables limited to SELECT and INSERT")]
    private static partial void LogGranted(ILogger logger, string role);
}

/// <summary>A schema without a module DbContext (e.g. Quartz tables) that the runtime role also needs.</summary>
public sealed record ExtraSchema(string Name);
