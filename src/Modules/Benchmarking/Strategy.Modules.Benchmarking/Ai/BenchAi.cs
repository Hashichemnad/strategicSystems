using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Benchmarking.Ai;

/// <summary>AI features of Benchmarking (SPEC §8.6, ADR 0129). Drafts only (CLAUDE.md rule 6).</summary>
public static class BenchAiFeatures
{
    public const string Comparators = "bench-comparators";
    public const string Questionnaire = "bench-questionnaire";
    public const string Analysis = "bench-analysis";
    public const string Summary = "bench-summary";
    public const string Search = "bench-search";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Comparators, "اقتراح الجهات المقارن بها", "Comparator suggestions", BenchPermissions.AiComparators),
        new(Questionnaire, "صياغة الاستبيان المسبق", "Pre-visit questionnaire drafting", BenchPermissions.AiQuestionnaire),
        new(Analysis, "تحويل ملاحظات الزيارة إلى مقارنات وفجوات", "Visit notes to comparisons and gaps", BenchPermissions.AiAnalysis),
        new(Summary, "ملخص الدراسة للقيادة", "Study summary for leadership", BenchPermissions.AiSummary),
        new(Search, "البحث في أفضل الممارسات بلغة طبيعية", "Natural-language best-practice search", BenchPermissions.AiSearch, NeedsProvider: false),
    ];
}

public sealed record SuggestedComparatorDto(string? NameAr, string? NameEn, string? Country, string? Scope, string? Reason, Guid? RegisterId);

public sealed record ComparatorSuggestionDto(IReadOnlyList<SuggestedComparatorDto> Entities, Guid UsageId);

public sealed record QuestionnaireDraftDto(string QuestionsAr, string QuestionsEn, Guid UsageId);

public sealed record SuggestedPracticeDto(Guid? TopicId, string? Subject, string? OurPractice, string? TheirPractice, string Category);

public sealed record SuggestedPlanItemDto(string? TitleAr, string? TitleEn, string Priority);

public sealed record VisitAnalysisDto(IReadOnlyList<SuggestedPracticeDto> Practices, IReadOnlyList<string> Gaps, IReadOnlyList<SuggestedPlanItemDto> Actions, Guid UsageId);

public sealed record StudySummaryDto(string SummaryAr, string SummaryEn, Guid UsageId);

public sealed record PracticeSearchDto(IReadOnlyList<string> Keywords, PagedResult<BenchBestPracticeDto> Results, Guid? UsageId);

