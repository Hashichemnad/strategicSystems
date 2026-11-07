using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Ai;
using Strategy.Modules.Kpi.Api;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Modules.Kpi.Jobs;
using Strategy.Modules.Kpi.Mcp;
using Strategy.Modules.Kpi.Reporting;
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

namespace Strategy.Modules.Kpi;

/// <summary>Performance Management — KPI cards, annual plans, results, approval, scorecards (SPEC §2, docs/design/kpi.md).</summary>
public sealed class KpiModule : IModule
{
    public string Name => "kpi";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<KpiDbContext>(KpiDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(KpiModule).Assembly);
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.KpiDemoPack>();
        services.AddAccessCatalogue(new KpiAccessCatalogue());
        services.AddNumberingFormat(KpiAccessCatalogue.CardNumbering);
        foreach (var lookup in KpiAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        services.AddFeature(KpiAccessCatalogue.ChangeControl);
        foreach (var template in KpiNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<KpiReader>();
        services.AddScoped<KpiNotifier>();
        services.AddScoped<KpiDeadlines>();
        services.AddScoped<KpiCalendar>();
        services.AddScoped<ResultOpener>();
        services.AddScoped<PlanCalculator>();
        services.AddScoped<CardRules>();
        services.AddScoped<CardQueries>();
        services.AddScoped<ResultViews>();
        services.AddScoped<ScorecardBuilder>();
        services.AddScoped<IKpiCatalog, KpiCatalog>();
        services.AddScoped<IKpiStrategyCatalog, KpiStrategyCatalog>();
        services.AddScoped<IKpiPerformance, KpiPerformance>();
        services.AddScoped<IKpiBenchmarkWriter, KpiBenchmarkWriter>();
        services.AddScoped<IKpiResultFeed, KpiResultFeed>();

        services.AddApprovalProcess(ResultApprovalProcess.Definition);
        services.AddApprovalProcess(DefinitionChangeProcess.Definition);
        services.AddApprovalHook<ResultApprovalHook>();
        services.AddApprovalHook<DefinitionChangeHook>();
        services.AddCurrentStatusProvider<ResultCurrentStatus>();
        services.AddMyWorkSource<KpiMyWork>();

        // Phase 17 — reporting sources (docs/design/reporting.md §2.5, §6, §7).
        services.AddReportingMetricSource<KpiMetricSource>();
        services.AddReportingDimensionLabelSource<KpiDimensionLabels>();
        services.AddReportingListSource<KpiListSource>();
        services.AddReportingDatasetSource<KpiDatasetSource>();
        services.AddMyWorkCardSource<KpiMyWorkCards>();

        services.AddFilePurpose(KpiFilePurposes.Evidence);
        services.AddFilePurpose(KpiFilePurposes.CardDocument);
        services.AddFileOwnerAccess<ResultFiles>();
        services.AddFileOwnerAccess<CardFiles>();

        services.AddLinkableType(CardLinkTargets.Definition);
        services.AddLinkTargetSource<CardLinkTargets>();
        services.AddLinkableType(ResultLinkTargets.Definition);
        services.AddLinkTargetSource<ResultLinkTargets>();
        services.AddSearchContributor<KpiSearchContributor>(KpiPermissions.SystemCode); // global search (ADR 0160)

        services.AddScoped<WindowRunner>();
        services.AddScheduledJob(PeriodWindowsJob.Definition);

        foreach (var feature in KpiAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(KpiModule).Assembly);
        services.AddScoped<KpiAiService>();
        services.AddRecordSearchSource<CardSearchSource>();
        services.AddAppTool<MyPendingKpiResultsTool>();
        services.AddAppTool<KpiCardLookupTool>();
        services.AddAppTool<DepartmentScorecardTool>();
        services.AddAppTool<ApprovalQueueSummaryTool>();
        services.AddAppTool<SubmitKpiResultDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => KpiEndpoints.Map(endpoints.MapModuleGroup(this));
}
