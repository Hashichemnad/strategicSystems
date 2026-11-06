using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Audit.Api;
using Strategy.Platform.Audit.Application;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Audit;

/// <summary>Hash-chained audit trail (<c>/api/v1/audit</c>, schema <c>audit</c>). docs/design/audit-files-notifications-jobs.md §2.</summary>
public sealed class AuditComponent : IPlatformComponent
{
    public const string SystemCode = "audit";

    public string Name => SystemCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName);
        services.AddScoped<IAuditSink, AuditChainWriter>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditQuery, AuditQuery>();
        services.AddScoped<AuditVerifier>();
        services.AddScoped<IAuditChainVerification, AuditChainVerification>();
        services.AddAdminHealthContributor<AuditHealthContributor>();
        services.AddScoped<AuditExport>();
        services.AddScoped<ChainAlert>();
        services.AddScheduledJob(AuditVerifyJob.Definition);
        services.AddNotificationTemplate(ChainAlert.Template);
        services.AddScoped<IDatabaseSeeder, AuditSeeder>();
        services.AddSingleton(new AppendOnlyTable(AuditDbContext.SchemaName, "entry"));
        services.AddAccessCatalogue(new AuditCatalogue());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuditEndpoints.Map(endpoints.MapPlatformGroup(this));
}
