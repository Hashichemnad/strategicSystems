using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling.Api;
using Strategy.Platform.Scheduling.Application;
using Strategy.Platform.Scheduling.Infrastructure;

namespace Strategy.Platform.Scheduling;

/// <summary>
/// Outbox and scheduled jobs (<c>/api/v1/scheduling</c>, schema <c>scheduling</c>). Quartz.NET uses a clustered PostgreSQL job store
/// (<c>scheduling.qrtz_*</c>); every job declared with <see cref="SchedulingServiceCollectionExtensions.AddScheduledJob"/> is scheduled at start.
/// </summary>
public sealed class SchedulingComponent : IPlatformComponent
{
    public string Name => "scheduling";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SchedulingDbContext>(SchedulingDbContext.SchemaName);
        services.AddSingleton<OutboxSignal>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<Strategy.Platform.Kernel.Messaging.IIntegrationEventPublisher, IntegrationEventPublisher>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddSingleton<JobRunListener>();
        services.AddScoped<JobAdministration>();
        services.AddScheduledJob(SchedulingCleanupJob.Definition);

        var connectionString = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName) ?? string.Empty;
        var timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(configuration["Scheduling:TimeZone"] ?? "UTC", out var zone) ? zone : TimeZoneInfo.Utc;
        services.AddSingleton(new SchedulingOptions(timeZone));
        services.AddQuartz(quartz =>
        {
            // Unique per process in tests (several hosts share one process); the same name on every instance of an install (cluster).
            quartz.SchedulerName = configuration["Scheduling:SchedulerName"] ?? "strategy";
            quartz.SchedulerId = "AUTO";
            quartz.MisfireThreshold = TimeSpan.FromMinutes(1);
            quartz.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.PerformSchemaValidation = false;
                store.UsePostgres(postgres =>
                {
                    postgres.ConnectionString = connectionString;
                    postgres.TablePrefix = "scheduling.qrtz_";
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering(cluster => cluster.CheckinInterval = TimeSpan.FromSeconds(20));
            });
            quartz.AddJobListener<JobRunListener>();
        });
        services.AddHostedService<JobScheduleSynchroniser>();
        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
            options.AwaitApplicationStarted = true;
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SchedulingEndpoints.Map(endpoints.MapPlatformGroup(this));
}

public sealed record SchedulingOptions(TimeZoneInfo TimeZone);
