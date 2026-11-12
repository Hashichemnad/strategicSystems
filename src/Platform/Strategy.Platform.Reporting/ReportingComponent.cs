using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting.Api;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Reporting.Jobs;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Reporting;

/// <summary>
/// Dashboards and reporting (<c>/api/v1/reporting</c>, schema <c>reporting</c>, docs/design/reporting.md, ADRs 0140–0149): the read model of
/// module aggregates, dashboards and the builder, the executive dashboard and scorecard, the report builder, schedules, the AI brief and My
/// Work cards. Registered after the platform components it builds on.
/// </summary>
public sealed class ReportingComponent : IPlatformComponent
{
    public string Name => "reporting";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ReportingDbContext>(ReportingDbContext.SchemaName);
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ReportingChangeTracker>();
        services.AddSingleton<ISaveChangesObserver>(sp => sp.GetRequiredService<ReportingChangeTracker>());
        services.AddScoped<MetricRegistry>();
        services.AddScoped<IReportableMetadata>(sp => sp.GetRequiredService<MetricRegistry>());
        services.AddScoped<ReportingClock>();
        services.AddScoped<FactRefresher>();
        services.AddScoped<FactStatus>();
        services.AddScoped<MetricEvaluator>();
        services.AddScoped<DepartmentMap>();
        services.AddScoped<ScorecardBuilder>();
        services.AddScoped<WidgetValidator>();
        services.AddScoped<WidgetEvaluator>();
        services.AddScoped<UnitNames>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DashboardRenderer>();
        services.AddScoped<ReportService>();
        services.AddScoped<ReportTypes.ReportValueFormatter>();
        services.AddScoped<ReportTypes.ReportTypeEngine>();
        services.AddScoped<ReportTypes.ReportTypeService>();
        services.AddScoped<ReportTypes.ReportTypeExport>();
        services.AddScoped<FolderService>();
        services.AddScoped<Dossiers.DossierBuilder>();
        services.AddScoped<Dossiers.DossierTemplateService>();
        services.AddScoped<Datasets.ExternalReferenceColumns>();
        services.AddReportingDimensionLabelSource<Datasets.SystemNameLabels>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<DeliveryRunner>();
        services.AddScoped<BriefService>();
        services.AddScoped<MyWorkCardsService>();
        services.AddMyWorkCardSource<PinsMyWorkCards>();
        services.AddScoped<IDatabaseSeeder, ReportingSeeder>();
        services.AddAccessCatalogue(new ReportingCatalogue());
        services.AddFilePurpose(new FilePurpose(DeliveryRunner.FilePurposeKey, "reporting.delivery", ReportingPermissions.SystemCode,
            [FileKinds.Pdf, FileKinds.Xlsx], 50L * 1024 * 1024, SingleFile: true));
        services.AddFileOwnerAccess<DeliveryFiles>();
        foreach (var template in ReportingNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddAiFeature(new AiFeatureDefinition(BriefService.Feature, "الموجز التنفيذي", "Executive brief", ReportingPermissions.BriefGenerate));
        services.AddAiPromptAssembly(typeof(ReportingComponent).Assembly);
        services.AddScheduledJob(ReportingRefreshJob.Definition);
        services.AddScheduledJob(ReportingSnapshotJob.Definition);
        services.AddScheduledJob(ReportingRebuildJob.Definition);
        services.AddScheduledJob(ReportingDeliveryJob.Definition);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ReportingEndpoints.Map(endpoints.MapPlatformGroup(this));
}
