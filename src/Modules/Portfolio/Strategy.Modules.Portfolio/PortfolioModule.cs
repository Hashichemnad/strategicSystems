using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Api;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Portfolio.Mcp;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio;

/// <summary>Portfolio Management — Phase 8: program requests up to the automatic Project / Initiative (SPEC §1, docs/design/portfolio.md).</summary>
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
        foreach (var lookup in PortfolioAccessCatalogue.Lookups)
        {
            services.AddLookupType(lookup);
        }

        foreach (var template in PortfolioNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<RequestReader>();
        services.AddScoped<RequestQueries>();
        services.AddScoped<RequestEvaluator>();
        services.AddScoped<RequestFileFlags>();
        services.AddScoped<PortfolioNotifier>();
        services.AddScoped<IPortfolioDirectory, PortfolioDirectory>();

        services.AddApprovalProcess(RequestApprovalProcess.Definition);
        services.AddApprovalHook<RequestApprovalHook>();
        services.AddCurrentStatusProvider<RequestCurrentStatus>();
        services.AddCurrentStatusProvider<ProjectCurrentStatus>();
        services.AddMyWorkSource<RequestMyWork>();

        foreach (var purpose in RequestFilePurposes.OnRequest)
        {
            services.AddFilePurpose(purpose);
        }

        services.AddFilePurpose(RequestFilePurposes.Template);
        services.AddFileOwnerAccess<RequestFiles>();
        services.AddFileOwnerAccess<TemplateFiles>();

        services.AddLinkableType(RequestLinks.Definition);
        services.AddLinkTargetSource<RequestLinkTargets>();

        foreach (var feature in PortfolioAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(PortfolioModule).Assembly);
        services.AddAiForm(RequestAiForm.Definition);
        services.AddAiFormChoiceSource<RequestFormChoices>();
        services.AddScoped<AssessmentService>();
        services.AddAiRecordSource<RequestRecordSource>();
        services.AddRecordSearchSource<RequestSearchSource>();
        services.AddRecordSearchSource<ProjectSearchSource>();
        services.AddAppTool<SearchRequestsTool>();
        services.AddAppTool<MyRequestsTool>();
        services.AddAppTool<GetRequestTool>();
        services.AddAppTool<GetProjectTool>();
        services.AddAppTool<CreateDraftRequestTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PortfolioEndpoints.Map(endpoints.MapModuleGroup(this));
}
