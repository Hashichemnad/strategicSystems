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
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
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
        services.AddLinkableType(DocumentLinks.Definition);
        services.AddLinkTargetSource<DocumentLinkTargets>();
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
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => DocRegEndpoints.Map(endpoints.MapModuleGroup(this));
}
