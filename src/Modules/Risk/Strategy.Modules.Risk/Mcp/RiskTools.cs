using System.Text.Json;
using Strategy.Modules.Risk.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Risk.Mcp;

/// <summary>
/// MCP tools of Risk Management (SPEC §3.9, ADR 0106). They run as the token owner through the same handlers as the UI (same scopes). The write tool
/// records a draft progress note only and is off by default; no approve tool exists.
/// </summary>
internal static class RiskToolResults
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

    public static object Risk(RiskListItemDto r) => new
    {
        riskId = r.Id,
        number = r.Number,
        title = r.TitleEn ?? r.TitleAr,
        r.Status,
        unit = r.Unit?.NameEn ?? r.Unit?.NameAr,
        owner = r.Owner?.NameEn ?? r.Owner?.NameAr,
        inherent = new { score = r.InherentScore, band = r.InherentBand },
        residual = new { score = r.ResidualScore, band = r.ResidualBand, status = r.ResidualStatus },
        target = r.TargetScore is null ? null : new { score = r.TargetScore, band = r.TargetBand },
        r.WithinAppetite,
        r.Top10,
        r.NextReviewOn,
        r.ReviewOverdue,
        link = $"/risk/risks/{r.Id:D}",
    };

    public static RiskFilter Filter(JsonElement arguments) => new(arguments.Id("unitId"), arguments.Text("category"), null,
        arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null,
        arguments.TryGetProperty("includeProjectRisks", out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null);
}

internal sealed class RiskRegisterSearchTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "risk_register_search", RiskPermissions.SystemCode, ToolKind.Read, "البحث في سجل المخاطر", "Risk register search",
        "Searches the enterprise risk register the user may see by number, title or event text, with optional view (mine, my-unit, top10, outside-appetite, overdue-review, escalated), residual band and status filters. Returns inherent, residual and target ratings.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"search":{"type":"string","maxLength":200},"view":{"type":"string","maxLength":40},
              "residualBand":{"type":"string","maxLength":40},"status":{"type":"string","maxLength":40},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}
            """),
        RiskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var size = arguments.TryGetProperty("pageSize", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetInt32(), 1, 100) : 25;
        var result = await dispatcher.SendAsync(new ListRisks(new PageRequest(1, size), arguments.Text("view"), arguments.Text("search"), null, null, arguments.Text("status"),
            arguments.Text("residualBand"), null, null), cancellationToken).ConfigureAwait(false);
        return RiskToolResults.From(result, page => new { total = page.TotalCount, risks = page.Items.Select(RiskToolResults.Risk) });
    }
}

internal sealed class RiskDetailsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "risk_details", RiskPermissions.SystemCode, ToolKind.Read, "تفاصيل خطر", "Risk details",
        "Returns one risk: statement (cause, event, consequence), ratings, controls, treatment actions and key risk indicators.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["riskId"],"properties":{"riskId":{"type":"string","format":"uuid"}}}"""),
        RiskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("riskId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "riskId is required.");
        }

        var risk = await dispatcher.SendAsync(new GetRisk(id), cancellationToken).ConfigureAwait(false);
        if (risk.IsFailure)
        {
            return RiskToolResults.From(risk, _ => null);
        }

        var controls = await dispatcher.SendAsync(new ListControls(id), cancellationToken).ConfigureAwait(false);
        var actions = await dispatcher.SendAsync(new ListActions(id), cancellationToken).ConfigureAwait(false);
        var kris = await dispatcher.SendAsync(new ListKris(id), cancellationToken).ConfigureAwait(false);
        var r = risk.Value;
        return ToolResult.Ok(new
        {
            riskId = r.Id,
            r.Number,
            title = r.Values.TitleEn ?? r.Values.TitleAr,
            r.Status,
            cause = r.Values.Cause,
            @event = r.Values.EventText,
            consequence = r.Values.Consequence,
            categories = r.Values.CategoryCodes,
            unit = r.Unit?.NameEn ?? r.Unit?.NameAr,
            owner = r.Owner?.NameEn ?? r.Owner?.NameAr,
            inherent = new { r.Values.InherentLikelihood, r.Values.InherentImpact, score = r.InherentScore, band = r.InherentBand },
            residual = new { score = r.ResidualScore, band = r.ResidualBand, status = r.ResidualStatus, controlEffectiveness = r.ControlEffectiveness },
            target = new { score = r.TargetScore, band = r.TargetBand, date = r.Values.TargetDate },
            r.WithinAppetite,
            r.Top10,
            r.Escalated,
            r.NextReviewOn,
            controls = controls.IsSuccess ? controls.Value.Where(c => c.Status == "active").Select(c => new { name = c.Values.NameEn ?? c.Values.NameAr, c.Values.EffectivenessCode }) : null,
            actions = actions.IsSuccess ? actions.Value.Select(a => new { a.Number, title = a.Values.TitleEn ?? a.Values.TitleAr, a.Status, a.CompletionPct, a.Values.PlannedEnd, a.Overdue }) : null,
            kris = kris.IsSuccess ? kris.Value.Select(k => new { kpi = k.CardCode, k.BreachThreshold, k.Direction, k.LastValue, k.LastLevel }) : null,
            link = $"/risk/risks/{r.Id:D}",
        });
    }
}

