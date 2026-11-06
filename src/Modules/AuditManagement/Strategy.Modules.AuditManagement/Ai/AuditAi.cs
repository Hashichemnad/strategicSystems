using System.Globalization;
using System.Text;
using System.Text.Json;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Ai;

/// <summary>AI features of Audit Management (SPEC §10.7, ADR 0137). Drafts only (CLAUDE.md rule 6).</summary>
public static class AuditAiFeatures
{
    public const string Extract = "auditmgmt-extract";
    public const string Evidence = "auditmgmt-evidence";
    public const string Repeat = "auditmgmt-repeat";
    public const string Summary = "auditmgmt-summary";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Extract, "استخراج الملاحظات من تقرير التدقيق", "Findings extraction from an audit report", AuditPermissions.AiExtract),
        new(Evidence, "مراجعة الأدلة وصياغة تعليق المتابعة", "Evidence review and follow-up comment", AuditPermissions.AiEvidence),
        new(Repeat, "كشف الملاحظات المتكررة", "Repeat-finding detection", AuditPermissions.AiRepeat),
        new(Summary, "ملخص لجنة التدقيق", "Audit committee summary", AuditPermissions.AiSummary),
    ];
}

public sealed record ExtractedRecommendationDto(string? ActionAr, string? ActionEn);

public sealed record ExtractedFindingDto(
    string? TitleAr, string? TitleEn, string? Condition, string? Criteria, string? Cause, string? Effect, Guid? ResponsibleUnitId, string? UnitCode, string Priority,
    string TypeCode, IReadOnlyList<ExtractedRecommendationDto> Recommendations);

/// <summary>Draft findings — nothing is saved until the Audit Office confirms the rows it keeps (ADR 0134).</summary>
public sealed record FindingExtractionDto(IReadOnlyList<ExtractedFindingDto> Findings, int Characters, Guid UsageId);

public sealed record EvidenceReviewDto(string Support, IReadOnlyList<string> Gaps, string CommentAr, string CommentEn, Guid UsageId);

public sealed record RepeatCheckDto(IReadOnlyList<AuditSimilarFindingDto> Similar, string? RepeatOf, string? RationaleAr, string? RationaleEn, Guid? UsageId);

public sealed record AuditSummaryDraftDto(string SummaryAr, string SummaryEn, Guid UsageId);

public sealed record EvidenceReviewInput(decimal Pct, string? ActionsImplemented, string? EvidenceText);

