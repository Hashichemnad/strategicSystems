using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Benchmarking.Ai;
using Strategy.Modules.Benchmarking.Api;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Benchmarking.Jobs;
using Strategy.Modules.Benchmarking.Mcp;
using Strategy.Modules.Benchmarking.Reporting;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Benchmarking;

/// <summary>Benchmarking — Plan (Request Framework), Search, Acquire, Analysis, CAP hand-off, library (SPEC §8, docs/design/benchmarking.md).</summary>
public sealed class BenchModule : IModule
{
    public string Name => "bench";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<BenchDbContext>(BenchDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(BenchModule).Assembly);
        services.AddAccessCatalogue(new BenchAccessCatalogue());
        services.AddNumberingFormat(BenchAccessCatalogue.StudyNumbering);
        foreach (var lookup in BenchAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in BenchNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<BenchReader>();
        services.AddScoped<BenchCalendar>();
        services.AddScoped<BenchNotifier>();
        services.AddScoped<BenchViews>();
        services.AddScoped<BenchRoles>();

        services.AddRequestType(BenchRequestTypes.RequestType);
        services.AddRequestTypeHandler<BenchRequestHandler>();
        services.AddApprovalProcess(BenchProcesses.PlanDefinition);
        services.AddApprovalProcess(BenchProcesses.SearchDefinition);
        services.AddApprovalProcess(BenchProcesses.AnalysisDefinition);
        services.AddApprovalProcess(BenchProcesses.ClosureDefinition);
        services.AddApprovalHook<BenchApprovalHook>();
        services.AddCurrentStatusProvider<StudyCurrentStatus>();
        services.AddMyWorkSource<BenchMyWork>();
        services.AddReportingMetricSource<BenchMetricSource>();
        services.AddReportingDatasetSource<BenchDatasetSource>();
        services.AddMyWorkCardSource<BenchMyWorkCards>();

        services.AddFilePurpose(BenchFilePurposes.StudyDocument);
        services.AddFileOwnerAccess<StudyFiles>();
        services.AddLinkableType(StudyLinkTargets.Definition);
        services.AddLinkTargetSource<StudyLinkTargets>();
        services.AddSearchContributor<BenchSearchContributor>(BenchPermissions.SystemCode); // global search (ADR 0160)
        services.AddResponsibilitySource<StudyCoordinatorResponsibility>(); // Settings → Responsibilities (ADR 0168)

        services.AddIntegrationEventHandler<CapRemarkStatusChanged, CapRemarkStatusChangedHandler>();
        services.AddScoped<BenchMonitorRunner>();
        services.AddScheduledJob(BenchMonitorJob.Definition);

        foreach (var feature in BenchAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(BenchModule).Assembly);
        services.AddScoped<BenchAiService>();
        services.AddRecordSearchSource<StudySearchSource>();
        services.AddAppTool<BenchStudiesTool>();
        services.AddAppTool<BenchStudyDetailsTool>();
        services.AddAppTool<BenchBestPracticesTool>();

        // Phase 22: analytics feeds and extracts (ADR 0190).
        services.AddReportableRecordSource<Feeds.BenchFeeds>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => BenchEndpoints.Map(endpoints.MapModuleGroup(this));
}
