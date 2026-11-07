using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Tasks.Ai;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Audit;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.Modules.Tasks.Api;

/// <summary>
/// <c>/api/v1/tasks</c> (docs/design/tasks.md §9). Endpoints declare the permission needed to use them at all; handlers check the task itself
/// (404 outside <see cref="TaskVisibility"/>, 403 for a visible task and a forbidden action).
/// </summary>
internal static class TasksEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).WithName("ListTasks").RequirePermission(TaskPermissions.Read);
        group.MapGet("/counts", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTaskCounts(), ct)).ToOk())
            .WithName("GetTaskCounts").RequirePermission(TaskPermissions.Read);
        group.MapPost("/", async Task<Results<Created<CreatedTasksDto>, ProblemHttpResult>> (CreateTask body, IDispatcher d, CancellationToken ct) =>
            {
                var result = await d.SendAsync(body, ct);
                return result.IsSuccess ? TypedResults.Created("/api/v1/tasks", result.Value) : result.Error.ToProblem();
            })
            .WithName("CreateTask").RequirePermission(TaskPermissions.Create).WithIdempotency();
        group.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTask(id), ct)).ToOk())
            .WithName("GetTask").RequirePermission(TaskPermissions.Read);
        group.MapPut("/{id:guid}", async (Guid id, UpdateTaskInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateTask(id, b.TitleAr, b.TitleEn, b.Description, b.SourceCode, b.CategoryCode, b.Priority, b.Confidential, b.PlannedStart,
                    b.DueDate, b.EstimatedHours, b.PercentComplete, b.Version), ct)).ToOk())
            .WithName("UpdateTask").RequirePermission(TaskPermissions.Read);

        foreach (var (action, name) in new[]
                 {
                     (TaskActions.Assign, "AssignTask"), (TaskActions.Accept, "AcceptTask"), (TaskActions.Reject, "RejectTask"), (TaskActions.Pause, "PauseTask"),
                     (TaskActions.Start, "StartTask"), (TaskActions.Submit, "SubmitTask"), (TaskActions.Complete, "CompleteTask"),
                     (TaskActions.Approve, "ApproveTask"), (TaskActions.Return, "ReturnTask"), (TaskActions.Reassign, "ReassignTask"), (TaskActions.Cancel, "CancelTask"),
                 })
        {
            var endpoint = group.MapPost($"/{{id:guid}}/{action}", async (Guid id, TaskActionInput b, IDispatcher d, CancellationToken ct) =>
                    (await d.SendAsync(new ActOnTask(id, action, b.Version, b.Reason, b.Comment, b.FeedbackPoint, b.AssigneeEmployeeId), ct)).ToOk())
                .WithName(name).RequirePermission(TaskPermissions.Read);
            if (action is TaskActions.Submit or TaskActions.Approve or TaskActions.Complete)
            {
                endpoint.WithIdempotency();
            }
        }

        group.MapPost("/{id:guid}/subtasks", async (Guid id, SubtaskInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CreateSubtask(id, b.TitleAr, b.TitleEn, b.Description, b.AssigneeEmployeeId, b.Priority, b.PlannedStart, b.DueDate), ct)).ToOk())
            .WithName("CreateSubtask").RequirePermission(TaskPermissions.Read);
        group.MapGet("/{id:guid}/timeline", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTaskTimeline(id), ct)).ToOk())
            .WithName("GetTaskTimeline").RequirePermission(TaskPermissions.Read);
        group.MapGet("/{id:guid}/comments", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new ListTaskComments(id), ct)).ToOk())
            .WithName("ListTaskComments").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/comments", async (Guid id, CommentInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AddTaskComment(id, b.Text, b.Mentions, b.FileIds), ct)).ToOk())
            .WithName("AddTaskComment").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/follow-ups", async (Guid id, FollowUpInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AddFollowUp(id, b.EmployeeId, b.CanUpload ?? true), ct)).ToOk())
            .WithName("AddTaskFollowUp").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/follow-ups/{employeeId:guid}/remove", async (Guid id, Guid employeeId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RemoveFollowUp(id, employeeId), ct)).ToOk())
            .WithName("RemoveTaskFollowUp").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/checklist", async (Guid id, ChecklistInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AddChecklistItem(id, b.Text), ct)).ToOk())
            .WithName("AddTaskChecklistItem").RequirePermission(TaskPermissions.Read);
        group.MapPut("/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, ChecklistUpdateInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new UpdateChecklistItem(id, itemId, b.Text, b.Done), ct)).ToOk())
            .WithName("UpdateTaskChecklistItem").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/checklist/{itemId:guid}/remove", async (Guid id, Guid itemId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RemoveChecklistItem(id, itemId), ct)).ToOk())
            .WithName("RemoveTaskChecklistItem").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/dependencies", async (Guid id, DependencyInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new AddDependency(id, b.DependsOnTaskId), ct)).ToOk())
            .WithName("AddTaskDependency").RequirePermission(TaskPermissions.Read);
        group.MapPost("/{id:guid}/dependencies/{dependsOnId:guid}/remove", async (Guid id, Guid dependsOnId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new RemoveDependency(id, dependsOnId), ct)).ToOk())
            .WithName("RemoveTaskDependency").RequirePermission(TaskPermissions.Read);

        group.MapRecordHistory(TaskItem.EntityType, TaskPermissions.Read, "GetTask", async (id, context) =>
            (await context.RequestServices.GetRequiredService<IDispatcher>().SendAsync(new GetTask(id), context.RequestAborted)).IsSuccess);

        var groups = group.MapGroup("/groups");
        groups.MapGet("/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetAssignmentGroup(id), ct)).ToOk())
            .WithName("GetAssignmentGroup").RequirePermission(TaskPermissions.Read);
        groups.MapPost("/{id:guid}/message", async (Guid id, GroupMessageInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new MessageGroup(id, b.Text), ct)).ToOk())
            .WithName("MessageAssignmentGroup").RequirePermission(TaskPermissions.Read);
        groups.MapPost("/{id:guid}/extend-due", async (Guid id, GroupDueInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new ExtendGroupDue(id, b.DueDate), ct)).ToOk())
            .WithName("ExtendAssignmentGroupDue").RequirePermission(TaskPermissions.Read);
        groups.MapPost("/{id:guid}/cancel", async (Guid id, GroupCancelInput b, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new CancelGroup(id, b.Reason), ct)).ToOk())
            .WithName("CancelAssignmentGroup").RequirePermission(TaskPermissions.Read);

        group.MapGet("/dashboard", async (DateOnly? from, DateOnly? to, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetTaskDashboard(from, to), ct)).ToOk())
            .WithName("GetTaskDashboard").RequirePermission(TaskPermissions.DashboardRead);
        group.MapGet("/performance/{employeeId:guid}", async (Guid employeeId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new GetEmployeePerformance(employeeId), ct)).ToOk())
            .WithName("GetEmployeeTaskPerformance").RequirePermission(TaskPermissions.Read);
        group.MapGet("/settings", async (IDispatcher d, CancellationToken ct) => (await d.SendAsync(new GetTaskSettings(), ct)).ToOk())
            .WithName("GetTaskSettings").RequirePermission(TaskPermissions.SettingsManage);
        group.MapPut("/settings", async (SaveTaskSettings b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(b, ct)).ToOk())
            .WithName("SaveTaskSettings").RequirePermission(TaskPermissions.SettingsManage);

        // Administration: reassign the open tasks of an employee who leaves or moves (ADR 0040).
        group.MapGet("/reassignment/{employeeId:guid}", async (Guid employeeId, IDispatcher d, CancellationToken ct) =>
                (await d.SendAsync(new PreviewReassignment(employeeId), ct)).ToOk())
            .WithName("PreviewTaskReassignment").RequirePermission(TaskPermissions.ReassignmentManage);
        group.MapPost("/reassignment", async (ReassignEmployeeTasks b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(b, ct)).ToOk())
            .WithName("ReassignEmployeeTasks").RequirePermission(TaskPermissions.ReassignmentManage).WithIdempotency();
        group.MapPost("/reassignment/creator", async (TransferCreatorRole b, IDispatcher d, CancellationToken ct) => (await d.SendAsync(b, ct)).ToOk())
            .WithName("TransferTaskCreatorRole").RequirePermission(TaskPermissions.ReassignmentManage).WithIdempotency();

        var ai = group.MapGroup("/ai");
        ai.MapPost("/from-text", async (FromTextInput b, TaskAiService service, CancellationToken ct) => (await service.DraftFromTextAsync(b.Text, ct)).ToOk())
            .WithName("DraftTasksFromText").RequirePermission(TaskPermissions.Create);
        ai.MapPost("/suggest-assignees", async (SuggestInput b, TaskAiService service, CancellationToken ct) =>
                (await service.SuggestAssigneesAsync(b.Title, b.UnitId, ct)).ToOk())
            .WithName("SuggestTaskAssignees").RequirePermission(TaskPermissions.Create);
        ai.MapGet("/workload", async (TaskAiService service, CancellationToken ct) => (await service.WorkloadAsync(ct)).ToOk())
            .WithName("GetTaskWorkloadInsights").RequirePermission(TaskPermissions.DashboardRead);
        group.MapPost("/{id:guid}/ai/summary", async (Guid id, SummaryInput b, TaskAiService service, CancellationToken ct) =>
                (await service.SummariseAsync(id, b.Mode, ct)).ToOk())
            .WithName("SummariseTask").RequirePermission(TaskPermissions.Read);
    }

    private static async Task<Results<Ok<PagedResult<TaskListItemDto>>, ProblemHttpResult>> ListAsync(
        int? page, int? pageSize, string? sort, string? view, string? status, string? priority, string? indicator, string? search, string? linkedType, Guid? linkedId,
        Guid? groupId, Guid? parentId, DateOnly? dueFrom, DateOnly? dueTo, bool? openOnly, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        (await dispatcher.SendAsync(new ListTasks(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), view, status, priority, indicator, search,
            linkedType, linkedId, groupId, parentId, dueFrom, dueTo, openOnly ?? false), cancellationToken)).ToOk();
}