/// <summary>Every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class AuditAiService(
    AiGateway gateway, AuditReader reader, AuditQueries queries, ILookupService lookups, IOrgDirectory org, IOcrEngine ocr, IRequestContext context)
{
    private static readonly JsonElement ExtractSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["findings"],"properties":{"findings":{"type":"array","maxItems":20,"items":{"type":"object",
          "required":["titleAr","titleEn","condition","criteria","cause","effect","unitCode","priority","typeCode","recommendations"],"properties":{
          "titleAr":{"type":"string","maxLength":400},"titleEn":{"type":"string","maxLength":400},"condition":{"type":"string","maxLength":4000},
          "criteria":{"type":"string","maxLength":2000},"cause":{"type":"string","maxLength":2000},"effect":{"type":"string","maxLength":2000},
          "unitCode":{"type":"string","maxLength":60},"priority":{"type":"string","maxLength":40},"typeCode":{"type":"string","maxLength":40},
          "recommendations":{"type":"array","maxItems":6,"items":{"type":"object","required":["actionAr","actionEn"],"properties":{
            "actionAr":{"type":"string","maxLength":2000},"actionEn":{"type":"string","maxLength":2000}}}}}}}}}
        """);

    private static readonly JsonElement EvidenceSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["support","gaps","commentAr","commentEn"],"properties":{"support":{"type":"string","enum":["yes","partly","no"]},
          "gaps":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":400}},"commentAr":{"type":"string","maxLength":2000},
          "commentEn":{"type":"string","maxLength":2000}}}
        """);

    private static readonly JsonElement RepeatSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["repeatOf","rationaleAr","rationaleEn"],"properties":{"repeatOf":{"type":"string","maxLength":60},
          "rationaleAr":{"type":"string","maxLength":1000},"rationaleEn":{"type":"string","maxLength":1000}}}
        """);

    private static readonly JsonElement SummarySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summaryAr","summaryEn"],"properties":{"summaryAr":{"type":"string","maxLength":8000},"summaryEn":{"type":"string","maxLength":8000}}}
        """);

    /// <summary>Audit report (text and / or uploaded file, scans through OCR) → draft findings for the Audit Office to confirm.</summary>
    public async Task<Result<FindingExtractionDto>> ExtractAsync(Guid reportId, string? text, byte[]? document, string? fileName, CancellationToken ct)
    {
        if (await reader.ReportAsync(reportId, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        if (document is { Length: > 0 })
        {
            var ready = await gateway.PrepareAsync(AuditAiFeatures.Extract, createClient: false, ct).ConfigureAwait(false);
            if (ready.IsFailure)
            {
                return ready.Error;
            }

            var recognition = ready.Value.Settings.Recognition;
            var ocrOptions = recognition.Enabled && await ocr.IsAvailableAsync(recognition.Languages, ct).ConfigureAwait(false)
                ? new OcrOptions(ocr, recognition.Languages, recognition.MaxPages)
                : null;
            var extracted = await DocumentTextExtractor.ExtractAsync(document, fileName ?? "report", ocrOptions, ct).ConfigureAwait(false);
            if (extracted.IsFailure)
            {
                return extracted.Error;
            }

            text = $"{text}\n{extracted.Value.Text}".Trim();
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > DocumentTextExtractor.MaxCharacters)
        {
            return Error.Validation([new FieldError("text", "validation.notEmpty", "validation.notEmpty")]);
        }

        var priorities = (await lookups.GetValuesAsync(AuditPermissions.PriorityLookup, cancellationToken: ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var units = await org.ListActiveUnitsAtLevelAsync(settings.DepartmentLevelCode, ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(AuditAiFeatures.Extract, new Dictionary<string, string>
        {
            ["priorities"] = string.Join(", ", priorities),
            ["units"] = string.Join("; ", units.Take(150).Select(u => $"{u.NameEn ?? u.NameAr} → {u.Code}")),
        }, [new AiDocument("report", text)], ExtractSchema, AuditPermissions.SystemCode, ResourceType: AuditReport.EntityType, ResourceId: reportId.ToString("D"),
            IsUpload: document is not null, AuditDetail: new { characters = text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        // Unit codes are unique as stored but compared case-insensitively here (the model may change the case): keep the first of codes
        // that differ only in case instead of failing the whole extraction.
        var byCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in units)
        {
            byCode.TryAdd(u.Code, u.Id);
        }

        var findings = new List<ExtractedFindingDto>();
        if (call.Value.Output.TryGetProperty("findings", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in array.EnumerateArray().Take(20))
            {
                var code = Str(f, "unitCode");
                Guid? unit = code is not null && byCode.TryGetValue(code, out var id) ? id : null;
                if (unit is null && code is not null && await org.GetUnitByCodeAsync(code, ct).ConfigureAwait(false) is { } known)
                {
                    unit = known.Id;
                }

                var priority = Str(f, "priority") is { } p && priorities.Contains(p) ? p : Contracts.AuditPriorities.Medium;
                var type = Str(f, "typeCode") is FindingTypes.Improvement ? FindingTypes.Improvement : FindingTypes.Finding;
                var recs = new List<ExtractedRecommendationDto>();
                if (f.TryGetProperty("recommendations", out var ra) && ra.ValueKind == JsonValueKind.Array)
                {
                    recs.AddRange(ra.EnumerateArray().Take(6).Select(r => new ExtractedRecommendationDto(Str(r, "actionAr"), Str(r, "actionEn")))
                        .Where(r => r.ActionAr is not null || r.ActionEn is not null));
                }

                findings.Add(new ExtractedFindingDto(Str(f, "titleAr"), Str(f, "titleEn"), Str(f, "condition"), Str(f, "criteria"), Str(f, "cause"), Str(f, "effect"), unit,
                    code, priority, type, recs));
            }
        }

        return new FindingExtractionDto(findings, text.Length, call.Value.UsageId);
    }

    /// <summary>Evidence review: does the evidence support the progress being recorded; draft follow-up comment for the auditor.</summary>
    public async Task<Result<EvidenceReviewDto>> EvidenceAsync(Guid recommendationId, EvidenceReviewInput input, byte[]? document, string? fileName, CancellationToken ct)
    {
        var recommendation = await reader.RecommendationAsync(recommendationId, context.User, ct).ConfigureAwait(false);
        if (recommendation is null)
        {
            return Error.NotFound();
        }

        var evidence = input.EvidenceText;
        if (document is { Length: > 0 })
        {
            var extracted = await DocumentTextExtractor.ExtractAsync(document, fileName ?? "evidence", null, ct).ConfigureAwait(false);
            if (extracted.IsSuccess)
            {
                evidence = $"{evidence}\n{extracted.Value.Text}".Trim();
            }
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Recommendation {recommendation.Number}: {recommendation.ActionEn ?? recommendation.ActionAr}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Implementation plan: {recommendation.ImplementationPlan}\nDue: {AuditDeadlines.Date(recommendation.EffectiveDue)}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Previous progress: {Math.Round(recommendation.LatestPct * 100, 0)} %\nProgress to record: {Math.Round(input.Pct * 100, 0)} %\n");
        sb.Append(CultureInfo.InvariantCulture, $"Actions implemented (as reported): {input.ActionsImplemented}\nEvidence received: {evidence}");
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(AuditAiFeatures.Evidence, new Dictionary<string, string>(), [new AiDocument("follow-up", sb.ToString())],
            EvidenceSchema, AuditPermissions.SystemCode, ResourceType: Recommendation.EntityType, ResourceId: recommendation.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var support = Str(o, "support") is "yes" or "partly" or "no" ? Str(o, "support")! : "partly";
        return new EvidenceReviewDto(support, Strings(o, "gaps"), Str(o, "commentAr") ?? string.Empty, Str(o, "commentEn") ?? string.Empty, call.Value.UsageId);
    }

    /// <summary>Similar earlier findings without a model; the model's opinion when a provider is available (ADR 0137).</summary>
    public async Task<Result<RepeatCheckDto>> RepeatAsync(Guid findingId, CancellationToken ct)
    {
        var finding = await reader.FindingAsync(findingId, context.User, ct).ConfigureAwait(false);
        if (finding is null)
        {
            return Error.NotFound();
        }

        var similar = await queries.SimilarAsync($"{finding.TitleEn ?? finding.TitleAr} {finding.Condition}".Trim(), finding.Id, ct).ConfigureAwait(false);
        if (similar.Count == 0 || !await gateway.IsAvailableAsync(AuditAiFeatures.Repeat, ct).ConfigureAwait(false))
        {
            return new RepeatCheckDto(similar, null, null, null, null);
        }

        var sb = new StringBuilder($"Finding {finding.Number}: {finding.TitleEn ?? finding.TitleAr}\nCondition: {finding.Condition}\nCriteria: {finding.Criteria}\n");
        foreach (var s in similar)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Earlier finding {s.Number} ({s.Year}, {s.Unit?.NameEn ?? s.Unit?.Code}): {s.TitleEn ?? s.TitleAr}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(AuditAiFeatures.Repeat, new Dictionary<string, string>(), [new AiDocument("findings", sb.ToString())],
            RepeatSchema, AuditPermissions.SystemCode, ResourceType: Finding.EntityType, ResourceId: finding.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return new RepeatCheckDto(similar, null, null, null, null);
        }

        var o = call.Value.Output;
        var repeatOf = Str(o, "repeatOf") is { } n && similar.Any(s => s.Number == n) ? n : null;
        return new RepeatCheckDto(similar, repeatOf, Str(o, "rationaleAr"), Str(o, "rationaleEn"), call.Value.UsageId);
    }

    public async Task<Result<AuditSummaryDraftDto>> SummaryAsync(AuditDashboardDto d, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"Findings {d.Findings}: open {d.Open}, late {d.Late}, closed {d.Closed}, risk accepted {d.RiskAccepted}, cancelled {d.Cancelled}, repeat {d.Repeat}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Closure rate {d.ClosureRate:P0}; late recommendations {d.OverdueRecommendations}; reports {d.Reports}\n");
        foreach (var t in d.Trend.TakeLast(6))
        {
            sb.Append(CultureInfo.InvariantCulture, $"Month {t.Month}: opened {t.Opened}, closed {t.Closed}\n");
        }

        foreach (var u in d.ByDepartment.Take(15))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"Department {u.Unit?.NameEn ?? u.Unit?.Code}: findings {u.Findings}, open {u.Open}, late {u.Late}, closure {u.ClosureRate:P0}\n");
        }

        foreach (var f in d.Overdue)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Overdue {f.Priority} finding {f.Number}: {f.TitleEn ?? f.TitleAr} ({f.ResponsibleUnit?.NameEn})\n");
        }

        foreach (var f in d.RiskAcceptedFindings)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Risk accepted: {f.Number} {f.TitleEn ?? f.TitleAr}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(AuditAiFeatures.Summary, new Dictionary<string, string>(), [new AiDocument("figures", sb.ToString())],
            SummarySchema, AuditPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new AuditSummaryDraftDto(Str(o, "summaryAr") ?? string.Empty, Str(o, "summaryEn") ?? string.Empty, call.Value.UsageId);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
