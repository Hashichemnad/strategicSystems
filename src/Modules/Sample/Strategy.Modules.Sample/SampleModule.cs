using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Sample.Ai;
using Strategy.Modules.Sample.Api;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Contracts;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Links;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting;
using Strategy.Platform.Search;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Sample;

/// <summary>Template module (docs/design/sample.md). Copy this shape for a new system.</summary>
public sealed class SampleModule : IModule
{
    public string Name => "sample";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SampleDbContext>(SampleDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(SampleModule).Assembly);
        services.AddScoped<ISampleItemDirectory, SampleItemDirectory>();
        services.AddScoped<Reporting.SampleReporting>();
        services.AddScoped<IReportingMetricSource>(sp => sp.GetRequiredService<Reporting.SampleReporting>());
        services.AddScoped<IReportingListSource>(sp => sp.GetRequiredService<Reporting.SampleReporting>());
        services.AddScoped<IReportingDatasetSource>(sp => sp.GetRequiredService<Reporting.SampleReporting>());
        services.AddAccessCatalogue(new SampleAccessCatalogue());
        services.AddNumberingFormat(SampleAccessCatalogue.Numbering);
        services.AddFilePurpose(SampleAttachments.Purpose);
        services.AddFileOwnerAccess<SampleAttachments>();
        services.AddNotificationTemplate(SampleNotifications.EditorAdded);
        services.AddApprovalProcess(SampleApprovalProcess.Definition);
        services.AddApprovalHook<SampleApprovalHook>();
        services.AddCurrentStatusProvider<SampleCurrentStatus>();
        services.AddMyWorkSource<SampleMyWork>();

        // Phase 5: AI form, record source (approver brief, get_record) and search source (search_records, similar records).
        services.AddAiForm(SampleAiForm.Definition);
        services.AddAiRecordSource<SampleRecordSource>();
        services.AddRecordSearchSource<SampleSearchSource>();

        // Phase 6: sample items are task-able (links registry, ADR 0034).
        services.AddLinkableType(SampleLinks.Definition);
        services.AddLinkTargetSource<SampleLinkTargets>();

        // Phase 19: global search (ADR 0160).
        services.AddSearchContributor<SampleSearchContributor>("sample");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SampleEndpoints.Map(endpoints.MapModuleGroup(this));
}