/// <summary>Every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class BenchAiService(AiGateway gateway, BenchDbContext db, BenchReader reader, BenchViews views, IDispatcher dispatcher, IRequestContext context,
    IServiceProvider services)
{
    private static readonly JsonElement ComparatorSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["entities"],"properties":{"entities":{"type":"array","maxItems":6,"items":{"type":"object",
          "required":["nameAr","nameEn","country","scope","reason"],"properties":{"nameAr":{"type":"string","maxLength":400},"nameEn":{"type":"string","maxLength":400},
          "country":{"type":"string","maxLength":100},"scope":{"type":"string","maxLength":40},"reason":{"type":"string","maxLength":1000}}}}}}
        """);

    private static readonly JsonElement QuestionnaireSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["questionsAr","questionsEn"],"properties":{"questionsAr":{"type":"string","maxLength":12000},
          "questionsEn":{"type":"string","maxLength":12000}}}
        """);

    private static readonly JsonElement AnalysisSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["practices","gaps","actions"],"properties":{
          "practices":{"type":"array","maxItems":12,"items":{"type":"object","required":["topic","subject","ourPractice","theirPractice","category"],"properties":{
            "topic":{"type":"string","maxLength":400},"subject":{"type":"string","maxLength":400},"ourPractice":{"type":"string","maxLength":2000},
            "theirPractice":{"type":"string","maxLength":2000},"category":{"type":"string","maxLength":40}}}},
          "gaps":{"type":"array","maxItems":6,"items":{"type":"string","maxLength":500}},
          "actions":{"type":"array","maxItems":6,"items":{"type":"object","required":["titleAr","titleEn","priority"],"properties":{
            "titleAr":{"type":"string","maxLength":400},"titleEn":{"type":"string","maxLength":400},"priority":{"type":"string","maxLength":20}}}}}}
        """);

    private static readonly JsonElement SummarySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summaryAr","summaryEn"],"properties":{"summaryAr":{"type":"string","maxLength":6000},"summaryEn":{"type":"string","maxLength":6000}}}
        """);

    private static readonly JsonElement SearchSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["keywords"],"properties":{"keywords":{"type":"array","maxItems":6,"items":{"type":"string","maxLength":100}}}}
        """);

    public async Task<Result<ComparatorSuggestionDto>> ComparatorsAsync(Guid studyId, CancellationToken ct)
    {
        var study = await reader.StudyAsync(studyId, context.User, ct).ConfigureAwait(false);
        if (study is null)
        {
            return Error.NotFound();
        }

        var register = await db.Comparators.AsNoTracking().Where(c => c.Active).OrderBy(c => c.NameEn).Take(60).ToListAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder(await StudyTextAsync(study, ct).ConfigureAwait(false));
        foreach (var c in register)
        {
            sb.Append("Register: ").Append(c.NameEn ?? c.NameAr).Append(" (").Append(c.Country).Append(", ").Append(c.Scope).Append(")\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(BenchAiFeatures.Comparators, new Dictionary<string, string>(), [new AiDocument("study", sb.ToString())],
            ComparatorSchema, BenchPermissions.SystemCode, ResourceType: Study.EntityType, ResourceId: study.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var list = new List<SuggestedComparatorDto>();
        if (call.Value.Output.TryGetProperty("entities", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in array.EnumerateArray().Take(6))
            {
                var nameEn = Str(e, "nameEn");
                var nameAr = Str(e, "nameAr");
                var known = register.FirstOrDefault(c => (nameEn is not null && string.Equals(c.NameEn, nameEn, StringComparison.OrdinalIgnoreCase))
                    || (nameAr is not null && c.NameAr == nameAr));
                var scope = Str(e, "scope") is { } s && ComparatorScopes.All.Contains(s) ? s : ComparatorScopes.International;
                list.Add(new SuggestedComparatorDto(nameAr, nameEn, Str(e, "country"), scope, Str(e, "reason"), known?.Id));
            }
        }

        return new ComparatorSuggestionDto(list, call.Value.UsageId);
    }

    public async Task<Result<QuestionnaireDraftDto>> QuestionnaireAsync(Guid studyId, Guid? comparatorId, CancellationToken ct)
    {
        var study = await reader.StudyAsync(studyId, context.User, ct).ConfigureAwait(false);
        if (study is null)
        {
            return Error.NotFound();
        }

        var text = await StudyTextAsync(study, ct).ConfigureAwait(false);
        if (comparatorId is { } id && await db.Comparators.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false) is { } comparator)
        {
            text += $"Comparator: {comparator.NameEn ?? comparator.NameAr} ({comparator.Country})\n";
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(BenchAiFeatures.Questionnaire, new Dictionary<string, string>(), [new AiDocument("study", text)],
            QuestionnaireSchema, BenchPermissions.SystemCode, ResourceType: Study.EntityType, ResourceId: study.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        return new QuestionnaireDraftDto(Str(call.Value.Output, "questionsAr") ?? string.Empty, Str(call.Value.Output, "questionsEn") ?? string.Empty, call.Value.UsageId);
    }

    public async Task<Result<VisitAnalysisDto>> AnalysisAsync(Guid studyId, string? notes, CancellationToken ct)
    {
        var study = await reader.StudyAsync(studyId, context.User, ct).ConfigureAwait(false);
        if (study is null)
        {
            return Error.NotFound();
        }

        var visitNotes = notes;
        if (string.IsNullOrWhiteSpace(visitNotes))
        {
            visitNotes = string.Join("\n", await db.Visits.AsNoTracking().Where(v => v.StudyId == study.Id && !v.Removed && v.Notes != null).Select(v => v.Notes).ToListAsync(ct)
                .ConfigureAwait(false));
        }

        if (string.IsNullOrWhiteSpace(visitNotes) || visitNotes.Length > 60000)
        {
            return Error.Validation([new FieldError("notes", "validation.notEmpty", "validation.notEmpty")]);
        }

        var topics = await db.Topics.AsNoTracking().Where(t => t.StudyId == study.Id && !t.Removed).ToListAsync(ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(BenchAiFeatures.Analysis, new Dictionary<string, string>(),
            [new AiDocument("study", await StudyTextAsync(study, ct).ConfigureAwait(false)), new AiDocument("visit-notes", visitNotes)], AnalysisSchema,
            BenchPermissions.SystemCode, ResourceType: Study.EntityType, ResourceId: study.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var practices = new List<SuggestedPracticeDto>();
        if (o.TryGetProperty("practices", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rows.EnumerateArray().Take(12))
            {
                var topic = Str(r, "topic") is { } t ? topics.FirstOrDefault(x => string.Equals(x.Title, t, StringComparison.OrdinalIgnoreCase))?.Id : null;
                var category = Str(r, "category") is { } c && PracticeCategories.All.Contains(c) ? c : PracticeCategories.Compared;
                practices.Add(new SuggestedPracticeDto(topic, Str(r, "subject"), Str(r, "ourPractice"), Str(r, "theirPractice"), category));
            }
        }

        var actions = new List<SuggestedPlanItemDto>();
        if (o.TryGetProperty("actions", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in items.EnumerateArray().Take(6))
            {
                var priority = Str(a, "priority") is { } p && CapPriorities.All.Contains(p) ? p : CapPriorities.Medium;
                actions.Add(new SuggestedPlanItemDto(Str(a, "titleAr"), Str(a, "titleEn"), priority));
            }
        }

        return new VisitAnalysisDto(practices, Strings(o, "gaps"), actions, call.Value.UsageId);
    }

    public async Task<Result<StudySummaryDto>> SummaryAsync(Guid studyId, CancellationToken ct)
    {
        var study = await reader.StudyAsync(studyId, context.User, ct).ConfigureAwait(false);
        if (study is null)
        {
            return Error.NotFound();
        }

        var sb = new StringBuilder(await StudyTextAsync(study, ct).ConfigureAwait(false));
        var analysis = await views.AnalysisAsync(study.Id, ct).ConfigureAwait(false);
        foreach (var p in analysis.Practices)
        {
            sb.Append("Practice ").Append(p.Subject).Append(": ours = ").Append(p.OurPractice).Append("; theirs = ").Append(p.TheirPractice).Append('\n');
        }

        foreach (var i in analysis.PlanItems)
        {
            sb.Append("Development action: ").Append(i.TitleEn ?? i.TitleAr).Append('\n');
        }

        if (services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap && await cap.GetProgressAsync(Study.EntityType, study.Id, ct).ConfigureAwait(false) is { } progress)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Actions completed: {progress.CompletedCount} of {progress.ActionCount}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(BenchAiFeatures.Summary, new Dictionary<string, string>(), [new AiDocument("study", sb.ToString())],
            SummarySchema, BenchPermissions.SystemCode, ResourceType: Study.EntityType, ResourceId: study.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        return new StudySummaryDto(Str(call.Value.Output, "summaryAr") ?? string.Empty, Str(call.Value.Output, "summaryEn") ?? string.Empty, call.Value.UsageId);
    }

    /// <summary>Keywords from the model when a provider is available, else the question itself; the server searches the library (permission-checked).</summary>
    public async Task<Result<PracticeSearchDto>> SearchAsync(string? question, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 1000)
        {
            return Error.Validation([new FieldError("question", "validation.notEmpty", "validation.notEmpty")]);
        }

        IReadOnlyList<string> keywords = [question.Trim()];
        Guid? usage = null;
        if (await gateway.IsAvailableAsync(BenchAiFeatures.Search, ct).ConfigureAwait(false))
        {
            var call = await gateway.CompleteJsonAsync(new AiJsonRequest(BenchAiFeatures.Search, new Dictionary<string, string>(), [new AiDocument("question", question)],
                SearchSchema, BenchPermissions.SystemCode), ct).ConfigureAwait(false);
            if (call.IsSuccess && Strings(call.Value.Output, "keywords") is { Count: > 0 } words)
            {
                keywords = words.Take(6).ToList();
                usage = call.Value.UsageId;
            }
        }

        var results = await dispatcher.SendAsync(new SearchBestPractices(new PageRequest(1, 20), null, null, null, keywords), ct).ConfigureAwait(false);
        return results.IsFailure ? results.Error : new PracticeSearchDto(keywords, results.Value, usage);
    }

    private async Task<string> StudyTextAsync(Study study, CancellationToken ct)
    {
        var topics = await db.Topics.AsNoTracking().Where(t => t.StudyId == study.Id && !t.Removed).OrderBy(t => t.Sort).ToListAsync(ct).ConfigureAwait(false);
        var sb = new StringBuilder($"Study {study.Number}: {study.TitleEn ?? study.TitleAr}\nArabic title: {study.TitleAr}\nComparison type: {study.ComparisonTypeCode}\n" +
            $"Purpose: {study.PurposeCode}\nScope: {study.Scope}\n");
        foreach (var t in topics)
        {
            sb.Append("Topic: ").Append(t.Title).Append(t.Description is null ? string.Empty : " — " + t.Description).Append('\n');
        }

        return sb.ToString();
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
