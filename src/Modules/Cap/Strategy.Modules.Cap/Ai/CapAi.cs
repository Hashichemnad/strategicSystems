using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Cap.Ai;

/// <summary>AI features of CAP (SPEC §9.8, ADR 0129). Drafts only (CLAUDE.md rule 6).</summary>
public static class CapAiFeatures
{
    public const string Extract = "cap-extract";
    public const string RootCause = "cap-root-cause";
    public const string Suggest = "cap-suggest";
    public const string Quality = "cap-quality";
    public const string Summary = "cap-summary";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Extract, "استخراج الملاحظات من تقرير", "Remark extraction from a report", CapPermissions.AiExtract),
        new(RootCause, "مساعد تحليل السبب الجذري", "Root cause assistant", CapPermissions.AiRootCause),
        new(Suggest, "اقتراح الإجراءات وكشف المكرر", "Action suggestions and duplicate check", CapPermissions.AiSuggest),
        new(Quality, "فحص جودة الإجراءات", "Action quality check", CapPermissions.AiQuality),
        new(Summary, "ملخص خطة العمل الشهري", "Monthly CAP summary", CapPermissions.AiSummary),
    ];
}

public sealed record ExtractedRemarkDto(string? TextAr, string? TextEn, string? CriterionCode, string? TypeCode, string? Priority, Guid? DepartmentUnitId);

public sealed record RemarkExtractionDto(IReadOnlyList<ExtractedRemarkDto> Remarks, Guid UsageId);

public sealed record RootCauseCauseDto(string CategoryCode, string Text);

public sealed record RootCauseDraftDto(IReadOnlyList<string> Whys, IReadOnlyList<RootCauseCauseDto> Causes, string? Analysis, Guid UsageId);

public sealed record SuggestedCapActionDto(string? DetailsAr, string? DetailsEn, string? TypeCode, int? DurationDays);

public sealed record DuplicateActionDto(Guid Id, string Number, string? DetailsAr, string? DetailsEn, string Status, double Score);

public sealed record ActionSuggestionDto(IReadOnlyList<SuggestedCapActionDto> Actions, IReadOnlyList<DuplicateActionDto> Duplicates, Guid? UsageId);

public sealed record ActionQualityDto(IReadOnlyList<string> Flags, IReadOnlyList<string> Issues, string? RewriteAr, string? RewriteEn, Guid? UsageId);

public sealed record CapSummaryDraftDto(string SummaryAr, string SummaryEn, string LateUnitsAr, string LateUnitsEn, Guid UsageId);

