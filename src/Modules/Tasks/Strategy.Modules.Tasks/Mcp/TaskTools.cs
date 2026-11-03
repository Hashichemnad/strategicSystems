using System.Text.Json;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Tasks.Mcp;

/// <summary>
/// MCP tools of Task Management (SPEC §11.10, ADR 0031). They run as the token owner through the same handlers as the UI, so the same access and
/// visibility rules apply (confidential tasks included). Write tools are off by default; the approve tool is never enabled by default.
/// </summary>
internal static class TaskToolResults
{
    public static ToolResult From<T>(Result<T> result, Func<T, object?> map)
    {
        if (result.IsSuccess)
        {
            return ToolResult.Ok(map(result.Value));
        }

        return result.Error.Kind switch
        {
            ErrorKind.NotFound => ToolResult.NotFound(),
            ErrorKind.Forbidden => ToolResult.Fail(ToolErrors.Forbidden, result.Error.MessageKey),
            _ => ToolResult.Fail(ToolErrors.Validation, result.Error.Fields is { Count: > 0 } fields
                ? result.Error.MessageKey + ": " + string.Join(", ", fields.Select(f => $"{f.Field} {f.Code}"))
                : result.Error.MessageKey),
        };
    }

    public static object Summary(TaskListItemDto t) => new
    {
        t.Id,
        t.Number,
        t.TitleAr,
        t.TitleEn,
        t.Status,
        t.Priority,
        t.Indicator,
        t.DueDate,
        assignee = t.Assignee.NameEn ?? t.Assignee.NameAr,
        creator = t.Creator.NameEn ?? t.Creator.NameAr,
        actions = t.Actions,
        link = $"/tasks/{t.Id:D}",
    };
}

