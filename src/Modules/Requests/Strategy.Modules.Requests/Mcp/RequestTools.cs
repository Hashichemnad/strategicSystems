using System.Text.Json;
using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Requests.Mcp;

/// <summary>
/// MCP tools available to every request type (SPEC §5.1, ADR 0031): read tools on; the write tool saves a <b>draft</b> only, never submits, and
/// is off by default. No approve tool.
/// </summary>
internal static class RequestToolResults
{
    public static ToolResult From<T>(Result<T> result, Func<T, object?> map) => result.IsSuccess
        ? ToolResult.Ok(map(result.Value))
        : result.Error.Kind switch
        {
            ErrorKind.NotFound => ToolResult.NotFound(),
            ErrorKind.Forbidden => ToolResult.Fail(ToolErrors.Forbidden, result.Error.MessageKey),
            _ => ToolResult.Fail(ToolErrors.Validation, result.Error.Fields is { Count: > 0 } fields
                ? result.Error.MessageKey + ": " + string.Join(", ", fields.Select(f => $"{f.Field} {f.Code}"))
                : result.Error.MessageKey),
        };

    public static object Item(RequestListItemDto r) => new
    {
        requestId = r.Id,
        r.Number,
        type = r.TypeNameEn ?? r.TypeNameAr ?? r.TypeCode,
        title = r.TitleEn ?? r.TitleAr,
        r.Status,
        stage = r.StageNameEn ?? r.StageNameAr ?? r.Stage,
        r.SubmittedAt,
        dueAt = r.StageDueAt,
        r.Overdue,
        link = $"/requests/{r.Id:D}",
    };
}

internal sealed class MyRequestsTool(RequestQueries queries) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_requests", RequestPermissions.SystemCode, ToolKind.Read, "طلباتي", "My requests",
        "Lists the signed-in user's requests of every type (documents, intellectual property, custom requests) with status, stage and due date.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"status":{"type":"string"},"type":{"type":"string"}}}"""),
        RequestPermissions.Submit);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var page = await queries.MineAsync(new PageRequest(1, 50), arguments.Text("status"), arguments.Text("type"), null, cancellationToken).ConfigureAwait(false);
        return ToolResult.Ok(new { total = page.TotalCount, requests = page.Items.Select(RequestToolResults.Item) });
    }
}

internal sealed class RequestStatusTool(RequestQueries queries) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "request_status", RequestPermissions.SystemCode, ToolKind.Read, "حالة طلب", "Request status",
        "Returns one request the user may see: status, current stage, who acts next, timeline. Secret values are masked.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"requestId":{"type":"string","format":"uuid"}},"required":["requestId"]}"""),
        RequestPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("requestId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "requestId is required.");
        }

        var result = await queries.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return RequestToolResults.From(result, r => new
        {
            requestId = r.Id,
            r.Number,
            type = r.TypeNameEn ?? r.TypeNameAr,
            title = r.TitleEn ?? r.TitleAr,
            r.Status,
            r.Stage,
            stages = r.Stages.Select(s => new { s.Code, name = s.NameEn ?? s.NameAr, s.State }),
            approvers = r.Approval?.Approvers.Select(a => a.NameEn ?? a.NameAr),
            r.StageDueAt,
            r.Outcome,
            values = r.Data,
            timeline = r.Events.Select(e => new { e.Action, e.ToStatus, e.ToStage, e.ActorName, e.At }),
            link = $"/requests/{r.Id:D}",
        });
    }
}

internal sealed class SubmitRequestDraftTool(RequestCommands commands, RequestQueries queries) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "submit_request_draft", RequestPermissions.SystemCode, ToolKind.Write, "مسودة طلب", "Request draft",
        "Creates (or updates, with requestId and version) a DRAFT request of a type from the catalogue with form values keyed by field code. "
        + "It never submits: the requester reviews and submits it in the application.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"type":{"type":"string"},"requestId":{"type":"string","format":"uuid"},"version":{"type":"integer"},"values":{"type":"object"}}}"""),
        RequestPermissions.Submit, DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var values = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(v.GetRawText()) as JsonObject
            : null;
        Result<RequestDto> result;
        if (arguments.Id("requestId") is { } id)
        {
            var version = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("version", out var ver) && ver.TryGetUInt32(out var n) ? n : 0u;
            result = await commands.SaveAsync(id, new SaveInput(values, version), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await commands.CreateAsync(new DraftInput(arguments.Text("type"), values, "mcp"), cancellationToken).ConfigureAwait(false);
        }

        _ = queries;
        return RequestToolResults.From(result, r => new { requestId = r.Id, r.Number, r.Status, version = r.Version, link = $"/requests/{r.Id:D}" });
    }
}
