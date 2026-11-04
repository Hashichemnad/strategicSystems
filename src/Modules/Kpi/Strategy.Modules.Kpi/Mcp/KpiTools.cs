using System.Text.Json;
using Strategy.Modules.Kpi.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Kpi.Mcp;

/// <summary>
/// MCP tools of Performance Management (SPEC §2.9, ADR 0031). They run as the token owner through the same handlers as the UI (same scopes). The
/// write tool saves a draft only and is off by default; no approve tool exists.
/// </summary>
internal static class KpiToolResults
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

    public static object Result(KpiResultListItemDto r) => new
    {
        resultId = r.Id,
        kpi = r.CardCode,
        name = r.CardNameEn ?? r.CardNameAr,
        r.Year,
        period = r.PeriodCode.ToUpperInvariant(),
        target = r.TargetType == "range" ? $"{r.RangeMin} – {r.RangeMax}" : r.Target?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        unit = r.UnitCode,
        r.Achieve,
        performance = r.Capped,
        r.Rag,
        r.Status,
        r.DueOn,
        r.Overdue,
        link = $"/kpi/results/{r.Id:D}",
    };
}

internal sealed class MyPendingKpiResultsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "my_pending_kpi_results", KpiPermissions.SystemCode, ToolKind.Read, "نتائج مؤشراتي المعلقة", "My pending KPI results",
        "Lists the signed-in user's KPI results that are open for update, returned for correction or waiting for approval, with targets and due dates.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"year":{"type":"integer"}}}"""),
        KpiPermissions.ResultSubmit);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        var result = await dispatcher.SendAsync(new ListMyPendingKpiResults(null, year), cancellationToken).ConfigureAwait(false);
        return KpiToolResults.From(result, items => new { total = items.Count, results = items.Select(KpiToolResults.Result) });
    }
}

internal sealed class KpiCardLookupTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "kpi_card_lookup", KpiPermissions.SystemCode, ToolKind.Read, "البحث عن بطاقة مؤشر", "KPI card lookup",
        "Finds KPI cards by code or name (or returns one card by id) with unit, period, direction, owner and this year's performance (capped and actual).",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"search":{"type":"string","maxLength":200},"cardId":{"type":"string","format":"uuid"},"year":{"type":"integer"}}}"""),
        KpiPermissions.CardRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        if (arguments.Id("cardId") is { } id)
        {
            var card = await dispatcher.SendAsync(new GetKpiCard(id), cancellationToken).ConfigureAwait(false);
            var performance = card.IsSuccess ? await dispatcher.SendAsync(new GetKpiPerformance(id, year), cancellationToken).ConfigureAwait(false) : null;
            return KpiToolResults.From(card, c => new
            {
                c.Id,
                c.Code,
                c.Values.NameAr,
                c.Values.NameEn,
                c.Status,
                c.Values.MeasuringEquation,
                unit = c.Values.UnitCode,
                period = c.Values.MeasuringPeriod,
                direction = c.Values.Direction,
                responsibleUnit = c.ResponsibleUnit?.NameEn ?? c.ResponsibleUnit?.NameAr,
                owner = c.ResponsiblePerson?.NameEn ?? c.ResponsiblePerson?.NameAr,
                objective = c.ObjectiveNameEn ?? c.ObjectiveNameAr,
                years = performance?.IsSuccess == true ? performance.Value.Years.Select(p => new { p.Year, p.Target, p.Achieved, capped = p.Capped, actual = p.Actual, p.Rag }) : null,
                link = $"/kpi/cards/{c.Id:D}",
            });
        }

        var page = await dispatcher.SendAsync(new ListKpiCards(new PageRequest(1, 25), arguments.Text("search"), null, null, null, null, null, null, year), cancellationToken)
            .ConfigureAwait(false);
        return KpiToolResults.From(page, p => new
        {
            total = p.TotalCount,
            cards = p.Items.Select(c => new
            {
                c.Id,
                c.Code,
                name = c.NameEn ?? c.NameAr,
                c.Status,
                unit = c.UnitCode,
                period = c.MeasuringPeriod,
                c.Year,
                c.Target,
                c.Achieved,
                capped = c.Capped,
                actual = c.Actual,
                c.Rag,
                link = $"/kpi/cards/{c.Id:D}",
            }),
        });
    }
}

internal sealed class DepartmentScorecardTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "department_scorecard", KpiPermissions.SystemCode, ToolKind.Read, "بطاقة أداء الإدارة", "Department scorecard",
        "Returns the weighted scorecard (capped performance) of a department and below, or of the organisation when no unit is given, with perspectives, objectives and KPIs.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"unitId":{"type":"string","format":"uuid"},"year":{"type":"integer"}}}"""),
        KpiPermissions.ScorecardRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        var result = await dispatcher.SendAsync(new GetKpiScorecard(year, arguments.Id("unitId")), cancellationToken).ConfigureAwait(false);
        static object Node(KpiScoreNodeDto n) => new { n.Kind, n.Code, name = n.NameEn ?? n.NameAr, n.Weight, score = n.Score, n.Rag, n.KpiCount, children = n.Children.Select(Node) };
        return KpiToolResults.From(result, s => new
        {
            s.Year,
            unit = s.Unit?.NameEn ?? s.Unit?.NameAr,
            scorecard = Node(s.Root),
            departments = s.Departments.Select(d => new { unit = d.Unit.NameEn ?? d.Unit.NameAr, score = d.Score, d.Rag, d.KpiCount, d.Reported }),
        });
    }
}

internal sealed class ApprovalQueueSummaryTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "kpi_approval_queue_summary", KpiPermissions.SystemCode, ToolKind.Read, "ملخص قائمة اعتماد النتائج", "KPI approval queue summary",
        "Summarises the KPI results waiting for approval that the signed-in user can see: counts by department and period, late and anomalous results, and those waiting for the user's own decision.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"year":{"type":"integer"}}}"""),
        KpiPermissions.ResultRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        var result = await dispatcher.SendAsync(new ListKpiApprovalQueue(year, null, null, false), cancellationToken).ConfigureAwait(false);
        return KpiToolResults.From(result, items => new
        {
            total = items.Count,
            mine = items.Count(i => i.MyDecision),
            late = items.Count(i => i.Late),
            withAnomalies = items.Count(i => i.Anomalies.Count > 0),
            withoutEvidence = items.Count(i => i.EvidenceCount == 0 && !i.NotApplicable),
            byDepartment = items.GroupBy(i => i.OwnerUnit?.NameEn ?? i.OwnerUnit?.NameAr ?? "-").Select(g => new { department = g.Key, count = g.Count() }),
            byPeriod = items.GroupBy(i => $"{i.PeriodCode.ToUpperInvariant()} {i.Year}").Select(g => new { period = g.Key, count = g.Count() }),
            results = items.Take(30).Select(KpiToolResults.Result),
        });
    }
}

internal sealed class SubmitKpiResultDraftTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "submit_kpi_result_draft", KpiPermissions.SystemCode, ToolKind.Write, "حفظ مسودة نتيجة مؤشر", "Save a KPI result draft",
        "Saves the achieved value, causality, corrective actions and notes on one of the user's open KPI results as a DRAFT. It never submits for approval: the owner reviews and submits in the application.",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","required":["resultId"],"properties":{
              "resultId":{"type":"string","format":"uuid"},"achieve":{"type":"number"},"causality":{"type":"string","maxLength":4000},
              "correctiveActions":{"type":"string","maxLength":4000},"notes":{"type":"string","maxLength":4000}}}
            """),
        KpiPermissions.ResultSubmit,
        DefaultEnabled: false);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("resultId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "resultId is required.");
        }

        var current = await dispatcher.SendAsync(new GetKpiResult(id), cancellationToken).ConfigureAwait(false);
        if (current.IsFailure)
        {
            return KpiToolResults.From(current, _ => null);
        }

        decimal? achieve = arguments.TryGetProperty("achieve", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : current.Value.Achieve;
        var entry = new KpiEntryInput(achieve, current.Value.ElementValues, current.Value.ScaleValue, arguments.Text("causality") ?? current.Value.Causality,
            arguments.Text("correctiveActions") ?? current.Value.CorrectiveActions, arguments.Text("notes") ?? current.Value.Notes, false, null);
        var saved = await dispatcher.SendAsync(new SaveKpiResultDraft(id, entry, current.Value.Version, FromMcp: true), cancellationToken).ConfigureAwait(false);
        return KpiToolResults.From(saved, r => new { resultId = r.Id, r.Status, r.Achieve, performance = r.Capped, draft = true, link = $"/kpi/results/{r.Id:D}" });
    }
}
