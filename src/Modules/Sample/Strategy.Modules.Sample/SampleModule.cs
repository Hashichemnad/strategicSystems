using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Sample.Ai;
using Strategy.Modules.Sample.Api;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Contracts;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
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
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SampleEndpoints.Map(endpoints.MapModuleGroup(this));
}
