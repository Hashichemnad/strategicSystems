using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Risk.Ai;

/// <summary>AI features of Risk Management (SPEC §3.9, ADR 0106). Drafts only (CLAUDE.md rule 6).</summary>
public static class RiskAiFeatures
{
    public const string Identify = "risk-identify";
    public const string Quality = "risk-quality";
    public const string Suggest = "risk-suggest";
    public const string Duplicates = "risk-duplicates";
    public const string Review = "risk-review-assistant";
    public const string Report = "risk-report";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Identify, "مساعد تحديد المخاطر", "Risk identification assistant", RiskPermissions.AiIdentify),
        new(Quality, "فحص جودة صياغة الخطر", "Risk statement quality check", RiskPermissions.AiQuality),
        new(Suggest, "اقتراح الضوابط والمعالجة", "Control and treatment suggestions", RiskPermissions.AiSuggest),
        new(Duplicates, "كشف المخاطر المكررة", "Duplicate risk detection", RiskPermissions.AiDuplicates, NeedsProvider: false),
        new(Review, "مساعد مراجعة المخاطر", "Risk review assistant", RiskPermissions.AiReview),
        new(Report, "كاتب تقرير المخاطر", "Risk report writer", RiskPermissions.AiReport),
    ];
}

public sealed record SuggestedRiskDto(
    string? TitleAr, string? TitleEn, string? Cause, string? EventText, string? Consequence, string? CategoryCode, int? Likelihood, int? Impact, string? Reasoning);

public sealed record RiskIdentificationDto(IReadOnlyList<SuggestedRiskDto> Risks, Guid UsageId);

public sealed record StatementCheckDto(IReadOnlyList<string> Flags, IReadOnlyList<string> Issues, string? Cause, string? EventText, string? Consequence, Guid? UsageId);

public sealed record SuggestedControlDto(string? NameAr, string? NameEn, string? TypeCode);

public sealed record SuggestedActionDto(string? TitleAr, string? TitleEn);

public sealed record TreatmentSuggestionDto(IReadOnlyList<SuggestedControlDto> Controls, IReadOnlyList<SuggestedActionDto> Actions, IReadOnlyList<RiskListRefDto> SimilarRisks,
    Guid UsageId);

public sealed record DuplicateMatchDto(Guid Id, string Number, string? TitleAr, string? TitleEn, double Score);

public sealed record DuplicateCheckDto(IReadOnlyList<DuplicateMatchDto> Visible, int OtherUnits);

public sealed record ReviewAssistDto(IReadOnlyList<string> Changes, string Commentary, IReadOnlyList<string> Facts, Guid UsageId);

public sealed record RiskReportDraftDto(string ReportAr, string ReportEn, string Top10Ar, string Top10En, Guid UsageId);

public sealed record StatementInput(string? Cause, string? EventText, string? Consequence);

