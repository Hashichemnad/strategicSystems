using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Api;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Portfolio.Mcp;
using Strategy.Modules.Teams.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio;

/// <summary>Portfolio Management — Phase 8 program requests and Phase 9 project lifecycle (SPEC §1, docs/design/portfolio.md).</summary>
public sealed class PortfolioModule : IModule
{
    public string Name => "portfolio";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PortfolioDbContext>(PortfolioDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(PortfolioModule).Assembly);
        services.AddAccessCatalogue(new PortfolioAccessCatalogue());
        services.AddNumberingFormat(PortfolioAccessCatalogue.RequestNumbering);
        services.AddNumberingFormat(PortfolioAccessCatalogue.ProjectNumbering);
        services.AddNumberingFormat(PortfolioAccessCatalogue.VendorNumbering);
        services.AddAccessCatalogue(new WaselAccessCatalogue());
        foreach (var lookup in PortfolioAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in PortfolioNotifications.All.Concat(PortfolioNotifications.Wasel))
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<RequestReader>();
        services.AddScoped<RequestQueries>();
        services.AddScoped<RequestEvaluator>();
        services.AddScoped<RequestFileFlags>();
        services.AddScoped<PortfolioNotifier>();
        services.AddScoped<IPortfolioDirectory, PortfolioDirectory>();
        services.AddScoped<StrategicObjectives>();
        services.AddScoped<IDatabaseSeeder, ObjectivesToKpiSeeder>();
        services.AddScoped<ProjectLoader>();
        services.AddScoped<ProjectProgress>();
        services.AddScoped<ProjectNotifier>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectItems>();
        services.AddScoped<MilestoneService>();
        services.AddScoped<MonthlyWindows>();
        services.AddScoped<ChangeRequestService>();
        services.AddScoped<MonthlyCycleRunner>();
        services.AddScoped<IProjectRiskScoring, DefaultProjectRiskScoring>();
        services.AddScoped<IProjectRiskDirectory, ProjectRiskDirectory>();
        services.AddScoped<IKpiLinkedRecordSource, ProjectKpiLinks>();
        services.AddScheduledJob(MonthlyCycleJob.Definition);
        services.AddScoped<CostVisibility>();
        services.AddScoped<ProjectGuard>();
        services.AddScoped<AgileService>();
        services.AddScoped<HealthService>();
        services.AddScoped<BaselineService>();
        services.AddScoped<RaidService>();
        services.AddScoped<RaciService>();
        services.AddScoped<CommunicationService>();
        services.AddScoped<BenefitService>();
        services.AddScoped<ProjectExtrasService>();
        services.AddScoped<TemplateService>();
        services.AddScoped<GroupService>();
        services.AddScoped<PortfolioViewService>();
        services.AddScoped<StatusReportService>();
        services.AddScoped<CommunicationReminderRunner>();
        services.AddScheduledJob(CommunicationReminderJob.Definition);
        services.AddIntegrationEventHandler<TeamMembershipChanged, ProjectTeamMembershipHandler>();

        // Phase 11 — Wasel vendor portal (ADRs 0110–0115) and Portfolio completion (ADR 0116).
        services.AddScoped<WaselContext>();
        services.AddScoped<WaselGrants>();
        services.AddScoped<WaselNotifier>();
        services.AddScoped<VendorRegisterService>();
        services.AddScoped<WaselManagementService>();
        services.AddScoped<VendorPortalService>();
        services.AddScoped<IUserDirectoryLookup, UserDirectoryLookup>();
        services.AddScoped<IVendorDirectory, VendorDirectory>();
        services.AddScoped<Strategy.Platform.Identity.Wasel.IVendorSignInPolicy, VendorSignInPolicy>();
        services.AddScoped<WaselAccessRunner>();
        services.AddScheduledJob(WaselAccessJob.Definition);
        services.AddScoped<VendorReviewAiService>();
        services.AddAiFeature(VendorReviewAiService.Definition);
        services.AddScoped<CapacityService>();
        services.AddScoped<PortfolioReviewService>();
        services.AddFilePurpose(WaselFilePurposes.VendorEvidence);
        services.AddFilePurpose(WaselFilePurposes.Deliverable);
        services.AddFileOwnerAccess<DeliverableFiles>();
        services.AddIntegrationEventHandler<Risk.Contracts.RiskFrameworkChanged, ProjectRiskRescoreHandler>();

        services.AddApprovalProcess(RequestApprovalProcess.Definition);
        foreach (var process in ProjectApprovalProcesses.All)
        {
            services.AddApprovalProcess(process);
        }

        services.AddApprovalHook<ProjectApprovalHook>();
        services.AddMyWorkSource<ProjectMyWork>();
        services.AddApprovalHook<RequestApprovalHook>();
        services.AddCurrentStatusProvider<RequestCurrentStatus>();
        services.AddCurrentStatusProvider<ProjectCurrentStatus>();
        services.AddMyWorkSource<RequestMyWork>();

        foreach (var purpose in RequestFilePurposes.OnRequest)
        {
            services.AddFilePurpose(purpose);
        }

        services.AddFilePurpose(RequestFilePurposes.Template);
        foreach (var purpose in ProjectFilePurposes.OnProject)
        {
            services.AddFilePurpose(purpose);
        }

        services.AddFilePurpose(ProjectFilePurposes.Evidence);
        services.AddFileOwnerAccess<ProjectFiles>();
        services.AddFileOwnerAccess<MilestoneFiles>();
        services.AddFileOwnerAccess<RequestFiles>();
        services.AddFileOwnerAccess<TemplateFiles>();

        services.AddLinkableType(RequestLinks.Definition);
        services.AddLinkTargetSource<RequestLinkTargets>();
        services.AddLinkableType(ProjectLinks.Project);
        services.AddLinkableType(ProjectLinks.Milestone);
        services.AddLinkableType(ProjectLinks.Story);
        services.AddLinkTargetSource<StoryLinkTargets>();
        services.AddLinkTargetSource<ProjectLinkTargets>();
        services.AddLinkTargetSource<MilestoneLinkTargets>();

        foreach (var feature in PortfolioAiFeatures.All.Concat(ProjectAssistFeatures.All))
        {
            services.AddAiFeature(feature);
        }

        services.AddScoped<ProjectAssistService>();

        services.AddAiPromptAssembly(typeof(PortfolioModule).Assembly);
        services.AddAiForm(RequestAiForm.Definition);
        services.AddAiFormChoiceSource<RequestFormChoices>();
        services.AddScoped<AssessmentService>();
        services.AddAiRecordSource<RequestRecordSource>();
        services.AddAiRecordSource<ProjectRecordSource>();
        services.AddRecordSearchSource<RequestSearchSource>();
        services.AddRecordSearchSource<ProjectSearchSource>();
        services.AddAppTool<SearchRequestsTool>();
        services.AddAppTool<MyRequestsTool>();
        services.AddAppTool<GetRequestTool>();
        services.AddAppTool<GetProjectTool>();
        services.AddAppTool<CreateDraftRequestTool>();
        services.AddAppTool<MyProjectsTool>();
        services.AddAppTool<ProjectMilestonesTool>();
        services.AddAppTool<UpdateMilestoneDraftTool>();
        services.AddAppTool<ProjectBacklogTool>();
        services.AddAppTool<SprintSummaryTool>();
        services.AddAppTool<PortfolioSummaryTool>();
        services.AddAppTool<CreateStoryDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PortfolioEndpoints.Map(endpoints.MapModuleGroup(this));
        WaselEndpoints.Map(endpoints);
    }
}
