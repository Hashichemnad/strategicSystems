using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Requests.Ai;
using Strategy.Modules.Requests.Api;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Modules.Requests.Mcp;
using Strategy.Modules.Requests.Reporting;
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

namespace Strategy.Modules.Requests;

/// <summary>Request Framework — request types, form builder, stages and actions, catalogue, My Requests, queues (SPEC §5, docs/design/requests.md).</summary>
public sealed class RequestsModule : IModule
{
    public string Name => "requests";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<RequestsDbContext>(RequestsDbContext.SchemaName);
        services.AddAccessCatalogue(new RequestsAccessCatalogue());
        services.AddNumberingFormat(RequestsAccessCatalogue.Numbering);
        services.AddLookupType(RequestsAccessCatalogue.Categories);
        foreach (var template in RequestNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddSingleton<RequestTypeCache>();
        services.AddSingleton<IApprovalProcessSource>(sp => sp.GetRequiredService<RequestTypeCache>());
        services.AddScoped<RoleMembership>();
        services.AddScoped<RequestReader>();
        services.AddScoped<FormSecrets>();
        services.AddScoped<FormResolverFactory>();
        services.AddScoped<PendingDecision>();
        services.AddScoped<RequestNotifier>();
        services.AddScoped<RequestEngine>();
        services.AddScoped<RequestQueries>();
        services.AddScoped<RequestCommands>();
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.RequestsDemoPack>();
        services.AddScoped<TypeAdministration>();
        services.AddScoped<RequestPdf>();
        services.AddScoped<IRequestDirectory, RequestDirectory>();
        services.AddScoped<IDatabaseSeeder, RequestTypeSeeder>();

        services.AddApprovalHook<RequestApprovalHook>();
        services.AddCurrentStatusProvider<RequestCurrentStatus>();
        services.AddMyWorkSource<RequestsMyWork>();
        services.AddReportingMetricSource<RequestMetricSource>();
        services.AddReportingDatasetSource<RequestDatasetSource>();
        services.AddMyWorkCardSource<RequestMyWorkCards>();
        services.AddFilePurpose(RequestFilePurposes.Attachment);
        services.AddFileOwnerAccess<RequestFiles>();
        services.AddLinkableType(RequestLinks.Definition);
        services.AddLinkTargetSource<RequestLinkTargets>();
        services.AddSearchContributor<RequestSearchContributor>(RequestPermissions.SystemCode); // global search (ADR 0160)
        services.AddRecordSearchSource<RequestSearchSource>();
        services.AddScheduledJob(StageOverdueJob.Definition);

        foreach (var feature in RequestAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(RequestsModule).Assembly);
        services.AddScoped<RequestAiService>();
        services.AddAppTool<MyRequestsTool>();
        services.AddAppTool<RequestStatusTool>();
        services.AddAppTool<SubmitRequestDraftTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => RequestEndpoints.Map(endpoints.MapModuleGroup(this));
}
