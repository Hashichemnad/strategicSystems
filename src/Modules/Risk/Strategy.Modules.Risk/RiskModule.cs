using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Ai;
using Strategy.Modules.Risk.Api;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Modules.Risk.Jobs;
using Strategy.Modules.Risk.Mcp;
using Strategy.Modules.Risk.Reporting;
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
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Risk;

/// <summary>Risk Management — framework, register, controls, treatment, KRIs, reviews, Project Risks (SPEC §3, docs/design/risk.md).</summary>
public sealed class RiskModule : IModule
{
    public string Name => "risk";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<RiskDbContext>(RiskDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(RiskModule).Assembly);
        services.AddAccessCatalogue(new RiskAccessCatalogue());
        services.AddNumberingFormat(RiskAccessCatalogue.RiskNumbering);
        services.AddNumberingFormat(RiskAccessCatalogue.ActionNumbering);
        services.AddNumberingFormat(RiskAccessCatalogue.EventNumbering);
        foreach (var lookup in RiskAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in RiskNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<RiskReader>();
        services.AddScoped<RiskDeadlines>();
        services.AddScoped<RiskNotifier>();
        services.AddScoped<FrameworkStore>();
        services.AddScoped<RiskRater>();
        services.AddScoped<RiskRules>();
        services.AddScoped<RiskViews>();
        services.AddScoped<RiskRoleSync>();
        services.AddScoped<IRiskScoring, RiskScoringService>();
        services.AddScoped<IRiskDirectory, RiskDirectory>();
        services.AddScoped<IKpiLinkedRecordSource, RiskKpiLinks>();
        services.AddScoped<IDatabaseSeeder, RiskSeeder>();

        services.AddApprovalProcess(RiskProcesses.ReviewDefinition);
        services.AddApprovalProcess(RiskProcesses.PeriodicReviewDefinition);
        services.AddApprovalProcess(RiskProcesses.ClosureDefinition);
        services.AddApprovalHook<RiskApprovalHook>();
        services.AddCurrentStatusProvider<RiskCurrentStatus>();
        services.AddMyWorkSource<RiskMyWork>();
        services.AddReportingMetricSource<RiskMetricSource>();
        services.AddReportingListSource<RiskListSource>();
        services.AddReportingDatasetSource<RiskDatasetSource>();
        services.AddMyWorkCardSource<RiskMyWorkCards>();

        services.AddFilePurpose(RiskFilePurposes.RiskDocument);
        services.AddFilePurpose(RiskFilePurposes.ActionEvidence);
        services.AddFileOwnerAccess<RiskFiles>();
        services.AddFileOwnerAccess<ActionFiles>();

        services.AddLinkableType(RiskLinkTargets.Definition);
        services.AddLinkTargetSource<RiskLinkTargets>();
        services.AddLinkableType(ActionLinkTargets.Definition);
        services.AddLinkTargetSource<ActionLinkTargets>();

        services.AddIntegrationEventHandler<KpiResultApproved, KpiResultApprovedHandler>();
        services.AddScoped<MonitorRunner>();
        services.AddScheduledJob(RiskMonitorJob.Definition);

        foreach (var feature in RiskAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(RiskModule).Assembly);
        services.AddScoped<RiskAiService>();
        services.AddScoped<RiskSearchSourceAccessor>();
        services.AddRecordSearchSource<RiskSearchSource>();
        services.AddAppTool<RiskRegisterSearchTool>();
        services.AddAppTool<RiskDetailsTool>();
        services.AddAppTool<MyRiskActionsTool>();
        services.AddAppTool<RiskHeatmapSummaryTool>();
        services.AddAppTool<RiskTop10Tool>();
        services.AddAppTool<RisksOutsideAppetiteTool>();
        services.AddAppTool<UpdateRiskActionProgressDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => RiskEndpoints.Map(endpoints.MapModuleGroup(this));
}
