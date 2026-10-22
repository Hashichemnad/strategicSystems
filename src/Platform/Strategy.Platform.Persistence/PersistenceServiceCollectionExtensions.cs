using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Strategy.Platform.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Strategy";
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string ReadyTag = "ready";

    /// <summary>Registers the shared PostgreSQL data source, interceptors and the migrator.</summary>
    public static IServiceCollection AddPlatformPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            return new NpgsqlDataSourceBuilder(connectionString).Build();
        });
        services.TryAddScoped<AuditFieldsInterceptor>();
        services.TryAddSingleton<ModuleMigrator>();
        return services;
    }

    /// <summary>Registers a module DbContext on its own schema, with its own migrations history and readiness check.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schema">The schema the context owns.</param>
    /// <param name="historyTable">Migrations history table; only change it when two contexts share a schema (ADR 0010).</param>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string schema, string historyTable = MigrationsHistoryTable)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredService<NpgsqlDataSource>(),
                npgsql => npgsql
                    .MigrationsHistoryTable(historyTable, schema)
                    .MigrationsAssembly(typeof(TContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditFieldsInterceptor>()));

        services.AddSingleton(new ModuleDatabase(typeof(TContext), schema));
        var checkName = historyTable == MigrationsHistoryTable ? $"db:{schema}" : $"db:{schema}:{typeof(TContext).Name}";
        services.AddHealthChecks().AddDbContextCheck<TContext>(checkName, tags: [ReadyTag]);
        return services;
    }
}
