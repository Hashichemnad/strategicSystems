using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Cap.Ai;
using Strategy.Modules.Cap.Api;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Cap.Jobs;
using Strategy.Modules.Cap.Mcp;
using Strategy.Modules.Cap.Reporting;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Contracts;
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

namespace Strategy.Modules.Cap;

/// <summary>Corrective Action Plan — sources, remarks, actions, validation, implementation, integrations (SPEC §9, docs/design/cap.md).</summary>
public sealed class CapModule : IModule
{
    public string Name => "cap";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CapDbContext>(CapDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(CapModule).Assembly);
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.CapDemoPack>();
        services.AddAccessCatalogue(new CapAccessCatalogue());
        services.AddNumberingFormat(CapAccessCatalogue.SourceNumbering);
        services.AddNumberingFormat(CapAccessCatalogue.RemarkNumbering);
        foreach (var lookup in CapAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in CapNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<CapReader>();
        services.AddScoped<CapDeadlines>();
        services.AddScoped<CapNotifier>();
        services.AddScoped<CapService>();
        services.AddScoped<CapViews>();
        services.AddScoped<ActionViews>();
        services.AddScoped<CapApprovals>();
        services.AddScoped<SuggestionRaiser>();
        services.AddScoped<ICapRemarkSources, CapRemarkSources>();

        services.AddApprovalProcess(CapProcessDefinitions.Validation);
        services.AddApprovalProcess(CapProcessDefinitions.Completion);
        services.AddApprovalProcess(CapProcessDefinitions.Extension);
        services.AddApprovalHook<CapApprovalHook>();
        services.AddCurrentStatusProvider<RemarkCurrentStatus>();
        services.AddCurrentStatusProvider<ActionCurrentStatus>();
        services.AddMyWorkSource<CapMyWork>();
        services.AddReportingMetricSource<CapMetricSource>();
        services.AddReportingListSource<CapListSource>();
        services.AddReportingDatasetSource<CapDatasetSource>();
        services.AddMyWorkCardSource<CapMyWorkCards>();

        services.AddFilePurpose(CapFilePurposes.SourceDocument);
        services.AddFilePurpose(CapFilePurposes.RootCause);
        services.AddFilePurpose(CapFilePurposes.Evidence);
        services.AddFileOwnerAccess<SourceFiles>();
        services.AddFileOwnerAccess<RemarkFiles>();
        services.AddFileOwnerAccess<ActionFiles>();

        services.AddLinkableType(SourceLinkTargets.Definition);
        services.AddLinkTargetSource<SourceLinkTargets>();
        services.AddLinkableType(RemarkLinkTargets.Definition);
        services.AddLinkTargetSource<RemarkLinkTargets>();
        services.AddLinkableType(ActionLinkTargets.Definition);
        services.AddLinkTargetSource<ActionLinkTargets>();

        services.AddIntegrationEventHandler<KpiTargetMissed, KpiTargetMissedHandler>();
        services.AddIntegrationEventHandler<RiskEventLogged, RiskEventLoggedHandler>();
        services.AddIntegrationEventHandler<RiskControlRatedIneffective, RiskControlIneffectiveHandler>();
        services.AddScoped<CapMonitorRunner>();
        services.AddScheduledJob(CapMonitorJob.Definition);

        foreach (var feature in CapAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(CapModule).Assembly);
        services.AddScoped<CapAiService>();
        services.AddRecordSearchSource<RemarkSearchSource>();
        services.AddAppTool<CapMyRemarksTool>();
        services.AddAppTool<CapMyActionsTool>();
        services.AddAppTool<CapRemarksBySourceTool>();
        services.AddAppTool<CapOverdueActionsTool>();
        services.AddAppTool<CapUpdateAchievementDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CapEndpoints.Map(endpoints.MapModuleGroup(this));
}
