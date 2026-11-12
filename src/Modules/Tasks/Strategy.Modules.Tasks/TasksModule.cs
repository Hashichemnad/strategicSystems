using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Tasks.Ai;
using Strategy.Modules.Tasks.Api;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Contracts;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Modules.Tasks.Jobs;
using Strategy.Modules.Tasks.Mcp;
using Strategy.Modules.Tasks.Reporting;
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

namespace Strategy.Modules.Tasks;

/// <summary>Task Management — the shared task engine (SPEC §11, docs/design/tasks.md). Always on (ADR 0036).</summary>
public sealed class TasksModule : IModule
{
    public string Name => "tasks";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TasksDbContext>(TasksDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(TasksModule).Assembly);
        services.AddScoped<Strategy.Platform.Kernel.Demo.IDemoPackContributor, Demo.TasksDemoPack>();
        services.AddAccessCatalogue(new TasksAccessCatalogue());
        services.AddNumberingFormat(TasksAccessCatalogue.TaskNumbering);
        services.AddNumberingFormat(TasksAccessCatalogue.TimesheetNumbering);
        services.AddLookupType(TasksAccessCatalogue.Types);
        services.AddLookupType(TasksAccessCatalogue.Sources);
        services.AddLookupType(TasksAccessCatalogue.Categories);
        services.AddTasksSyncTargets(); // Phase 23, ADR 0202
        foreach (var template in TaskNotifications.All)
        {
            services.AddNotificationTemplate(template);
        }

        services.AddScoped<TaskVisibility>();
        services.AddScoped<TaskDeadlines>();
        services.AddScoped<TaskReader>();
        services.AddScoped<TaskNotifier>();
        services.AddScoped<TaskAssignees>();
        services.AddScoped<TaskCreator>();
        services.AddScoped<ITaskDirectory, TaskDirectory>();
        services.AddScoped<Platform.Organisation.Contracts.IEmployeeLifecycleHook, OfferReassignmentWhenEmployeeLeaves>();

        services.AddFilePurpose(TaskFiles.Purpose);
        services.AddFileOwnerAccess<TaskFiles>();
        services.AddCurrentStatusProvider<TaskCurrentStatus>();
        services.AddMyWorkSource<TaskMyWork>();
        services.AddReportingMetricSource<TaskMetricSource>();
        services.AddReportingListSource<TaskListSource>();
        services.AddReportingDatasetSource<TaskDatasetSource>();
        services.AddMyWorkCardSource<TaskMyWorkCards>();
        services.AddLinkableType(TaskLinkTargets.Definition);
        services.AddLinkTargetSource<TaskLinkTargets>();
        services.AddSearchContributor<TaskSearchContributor>(TaskPermissions.SystemCode); // global search (ADR 0160)
        services.AddResponsibilitySource<TaskAssigneeResponsibility>(); // Settings → Responsibilities (ADR 0168)

        services.AddScoped<TaskReminders>();
        services.AddScoped<TaskRecurrence>();
        services.AddScoped<TaskDigest>();
        services.AddScheduledJob(TaskRemindersJob.Definition);
        services.AddScheduledJob(TaskRecurrenceJob.Definition);
        services.AddScheduledJob(TaskDigestJob.Definition);

        // AI features and tools register through the Phase 5 registries (ADR 0028, 0031).
        foreach (var feature in TaskAiFeatures.All)
        {
            services.AddAiFeature(feature);
        }

        services.AddAiPromptAssembly(typeof(TasksModule).Assembly);
        services.AddScoped<TaskAiService>();
        services.AddAiRecordSource<TaskRecordSource>();
        services.AddRecordSearchSource<TaskSearchSource>();
        services.AddAppTool<MyTasksTool>();
        services.AddAppTool<CreateTaskTool>();
        services.AddAppTool<UpdateTaskStatusTool>();
        services.AddAppTool<AddTaskCommentTool>();
        services.AddAppTool<ApproveTaskTool>();

        // Phase 22: analytics feeds and extracts (ADR 0190).
        services.AddReportableRecordSource<Feeds.TaskFeeds>();

        // Phase 22: webhook events (ADR 0194).
        services.AddWebhookEvent<TaskCompleted>(
            "tasks.task.completed", TaskPermissions.SystemCode, "إنجاز مهمة", "Task completed", Domain.TaskItem.EntityType, TaskPermissions.Read, e => e.TaskId,
            new TaskCompleted(
                Guid.Parse("0190a4f0-0000-7000-8000-000000000021"), "TASK-00042", Domain.TaskStatuses.Submitted, Domain.TaskStatuses.Approved, "portfolio.project",
                Guid.Parse("0190a4f0-0000-7000-8000-000000000022"), Guid.Parse("0190a4f0-0000-7000-8000-000000000023"), true,
                new DateTimeOffset(2026, 7, 2, 13, 45, 0, TimeSpan.Zero)));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TasksEndpoints.Map(endpoints.MapModuleGroup(this));
}
