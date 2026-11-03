using System.Text.Json;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Workflow.Application;

namespace Strategy.Platform.Workflow.Mcp;

/// <summary><c>my_pending_approvals</c>: the approvals waiting for the token owner (own and delegated), as on My Work.</summary>
internal sealed class MyPendingApprovalsTool(ApprovalQueries queries) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_pending_approvals", "workflow", ToolKind.Read, "الموافقات المعلقة لدي", "My pending approvals",
        "Lists the approval tasks waiting for the signed-in user (including those delegated to them), overdue first, with links.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var work = await queries.MyWorkAsync(cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new
        {
            work.Counts.PendingApprovals,
            work.Counts.Overdue,
            approvals = work.Approvals.Select(a => new
            {
                a.TaskId,
                a.ProcessNameEn,
                a.ProcessNameAr,
                a.ResourceType,
                id = a.ResourceId,
                a.Number,
                a.TitleAr,
                a.TitleEn,
                a.StepNameEn,
                a.StepNameAr,
                a.SubmitterName,
                a.SubmittedAt,
                a.DueAt,
                a.Overdue,
                onBehalfOf = a.OnBehalfOf?.NameEn,
                a.Attempt,
                link = a.Link,
            }),
        });
    }
}

/// <summary><c>record_status</c>: where a record stands and who acts next (the record's current-status provider; not found outside scope).</summary>
internal sealed class RecordStatusTool(ApprovalQueries queries) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "record_status", "workflow", ToolKind.Read, "حالة سجل", "Record status",
        "Returns the current status of one record: the status message, who must act next and the actions available to the signed-in user.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "resourceType":{"type":"string","description":"Record type, e.g. sample.item"},
              "id":{"type":"string","description":"Record id (GUID)"}},
             "required":["resourceType","id"]}
            """));

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Text("resourceType") is not { } type || arguments.Id("id") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "resourceType and id are required.");
        }

        var status = await queries.StatusAsync(type, id, cancellationToken).ConfigureAwait(false);
        if (status.IsFailure)
        {
            return ToolResult.NotFound();
        }

        var value = status.Value;
        return ToolResult.Ok(new
        {
            resourceType = type,
            id,
            value.MessageKey,
            parameters = value.Params.ToDictionary(p => p.Key, p => context.Language == "ar" ? p.Value.Ar ?? p.Value.En : p.Value.En ?? p.Value.Ar),
            value.Stage,
            value.Tone,
            actors = value.Actors.Select(a => context.Language == "ar" ? a.NameAr ?? a.NameEn : a.NameEn ?? a.NameAr),
            nextActions = value.NextActions.Select(a => a.Code),
        });
    }
}

/// <summary>
/// <c>approval_decide</c>: approve, reject or return a task assigned to the token owner. An <b>approve</b> tool: never enabled by default (SPEC
/// §0.5); an administrator may enable it in Settings → MCP, and the engine still checks the caller is an approver of the task.
/// </summary>
internal sealed class ApprovalDecideTool(ApprovalEngine engine) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "approval_decide", "workflow", ToolKind.Approve, "اتخاذ قرار الموافقة", "Decide on an approval",
        "Records the signed-in user's own decision on one of their approval tasks (approve, reject or return). Only when the user explicitly asks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{
              "taskId":{"type":"string","description":"Task id from my_pending_approvals"},
              "action":{"type":"string","description":"approve, reject, return or a custom decision code"},
              "comment":{"type":"string","maxLength":2000}},
             "required":["taskId","action"]}
            """),
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("taskId") is not { } taskId || arguments.Text("action") is not { Length: > 0 } action)
        {
            return ToolResult.Fail(ToolErrors.Validation, "taskId and action are required.");
        }

        var result = await engine.ActAsync(taskId, new ActInput(action, arguments.Text("comment"), null, null), cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return ToolResult.Ok(result.Value);
        }

        return result.Error.Kind switch
        {
            ErrorKind.NotFound => ToolResult.NotFound(),
            ErrorKind.Forbidden => ToolResult.Fail(ToolErrors.Forbidden, result.Error.MessageKey),
            _ => ToolResult.Fail(ToolErrors.Validation, result.Error.MessageKey),
        };
    }
}
