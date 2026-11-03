using Strategy.Platform.AI.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Workflow.Api;
using Strategy.Platform.Workflow.Application;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow;

/// <summary>Approval engine (<c>/api/v1/workflow</c>, schema <c>workflow</c>). docs/design/workflow.md.</summary>
public sealed class WorkflowComponent : IPlatformComponent
{
    public string Name => "workflow";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppTool<Mcp.MyPendingApprovalsTool>();
        services.AddAppTool<Mcp.RecordStatusTool>();
        services.AddAppTool<Mcp.ApprovalDecideTool>();
        services.AddModuleDbContext<WorkflowDbContext>(WorkflowDbContext.SchemaName);
        services.AddSingleton<ProcessRegistry>();
        services.AddScoped<OrgRelations>();
        services.AddScoped<ApproverResolver>();
        services.AddScoped<StepValidator>();
        services.AddScoped<ApprovalEngine>();
        services.AddScoped<IApprovalEngine>(sp => sp.GetRequiredService<ApprovalEngine>());
        services.AddScoped<ApprovalQueries>();
        services.AddScoped<ProcessAdministration>();
        services.AddScoped<SlaEscalation>();
        services.AddScoped<IRoleTransferHook, ApprovalRoleTransferHook>();
        services.AddScoped<IDatabaseSeeder, WorkflowSeeder>();
        services.AddScheduledJob(SlaEscalationJob.Definition);
        services.AddScheduledJob(IdempotencyCleanupJob.Definition);
        services.AddAccessCatalogue(new WorkflowCatalogue());
        foreach (var template in WorkflowNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }
    }

    public void UseMiddleware(IApplicationBuilder app) => app.UseMiddleware<IdempotencyMiddleware>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => WorkflowEndpoints.Map(endpoints.MapPlatformGroup(this));
}
