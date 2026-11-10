using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.AuditManagement.Ai;
using Strategy.Modules.AuditManagement.Api;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.AuditManagement.Jobs;
using Strategy.Modules.AuditManagement.Mcp;
using Strategy.Modules.AuditManagement.Reporting;
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

namespace Strategy.Modules.AuditManagement;

/// <summary>Audit Management — findings and follow-up (Layer 1) and the switchable audit lifecycle (Layer 2). SPEC §10, docs/design/audit.md.</summary>
public sealed class AuditMgmtModule : IModule
{
    public string Name => "auditmgmt";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditMgmtDbContext>(AuditMgmtDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(AuditMgmtModule).Assembly);
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.AuditDemoPack>();
        services.AddAccessCatalogue(new AuditAccessCatalogue());
        foreach (var numbering in AuditAccessCatalogue.Numbering)
        {
            services.AddNumberingFormat(numbering);
        }

        foreach (var lookup in AuditAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in AuditNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddFeature(AuditAccessCatalogue.LifecycleFeature);
        services.AddScoped<AuditReader>();
        services.AddScoped<AuditDeadlines>();
        services.AddScoped<AuditNotifier>();
        services.AddScoped<AuditService>();
        services.AddScoped<AuditViews>();
        services.AddScoped<AuditQueries>();
        services.AddScoped<AuditImporter>();
        services.AddScoped<AuditLifecycle>();

        services.AddApprovalProcess(AuditProcessDefinitions.FollowUpReview);
        services.AddApprovalProcess(AuditProcessDefinitions.Plan);
        services.AddApprovalHook<AuditApprovalHook>();
        services.AddCurrentStatusProvider<FindingCurrentStatus>();
        services.AddMyWorkSource<AuditMyWork>();
        services.AddReportingMetricSource<AuditMetricSource>();
        services.AddReportingListSource<AuditListSource>();
        services.AddReportingDatasetSource<AuditDatasetSource>();
        services.AddMyWorkCardSource<AuditMyWorkCards>();

        services.AddFilePurpose(AuditFilePurposes.ReportFile);
        services.AddFilePurpose(AuditFilePurposes.Evidence);
        services.AddFilePurpose(AuditFilePurposes.WorkPaper);
        services.AddFileOwnerAccess<ReportFiles>();
        services.AddFileOwnerAccess<FollowUpFiles>();
        services.AddFileOwnerAccess<WorkPaperFiles>();

        services.AddLinkableType(FindingLinkTargets.Definition);
        services.AddLinkTargetSource<FindingLinkTargets>();
        services.AddLinkableType(ReportLinkTargets.Definition);
        services.AddLinkTargetSource<ReportLinkTargets>();
        services.AddSearchContributor<AuditSearchContributor>(AuditPermissions.SystemCode); // global search (ADR 0160)
        services.AddResponsibilitySource<ReportLeadAuditorResponsibility>(); // Settings → Responsibilities (ADR 0168)
        services.AddResponsibilitySource<FindingAuditorResponsibility>();

        services.AddScoped<AuditMonitorRunner>();
        services.AddScheduledJob(AuditMonitorJob.Definition);

        foreach (var feature in AuditAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(AuditMgmtModule).Assembly);
        services.AddScoped<AuditAiService>();
        services.AddRecordSearchSource<FindingSearchSource>();
        services.AddAppTool<AuditFindingsByDepartmentTool>();
        services.AddAppTool<AuditFindingDetailsTool>();
        services.AddAppTool<AuditFollowUpsDueTool>();
        services.AddAppTool<AuditOverdueFindingsTool>();
        services.AddAppTool<AuditMyFindingStatusTool>();
        services.AddAppTool<AuditRecordFollowUpDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuditEndpoints.Map(endpoints.MapModuleGroup(this));
}
