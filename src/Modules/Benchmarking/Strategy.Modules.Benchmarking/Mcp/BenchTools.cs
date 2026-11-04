using System.Text.Json;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Benchmarking.Mcp;

/// <summary>MCP read tools of Benchmarking (ADR 0129): same handlers and scopes as the UI. No write tool.</summary>
internal static class BenchToolResults
{
    public static ToolResult From<T>(Result<T> result, Func<T, object?> map) => result.IsSuccess
        ? ToolResult.Ok(map(result.Value))
        : result.Error.Kind switch
        {
            ErrorKind.NotFound => ToolResult.NotFound(),
            ErrorKind.Forbidden => ToolResult.Fail(ToolErrors.Forbidden, result.Error.MessageKey),
            _ => ToolResult.Fail(ToolErrors.Validation, result.Error.MessageKey),
        };

    public static object Study(BenchStudyListItemDto s) => new
    {
        studyId = s.Id,
        number = s.Number,
        title = s.TitleEn ?? s.TitleAr,
        unit = s.ResponsibleUnit?.NameEn ?? s.ResponsibleUnit?.NameAr,
        coordinator = s.ResponsibleEmployee?.NameEn ?? s.ResponsibleEmployee?.NameAr,
        target = $"Q{s.TargetQuarter} {s.TargetYear}",
        s.Stage,
        s.Status,
        waitingFor = s.PendingProcess,
        link = $"/bench/studies/{s.Id:D}",
    };
}

internal sealed class BenchStudiesTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "bench_studies", BenchPermissions.SystemCode, ToolKind.Read, "دراسات المقارنة المعيارية", "Benchmarking studies",
        "Lists the benchmarking studies the user may see (view: all, mine, my-unit, pending, active), optionally for a target year and stage (plan, search, acquire, analysis, completed).",
        ToolRegistrationExtensions.Schema("""
            {"type":"object","properties":{"view":{"type":"string","maxLength":20},"year":{"type":"integer"},"stage":{"type":"string","maxLength":20},
              "search":{"type":"string","maxLength":200},"pageSize":{"type":"integer","minimum":1,"maximum":100}}}
            """),
        BenchPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var size = arguments.TryGetProperty("pageSize", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetInt32(), 1, 100) : 25;
        int? year = arguments.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        var result = await dispatcher.SendAsync(new ListStudies(new PageRequest(1, size), arguments.Text("view"), arguments.Text("search"), year, null, arguments.Text("stage"), null,
            null), cancellationToken).ConfigureAwait(false);
        return BenchToolResults.From(result, page => new { total = page.TotalCount, studies = page.Items.Select(BenchToolResults.Study) });
    }
}

internal sealed class BenchStudyDetailsTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "bench_study_details", BenchPermissions.SystemCode, ToolKind.Read, "تفاصيل دراسة مقارنة", "Benchmarking study details",
        "Returns one study: plan, topics, selected comparators, development plan and the live progress of its corrective actions.",
        ToolRegistrationExtensions.Schema("""{"type":"object","required":["studyId"],"properties":{"studyId":{"type":"string","format":"uuid"}}}"""),
        BenchPermissions.Read);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (arguments.Id("studyId") is not { } id)
        {
            return ToolResult.Fail(ToolErrors.Validation, "studyId is required.");
        }

        var study = await dispatcher.SendAsync(new GetStudy(id), cancellationToken).ConfigureAwait(false);
        if (study.IsFailure)
        {
            return BenchToolResults.From(study, _ => null);
        }

        var candidates = await dispatcher.SendAsync(new GetStudySearch(id), cancellationToken).ConfigureAwait(false);
        var analysis = await dispatcher.SendAsync(new GetStudyAnalysis(id), cancellationToken).ConfigureAwait(false);
        var actions = await dispatcher.SendAsync(new GetStudyActions(id), cancellationToken).ConfigureAwait(false);
        var s = study.Value;
        return ToolResult.Ok(new
        {
            studyId = s.Id,
            s.Number,
            title = s.TitleEn ?? s.TitleAr,
            target = $"Q{s.TargetQuarter} {s.TargetYear}",
            s.Stage,
            s.Status,
            s.ComparisonTypeCode,
            s.PurposeCode,
            s.Scope,
            topics = s.Topics.Select(t => new { t.Title, owner = t.Owner?.NameEn ?? t.Owner?.NameAr }),
            comparators = candidates.IsSuccess ? candidates.Value.Where(c => c.Selected).Select(c => new { name = c.NameEn ?? c.NameAr, c.Country, c.Reason }) : null,
            developmentPlan = analysis.IsSuccess ? analysis.Value.PlanItems.Select(i => new { title = i.TitleEn ?? i.TitleAr, i.DueOn, i.Priority }) : null,
            actions = actions.IsSuccess && actions.Value is { } p ? new { total = p.ActionCount, completed = p.CompletedCount, allCompleted = p.AllActionsCompleted } : null,
            link = $"/bench/studies/{s.Id:D}",
        });
    }
}

internal sealed class BenchBestPracticesTool(IDispatcher dispatcher) : IAppTool
{
    public ToolDefinition Definition { get; } = new(
        "bench_best_practices", BenchPermissions.SystemCode, ToolKind.Read, "مكتبة أفضل الممارسات", "Best-practice library",
        "Searches the best-practice library (practices learnt in benchmarking studies) by keywords in Arabic or English.",
        ToolRegistrationExtensions.Schema("""{"type":"object","properties":{"search":{"type":"string","maxLength":200},"pageSize":{"type":"integer","minimum":1,"maximum":50}}}"""),
        BenchPermissions.PracticeRead);

    public async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var size = arguments.TryGetProperty("pageSize", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetInt32(), 1, 50) : 20;
        var result = await dispatcher.SendAsync(new SearchBestPractices(new PageRequest(1, size), arguments.Text("search"), null, null), cancellationToken).ConfigureAwait(false);
        return BenchToolResults.From(result, page => new
        {
            total = page.TotalCount,
            practices = page.Items.Select(p => new { title = p.Values.TitleEn ?? p.Values.TitleAr, p.Values.Description, p.Values.Topic, study = p.StudyNumber,
                comparator = p.ComparatorNameEn ?? p.ComparatorNameAr }),
        });
    }
}