/// <summary>Every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class RiskAiService(
    AiGateway gateway, RiskDbContext db, RiskReader reader, FrameworkStore frameworks, ILookupService lookups, IOcrEngine ocr, IRequestContext context,
    IServiceProvider services)
{
    /// <summary>Vague wording the deterministic check flags (SPEC §3.9 example "lack of resources").</summary>
    private static readonly string[] VagueTerms =
    [
        "lack of", "insufficient", "poor", "inadequate", "failure to", "issues", "problems", "resources", "نقص", "قلة", "ضعف", "عدم كفاية", "مشاكل", "الموارد",
    ];

    private static readonly JsonElement IdentifySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["risks"],"properties":{"risks":{"type":"array","maxItems":8,"items":{"type":"object",
          "required":["titleAr","titleEn","cause","event","consequence","categoryCode","likelihood","impact","reasoning"],"properties":{
          "titleAr":{"type":"string","maxLength":400},"titleEn":{"type":"string","maxLength":400},"cause":{"type":"string","maxLength":2000},
          "event":{"type":"string","maxLength":2000},"consequence":{"type":"string","maxLength":2000},"categoryCode":{"type":"string","maxLength":100},
          "likelihood":{"type":"integer"},"impact":{"type":"integer"},"reasoning":{"type":"string","maxLength":1000}}}}}}
        """);

    private static readonly JsonElement QualitySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["issues","cause","event","consequence"],"properties":{
          "issues":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":300}},"cause":{"type":"string","maxLength":2000},
          "event":{"type":"string","maxLength":2000},"consequence":{"type":"string","maxLength":2000}}}
        """);

    private static readonly JsonElement SuggestSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["controls","actions"],"properties":{
          "controls":{"type":"array","maxItems":5,"items":{"type":"object","required":["nameAr","nameEn","typeCode"],"properties":{
            "nameAr":{"type":"string","maxLength":400},"nameEn":{"type":"string","maxLength":400},"typeCode":{"type":"string","maxLength":100}}}},
          "actions":{"type":"array","maxItems":5,"items":{"type":"object","required":["titleAr","titleEn"],"properties":{
            "titleAr":{"type":"string","maxLength":600},"titleEn":{"type":"string","maxLength":600}}}}}}
        """);

    private static readonly JsonElement ReviewSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["changes","commentary"],"properties":{"changes":{"type":"array","maxItems":6,"items":{"type":"string","maxLength":300}},
          "commentary":{"type":"string","maxLength":4000}}}
        """);

    private static readonly JsonElement ReportSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["reportAr","reportEn","top10Ar","top10En"],"properties":{"reportAr":{"type":"string","maxLength":12000},
          "reportEn":{"type":"string","maxLength":12000},"top10Ar":{"type":"string","maxLength":6000},"top10En":{"type":"string","maxLength":6000}}}
        """);

    public async Task<Result<RiskIdentificationDto>> IdentifyAsync(string? description, byte[]? document, string? fileName, CancellationToken ct)
    {
        var text = description;
        if (document is { Length: > 0 })
        {
            var ready = await gateway.PrepareAsync(RiskAiFeatures.Identify, createClient: false, ct).ConfigureAwait(false);
            if (ready.IsFailure)
            {
                return ready.Error;
            }

            var recognition = ready.Value.Settings.Recognition;
            var ocrOptions = recognition.Enabled && await ocr.IsAvailableAsync(recognition.Languages, ct).ConfigureAwait(false)
                ? new OcrOptions(ocr, recognition.Languages, recognition.MaxPages)
                : null;
            var extracted = await DocumentTextExtractor.ExtractAsync(document, fileName ?? "document", ocrOptions, ct).ConfigureAwait(false);
            if (extracted.IsFailure)
            {
                return extracted.Error;
            }

            text = $"{description}\n{extracted.Value.Text}".Trim();
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > DocumentTextExtractor.MaxCharacters)
        {
            return Error.Validation([new FieldError("description", "validation.notEmpty", "validation.notEmpty")]);
        }

        var categories = await db.Categories.AsNoTracking().Where(c => c.Active).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RiskAiFeatures.Identify, new Dictionary<string, string>
        {
            ["categories"] = string.Join(", ", categories), ["max"] = scorer.MaxLikelihood.ToString(CultureInfo.InvariantCulture),
        }, [new AiDocument("risk-source", text)], IdentifySchema, RiskPermissions.SystemCode, IsUpload: document is not null,
            AuditDetail: new { characters = text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var risks = new List<SuggestedRiskDto>();
        if (call.Value.Output.TryGetProperty("risks", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in array.EnumerateArray().Take(8))
            {
                var category = Str(r, "categoryCode") is { } c && categories.Contains(c) ? c : null;
                risks.Add(new SuggestedRiskDto(Str(r, "titleAr"), Str(r, "titleEn"), Str(r, "cause"), Str(r, "event"), Str(r, "consequence"), category,
                    Int(r, "likelihood") is { } l && scorer.IsLikelihood(l) ? l : null, Int(r, "impact") is { } i && scorer.IsImpact(i) ? i : null, Str(r, "reasoning")));
            }
        }

        return new RiskIdentificationDto(risks, call.Value.UsageId);
    }

    /// <summary>Deterministic flags always; the model rewrite when AI is available.</summary>
    public async Task<Result<StatementCheckDto>> CheckStatementAsync(StatementInput input, CancellationToken ct)
    {
        var flags = Flags(input);
        var statement = $"Cause: {input.Cause}\nEvent: {input.EventText}\nConsequence: {input.Consequence}";
        if (string.IsNullOrWhiteSpace(input.Cause) && string.IsNullOrWhiteSpace(input.EventText) && string.IsNullOrWhiteSpace(input.Consequence))
        {
            return Error.Validation([new FieldError("eventText", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (!await gateway.IsAvailableAsync(RiskAiFeatures.Quality, ct).ConfigureAwait(false))
        {
            return new StatementCheckDto(flags, [], null, null, null, null);
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RiskAiFeatures.Quality, new Dictionary<string, string>(), [new AiDocument("risk-statement", statement)],
            QualitySchema, RiskPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new StatementCheckDto(flags, Strings(o, "issues"), Str(o, "cause"), Str(o, "event"), Str(o, "consequence"), call.Value.UsageId);
    }

    /// <summary>Pure deterministic checks (unit-tested): missing parts, too short, vague terms, event equal to cause.</summary>
    public static List<string> Flags(StatementInput input)
    {
        var flags = new List<string>();
        foreach (var (code, value) in new[] { ("cause", input.Cause), ("event", input.EventText), ("consequence", input.Consequence) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                flags.Add($"missing-{code}");
            }
            else if (value.Trim().Split((char[])[' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length < 4)
            {
                flags.Add($"short-{code}");
            }
        }

        var all = $"{input.Cause} {input.EventText}".ToLowerInvariant();
        if (VagueTerms.Any(t => all.Contains(t, StringComparison.Ordinal)))
        {
            flags.Add("vague");
        }

        if (!string.IsNullOrWhiteSpace(input.Cause) && string.Equals(input.Cause.Trim(), input.EventText?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            flags.Add("cause-equals-event");
        }

        return flags;
    }

    public async Task<Result<TreatmentSuggestionDto>> SuggestAsync(Guid riskId, CancellationToken ct)
    {
        var risk = await reader.RiskAsync(riskId, context.User, ct).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var similar = services.GetService(typeof(RiskSearchSourceAccessor)) is RiskSearchSourceAccessor accessor
            ? await accessor.SimilarAsync($"{risk.TitleEn ?? risk.TitleAr} {risk.EventText}", risk.Id, ct).ConfigureAwait(false)
            : [];
        var ids = similar.Select(s => s.Id).ToList();
        var controls = await db.Controls.AsNoTracking().Where(c => ids.Contains(c.RiskId) && c.Status == ControlStatuses.Active).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var actions = await db.Actions.AsNoTracking().Where(a => ids.Contains(a.RiskId)).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var context1 = new StringBuilder($"Risk: {risk.TitleEn ?? risk.TitleAr}\nCause: {risk.Cause}\nEvent: {risk.EventText}\nConsequence: {risk.Consequence}\n");
        foreach (var s in similar)
        {
            context1.Append("Similar risk ").Append(s.Number).Append(": ").Append(s.TitleEn ?? s.TitleAr).Append('\n');
            foreach (var c in controls.Where(c => c.RiskId == s.Id))
            {
                context1.Append("  control: ").Append(c.NameEn ?? c.NameAr).Append('\n');
            }

            foreach (var a in actions.Where(a => a.RiskId == s.Id))
            {
                context1.Append("  action: ").Append(a.TitleEn ?? a.TitleAr).Append('\n');
            }
        }

        var types = (await lookups.GetValuesAsync(RiskPermissions.ControlTypeLookup, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RiskAiFeatures.Suggest, new Dictionary<string, string> { ["controlTypes"] = string.Join(", ", types) },
            [new AiDocument("risk-and-similar", context1.ToString())], SuggestSchema, RiskPermissions.SystemCode, ResourceType: RiskRecord.EntityType,
            ResourceId: risk.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var suggestedControls = Objects(o, "controls").Select(c => new SuggestedControlDto(Str(c, "nameAr"), Str(c, "nameEn"),
            Str(c, "typeCode") is { } t && types.Contains(t) ? t : null)).ToList();
        var suggestedActions = Objects(o, "actions").Select(a => new SuggestedActionDto(Str(a, "titleAr"), Str(a, "titleEn"))).ToList();
        return new TreatmentSuggestionDto(suggestedControls, suggestedActions, similar.Select(s => new RiskListRefDto(s.Id, s.Number, s.TitleAr, s.TitleEn)).ToList(),
            call.Value.UsageId);
    }

    /// <summary>
    /// Duplicate detection without a model (ADR 0106): trigram similarity across every unit; details only for risks the caller may read, the
    /// rest as a count.
    /// </summary>
    public async Task<Result<DuplicateCheckDto>> DuplicatesAsync(string? text, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000)
        {
            return Error.Validation([new FieldError("text", "validation.notEmpty", "validation.notEmpty")]);
        }

        var ready = await gateway.PrepareAsync(RiskAiFeatures.Duplicates, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var all = await db.Risks.AsNoTracking().Where(r => r.Id != excludeId && r.Status != RiskStatuses.Rejected)
            .Select(r => new
            {
                r.Id,
                Score = Math.Max(Math.Max(EF.Functions.TrigramsSimilarity(r.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(r.TitleAr ?? string.Empty, text)),
                    EF.Functions.TrigramsSimilarity(r.EventText ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.3).OrderByDescending(x => x.Score).Take(50).ToListAsync(ct).ConfigureAwait(false);
        var ids = all.Select(a => a.Id).ToList();
        var visible = await (await reader.RisksAsync(context.User, ct).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        await gateway.RecordAsync(ready.Value, RiskRecord.EntityType, excludeId?.ToString("D"), true, null, 0, 0, 0, ct, new { matches = all.Count }).ConfigureAwait(false);
        return new DuplicateCheckDto(
            all.Where(a => visible.ContainsKey(a.Id)).Take(10).Select(a => new DuplicateMatchDto(a.Id, visible[a.Id].Number, visible[a.Id].TitleAr, visible[a.Id].TitleEn,
                Math.Round(a.Score, 2))).ToList(),
            all.Count(a => !visible.ContainsKey(a.Id)));
    }

    public async Task<Result<ReviewAssistDto>> ReviewAssistAsync(Guid riskId, CancellationToken ct)
    {
        var risk = await reader.RiskAsync(riskId, context.User, ct).ConfigureAwait(false);
        if (risk is null)
        {
            return Error.NotFound();
        }

        var since = risk.LastReviewOn is { } last ? new DateTimeOffset(last.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : risk.CreatedAt;
        var facts = new List<string>();
        var actions = await db.Actions.AsNoTracking().Where(a => a.RiskId == risk.Id).ToListAsync(ct).ConfigureAwait(false);
        var actionIds = actions.Select(a => a.Id).ToList();
        var updates = await db.ActionUpdates.AsNoTracking().Where(u => actionIds.Contains(u.ActionId) && u.At >= since).OrderBy(u => u.At).ToListAsync(ct).ConfigureAwait(false);
        foreach (var a in actions)
        {
            facts.Add(string.Create(CultureInfo.InvariantCulture, $"Action {a.Number} '{a.TitleEn ?? a.TitleAr}': {a.Status}, {a.CompletionPct * 100:0} %, planned end {a.PlannedEnd:yyyy-MM-dd}"));
            facts.AddRange(updates.Where(u => u.ActionId == a.Id).Select(u => string.Create(CultureInfo.InvariantCulture, $"  update {u.At:yyyy-MM-dd}: {u.CompletionPct * 100:0} % — {u.Note}")));
        }

        foreach (var k in await db.Kris.AsNoTracking().Where(k => k.RiskId == risk.Id && k.Status == ControlStatuses.Active).ToListAsync(ct).ConfigureAwait(false))
        {
            facts.Add(string.Create(CultureInfo.InvariantCulture, $"KRI {k.CardCode}: last {k.LastValue} ({k.LastLevel}) {k.LastPeriod}, breach at {k.BreachThreshold} ({k.Direction})"));
        }

        var eventIds = db.EventRisks.Where(l => l.RiskId == risk.Id).Select(l => l.EventId);
        foreach (var e in await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id) && e.CreatedAt >= since).ToListAsync(ct).ConfigureAwait(false))
        {
            facts.Add(string.Create(CultureInfo.InvariantCulture, $"Event {e.Number} {e.OccurredOn:yyyy-MM-dd}: {e.Description} (near miss: {e.NearMiss}, impact {e.FinancialImpact})"));
        }

        foreach (var c in await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).ToListAsync(ct).ConfigureAwait(false))
        {
            facts.Add($"Control '{c.NameEn ?? c.NameAr}': {c.EffectivenessCode ?? "not rated"}");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RiskAiFeatures.Review, new Dictionary<string, string>
        {
            ["risk"] = $"{risk.Number} {risk.TitleEn ?? risk.TitleAr}",
            ["residual"] = string.Create(CultureInfo.InvariantCulture, $"{risk.ResidualScore:0.##} ({risk.ResidualBand})"),
            ["lastReview"] = RiskDeadlines.Date(risk.LastReviewOn),
        }, [new AiDocument("review-facts", string.Join('\n', facts))], ReviewSchema, RiskPermissions.SystemCode, ResourceType: RiskRecord.EntityType,
            ResourceId: risk.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        return new ReviewAssistDto(Strings(call.Value.Output, "changes"), Str(call.Value.Output, "commentary") ?? string.Empty, facts, call.Value.UsageId);
    }

    public async Task<Result<RiskReportDraftDto>> ReportAsync(RiskDashboardDto dashboard, string period, CancellationToken ct)
    {
        string N(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var data = new StringBuilder();
        data.Append(CultureInfo.InvariantCulture, $"Total risks {dashboard.Total}; active {dashboard.Active}; project risks {dashboard.ProjectRisks}\n");
        data.Append("By residual rating: ").AppendJoin(", ", dashboard.ByResidualBand.Select(c => $"{c.Code} {c.Count}")).Append('\n');
        data.Append("By category: ").AppendJoin(", ", dashboard.ByCategory.Select(c => $"{c.Code} {c.Count}")).Append('\n');
        data.Append(CultureInfo.InvariantCulture, $"Outside appetite {dashboard.OutsideAppetiteCount}; overdue actions {dashboard.OverdueActionsCount}; overdue reviews {dashboard.OverdueReviewsCount}\n");
        data.Append(CultureInfo.InvariantCulture, $"Movement this quarter: improved {dashboard.Movement.Improved}, worsened {dashboard.Movement.Worsened}\n");
        data.Append(CultureInfo.InvariantCulture, $"KRI breaches {dashboard.KriBreaches.Count}\nTop-10:\n");
        foreach (var r in dashboard.Top10)
        {
            data.Append(CultureInfo.InvariantCulture, $"{r.Number} {r.TitleEn ?? r.TitleAr}: residual {N(r.ResidualScore)} ({r.ResidualBand}), target {r.TargetBand}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RiskAiFeatures.Report, new Dictionary<string, string> { ["period"] = period },
            [new AiDocument("risk-aggregates", data.ToString())], ReportSchema, RiskPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new RiskReportDraftDto(Str(o, "reportAr") ?? string.Empty, Str(o, "reportEn") ?? string.Empty, Str(o, "top10Ar") ?? string.Empty, Str(o, "top10En") ?? string.Empty,
            call.Value.UsageId);
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];

    private static IEnumerable<JsonElement> Objects(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Take(5) : [];
}

/// <summary>Similar risks the caller may read (for suggestions), through the search source.</summary>
internal sealed class RiskSearchSourceAccessor(RiskReader reader, IRequestContext context)
{
    public async Task<IReadOnlyList<RiskRecord>> SimilarAsync(string text, Guid excludeId, CancellationToken ct) =>
        await (await reader.RisksAsync(context.User, ct).ConfigureAwait(false))
            .Where(r => r.Id != excludeId && r.Status != RiskStatuses.Rejected)
            .Select(r => new
            {
                Risk = r,
                Score = Math.Max(EF.Functions.TrigramsSimilarity(r.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(r.EventText ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.15).OrderByDescending(x => x.Score).Take(5).Select(x => x.Risk).ToListAsync(ct).ConfigureAwait(false);
}
