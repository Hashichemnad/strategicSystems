using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Ai;
using Strategy.Modules.Risk.Api;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Modules.Risk.Jobs;
using Strategy.Modules.Risk.Mcp;
using Strategy.Modules.Risk.Reporting;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Integration.Webhooks;
using Strategy.Platform.Kernel;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Search;
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
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.RiskDemoPack>();
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
        services.AddResponsibilitySource<RiskOwnerResponsibility>(); // Settings → Responsibilities (ADR 0168)
        services.AddResponsibilitySource<RiskActionOwnerResponsibility>();
        services.AddResponsibilitySource<RiskControlOwnerResponsibility>();
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
        services.AddReportingDimensionLabelSource<RiskDimensionLabels>();
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
        services.AddSearchContributor<RiskSearchContributor>(RiskPermissions.SystemCode); // global search (ADR 0160)

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

        // Phase 22: analytics feeds and extracts (ADR 0190).
        services.AddReportableRecordSource<Feeds.RiskFeeds>();

        // Phase 22: webhook events (ADR 0194), all about the risk. risk.event.logged is not offered: risk events are no linkable type, so a
        // subscriber's read access to the event could not be checked per record.
        var sampleRisk = Guid.Parse("0190a4f0-0000-7000-8000-000000000021");
        var sampleUnit = Guid.Parse("0190a4f0-0000-7000-8000-000000000022");
        services.AddWebhookEvent<RiskRatingChanged>(
            "risk.risk.rating-changed", RiskPermissions.SystemCode, "تغيّر تصنيف خطر", "Risk rating changed", RiskRecord.EntityType, RiskPermissions.Read,
            e => e.RiskId,
            new RiskRatingChanged(sampleRisk, "SPMO26-R004", "medium", 9m, "high", 15m, sampleUnit, new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero)));
        services.AddWebhookEvent<RiskKriBreached>(
            "risk.kri.breached", RiskPermissions.SystemCode, "تجاوز حدّ مؤشر خطر رئيسي", "Key risk indicator breached", RiskRecord.EntityType, RiskPermissions.Read,
            e => e.RiskId,
            new RiskKriBreached(sampleRisk, "SPMO26-R004", Guid.Parse("0190a4f0-0000-7000-8000-000000000023"), Guid.Parse("0190a4f0-0000-7000-8000-000000000024"),
                42m, 40m, "above", new DateTimeOffset(2026, 7, 15, 9, 5, 0, TimeSpan.Zero)));
        services.AddWebhookEvent<RiskControlRatedIneffective>(
            "risk.control.ineffective", RiskPermissions.SystemCode, "تقييم ضابط رقابي بأنه غير فعّال", "Risk control rated ineffective", RiskRecord.EntityType,
            RiskPermissions.Read, e => e.RiskId,
            new RiskControlRatedIneffective(Guid.Parse("0190a4f0-0000-7000-8000-000000000025"), sampleRisk, "SPMO26-R004", "مراجعة الصلاحيات الدورية",
                "Periodic access review", sampleUnit, Guid.Parse("0190a4f0-0000-7000-8000-000000000026"), new DateTimeOffset(2026, 5, 10, 7, 45, 0, TimeSpan.Zero)));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => RiskEndpoints.Map(endpoints.MapModuleGroup(this));
}
