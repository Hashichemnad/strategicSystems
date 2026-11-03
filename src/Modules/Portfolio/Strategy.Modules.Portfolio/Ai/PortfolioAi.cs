using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Portfolio.Ai;

/// <summary>AI features of Portfolio Management (SPEC §1.15 A). Drafts and suggestions only (CLAUDE.md rule 6).</summary>
public static class PortfolioAiFeatures
{
    public const string Assessment = "portfolio-assessment";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Assessment, "تقييم مقترح البرنامج", "Program proposal assessment", PortfolioPermissions.AiAssessment),
    ];
}

/// <summary>
/// "Start from a document" (SPEC §1.15 A3): the request form for the shared document-to-form extraction. Lookup choices and one score per
/// configured criterion are added at run time (<see cref="RequestFormChoices"/>, ADR 0050).
/// </summary>
public static class RequestAiForm
{
    public const string Key = "portfolio.request";
    public const string PriorityPrefix = "priority:";

    private static readonly IReadOnlyList<string> YesNo = ["yes", "no"];

    public static readonly AiFormDefinition Definition = new(Key, PortfolioPermissions.SystemCode, "طلب برنامج", "Program request", PortfolioPermissions.RequestCreate,
    [
        new("nameAr", AiFieldTypes.Text, "اسم البرنامج بالعربية", "Program name (Arabic)", "The program's name in Arabic", RequestRules.NameMaxLength),
        new("nameEn", AiFieldTypes.Text, "اسم البرنامج بالإنجليزية", "Program name (English)", "The program's name in English", RequestRules.NameMaxLength),
        new("description", AiFieldTypes.LongText, "وصف البرنامج", "Program description", "What the program is and does", RequestRules.LongTextMaxLength),
        new("justifications", AiFieldTypes.LongText, "مبررات البرنامج", "Program justifications", "Why the program is needed", RequestRules.LongTextMaxLength),
        new("scopeOfWork", AiFieldTypes.LongText, "نطاق العمل", "Scope of work", "What is in scope", RequestRules.LongTextMaxLength),
        new("strategicObjectiveCode", AiFieldTypes.Choice, "الهدف الاستراتيجي", "Strategic objective", "The organisation's objective the program serves."),
        new("suggestionSourceCode", AiFieldTypes.Choice, "مصدر الاقتراح", "Source of suggestion", "Where the idea came from."),
        new("suggestionSourceOther", AiFieldTypes.Text, "مصدر آخر", "Other source", "Only when the source is other", RequestRules.OtherMaxLength),
        new("programTypeCode", AiFieldTypes.Choice, "نوع البرنامج", "Program type", "Digital or non-digital."),
        new("programNature", AiFieldTypes.Choice, "طبيعة البرنامج", "Program nature", "new, or continuous of a previous program", Choices: ProgramNatures.All),
        new("previousProgramName", AiFieldTypes.Text, "اسم البرنامج السابق", "Previous program name", "Only for a continuous program", RequestRules.NameMaxLength),
        new("expectedStartDate", AiFieldTypes.Date, "تاريخ البدء المتوقع", "Expected start date"),
        new("expectedEndDate", AiFieldTypes.Date, "تاريخ الانتهاء المتوقع", "Expected end date"),
        new("requiresBudget", AiFieldTypes.Choice, "يتطلب ميزانية", "Requires budget", "yes when the program needs a budget", Choices: YesNo),
        new("estimatedBudget", AiFieldTypes.Number, "الميزانية التقديرية", "Estimated budget", "Total estimated budget"),
        new("year1Cost", AiFieldTypes.Number, "تكلفة السنة الأولى", "Year 1 cost"),
        new("year2Cost", AiFieldTypes.Number, "تكلفة السنة الثانية", "Year 2 cost"),
        new("year3Cost", AiFieldTypes.Number, "تكلفة السنة الثالثة", "Year 3 cost"),
        new("year4Cost", AiFieldTypes.Number, "تكلفة السنة الرابعة", "Year 4 cost"),
        new("mainOptionCode", AiFieldTypes.Choice, "خيار التنفيذ الرئيسي", "Main delivery option"),
        new("mainAdvantages", AiFieldTypes.LongText, "مزايا الخيار الرئيسي", "Main option advantages", null, RequestRules.TextMaxLength),
        new("mainDisadvantages", AiFieldTypes.LongText, "عيوب الخيار الرئيسي", "Main option disadvantages", null, RequestRules.TextMaxLength),
        new("altOptionCode", AiFieldTypes.Choice, "الخيار البديل", "Alternative delivery option"),
        new("altAdvantages", AiFieldTypes.LongText, "مزايا الخيار البديل", "Alternative option advantages", null, RequestRules.TextMaxLength),
        new("altDisadvantages", AiFieldTypes.LongText, "عيوب الخيار البديل", "Alternative option disadvantages", null, RequestRules.TextMaxLength),
        new("othersBearCosts", AiFieldTypes.Choice, "تتحمل جهات أخرى تكاليف", "Others bear costs", Choices: YesNo),
        new("sharedCostAmount", AiFieldTypes.Number, "مبلغ التكاليف المشتركة", "Shared cost amount"),
        new("partnerName", AiFieldTypes.Text, "الجهة الشريكة", "Partner entity", null, RequestRules.NameMaxLength),
        new("annualOperationCost", AiFieldTypes.Number, "تكلفة التشغيل السنوية", "Annual operation cost"),
        new("annualLicenceCost", AiFieldTypes.Number, "تكلفة الترخيص السنوية", "Annual licence cost"),
        new("annualMaintenanceCost", AiFieldTypes.Number, "تكلفة الصيانة السنوية", "Annual maintenance cost"),
        new("annualOtherCost", AiFieldTypes.Number, "تكاليف سنوية أخرى", "Other annual cost"),
        new("returnExpected", AiFieldTypes.Choice, "عائد مالي متوقع", "Financial return expected", Choices: YesNo),
        new("returnValue", AiFieldTypes.Number, "قيمة العائد المتوقع", "Expected return value"),
        new("returnYear", AiFieldTypes.Number, "سنة العائد", "Return year", "A 4-digit year"),
        new("reductionExpected", AiFieldTypes.Choice, "خفض التكاليف التشغيلية", "Operational cost reduction", Choices: YesNo),
        new("reductionValue", AiFieldTypes.Number, "قيمة الخفض", "Reduction value"),
        new("reductionYear", AiFieldTypes.Number, "سنة الخفض", "Reduction year", "A 4-digit year"),
        new("practiceOperation", AiFieldTypes.Text, "الارتباط بعملية", "Linked operation", null, RequestRules.TextMaxLength),
        new("practiceService", AiFieldTypes.Text, "الارتباط بخدمة", "Linked service", null, RequestRules.TextMaxLength),
        new("practiceProject", AiFieldTypes.Text, "الارتباط بمشروع", "Linked project", null, RequestRules.TextMaxLength),
        new("practiceInitiative", AiFieldTypes.Text, "الارتباط بمبادرة", "Linked initiative", null, RequestRules.TextMaxLength),
        new("screenCoreWork", AiFieldTypes.Choice, "العمل من صميم عمل الوحدة", "Work is core to the unit", Choices: YesNo),
        new("screenOneTime", AiFieldTypes.Choice, "يُنفذ مرة واحدة دون تكرار", "Done once without repetition", Choices: YesNo),
        new("screenTimeBound", AiFieldTypes.Choice, "له فترة زمنية محددة", "Specific time period", Choices: YesNo),
        new("screenUniqueOutput", AiFieldTypes.Choice, "نتائج أو مخرجات فريدة", "Unique results / outputs", Choices: YesNo),
        new("screenMinCost", AiFieldTypes.Choice, "يتطلب الحد الأدنى من التكلفة", "Requires at least the threshold amount", Choices: YesNo),
        new("screenOperationalCosts", AiFieldTypes.Choice, "تكاليف تشغيلية", "Operational costs", Choices: YesNo),
    ]);
}

