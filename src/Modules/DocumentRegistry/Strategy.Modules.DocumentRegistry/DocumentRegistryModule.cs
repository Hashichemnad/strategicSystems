using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.DocumentRegistry.Ai;
using Strategy.Modules.DocumentRegistry.Api;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Modules.DocumentRegistry.Jobs;
using Strategy.Modules.DocumentRegistry.Mcp;
using Strategy.Modules.DocumentRegistry.Reporting;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Integration.Webhooks;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.DocumentRegistry;

/// <summary>Document Registry on the Request Framework (SPEC §6, docs/design/requests.md §3).</summary>
public sealed class DocumentRegistryModule : IModule
{
    public string Name => "docreg";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DocRegDbContext>(DocRegDbContext.SchemaName);
        services.AddAccessCatalogue(new DocRegAccessCatalogue());
        services.AddNumberingFormat(DocRegAccessCatalogue.Numbering);
        foreach (var lookup in DocRegAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in DocRegNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddRequestType(DocumentRequestTypes.SubmissionType);
        services.AddRequestType(DocumentRequestTypes.WithdrawalType);
        services.AddRequestTypeHandler<DocumentSubmissionHandler>();
        services.AddRequestTypeHandler<DocumentWithdrawalHandler>();
        services.AddApprovalProcess(DocumentRequestTypes.ReviewDefinition);
        services.AddApprovalProcess(DocumentRequestTypes.WithdrawalDefinition);

        services.AddScoped<DocumentVisibility>();
        services.AddScoped<DocRegCalendar>();
        services.AddScoped<DocRegNotifier>();
        services.AddScoped<DocumentService>();
        services.AddScoped<Watermark>();
        services.AddScoped<DocumentSearchSource>();
        services.AddScoped<IRecordSearchSource>(sp => sp.GetRequiredService<DocumentSearchSource>());
        services.AddScoped<IDocumentDirectory, DocumentDirectory>();
        services.AddMyWorkSource<DocRegMyWork>();
        services.AddReportingMetricSource<DocRegMetricSource>();
        services.AddReportingListSource<DocRegListSource>();
        services.AddReportingDatasetSource<DocRegDatasetSource>();
        services.AddLinkableType(DocumentLinks.Definition);
        services.AddLinkTargetSource<DocumentLinkTargets>();
        services.AddSearchContributor<DocumentSearchContributor>(DocRegPermissions.SystemCode); // global search (ADR 0160)
        services.AddScoped<ValidityRunner>();
        services.AddScheduledJob(ValidityJob.Definition);

        foreach (var feature in DocRegAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(DocumentRegistryModule).Assembly);
        services.AddScoped<DocRegAiService>();
        services.AddAppTool<SearchDocumentsTool>();
        services.AddAppTool<GetDocumentTool>();
        services.AddAppTool<DocumentExpiryListTool>();

        // Phase 22: analytics feeds and extracts (ADR 0190).
        services.AddReportableRecordSource<Feeds.DocRegFeeds>();

        // Phase 22: webhook events (ADR 0194).
        services.AddWebhookEvent<DocumentPublished>("docreg.document.published", DocRegPermissions.SystemCode, "نشر وثيقة", "Document published",
            DocumentResource.Type, DocRegPermissions.Read, e => e.DocumentId,
            new DocumentPublished(Guid.Parse("0190a4f0-0000-7000-8000-000000000011"), Guid.Parse("0190a4f0-0000-7000-8000-000000000012"), "POL-SPMO-001", "1.0",
                "سياسة إدارة المخاطر", "Risk management policy", "سياسة تحدد إطار إدارة المخاطر في الجهة.", "policy",
                Guid.Parse("0190a4f0-0000-7000-8000-000000000013"), Guid.Parse("0190a4f0-0000-7000-8000-000000000014"),
                Guid.Parse("0190a4f0-0000-7000-8000-000000000015"), false, new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero)));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => DocRegEndpoints.Map(endpoints.MapModuleGroup(this));
}
