using System.Text.Json;
using Strategy.Modules.Cap.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Cap.Mcp;

/// <summary>
/// MCP tools of CAP (SPEC §9.8, ADR 0129). They run as the token owner through the same handlers as the UI (same scopes). The write tool stores a
/// draft achievement update only and is off by default; no approve tool exists.
/// </summary>
internal static class CapToolResults
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

    public static object Remark(RemarkListItemDto r) => new
    {
        remarkId = r.Id,
        number = r.Number,
        text = r.TextEn ?? r.TextAr,
        source = r.SourceNumber,
        sourceType = r.SourceTypeCode,
        r.Priority,
        r.Status,
        r.Stage,
        department = r.Department?.NameEn ?? r.Department?.NameAr,
        owner = r.Owner?.NameEn ?? r.Owner?.NameAr,
        r.DueOn,
        r.Overdue,
        actions = r.Actions,
        completedActions = r.CompletedActions,
        link = $"/cap/remarks/{r.Id:D}",
    };

    public static object Action(ActionListItemDto a) => new
    {
        actionId = a.Id,
        number = a.Number,
        details = a.DetailsEn ?? a.DetailsAr,
        remark = a.HomeRemarkNumber,
        a.ValidationStatus,
        a.Status,
        achievedPercent = Math.Round(a.AchievedPct * 100, 0),
        endDate = a.EffectiveEnd,
        assignedTo = a.Assignee?.NameEn ?? a.Assignee?.NameAr,
        programRequest = a.ProgramRequestNumber,
        link = $"/cap/actions/{a.Id:D}",
    };

    public static int Size(JsonElement arguments) =>
        arguments.TryGetProperty("pageSize", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetInt32(), 1, 100) : 25;
}

internal sealed class CapMyRemarksTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "cap_my_remarks", CapPermissions.SystemCode, ToolKind.Read, "ملاحظاتي في خطة العمل", "My CAP remarks",
        "Lists the corrective-action-plan remarks the user owns (to accept, plan or follow), with status, stage, priority, due date and action counts.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"view":{"type":"string","enum":["mine","to-accept","my-unit","overdue"]},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}"""),
        CapPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRemarks(new PageRequest(1, CapToolResults.Size(arguments)), arguments.Text("view") ?? "mine", null, null, null, null, null,
            null), cancellationToken).ConfigureAwait(false);
        return CapToolResults.From(result, page => new { total = page.TotalCount, remarks = page.Items.Select(CapToolResults.Remark) });
    }
}

internal sealed class CapMyActionsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "cap_my_actions", CapPermissions.SystemCode, ToolKind.Read, "إجراءاتي التصحيحية", "My corrective actions",
        "Lists the approved corrective actions assigned to the user with status (not started, in progress, late, completed), achievement and end date.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"pageSize":{"type":"integer","minimum":1,"maximum":100}}}"""),
        CapPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListActions(new PageRequest(1, CapToolResults.Size(arguments)), "mine", null, null, null), cancellationToken).ConfigureAwait(false);
        return CapToolResults.From(result, page => new { total = page.TotalCount, actions = page.Items.Select(CapToolResults.Action) });
    }
}

internal sealed class CapRemarksBySourceTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "cap_remarks_by_source", CapPermissions.SystemCode, ToolKind.Read, "الملاحظات حسب المصدر", "Remarks by source",
        "Lists the remarks the user may see for a source (by source id) or a source type (workshop, benchmarking, internal-audit, kpi-performance, risk-event…).",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"sourceId":{"type":"string","format":"uuid"},"sourceType":{"type":"string","maxLength":60},"search":{"type":"string","maxLength":200},
              "pageSize":{"type":"integer","minimum":1,"maximum":100}}}
            """),
        CapPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRemarks(new PageRequest(1, CapToolResults.Size(arguments)), "all", arguments.Text("search"), arguments.Id("sourceId"), null,
            null, null, arguments.Text("sourceType")), cancellationToken).ConfigureAwait(false);
        return CapToolResults.From(result, page => new { total = page.TotalCount, remarks = page.Items.Select(CapToolResults.Remark) });
    }
}

internal sealed class CapOverdueActionsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "cap_overdue_actions", CapPermissions.SystemCode, ToolKind.Read, "الإجراءات المتأخرة", "Overdue corrective actions",
        "Lists late corrective actions the user may see (past their end date below 100 %), with the remark, assignee and end date.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"pageSize":{"type":"integer","minimum":1,"maximum":100}}}"""),
        CapPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListActions(new PageRequest(1, CapToolResults.Size(arguments)), "late", null, null, null), cancellationToken).ConfigureAwait(false);
        return CapToolResults.From(result, page => new { total = page.TotalCount, actions = page.Items.Select(CapToolResults.Action) });
    }
}

internal sealed class CapUpdateAchievementDraftTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "cap_update_achievement_draft", CapPermissions.SystemCode, ToolKind.Write, "مسودة تحديث إنجاز إجراء", "Draft an achievement update",
        "Records a progress note with a proposed achievement percentage (0–100) on one of the user's corrective actions as a DRAFT. It does not change the action: the assignee confirms the update in the application.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["actionId","note","achievedPercent"],"properties":{
              "actionId":{"type":"string","format":"uuid"},"note":{"type":"string","maxLength":4000},"achievedPercent":{"type":"number","minimum":0,"maximum":100}}}
            """),
        CapPermissions.ActionUpdate,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("actionId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "actionId is required.");
        }

        var action = await dispatcher.SendAsync(new GetAction(id), cancellationToken).ConfigureAwait(false);
        if (action.IsFailure)
        {
            return CapToolResults.From(action, _ => null);
        }

        var pct = arguments.TryGetProperty("achievedPercent", out var p) && p.ValueKind == JsonValueKind.Number ? Math.Clamp(p.GetDecimal(), 0, 100) / 100m : action.Value.AchievedPct;
        var saved = await dispatcher.SendAsync(new UpdateAchievement(id, new ProgressInput(pct, arguments.Text("note"), null, null, action.Value.Version, FromMcp: true)),
            cancellationToken).ConfigureAwait(false);
        return CapToolResults.From(saved, a => new { actionId = a.Id, a.Number, draft = true, proposedPercent = pct * 100, link = $"/cap/actions/{a.Id:D}" });
    }
}