/// <summary>Active lookup values as choices and one 1–5 score field per active criterion (ADR 0050).</summary>
internal sealed class RequestFormChoices(ILookupService lookups, ISettingsStore settings) : IAiFormChoiceSource
{
    public string FormKey => RequestAiForm.Key;

    public async Task<IReadOnlyDictionary<string, AiFieldChoices>> GetChoicesAsync(CancellationToken cancellationToken)
    {
        async Task<AiFieldChoices> Of(string type)
        {
            var values = (await lookups.GetValuesAsync(type, cancellationToken: cancellationToken).ConfigureAwait(false)).Where(v => v.Active).OrderBy(v => v.Sort).ToList();
            return new AiFieldChoices(values.Select(v => v.Code).ToList(),
                values.Count == 0 ? null : "Codes: " + string.Join("; ", values.Select(v => $"{v.Code} = {v.LabelEn} / {v.LabelAr}")));
        }

        return new Dictionary<string, AiFieldChoices>
        {
            ["strategicObjectiveCode"] = await Of(PortfolioPermissions.ObjectiveLookup).ConfigureAwait(false),
            ["suggestionSourceCode"] = await Of(PortfolioPermissions.SourceLookup).ConfigureAwait(false),
            ["programTypeCode"] = await Of(PortfolioPermissions.ProgramTypeLookup).ConfigureAwait(false),
            ["mainOptionCode"] = await Of(PortfolioPermissions.DeliveryOptionLookup).ConfigureAwait(false),
            ["altOptionCode"] = await Of(PortfolioPermissions.DeliveryOptionLookup).ConfigureAwait(false),
        };
    }