/// <summary>Every call goes through the AI gateway (switches, permission, limits, masking, audit channel <c>ai</c>).</summary>
internal sealed class CapAiService(
    AiGateway gateway, CapDbContext db, CapReader reader, CapService service, CapDeadlines deadlines, ILookupService lookups, IOrgDirectory org, IOcrEngine ocr,
    IRequestContext context)
{
    private static readonly JsonElement ExtractSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["remarks"],"properties":{"remarks":{"type":"array","maxItems":15,"items":{"type":"object",
          "required":["textAr","textEn","criterionCode","typeCode","priority","departmentCode"],"properties":{
          "textAr":{"type":"string","maxLength":2000},"textEn":{"type":"string","maxLength":2000},"criterionCode":{"type":"string","maxLength":100},
          "typeCode":{"type":"string","maxLength":40},"priority":{"type":"string","maxLength":20},"departmentCode":{"type":"string","maxLength":60}}}}}}
        """);

    private static readonly JsonElement RootCauseSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["whys","causes","analysis"],"properties":{"whys":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":500}},
          "causes":{"type":"array","maxItems":6,"items":{"type":"object","required":["categoryCode","text"],"properties":{
            "categoryCode":{"type":"string","maxLength":40},"text":{"type":"string","maxLength":500}}}},
          "analysis":{"type":"string","maxLength":3000}}}
        """);

    private static readonly JsonElement SuggestSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["actions"],"properties":{"actions":{"type":"array","maxItems":5,"items":{"type":"object",
          "required":["detailsAr","detailsEn","typeCode","durationDays"],"properties":{"detailsAr":{"type":"string","maxLength":1500},
          "detailsEn":{"type":"string","maxLength":1500},"typeCode":{"type":"string","maxLength":40},"durationDays":{"type":"integer"}}}}}}
        """);

    private static readonly JsonElement QualitySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["issues","rewriteAr","rewriteEn"],"properties":{"issues":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":400}},
          "rewriteAr":{"type":"string","maxLength":2000},"rewriteEn":{"type":"string","maxLength":2000}}}
        """);

    private static readonly JsonElement SummarySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summaryAr","summaryEn","lateUnitsAr","lateUnitsEn"],"properties":{"summaryAr":{"type":"string","maxLength":8000},
          "summaryEn":{"type":"string","maxLength":8000},"lateUnitsAr":{"type":"string","maxLength":3000},"lateUnitsEn":{"type":"string","maxLength":3000}}}
        """);

    /// <summary>Report (text and / or uploaded document) → proposed remarks for the PMO to confirm one by one.</summary>
    public async Task<Result<RemarkExtractionDto>> ExtractAsync(Guid sourceId, string? text, byte[]? document, string? fileName, CancellationToken ct)
    {
        if (await reader.SourceAsync(sourceId, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        if (document is { Length: > 0 })
        {
            var ready = await gateway.PrepareAsync(CapAiFeatures.Extract, createClient: false, ct).ConfigureAwait(false);
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

            text = $"{text}\n{extracted.Value.Text}".Trim();
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > DocumentTextExtractor.MaxCharacters)
        {
            return Error.Validation([new FieldError("text", "validation.notEmpty", "validation.notEmpty")]);
        }

        var criteria = (await lookups.GetValuesAsync(CapPermissions.CriterionLookup, cancellationToken: ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var departments = await KnownUnitsAsync(ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(CapAiFeatures.Extract, new Dictionary<string, string>
        {
            ["criteria"] = string.Join(", ", criteria),
            ["departments"] = string.Join("; ", departments.Select(d => $"{d.Value} → {d.Key}")),
        }, [new AiDocument("report", text)], ExtractSchema, CapPermissions.SystemCode, ResourceType: CapSource.EntityType, ResourceId: sourceId.ToString("D"),
            IsUpload: document is not null, AuditDetail: new { characters = text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var unitsByCode = departments.Keys.ToDictionary(k => k, k => k);
        var codeToId = await UnitIdsByCodeAsync(ct).ConfigureAwait(false);
        var remarks = new List<ExtractedRemarkDto>();
        if (call.Value.Output.TryGetProperty("remarks", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in array.EnumerateArray().Take(15))
            {
                var criterion = Str(r, "criterionCode") is { } c && criteria.Contains(c) ? c : null;
                var type = Str(r, "typeCode") is { } t && CapRemarkTypes.All.Contains(t) ? t : CapRemarkTypes.Observation;
                var priority = Str(r, "priority") is { } p && CapPriorities.All.Contains(p) ? p : CapPriorities.Medium;
                Guid? unit = Str(r, "departmentCode") is { } d && codeToId.TryGetValue(d, out var id) ? id : null;
                remarks.Add(new ExtractedRemarkDto(Str(r, "textAr"), Str(r, "textEn"), criterion, type, priority, unit));
            }
        }

        _ = unitsByCode;
        return new RemarkExtractionDto(remarks, call.Value.UsageId);
    }

    public async Task<Result<RootCauseDraftDto>> RootCauseAsync(Guid remarkId, CancellationToken ct)
    {
        var remark = await reader.RemarkAsync(remarkId, context.User, ct).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        var source = await db.Sources.AsNoTracking().FirstAsync(s => s.Id == remark.SourceId, ct).ConfigureAwait(false);
        var text = $"Remark {remark.Number}: {remark.TextEn ?? remark.TextAr}\nArabic: {remark.TextAr}\nType: {remark.TypeCode}\nPriority: {remark.Priority}\n" +
            $"Source ({source.SourceTypeCode}): {source.TitleEn ?? source.TitleAr}\n{source.Description}";
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(CapAiFeatures.RootCause, new Dictionary<string, string>(), [new AiDocument("remark", text)],
            RootCauseSchema, CapPermissions.SystemCode, ResourceType: Remark.EntityType, ResourceId: remark.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        var causes = new List<RootCauseCauseDto>();
        if (o.TryGetProperty("causes", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in array.EnumerateArray().Take(6))
            {
                if (Str(c, "categoryCode") is { } code && RootCauseMethods.FishboneCategories.Contains(code) && Str(c, "text") is { } t)
                {
                    causes.Add(new RootCauseCauseDto(code, t));
                }
            }
        }

        return new RootCauseDraftDto(Strings(o, "whys").Take(5).ToList(), causes, Str(o, "analysis"), call.Value.UsageId);
    }

    /// <summary>Duplicates are found without a model (visible open actions, trigram similarity); suggestions need a provider.</summary>
    public async Task<Result<ActionSuggestionDto>> SuggestAsync(Guid remarkId, CancellationToken ct)
    {
        var remark = await reader.RemarkAsync(remarkId, context.User, ct).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        var duplicates = await DuplicatesAsync(remark, ct).ConfigureAwait(false);
        if (!await gateway.IsAvailableAsync(CapAiFeatures.Suggest, ct).ConfigureAwait(false))
        {
            return new ActionSuggestionDto([], duplicates, null);
        }

        var existing = await service.ActionsOfAsync(remark.Id, ct, tracked: false).ConfigureAwait(false);
        var sb = new StringBuilder($"Remark {remark.Number}: {remark.TextEn ?? remark.TextAr}\nRoot cause: {remark.RootCauseText}\n");
        foreach (var why in remark.RootCause().Whys)
        {
            sb.Append("Why: ").Append(why).Append('\n');
        }

        foreach (var a in existing)
        {
            sb.Append("Existing action of this remark: ").Append(a.DetailsEn ?? a.DetailsAr).Append('\n');
        }

        foreach (var d in duplicates)
        {
            sb.Append("Similar existing action ").Append(d.Number).Append(": ").Append(d.DetailsEn ?? d.DetailsAr).Append('\n');
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(CapAiFeatures.Suggest, new Dictionary<string, string>(), [new AiDocument("remark", sb.ToString())],
            SuggestSchema, CapPermissions.SystemCode, ResourceType: Remark.EntityType, ResourceId: remark.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var actions = new List<SuggestedCapActionDto>();
        if (call.Value.Output.TryGetProperty("actions", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in array.EnumerateArray().Take(5))
            {
                var type = Str(a, "typeCode") is { } t && CapActionTypes.All.Contains(t) ? t : CapActionTypes.Corrective;
                var days = Int(a, "durationDays") is { } d ? Math.Clamp(d, 1, 730) : (int?)null;
                actions.Add(new SuggestedCapActionDto(Str(a, "detailsAr"), Str(a, "detailsEn"), type, days));
            }
        }

        return new ActionSuggestionDto(actions, duplicates, call.Value.UsageId);
    }

    /// <summary>Deterministic flags always; model advice when available (shown on Validate Actions).</summary>
    public async Task<Result<ActionQualityDto>> QualityAsync(Guid actionId, CancellationToken ct)
    {
        var action = await reader.ActionAsync(actionId, context.User, ct).ConfigureAwait(false);
        if (action is null)
        {
            return Error.NotFound();
        }

        var home = await db.Remarks.AsNoTracking().FirstAsync(r => r.Id == action.HomeRemarkId, ct).ConfigureAwait(false);
        var flags = ActionQuality.Flags(action.Values(), await deadlines.TodayAsync(ct).ConfigureAwait(false), home.DueOn);
        if (!await gateway.IsAvailableAsync(CapAiFeatures.Quality, ct).ConfigureAwait(false))
        {
            return new ActionQualityDto(flags, [], null, null, null);
        }

        var text = $"Remark {home.Number}: {home.TextEn ?? home.TextAr}\nAction {action.Number}: {action.DetailsEn ?? action.DetailsAr}\nArabic: {action.DetailsAr}\n" +
            $"Type: {action.TypeCode}\nPlanned: {CapDeadlines.Date(action.PlannedStart)} → {CapDeadlines.Date(action.PlannedEnd)}\nRemark due: {CapDeadlines.Date(home.DueOn)}\n" +
            $"Assigned: {(action.AssignedEmployeeId is null ? "nobody" : "yes")}\nBudget required: {action.BudgetRequired}";
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(CapAiFeatures.Quality, new Dictionary<string, string>(), [new AiDocument("action", text)], QualitySchema,
            CapPermissions.SystemCode, ResourceType: CapAction.EntityType, ResourceId: action.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new ActionQualityDto(flags, Strings(o, "issues"), Str(o, "rewriteAr"), Str(o, "rewriteEn"), call.Value.UsageId);
    }

    public async Task<Result<CapSummaryDraftDto>> SummaryAsync(CapDashboardDto dashboard, string period, CancellationToken ct)
    {
        var sb = new StringBuilder($"Period: {period}\nRemarks: {dashboard.Remarks}, open {dashboard.OpenRemarks}, completed {dashboard.CompletedRemarks}, overdue {dashboard.OverdueRemarks}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Actions: {dashboard.Actions}, late {dashboard.LateActions}, completed {dashboard.CompletedActions}, budget-dependent {dashboard.BudgetActions}\n");
        sb.Append(CultureInfo.InvariantCulture, $"Effectiveness: effective {dashboard.Effective}, not effective {dashboard.NotEffective}, awaiting {dashboard.AwaitingVerification}\n");
        foreach (var u in dashboard.ByDepartment)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"Unit {u.Unit?.NameEn ?? u.Unit?.Code}: remarks {u.Remarks}, open {u.Open}, overdue {u.Overdue}, late actions {u.LateActions}, closure {u.ClosureRate:P0}\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(CapAiFeatures.Summary, new Dictionary<string, string>(), [new AiDocument("figures", sb.ToString())],
            SummarySchema, CapPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new CapSummaryDraftDto(Str(o, "summaryAr") ?? string.Empty, Str(o, "summaryEn") ?? string.Empty, Str(o, "lateUnitsAr") ?? string.Empty,
            Str(o, "lateUnitsEn") ?? string.Empty, call.Value.UsageId);
    }

    private async Task<List<DuplicateActionDto>> DuplicatesAsync(Remark remark, CancellationToken ct)
    {
        var text = remark.TextEn ?? remark.TextAr ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var linked = db.RemarkActions.Where(l => l.RemarkId == remark.Id).Select(l => l.ActionId);
        var visible = await reader.ActionsAsync(context.User, ct).ConfigureAwait(false);
        var hits = await visible.Where(a => !linked.Contains(a.Id) && a.Status != ActionStatuses.Cancelled && a.Status != ActionStatuses.Completed
                && a.ValidationStatus != ValidationStatuses.Rejected)
            .Select(a => new
            {
                Action = a,
                Score = Math.Max(EF.Functions.TrigramsSimilarity(a.DetailsEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(a.DetailsAr ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.15).OrderByDescending(x => x.Score).Take(5).ToListAsync(ct).ConfigureAwait(false);
        return hits.Select(h => new DuplicateActionDto(h.Action.Id, h.Action.Number, h.Action.DetailsAr, h.Action.DetailsEn, h.Action.Status, h.Score)).ToList();
    }

    private async Task<Dictionary<string, string>> KnownUnitsAsync(CancellationToken ct)
    {
        var ids = await db.Remarks.AsNoTracking().Select(r => r.DepartmentUnitId).Distinct().Take(100).ToListAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { Code: { } code } unit)
            {
                result[code] = unit.NameEn ?? unit.NameAr ?? code;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, Guid>> UnitIdsByCodeAsync(CancellationToken ct)
    {
        var ids = await db.Remarks.AsNoTracking().Select(r => r.DepartmentUnitId).Distinct().Take(100).ToListAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { Code: { } code })
            {
                result[code] = id;
            }
        }

        return result;
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
