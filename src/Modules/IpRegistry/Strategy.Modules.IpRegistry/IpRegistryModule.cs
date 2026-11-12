using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.IpRegistry.Ai;
using Strategy.Modules.IpRegistry.Api;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Modules.IpRegistry.Jobs;
using Strategy.Modules.IpRegistry.Mcp;
using Strategy.Modules.IpRegistry.Reporting;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.IpRegistry;

/// <summary>Intellectual Property Registry on the Request Framework (SPEC §7, docs/design/requests.md §4).</summary>
public sealed class IpRegistryModule : IModule
{
    public string Name => "ip";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IpDbContext>(IpDbContext.SchemaName);
        services.AddAccessCatalogue(new IpAccessCatalogue());
        services.AddNumberingFormat(IpAccessCatalogue.RecordNumbering);
        services.AddNumberingFormat(IpAccessCatalogue.InternalNumbering);
        foreach (var lookup in IpAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in IpNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddRequestType(IpRequestTypes.RegistrationType);
        services.AddRequestTypeHandler<IpRegistrationHandler>();
        services.AddApprovalProcess(IpRequestTypes.ReviewDefinition);

        services.AddScoped<IpReader>();
        services.AddScoped<EmiratesIds>();
        services.AddScoped<IpNotifier>();
        services.AddIpSyncTargets(); // Phase 23, ADR 0202
        services.AddScoped<IpService>();
        services.AddReportingMetricSource<IpMetricSource>();
        services.AddReportingListSource<IpListSource>();
        services.AddReportingDatasetSource<IpDatasetSource>();
        services.AddIntegrationEventHandler<DocumentPublished, DocumentPublishedHandler>();
        services.AddFilePurpose(IpFiles.Documents);
        services.AddFileOwnerAccess<IpFiles>();
        services.AddLinkableType(IpLinks.Definition);
        services.AddLinkTargetSource<IpLinkTargets>();
        services.AddSearchContributor<IpSearchContributor>(IpPermissions.SystemCode); // global search (ADR 0160)
        services.AddScoped<IpSearchSource>();
        services.AddScoped<IRecordSearchSource>(sp => sp.GetRequiredService<IpSearchSource>());
        services.AddScoped<RenewalRunner>();
        services.AddScheduledJob(RenewalJob.Definition);

        foreach (var feature in IpAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(IpRegistryModule).Assembly);
        services.AddScoped<IpAiService>();
        services.AddAppTool<SearchIpRecordsTool>();
        services.AddAppTool<GetIpRecordTool>();

        // Phase 22: analytics feeds and extracts (ADR 0190).
        services.AddReportableRecordSource<Feeds.IpFeeds>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => IpEndpoints.Map(endpoints.MapModuleGroup(this));
}