    public async Task<IReadOnlyList<AiFormField>> GetExtraFieldsAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync<PortfolioSettings>(cancellationToken).ConfigureAwait(false)).ActiveCriteria
        .Select(c => new AiFormField(RequestAiForm.PriorityPrefix + c.Code, AiFieldTypes.Number, c.LabelAr, c.LabelEn, "Suggested score from 1 (lowest) to 5 (highest)"))
        .ToList();
}

public sealed record AlignmentItem(string Code, string? LabelAr, string? LabelEn, string Strength, string? Reason);

public sealed record AlignmentDto(IReadOnlyList<AlignmentItem> Objectives, string? Summary);

public sealed record SuggestedClassificationDto(string? Value, string? Reasoning, string Computed);

public sealed record SuggestedPriorityDto(IReadOnlyDictionary<string, int> Scores, string? Reasoning, decimal? Score, string? Band);

public sealed record SimilarRecordDto(string ResourceType, Guid Id, string? Number, string? TitleAr, string? TitleEn, string? Status, string Link, double Score);

/// <summary>The stored Proposal Assessment (docs/design/portfolio.md §8.2).</summary>
public sealed record AssessmentContent(
    string? Summary,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Risks,
    AlignmentDto Alignment,
    SuggestedClassificationDto SuggestedClassification,
    SuggestedPriorityDto SuggestedPriority,
    IReadOnlyList<SimilarRecordDto> Similar,
    IReadOnlyList<string> Recommendations);

public sealed record AssessmentDto(
    Guid Id, int VersionNo, AssessmentContent Content, string? Language, IReadOnlyList<string> Documents, PersonDto? GeneratedBy, DateTimeOffset GeneratedAt);