internal sealed class MyRiskActionsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_risk_actions", RiskPermissions.SystemCode, ToolKind.Read, "إجراءات المعالجة الخاصة بي", "My risk treatment actions",
        "Lists the treatment actions the signed-in user owns, with their risk, planned end, completion and whether they are overdue.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"includeCompleted":{"type":"boolean"}}}"""),
        RiskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var completed = arguments.TryGetProperty("includeCompleted", out var c) && c.ValueKind == JsonValueKind.True;
        var result = await dispatcher.SendAsync(new ListMyActions(completed), cancellationToken).ConfigureAwait(false);
        return RiskToolResults.From(result, items => new
        {
            total = items.Count,
            actions = items.Select(a => new
            {
                actionId = a.Id, a.Number, title = a.Values.TitleEn ?? a.Values.TitleAr, risk = a.RiskNumber, a.Values.PlannedEnd, a.CompletionPct, a.Status, a.Overdue,
                link = $"/risk/my-actions?action={a.Id:D}",
            }),
        });
    }
}

internal sealed class RiskHeatmapSummaryTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "risk_heatmap_summary", RiskPermissions.SystemCode, ToolKind.Read, "ملخص خريطة المخاطر الحرارية", "Risk heat-map summary",
        "Returns the inherent and residual heat-maps (risk count per likelihood × impact cell with its band), counts by residual rating and category, optionally including project risks.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"unitId":{"type":"string","format":"uuid"},"category":{"type":"string","maxLength":100},"year":{"type":"integer"},
              "includeProjectRisks":{"type":"boolean"}}}
            """),
        RiskPermissions.DashboardRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new GetRiskDashboard(RiskToolResults.Filter(arguments)), cancellationToken).ConfigureAwait(false);
        return RiskToolResults.From(result, d => new
        {
            d.Total,
            d.Active,
            d.ProjectRisks,
            byResidualRating = d.ByResidualBand,
            byCategory = d.ByCategory,
            inherent = d.Inherent.Where(c => c.Enterprise + c.Project > 0).Select(c => new { c.Likelihood, c.Impact, c.Band, c.Enterprise, c.Project }),
            residual = d.Residual.Where(c => c.Enterprise > 0).Select(c => new { c.Likelihood, c.Impact, c.Band, count = c.Enterprise }),
        });
    }
}

internal sealed class RiskTop10Tool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "risk_top10", RiskPermissions.SystemCode, ToolKind.Read, "أهم المخاطر المؤسسية", "Top-10 institutional risks",
        "Returns the Top-10 institutional risks the user may see, ranked as the Risk Office set them, then by residual score.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""),
        RiskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRisks(new PageRequest(1, 50), "top10", null, null, null, null, null, null, null), cancellationToken).ConfigureAwait(false);
        return RiskToolResults.From(result, page => new { risks = page.Items.Select(RiskToolResults.Risk) });
    }
}

internal sealed class RisksOutsideAppetiteTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "risks_outside_appetite", RiskPermissions.SystemCode, ToolKind.Read, "المخاطر خارج الرغبة في المخاطر", "Risks outside appetite",
        "Lists active risks whose residual score is above the appetite of their category.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{}}"""),
        RiskPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ListRisks(new PageRequest(1, 100), "outside-appetite", null, null, null, null, null, null, null), cancellationToken)
            .ConfigureAwait(false);
        return RiskToolResults.From(result, page => new { total = page.TotalCount, risks = page.Items.Select(RiskToolResults.Risk) });
    }
}

internal sealed class UpdateRiskActionProgressDraftTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "update_risk_action_progress_draft", RiskPermissions.SystemCode, ToolKind.Write, "مسودة تحديث تقدم إجراء معالجة", "Draft a treatment action progress update",
        "Records a progress note with a proposed completion percentage (0–100) on one of the user's treatment actions as a DRAFT. It does not change the action: the owner confirms the update in the application.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["actionId","note","completionPercent"],"properties":{
              "actionId":{"type":"string","format":"uuid"},"note":{"type":"string","maxLength":4000},"completionPercent":{"type":"number","minimum":0,"maximum":100}}}
            """),
        RiskPermissions.ActionUpdate,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("actionId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "actionId is required.");
        }

        var mine = await dispatcher.SendAsync(new ListMyActions(true), cancellationToken).ConfigureAwait(false);
        if (mine.IsFailure || mine.Value.FirstOrDefault(a => a.Id == id) is not { } action)
        {
            return ToolResult.NotFound();
        }

        var pct = arguments.TryGetProperty("completionPercent", out var p) && p.ValueKind == JsonValueKind.Number ? Math.Clamp(p.GetDecimal(), 0, 100) / 100m : action.CompletionPct;
        var saved = await dispatcher.SendAsync(new UpdateActionProgress(id, new ProgressInput(pct, arguments.Text("note"), null, null, action.Version, FromMcp: true)),
            cancellationToken).ConfigureAwait(false);
        return RiskToolResults.From(saved, a => new { actionId = a.Id, a.Number, draft = true, proposedCompletion = pct, link = $"/risk/my-actions?action={a.Id:D}" });
    }
}