internal sealed record UpdateTaskInput(
    string? TitleAr, string? TitleEn, string? Description, string? SourceCode, string? CategoryCode, string? Priority, bool Confidential, DateOnly? PlannedStart,
    DateOnly? DueDate, decimal? EstimatedHours, int? PercentComplete, uint Version);

internal sealed record TaskActionInput(uint Version, string? Reason = null, string? Comment = null, int? FeedbackPoint = null, Guid? AssigneeEmployeeId = null);

internal sealed record SubtaskInput(string? TitleAr, string? TitleEn, string? Description, Guid? AssigneeEmployeeId, string? Priority, DateOnly? PlannedStart, DateOnly? DueDate);

internal sealed record CommentInput(string? Text, IReadOnlyList<Guid>? Mentions, IReadOnlyList<Guid>? FileIds);

internal sealed record FollowUpInput(Guid? EmployeeId, bool? CanUpload);

internal sealed record ChecklistInput(string? Text);

internal sealed record ChecklistUpdateInput(string? Text, bool? Done);

internal sealed record DependencyInput(Guid? DependsOnTaskId);

internal sealed record GroupMessageInput(string? Text);

internal sealed record GroupDueInput(DateOnly? DueDate);

internal sealed record GroupCancelInput(string? Reason);

internal sealed record FromTextInput(string? Text);

internal sealed record SuggestInput(string? Title, Guid? UnitId);

internal sealed record SummaryInput(string? Mode);
