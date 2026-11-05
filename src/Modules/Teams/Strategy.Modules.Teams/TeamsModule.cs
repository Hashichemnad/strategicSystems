using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Teams.Ai;
using Strategy.Modules.Teams.Api;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Contracts;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Modules.Teams.Jobs;
using Strategy.Modules.Teams.Mcp;
using Strategy.Modules.Teams.Reporting;
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

namespace Strategy.Modules.Teams;

/// <summary>Teams Management — teams, committees, evaluation and rewards, shared by every system (SPEC §4, docs/design/teams.md). Always on.</summary>
public sealed class TeamsModule : IModule
{
    public string Name => "teams";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TeamsDbContext>(TeamsDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(TeamsModule).Assembly);
        services.AddAccessCatalogue(new TeamsAccessCatalogue());
        foreach (var numbering in new[]
                 {
                     TeamsAccessCatalogue.TeamNumbering, TeamsAccessCatalogue.MeetingNumbering, TeamsAccessCatalogue.RecommendationNumbering,
                     TeamsAccessCatalogue.EvaluationNumbering, TeamsAccessCatalogue.MemoNumbering, TeamsAccessCatalogue.CertificateNumbering,
                 })
        {
            services.AddNumberingFormat(numbering);
        }

        services.AddLookupType(TeamsAccessCatalogue.ApprovedBy);
        foreach (var template in TeamNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<IDatabaseSeeder, TeamsSeeder>();
        services.AddScoped<TeamReader>();
        services.AddScoped<TeamRoleSync>();
        services.AddScoped<TeamQueries>();
        services.AddScoped<TeamNotifier>();
        services.AddScoped<TeamActivation>();
        services.AddScoped<TeamActivity>();
        services.AddScoped<TeamDeadlines>();
        services.AddScoped<EvaluationOpener>();
        services.AddScoped<EvaluationReader>();
        services.AddScoped<EvaluationReaderFactory>();
        services.AddScoped<ITeamsDirectory, TeamsDirectory>();
        services.AddScoped<ITeamAllocationDirectory, TeamAllocationDirectory>();
        services.AddScoped<ITeamRoleResolver, TeamRoleResolver>();

        services.AddApprovalProcess(FormationApprovalProcess.Definition);
        services.AddApprovalProcess(EvaluationApprovalProcesses.Evaluation);
        services.AddApprovalProcess(EvaluationApprovalProcesses.Reward);
        services.AddApprovalHook<FormationApprovalHook>();
        services.AddApprovalHook<EvaluationApprovalHook>();
        services.AddCurrentStatusProvider<TeamCurrentStatus>();
        services.AddMyWorkSource<TeamMyWork>();

        // Phase 17 — reporting sources (docs/design/reporting.md §2.5, §6, §7).
        services.AddReportingMetricSource<TeamsMetricSource>();
        services.AddReportingDatasetSource<TeamsDatasetSource>();
        services.AddMyWorkCardSource<TeamsMyWorkCards>();

        services.AddFilePurpose(TeamFilePurposes.Documents);
        services.AddFilePurpose(TeamFilePurposes.Generated);
        services.AddFilePurpose(TeamFilePurposes.Evidence);
        services.AddFilePurpose(TeamFilePurposes.Certificate);
        services.AddFileOwnerAccess<TeamFiles>();
        services.AddFileOwnerAccess<EvaluationFiles>();
        services.AddOutboxHandler<RenderMemoHandler>();
        services.AddOutboxHandler<RenderCertificatesHandler>();

        services.AddLinkableType(TeamLinkTargets.Definition);
        services.AddLinkTargetSource<TeamLinkTargets>();
        services.AddLinkableType(RecommendationLinkTargets.Definition);
        services.AddLinkTargetSource<RecommendationLinkTargets>();
        services.AddLinkableType(MeetingLinkTargets.Definition);
        services.AddLinkTargetSource<MeetingLinkTargets>();
        services.AddIntegrationEventHandler<LinkedRecordClosed, OpenEvaluationOnLinkedRecordClosed>();

        services.AddScoped<EvaluationTriggerRunner>();
        services.AddScoped<ReportScheduler>();
        services.AddScheduledJob(EvaluationTriggersJob.Definition);
        services.AddScheduledJob(TeamReportsJob.Definition);

        foreach (var feature in TeamAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(TeamsModule).Assembly);
        services.AddScoped<TeamAiService>();
        services.AddAiRecordSource<TeamRecordSource>();
        services.AddRecordSearchSource<TeamSearchSource>();
        services.AddAppTool<MyTeamsTool>();
        services.AddAppTool<TeamDetailsTool>();
        services.AddAppTool<MyPendingEvaluationsTool>();
        services.AddAppTool<EvaluationStatusTool>();
        services.AddAppTool<AddMeetingMinutesTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TeamsEndpoints.Map(endpoints.MapModuleGroup(this));
}