/// <summary>Generates and reads Proposal Assessment versions (ADR 0050). Every call goes through the gateway (switches, permission, limits, audit).</summary>
internal sealed class AssessmentService(
    AiGateway gateway, PortfolioDbContext db, RequestReader reader, IFileService files, IOcrEngine ocr, IEnumerable<IRecordSearchSource> searchSources,
    ILookupService lookups, IRequestContext context, IClock clock, IUserDirectory users)
{
    public const int MaxDocumentCharacters = 60_000;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly JsonElement Schema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summary","gaps","strengths","risks","alignment","suggestedClassification","suggestedPriority","recommendations"],"properties":{
          "summary":{"type":"string","maxLength":3000},
          "gaps":{"type":"array","maxItems":10,"items":{"type":"string","maxLength":500}},
          "strengths":{"type":"array","maxItems":8,"items":{"type":"string","maxLength":500}},
          "risks":{"type":"array","maxItems":8,"items":{"type":"string","maxLength":500}},
          "alignment":{"type":"object","required":["objectives"],"properties":{
            "objectives":{"type":"array","maxItems":20,"items":{"type":"object","required":["code","strength"],"properties":{
              "code":{"type":"string","maxLength":100},"strength":{"type":"string","enum":["strong","moderate","weak"]},"reason":{"type":["string","null"],"maxLength":500}}}},
            "summary":{"type":["string","null"],"maxLength":1000}}},
          "suggestedClassification":{"type":"object","required":["value"],"properties":{
            "value":{"type":"string","enum":["project","initiative"]},"reasoning":{"type":["string","null"],"maxLength":1000}}},
          "suggestedPriority":{"type":"object","required":["scores"],"properties":{
            "scores":{"type":"object"},"reasoning":{"type":["string","null"],"maxLength":1000}}},
          "recommendations":{"type":"array","maxItems":8,"items":{"type":"string","maxLength":500}}}}
        """);

    public async Task<Result<IReadOnlyList<AssessmentDto>>> ListAsync(Guid requestId, CancellationToken ct)
    {
        var request = await reader.FindAsync(requestId, context.User, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        var rows = await db.Assessments.AsNoTracking().Where(a => a.RequestId == requestId).OrderByDescending(a => a.VersionNo).ToListAsync(ct).ConfigureAwait(false);
        var people = await PeopleOfUsersAsync(rows.Select(r => r.GeneratedByUserId), ct).ConfigureAwait(false);
        return rows.Select(r => ToDto(r, r.GeneratedByUserId is { } u ? people.GetValueOrDefault(u) : null)).ToList();
    }

    public async Task<Result<AssessmentDto>> GenerateAsync(Guid requestId, CancellationToken ct)
    {
        var user = context.User;
        var request = await reader.FindAsync(requestId, user, ct).ConfigureAwait(false);
        if (request is null)
        {
            return Error.NotFound();
        }

        var allowed = (request.Status == RequestStatuses.Draft && await reader.CanAsync(user, PortfolioPermissions.RequestEdit, request, ct).ConfigureAwait(false))
            || (!RequestStatuses.IsFinal(request.Status) && await reader.CanAsync(user, PortfolioPermissions.RequestManage, request, ct).ConfigureAwait(false));
        if (!allowed)
        {
            return Error.Forbidden();
        }

        var ready = await gateway.PrepareAsync(PortfolioAiFeatures.Assessment, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var objectives = (await lookups.GetValuesAsync(PortfolioPermissions.ObjectiveLookup, cancellationToken: ct).ConfigureAwait(false)).Where(v => v.Active).ToList();
        var (documents, names) = await DocumentsAsync(request, ready.Value.Settings, ct).ConfigureAwait(false);
        documents.Insert(0, new AiDocument("request", Describe(request, objectives, settings)));
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            PortfolioAiFeatures.Assessment,
            new Dictionary<string, string>
            {
                ["objectives"] = objectives.Count == 0 ? "(none configured)" : string.Join("; ", objectives.Select(o => $"{o.Code} = {o.LabelEn} / {o.LabelAr}")),
                ["threshold"] = settings.ThresholdAmount.ToString("0.##", CultureInfo.InvariantCulture),
                ["classification"] = request.EffectiveClassification,
                ["criteria"] = string.Join("; ", settings.ActiveCriteria.Select(c => $"{c.Code} = {c.LabelEn} / {c.LabelAr}")),
            },
            documents, Schema, PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.RequestResource, ResourceId: request.Id.ToString("D"),
            AuditDetail: new { request = request.Number, documents = names.Count }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var similar = await SimilarAsync(request, user, ct).ConfigureAwait(false);
        var content = PostProcess(call.Value.Output, objectives, settings, request.EffectiveClassification, similar);
        var next = (await db.Assessments.Where(a => a.RequestId == request.Id).MaxAsync(a => (int?)a.VersionNo, ct).ConfigureAwait(false) ?? 0) + 1;
        var row = ProposalAssessment.Create(request.Id, next, JsonSerializer.Serialize(content, Json), ready.Value.Language, JsonSerializer.Serialize(names, Json),
            call.Value.UsageId, user.UserId(), clock.UtcNow);
        db.Assessments.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var people = await PeopleOfUsersAsync([row.GeneratedByUserId], ct).ConfigureAwait(false);
        return ToDto(row, row.GeneratedByUserId is { } u ? people.GetValueOrDefault(u) : null);
    }

    /// <summary>Never trusts the model: unknown objective / criterion codes dropped, scores clamped to 1–5, the priority recomputed by the calculator.</summary>
    internal static AssessmentContent PostProcess(
        JsonElement o, IReadOnlyList<LookupValueReference> objectives, PortfolioSettings settings, string computed, IReadOnlyList<SimilarRecordDto> similar)
    {
        var alignment = new List<AlignmentItem>();
        string? alignmentSummary = null;
        if (o.TryGetProperty("alignment", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            alignmentSummary = Str(a, "summary");
            if (a.TryGetProperty("objectives", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var code = Str(item, "code");
                    var objective = objectives.FirstOrDefault(x => x.Code == code);
                    var strength = Str(item, "strength");
                    if (objective is null || strength is not ("strong" or "moderate" or "weak") || alignment.Any(x => x.Code == code))
                    {
                        continue;
                    }

                    alignment.Add(new AlignmentItem(objective.Code, objective.LabelAr, objective.LabelEn, strength, Str(item, "reason")));
                }
            }
        }

        string? suggested = null;
        string? classificationReason = null;
        if (o.TryGetProperty("suggestedClassification", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            suggested = Str(c, "value") is { } v && Classifications.All.Contains(v) ? v : null;
            classificationReason = Str(c, "reasoning");
        }

        var scores = new Dictionary<string, int>();
        string? priorityReason = null;
        if (o.TryGetProperty("suggestedPriority", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            priorityReason = Str(p, "reasoning");
            if (p.TryGetProperty("scores", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in s.EnumerateObject())
                {
                    if (settings.ActiveCriteria.Any(x => x.Code == property.Name) && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetDecimal(out var score))
                    {
                        scores[property.Name] = (int)Math.Clamp(Math.Round(score), PriorityCalculator.MinScore, PriorityCalculator.MaxScore);
                    }
                }
            }
        }

        var total = PriorityCalculator.Score(scores, settings.ActiveCriteria);
        return new AssessmentContent(
            Str(o, "summary"), List(o, "gaps"), List(o, "strengths"), List(o, "risks"), new AlignmentDto(alignment, alignmentSummary),
            new SuggestedClassificationDto(suggested, classificationReason, computed),
            new SuggestedPriorityDto(scores, priorityReason, total, PriorityCalculator.Band(total, settings.PriorityBands)?.Code), similar, List(o, "recommendations"));
    }

    private async Task<(List<AiDocument> Documents, List<string> Names)> DocumentsAsync(ProgramRequest request, AiSettings aiSettings, CancellationToken ct)
    {
        var documents = new List<AiDocument>();
        var names = new List<string>();
        var recognition = aiSettings.Recognition;
        var ocrOptions = recognition.Enabled && await ocr.IsAvailableAsync(recognition.Languages, ct).ConfigureAwait(false)
            ? new OcrOptions(ocr, recognition.Languages, recognition.MaxPages)
            : null;
        var remaining = MaxDocumentCharacters;
        var attached = await files.ListAsync(PortfolioPermissions.RequestResource, request.Id.ToString("D"), RequestFilePurposes.Document.Key, ct).ConfigureAwait(false);
        foreach (var file in attached.OrderBy(f => f.UpdatedAt).Take(10))
        {
            if (remaining <= 0)
            {
                break;
            }

            var opened = await files.OpenAsync(file.Id, auditDownload: false, cancellationToken: ct).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                continue;
            }

            byte[] bytes;
            await using (opened.Value.Stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await opened.Value.Stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }

            var text = await DocumentTextExtractor.ExtractAsync(bytes, file.Name, ocrOptions, ct).ConfigureAwait(false);
            if (text.IsFailure || string.IsNullOrWhiteSpace(text.Value.Text))
            {
                continue;
            }

            var content = text.Value.Text.Length > remaining ? text.Value.Text[..remaining] : text.Value.Text;
            remaining -= content.Length;
            documents.Add(new AiDocument($"document-{names.Count + 1}", content));
            names.Add(file.Name);
        }

        return (documents, names);
    }

    private async Task<IReadOnlyList<SimilarRecordDto>> SimilarAsync(ProgramRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var name = request.NameEn ?? request.NameAr;
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var hits = new List<RecordSearchHit>();
        foreach (var source in searchSources.Where(s => s.SystemCode == PortfolioPermissions.SystemCode))
        {
            hits.AddRange(await source.SimilarAsync(name, request.Id, user, 5, ct).ConfigureAwait(false));
            if (request.NameAr is { } arabic && request.NameEn is not null)
            {
                hits.AddRange(await source.SimilarAsync(arabic, request.Id, user, 5, ct).ConfigureAwait(false));
            }
        }

        return hits.Where(h => h.Id != request.Id && h.Id != request.ProjectId).GroupBy(h => h.Id).Select(g => g.OrderByDescending(h => h.Score).First())
            .OrderByDescending(h => h.Score).Take(8)
            .Select(h => new SimilarRecordDto(h.ResourceType, h.Id, h.Number, h.TitleAr, h.TitleEn, h.Status, h.Link, h.Score)).ToList();
    }

    /// <summary>The request's values as "Label: value" lines (untrusted data for the prompt).</summary>
    internal static string Describe(ProgramRequest r, IReadOnlyList<LookupValueReference> objectives, PortfolioSettings settings)
    {
        var text = new StringBuilder();
        void Line(string label, object? value)
        {
            var formatted = value switch
            {
                null => null,
                bool b => b ? "Yes" : "No",
                decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (!string.IsNullOrWhiteSpace(formatted))
            {
                text.Append(label).Append(": ").Append(formatted).Append('\n');
            }
        }

        var objective = objectives.FirstOrDefault(o => o.Code == r.StrategicObjectiveCode);
        Line("Request number", r.Number);
        Line("Program name (Arabic)", r.NameAr);
        Line("Program name (English)", r.NameEn);
        Line("Description", r.Description);
        Line("Justifications", r.Justifications);
        Line("Scope of work", r.ScopeOfWork);
        Line("Strategic objective", objective is null ? r.StrategicObjectiveCode : $"{objective.Code} ({objective.LabelEn} / {objective.LabelAr})");
        Line("Source of suggestion", r.SuggestionSourceOther ?? r.SuggestionSourceCode);
        Line("Program type", r.ProgramTypeCode);
        Line("Program nature", r.ProgramNature);
        Line("Previous program", r.PreviousProgramName);
        Line("Expected start", r.ExpectedStartDate);
        Line("Expected end", r.ExpectedEndDate);
        Line("Requires budget", r.RequiresBudget);
        Line("Estimated budget", r.EstimatedBudget);
        Line("Year 1 cost", r.Year1Cost);
        Line("Year 2 cost", r.Year2Cost);
        Line("Year 3 cost", r.Year3Cost);
        Line("Year 4 cost", r.Year4Cost);
        Line("Main delivery option", r.MainOptionOther ?? r.MainOptionCode);
        Line("Main option advantages", r.MainAdvantages);
        Line("Main option disadvantages", r.MainDisadvantages);
        Line("Alternative option", r.AltOptionOther ?? r.AltOptionCode);
        Line("Others bear costs", r.OthersBearCosts);
        Line("Partner", r.PartnerName);
        Line("Annual operation cost", r.AnnualOperationCost);
        Line("Annual licence cost", r.AnnualLicenceCost);
        Line("Annual maintenance cost", r.AnnualMaintenanceCost);
        Line("Financial return expected", r.ReturnExpected);
        Line("Expected return", r.ReturnValue);
        Line("Cost reduction expected", r.ReductionExpected);
        Line("Linked operation", r.PracticeOperation);
        Line("Linked service", r.PracticeService);
        Line("Screening: core work / one time / time bound / unique output / min cost / operational costs",
            string.Join(" / ", r.ToValues().ScreeningAnswers.Select(x => x is null ? "?" : x.Value ? "Yes" : "No")));
        foreach (var (code, score) in r.ReadScores())
        {
            Line($"Priority score {settings.PriorityCriteria.FirstOrDefault(c => c.Code == code)?.LabelEn ?? code}", score);
        }

        return text.ToString();
    }

    private static AssessmentDto ToDto(ProposalAssessment row, PersonDto? by) => new(
        row.Id, row.VersionNo, JsonSerializer.Deserialize<AssessmentContent>(row.Content, Json)!, row.Language,
        JsonSerializer.Deserialize<List<string>>(row.Documents, Json) ?? [], by, row.GeneratedAt);

    private async Task<Dictionary<Guid, PersonDto>> PeopleOfUsersAsync(IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await users.GetManyAsync(ids, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(found.Values.Select(u => u.EmployeeId), ct).ConfigureAwait(false);
        return found.Values.Where(u => u.EmployeeId is { } e && people.ContainsKey(e)).ToDictionary(u => u.Id, u => people[u.EmployeeId!.Value]);
    }

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static List<string> List(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())).Select(x => x.GetString()!.Trim()).ToList()
            : [];
}

/// <summary>Request content for the approver brief, Ask and <c>get_record</c> (404 outside the read scope).</summary>
internal sealed class RequestRecordSource(RequestReader reader, PortfolioDbContext db) : IAiRecordSource
{
    public string ResourceType => PortfolioPermissions.RequestResource;

    public async Task<AiRecordContent?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var r = await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false);
        if (r is null)
        {
            return null;
        }

        var latest = await db.Assessments.AsNoTracking().Where(a => a.RequestId == id).OrderByDescending(a => a.VersionNo).Select(a => a.Content)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var summary = latest is null ? null : JsonSerializer.Deserialize<AssessmentContent>(latest, AssessmentService.Json)?.Summary;
        string? Money(decimal? d) => d?.ToString("0.##", CultureInfo.InvariantCulture);
        var people = await reader.PeopleAsync([r.RequesterEmployeeId], cancellationToken).ConfigureAwait(false);
        return new AiRecordContent(ResourceType, r.Id, PortfolioPermissions.SystemCode, ProgramRequest.EntityType, r.Number, r.NameAr, r.NameEn, $"/portfolio/requests/{r.Id}",
        [
            new(nameof(ProgramRequest.NameAr), "اسم البرنامج بالعربية", "Program name (Arabic)", r.NameAr),
            new(nameof(ProgramRequest.NameEn), "اسم البرنامج بالإنجليزية", "Program name (English)", r.NameEn),
            new(nameof(ProgramRequest.Description), "الوصف", "Description", r.Description),
            new(nameof(ProgramRequest.Justifications), "المبررات", "Justifications", r.Justifications),
            new(nameof(ProgramRequest.ScopeOfWork), "نطاق العمل", "Scope of work", r.ScopeOfWork),
            new(nameof(ProgramRequest.StrategicObjectiveCode), "الهدف الاستراتيجي", "Strategic objective", r.StrategicObjectiveCode),
            new(nameof(ProgramRequest.RequiresBudget), "يتطلب ميزانية", "Requires budget", r.RequiresBudget?.ToString()),
            new(nameof(ProgramRequest.EstimatedBudget), "الميزانية التقديرية", "Estimated budget", Money(r.EstimatedBudget)),
            new(nameof(ProgramRequest.ApprovedBudget), "الميزانية المعتمدة", "Approved budget", Money(r.ApprovedBudget)),
            new(nameof(ProgramRequest.ExpectedStartDate), "تاريخ البدء", "Expected start", r.ExpectedStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new(nameof(ProgramRequest.ExpectedEndDate), "تاريخ الانتهاء", "Expected end", r.ExpectedEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new(nameof(ProgramRequest.Classification), "التصنيف", "Classification", r.EffectiveClassification),
            new(nameof(ProgramRequest.EvaluationScore), "درجة التقييم", "Evaluation score", r.EvaluationScore is { } s ? (s * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %" : null),
            new(nameof(ProgramRequest.PriorityBand), "الأولوية", "Priority", r.PriorityBand),
            new(nameof(ProgramRequest.Status), "الحالة", "Status", r.Status),
            new("Assessment", "ملخص تقييم المقترح", "Proposal assessment summary", summary),
        ],
        people.Values.SelectMany(p => new[] { p.NameAr, p.NameEn }).Where(n => n is not null).Select(n => n!).Distinct().ToList());
    }
}

/// <summary>Search and similar records for requests (scope applied in SQL; trigram similarity without a model).</summary>
internal sealed class RequestSearchSource(RequestReader reader) : IRecordSearchSource
{
    public string ResourceType => PortfolioPermissions.RequestResource;

    public string SystemCode => PortfolioPermissions.SystemCode;

    public string ReadPermission => PortfolioPermissions.RequestRead;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var query = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await query.Where(r => EF.Functions.ILike(r.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(r.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.Number, pattern, "\\"))
            .OrderByDescending(r => r.UpdatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => Hit(r, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var needle = SearchText.Normalise(text);
        var query = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await query.Where(r => (excludeId == null || r.Id != excludeId) && r.Status != RequestStatuses.Cancelled)
            .Select(r => new { Request = r, Score = Math.Max(EF.Functions.TrigramsSimilarity(r.NameAr ?? string.Empty, needle), EF.Functions.TrigramsSimilarity(r.NameEn ?? string.Empty, needle)) })
            .Where(x => x.Score >= 0.3).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => Hit(x.Request, Math.Round(x.Score, 2))).ToList();
    }

    private RecordSearchHit Hit(ProgramRequest r, double score) =>
        new(ResourceType, r.Id, SystemCode, r.Number, r.NameAr, r.NameEn, r.Status, $"/portfolio/requests/{r.Id}", score);
}

/// <summary>Search and similar records for projects and initiatives.</summary>
internal sealed class ProjectSearchSource(RequestReader reader) : IRecordSearchSource
{
    public string ResourceType => PortfolioPermissions.ProjectResource;

    public string SystemCode => PortfolioPermissions.SystemCode;

    public string ReadPermission => PortfolioPermissions.ProjectRead;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var query = await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await query.Where(p => EF.Functions.ILike(p.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(p.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(p.Number, pattern, "\\"))
            .OrderByDescending(p => p.UpdatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(p => Hit(p, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var needle = SearchText.Normalise(text);
        var query = await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await query.Where(p => excludeId == null || p.Id != excludeId)
            .Select(p => new { Project = p, Score = Math.Max(EF.Functions.TrigramsSimilarity(p.NameAr ?? string.Empty, needle), EF.Functions.TrigramsSimilarity(p.NameEn ?? string.Empty, needle)) })
            .Where(x => x.Score >= 0.3).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => Hit(x.Project, Math.Round(x.Score, 2))).ToList();
    }

    private RecordSearchHit Hit(Project p, double score) =>
        new(ResourceType, p.Id, SystemCode, p.Number, p.NameAr, p.NameEn, p.Stage, $"/portfolio/projects/{p.Id}", score);
}
