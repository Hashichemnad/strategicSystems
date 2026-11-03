using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Ai;

/// <summary>AI features of Performance Management (SPEC §2.9), registered in the Phase 5 registry. Advice and drafts only (CLAUDE.md rule 6).</summary>
public static class KpiAiFeatures
{
    public const string Evidence = "kpi-evidence-check";
    public const string Anomalies = "kpi-anomalies";
    public const string Causality = "kpi-causality";
    public const string Builder = "kpi-card-builder";
    public const string Targets = "kpi-target-suggestion";
    public const string Narrative = "kpi-narrative";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Evidence, "مدقق أدلة المؤشرات", "KPI evidence checker", KpiPermissions.AiEvidence),
        new(Anomalies, "تنبيهات القيم غير المعتادة", "KPI anomaly flags", KpiPermissions.AiAnomalies, NeedsProvider: false),
        new(Causality, "مساعد الأسباب والإجراءات التصحيحية", "Causality and corrective action assistant", KpiPermissions.AiCausality),
        new(Builder, "منشئ بطاقة المؤشر", "KPI card builder", KpiPermissions.AiBuilder),
        new(Targets, "اقتراح المستهدفات", "KPI target suggestion", KpiPermissions.AiTargets, NeedsProvider: false),
        new(Narrative, "تقرير الأداء السردي", "Performance narrative", KpiPermissions.AiNarrative),
    ];
}

public sealed record KpiEvidenceCheckDto(bool? ValueFound, bool? PeriodMatches, IReadOnlyList<string> Missing, string Summary, int Documents, Guid UsageId);

public sealed record KpiAnomalyDto(Guid ResultId, IReadOnlyList<string> Flags, decimal? Value, decimal? Previous);

public sealed record KpiCausalityDraftDto(string Causality, string CorrectiveActions, bool Weak, IReadOnlyList<string> WeakReasons, Guid UsageId);

public sealed record KpiCardDraftDto(
    string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, string? MeasuringEquation, string? UnitCode, string? MeasuringPeriod, string? Direction,
    string? TargetType, decimal? Target, IReadOnlyList<string> Suggestions, IReadOnlyList<SmartCheck> Smart, Guid? UsageId);

public sealed record KpiTargetSuggestionDto(Guid CardId, int Year, decimal? Suggested, decimal? Trend, IReadOnlyList<TargetReason> Reasons);

public sealed record KpiNarrativeDto(string ReportAr, string ReportEn, Guid UsageId);