internal sealed class MyTasksTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_tasks", TaskPermissions.SystemCode, ToolKind.Read, "مهامي", "My tasks",
        "Lists the signed-in user's tasks: view 'assigned' (default), 'created', 'follow-up' or 'team'; optional status and indicator ('overdue', 'due_soon').",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "view":{"type":"string","enum":["assigned","created","follow-up","team"]},
              "status":{"type":"string","description":"Status code, e.g. assigned, in_progress, submitted"},
              "indicator":{"type":"string","enum":["overdue","due_soon","on_hold","on_time"]},
              "openOnly":{"type":"boolean","description":"Only open tasks (default true)"}}}
            """),
        TaskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListTasks(
            new PageRequest(1, 50, "dueDate"), arguments.Text("view") ?? TaskViews.Assigned, arguments.Text("status"), null, arguments.Text("indicator"),
            OpenOnly: arguments.Flag("openOnly") ?? true), cancellationToken).ConfigureAwait(false);
        return TaskToolResults.From(result, page => new { total = page.TotalCount, tasks = page.Items.Select(TaskToolResults.Summary) });
    }
}

internal sealed class CreateTaskTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "create_task", TaskPermissions.SystemCode, ToolKind.Write, "إنشاء مهمة", "Create a task",
        "Creates a task for one or several employees (one task each, tied by an assignment group). Only when the user explicitly asks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "title":{"type":"string","maxLength":300},
              "description":{"type":"string","maxLength":8000},
              "assigneeEmployeeIds":{"type":"array","items":{"type":"string"},"maxItems":50},
              "typeCode":{"type":"string","description":"Task type code (default manager-assignment)"},
              "sourceCode":{"type":"string","description":"Task source code (default manager-assignment)"},
              "priority":{"type":"string","enum":["high","medium","low"]},
              "dueDate":{"type":"string","description":"YYYY-MM-DD"}},
             "required":["title","assigneeEmployeeIds"]}
            """),
        TaskPermissions.Create, DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var ids = arguments.TryGetProperty("assigneeEmployeeIds", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(e => Guid.TryParse(e.GetString(), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList()
            : [];
        var title = arguments.Text("title");
        var arabic = title is not null && title.Any(c => c is >= '؀' and <= 'ۿ');
        var result = await dispatcher.SendAsync(new CreateTask(
            arabic ? title : null, arabic ? null : title, arguments.Text("description"), arguments.Text("typeCode") ?? "manager-assignment",
            arguments.Text("sourceCode") ?? "manager-assignment", null, arguments.Text("priority"), null, null,
            DateOnly.TryParse(arguments.Text("dueDate"), System.Globalization.CultureInfo.InvariantCulture, out var due) ? due : null, null, ids), cancellationToken)
            .ConfigureAwait(false);
        return TaskToolResults.From(result, created => new { created.GroupId, tasks = created.Tasks.Select(TaskToolResults.Summary) });
    }
}

internal sealed class UpdateTaskStatusTool(IDispatcher dispatcher) : IAppTool
{
    private static readonly string[] Allowed = [TaskActions.Accept, TaskActions.Reject, TaskActions.Start, TaskActions.Pause, TaskActions.Submit, TaskActions.Complete];

    public ToolDefinition Definition { get; } = new(
        "update_task_status", TaskPermissions.SystemCode, ToolKind.Write, "تحديث حالة مهمة", "Update a task's status",
        "Accepts, rejects, starts, pauses, submits or completes one of the signed-in user's tasks. Reject and pause need a reason. Only when the user asks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "taskId":{"type":"string"},
              "action":{"type":"string","enum":["accept","reject","start","pause","submit","complete"]},
              "reason":{"type":"string","maxLength":2000},
              "comment":{"type":"string","maxLength":4000}},
             "required":["taskId","action"]}
            """),
        TaskPermissions.Read, DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("taskId") is not { } id || arguments.Text("action") is not { } action || !Allowed.Contains(action))
        {
            return ToolResult.Fail(ToolErrors.Validation, "taskId and a supported action are required.");
        }

        return await TaskActTool.ActAsync(dispatcher, id, action, arguments, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ApproveTaskTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "approve_task", TaskPermissions.SystemCode, ToolKind.Approve, "اعتماد مهمة", "Approve a task",
        "Approves a submitted task the signed-in user created, with a feedback point. Only when the user explicitly asks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "taskId":{"type":"string"},
              "feedbackPoint":{"type":"integer","minimum":1,"maximum":10},
              "comment":{"type":"string","maxLength":2000}},
             "required":["taskId","feedbackPoint"]}
            """),
        TaskPermissions.Read, DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) =>
        arguments.Id("taskId") is { } id
            ? await TaskActTool.ActAsync(dispatcher, id, TaskActions.Approve, arguments, cancellationToken).ConfigureAwait(false)
            : ToolResult.Fail(ToolErrors.Validation, "taskId is required.");
}

internal sealed class AddTaskCommentTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "add_task_comment", TaskPermissions.SystemCode, ToolKind.Write, "إضافة تعليق على مهمة", "Comment on a task",
        "Adds a comment to a task the signed-in user may comment on. Only when the user asks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"taskId":{"type":"string"},"text":{"type":"string","maxLength":4000}},"required":["taskId","text"]}
            """),
        TaskPermissions.Read, DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("taskId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "taskId is required.");
        }

        var result = await dispatcher.SendAsync(new AddTaskComment(id, arguments.Text("text"), null, null), cancellationToken).ConfigureAwait(false);
        return TaskToolResults.From(result, c => new { c.Id, c.CreatedAt });
    }
}

internal static class TaskActTool
{
    /// <summary>Reads the current version, then acts (the tool has no version of its own; a concurrent change still answers a conflict).</summary>
    public static async Task<ToolResult> ActAsync(IDispatcher dispatcher, Guid id, string action, JsonElement arguments, CancellationToken ct)
    {
        var task = await dispatcher.SendAsync(new GetTask(id), ct).ConfigureAwait(false);
        if (task.IsFailure)
        {
            return ToolResult.NotFound();
        }

        var result = await dispatcher.SendAsync(new ActOnTask(id, action, task.Value.Version, arguments.Text("reason"), arguments.Text("comment"),
            arguments.Number("feedbackPoint")), ct).ConfigureAwait(false);
        return TaskToolResults.From(result, t => new { t.Id, t.Number, t.Status, link = $"/tasks/{t.Id:D}" });
    }
}