/// <summary>The six features; every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class KpiAiService(
    AiGateway gateway, KpiDbContext db, KpiReader reader, ResultViews views, ScorecardBuilder scorecards, IFileService files, IOcrEngine ocr, ILookupService lookups,
    IRequestContext context, IClock clock)
{
    private static readonly JsonElement EvidenceSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["valueFound","periodMatches","missing","summary"],"properties":{
          "valueFound":{"type":["boolean","null"]},"periodMatches":{"type":["boolean","null"]},
          "missing":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":300}},"summary":{"type":"string","maxLength":2000}}}
        """);

    private static readonly JsonElement CausalitySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["causality","correctiveActions","weak","weakReasons"],"properties":{
          "causality":{"type":"string","maxLength":3000},"correctiveActions":{"type":"string","maxLength":3000},"weak":{"type":"boolean"},
          "weakReasons":{"type":"array","maxItems":4,"items":{"type":"string","maxLength":300}}}}
        """);

    private static readonly JsonElement BuilderSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["nameAr","nameEn","measuringEquation","unitCode","measuringPeriod","direction","suggestions"],"properties":{
          "nameAr":{"type":"string","maxLength":400},"nameEn":{"type":"string","maxLength":400},
          "descriptionAr":{"type":["string","null"],"maxLength":3000},"descriptionEn":{"type":["string","null"],"maxLength":3000},
          "measuringEquation":{"type":"string","maxLength":1000},"unitCode":{"type":"string","maxLength":100},
          "measuringPeriod":{"type":"string","maxLength":20},"direction":{"type":"string","maxLength":20},
          "targetType":{"type":["string","null"],"maxLength":20},"target":{"type":["number","null"]},
          "suggestions":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":400}}}}
        """);

    private static readonly JsonElement NarrativeSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["reportAr","reportEn"],"properties":{"reportAr":{"type":"string","maxLength":12000},"reportEn":{"type":"string","maxLength":12000}}}
        """);

    private static string N(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";

    private static string P(decimal? ratio) => ratio is { } r ? (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %" : "-";

    public async Task<Result<KpiEvidenceCheckDto>> CheckEvidenceAsync(Guid resultId, CancellationToken ct)
    {
        var result = await reader.ResultAsync(resultId, context.User, ct).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        var ready = await gateway.PrepareAsync(KpiAiFeatures.Evidence, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var recognition = ready.Value.Settings.Recognition;
        var ocrOptions = recognition.Enabled && await ocr.IsAvailableAsync(recognition.Languages, ct).ConfigureAwait(false)
            ? new OcrOptions(ocr, recognition.Languages, recognition.MaxPages)
            : null;
        var evidence = await files.ListAsync(KpiResult.EntityType, result.Id.ToString("D"), KpiFilePurposes.Evidence.Key, ct).ConfigureAwait(false);
        if (evidence.Count == 0)
        {
            return Error.Rule("kpi.noEvidence", "kpi.errors.evidenceRequired");
        }

        var documents = new List<AiDocument>();
        foreach (var file in evidence.Take(5))
        {
            var opened = await files.OpenAsync(file.Id, null, auditDownload: false, ct).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                continue;
            }

            await using var stream = opened.Value.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var text = await DocumentTextExtractor.ExtractAsync(buffer.ToArray(), file.Name, ocrOptions, ct).ConfigureAwait(false);
            if (text.IsSuccess && !string.IsNullOrWhiteSpace(text.Value.Text))
            {
                documents.Add(new AiDocument(file.Name, text.Value.Text));
            }
        }

        if (documents.Count == 0)
        {
            return Error.Rule("kpi.evidenceUnreadable", "kpi.errors.evidenceUnreadable");
        }

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, ct).ConfigureAwait(false);
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == result.PlanId, ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(KpiAiFeatures.Evidence, new Dictionary<string, string>
        {
            ["kpi"] = $"{card.Code} {card.NameEn ?? card.NameAr}",
            ["equation"] = card.MeasuringEquation ?? card.Formula ?? "-",
            ["period"] = $"{result.PeriodCode.ToUpperInvariant()} {result.Year}",
            ["periodStart"] = result.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["periodEnd"] = result.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["value"] = result.NotApplicable ? "not applicable" : N(result.Achieve),
            ["unit"] = plan.UnitCode,
        }, documents, EvidenceSchema, KpiPermissions.SystemCode, ResourceType: KpiResult.EntityType, ResourceId: result.Id.ToString("D"),
            AuditDetail: new { files = documents.Count }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new KpiEvidenceCheckDto(Bool(o, "valueFound"), Bool(o, "periodMatches"), Strings(o, "missing"), Str(o, "summary") ?? string.Empty, documents.Count,
            call.Value.UsageId);
    }

    /// <summary>Deterministic anomaly flags of results (one or several) the user may read.</summary>
    public async Task<Result<IReadOnlyList<KpiAnomalyDto>>> AnomaliesAsync(IReadOnlyList<Guid> resultIds, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(KpiAiFeatures.Anomalies, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var results = await (await reader.ResultsAsync(context.User, ct).ConfigureAwait(false)).Where(r => resultIds.Contains(r.Id)).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var items = await views.ToListAsync(results, clock.UtcNow, false, ct).ConfigureAwait(false);
        var list = new List<KpiAnomalyDto>();
        foreach (var r in results)
        {
            var previous = await db.Results.AsNoTracking().Where(x => x.CardId == r.CardId && x.Status == ResultStatuses.Approved && !x.NotApplicable && x.Id != r.Id
                    && (x.Year < r.Year || (x.Year == r.Year && x.Sort < r.Sort)))
                .OrderByDescending(x => x.Year).ThenByDescending(x => x.Sort).Select(x => x.Achieve).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            list.Add(new KpiAnomalyDto(r.Id, items.First(i => i.Id == r.Id).Anomalies, r.Achieve, previous));
        }

        await gateway.RecordAsync(ready.Value, KpiResult.EntityType, null, true, null, 0, 0, 0, ct, new { results = list.Count }).ConfigureAwait(false);
        return list;
    }

    public async Task<Result<KpiCausalityDraftDto>> DraftCausalityAsync(Guid resultId, string? notes, CancellationToken ct)
    {
        var result = await reader.ResultAsync(resultId, context.User, ct).ConfigureAwait(false);
        if (result is null)
        {
            return Error.NotFound();
        }

        var text = $"{notes}\n{result.Notes}\n{result.Causality}\n{result.CorrectiveActions}".Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000)
        {
            return Error.Validation([new FieldError("notes", "validation.notEmpty", "validation.notEmpty")]);
        }

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, ct).ConfigureAwait(false);
        var documents = new List<AiDocument> { new("owner-notes", notes ?? string.Empty) };
        if (!string.IsNullOrWhiteSpace(result.Causality) || !string.IsNullOrWhiteSpace(result.CorrectiveActions))
        {
            documents.Add(new AiDocument("current-justification", $"Causality: {result.Causality}\nCorrective actions: {result.CorrectiveActions}\nNotes: {result.Notes}"));
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(KpiAiFeatures.Causality, new Dictionary<string, string>
        {
            ["kpi"] = $"{card.Code} {card.NameEn ?? card.NameAr}",
            ["period"] = $"{result.PeriodCode.ToUpperInvariant()} {result.Year}",
            ["target"] = result.TargetType == TargetTypes.Range ? $"{N(result.RangeMin)} – {N(result.RangeMax)}" : N(result.Target),
            ["value"] = N(result.Achieve),
            ["performance"] = P(result.CappedPerformance),
        }, documents, CausalitySchema, KpiPermissions.SystemCode, ResourceType: KpiResult.EntityType, ResourceId: result.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new KpiCausalityDraftDto(Str(o, "causality") ?? string.Empty, Str(o, "correctiveActions") ?? string.Empty,
            o.TryGetProperty("weak", out var weak) && weak.ValueKind == JsonValueKind.True, Strings(o, "weakReasons"), call.Value.UsageId);
    }

    public async Task<Result<KpiCardDraftDto>> DraftCardAsync(string? description, byte[]? document, string? fileName, CancellationToken ct)
    {
        var text = description;
        if (document is { Length: > 0 })
        {
            var ready = await gateway.PrepareAsync(KpiAiFeatures.Builder, createClient: false, ct).ConfigureAwait(false);
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

        if (string.IsNullOrWhiteSpace(text) || text.Length > 60_000)
        {
            return Error.Validation([new FieldError("description", "validation.notEmpty", "validation.notEmpty")]);
        }

        var units = (await lookups.GetValuesAsync(KpiPermissions.UnitLookup, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(KpiAiFeatures.Builder, new Dictionary<string, string> { ["units"] = string.Join(", ", units) },
            [new AiDocument("kpi-description", text)], BuilderSchema, KpiPermissions.SystemCode, IsUpload: document is not null,
            AuditDetail: new { characters = text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var unit = Str(o, "unitCode") is { } u && units.Contains(u) ? u : null;
        var period = Str(o, "measuringPeriod") is { } p && MeasuringPeriods.All.Contains(p) ? p : null;
        var direction = Str(o, "direction") is { } d && Directions.All.Contains(d) ? d : null;
        var targetType = Str(o, "targetType") is { } t && TargetTypes.All.Contains(t) ? t : TargetTypes.SingleValue;
        decimal? target = o.TryGetProperty("target", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetDecimal() : null;
        var draft = new KpiCardDraftDto(Str(o, "nameAr"), Str(o, "nameEn"), Str(o, "descriptionAr"), Str(o, "descriptionEn"), Str(o, "measuringEquation"), unit, period, direction,
            targetType, target, Strings(o, "suggestions"), [], call.Value.UsageId);
        return draft with { Smart = SmartRules.Check(new SmartInput(draft.NameAr, draft.NameEn, draft.MeasuringEquation, unit, period, direction, target, null, null, null, null, null)) };
    }

    /// <summary>The SMART check of an existing card (deterministic, part of the builder feature).</summary>
    public async Task<Result<IReadOnlyList<SmartCheck>>> SmartCheckAsync(Guid cardId, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(KpiAiFeatures.Builder, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var card = await reader.CardAsync(cardId, context.User, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        var target = await db.CardTargets.AsNoTracking().Where(t => t.CardId == card.Id).OrderByDescending(t => t.Year).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var plan = await db.Plans.AsNoTracking().Where(p => p.CardId == card.Id).OrderByDescending(p => p.Year).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Result.Success(SmartRules.Check(new SmartInput(card.NameAr, card.NameEn, card.MeasuringEquation, card.UnitCode, card.MeasuringPeriod, card.Direction,
            target?.Target ?? plan?.Target, target?.RangeMin ?? plan?.RangeMin, target?.RangeMax ?? plan?.RangeMax, card.ResponsibleUnitId, card.ResponsibleEmployeeId,
            card.Formula)));
    }

    public async Task<Result<KpiTargetSuggestionDto>> SuggestTargetAsync(Guid cardId, int year, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(KpiAiFeatures.Targets, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var card = await reader.CardAsync(cardId, context.User, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Error.NotFound();
        }

        var history = await db.Plans.AsNoTracking().Where(p => p.CardId == card.Id && p.Year < year && p.AchievedTotal != null).OrderBy(p => p.Year)
            .Select(p => new { p.Year, Achieved = p.AchievedTotal!.Value }).ToListAsync(ct).ConfigureAwait(false);
        var cycle = await db.CardTargets.AsNoTracking().FirstOrDefaultAsync(t => t.CardId == card.Id && t.Year == year, ct).ConfigureAwait(false);
        var proposal = TargetSuggester.Suggest(year, card.Direction, history.Select(h => (h.Year, h.Achieved)).ToList(), card.BaselineValue,
            card.BenchmarkResult, cycle?.Target);
        await gateway.RecordAsync(ready.Value, KpiCard.EntityType, card.Id.ToString("D"), true, null, 0, 0, 0, ct, new { year }).ConfigureAwait(false);
        return new KpiTargetSuggestionDto(card.Id, year, proposal.Suggested, proposal.Trend, proposal.Reasons);
    }

    public async Task<Result<KpiNarrativeDto>> NarrativeAsync(int? year, Guid? unitId, CancellationToken ct)
    {
        var y = year ?? clock.Today.Year;
        var cards = await scorecards.CardsAsync(KpiPermissions.ScorecardRead, unitId, ct).ConfigureAwait(false);
        if (cards.Count == 0)
        {
            return Error.Rule("kpi.nothingToReport", "kpi.errors.nothingToReport");
        }

        var root = await scorecards.BuildAsync(cards, y, ct).ConfigureAwait(false);
        var ids = cards.Select(c => c.Id).ToList();
        var missed = await db.Results.AsNoTracking().Where(r => ids.Contains(r.CardId) && r.Year == y && r.Status == ResultStatuses.Approved && r.CappedPerformance < 1m)
            .OrderByDescending(r => r.Sort).Take(30).ToListAsync(ct).ConfigureAwait(false);
        var data = new StringBuilder();
        void Walk(KpiScoreNodeDto node, int depth)
        {
            data.Append(new string(' ', depth * 2)).Append(node.Kind).Append(": ").Append(node.Code).Append(' ').Append(node.NameEn ?? node.NameAr)
                .Append(" | score ").Append(P(node.Score)).Append(" | ").Append(node.Rag);
            if (node.Actual is { } actual)
            {
                data.Append(" | actual ").Append(P(actual));
            }

            data.AppendLine();
            foreach (var child in node.Children)
            {
                Walk(child, depth + 1);
            }
        }

        Walk(root, 0);
        var byId = cards.ToDictionary(c => c.Id);
        foreach (var r in missed)
        {
            data.AppendLine(CultureInfo.InvariantCulture,
                $"Missed {byId[r.CardId].Code} {r.PeriodCode.ToUpperInvariant()}: performance {P(r.CappedPerformance)}; causality: {r.Causality}; actions: {r.CorrectiveActions}");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(KpiAiFeatures.Narrative,
            new Dictionary<string, string> { ["year"] = y.ToString(CultureInfo.InvariantCulture), ["scope"] = unitId is null ? "organisation" : "department" },
            [new AiDocument("scorecard", data.ToString())], NarrativeSchema, KpiPermissions.SystemCode, AuditDetail: new { year = y, cards = cards.Count }), ct)
            .ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        return new KpiNarrativeDto(Str(call.Value.Output, "reportAr") ?? string.Empty, Str(call.Value.Output, "reportEn") ?? string.Empty, call.Value.UsageId);
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];
}
