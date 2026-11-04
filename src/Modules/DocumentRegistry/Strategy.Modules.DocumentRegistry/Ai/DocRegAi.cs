using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Ai;

/// <summary>AI features of the Document Registry (SPEC §6.6). Suggestions and drafts only; every document read goes through the visibility rule.</summary>
public static class DocRegAiFeatures
{
    public const string Classify = "docreg-classify";
    public const string Summary = "docreg-summary";
    public const string Duplicates = "docreg-duplicates";
    public const string WhatChanged = "docreg-what-changed";
    public const string Qa = "docreg-qa";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Classify, "التصنيف الآلي للوثائق", "Document classification", DocRegPermissions.AiClassify),
        new(Summary, "ملخص الوثيقة", "Document summary", DocRegPermissions.AiSummary),
        new(Duplicates, "فحص التكرار والتعارض", "Duplicate / conflict check", DocRegPermissions.AiDuplicates),
        new(WhatChanged, "ما الذي تغير بين الإصدارين", "What changed between versions", DocRegPermissions.AiWhatChanged),
        new(Qa, "أسئلة وأجوبة على الوثائق", "Questions over documents", DocRegPermissions.AiQa),
    ];
}

public sealed record ClassificationSuggestionDto(string? Level, string? DocType, string? Sensitivity, IReadOnlyList<string> Reasons, Guid UsageId);

public sealed record SummaryDraftDto(string SummaryAr, string SummaryEn, Guid UsageId);

public sealed record OverlapDto(Guid DocumentId, string? DocumentNo, string? NameAr, string? NameEn, string Kind, string Explanation);

public sealed record DuplicateCheckDto(IReadOnlyList<OverlapDto> Findings, int Compared, Guid? UsageId);

public sealed record AnswerSourceDto(Guid DocumentId, string? DocumentNo, string? NameAr, string? NameEn, string Link);

public sealed record AnswerDto(string Answer, IReadOnlyList<AnswerSourceDto> Sources, Guid UsageId);

public sealed record QuestionInput(string? Question);

internal sealed class DocRegAiService(
    AiGateway gateway, DocRegDbContext db, DocumentVisibility visibility, DocumentService documents, DocumentSearchSource search, IRequestDirectory requests,
    ILookupService lookups, IRequestContext context)
{
    private const int PassageLength = 6000;

    /// <summary>Level, type and sensitivity suggested from the draft of a submission request the user can see (reviewer's dialog).</summary>
    public async Task<Result<ClassificationSuggestionDto>> ClassifyAsync(Guid requestId, CancellationToken ct)
    {
        if (await requests.GetAsync(requestId, context.User, ct).ConfigureAwait(false) is null
            || await db.Versions.AsNoTracking().FirstOrDefaultAsync(v => v.RequestId == requestId, ct).ConfigureAwait(false) is not { } version)
        {
            return Error.NotFound();
        }

        var text = await documents.TextOfAsync(version, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error.Rule("docreg.ai.noText", "docreg.errors.noText");
        }

        var levels = await CodesAsync(DocRegPermissions.LevelLookup, ct).ConfigureAwait(false);
        var types = await CodesAsync(DocRegPermissions.TypeLookup, ct).ConfigureAwait(false);
        var sensitivities = await CodesAsync(DocRegPermissions.SensitivityLookup, ct).ConfigureAwait(false);
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(DocRegAiFeatures.Classify,
            new Dictionary<string, string> { ["levels"] = string.Join(", ", levels), ["types"] = string.Join(", ", types), ["sensitivities"] = string.Join(", ", sensitivities) },
            [new AiDocument("draft", Trim(text))], ClassifySchema, DocRegPermissions.SystemCode, ResourceType: DocumentVersion.EntityType, ResourceId: version.Id.ToString("D")),
            ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new ClassificationSuggestionDto(Pick(o, "level", levels), Pick(o, "docType", types), Pick(o, "sensitivity", sensitivities), Strings(o, "reasons"),
            call.Value.UsageId);
    }

    public async Task<Result<SummaryDraftDto>> SummaryAsync(Guid documentId, CancellationToken ct)
    {
        var text = await CurrentTextAsync(documentId, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(DocRegAiFeatures.Summary, new Dictionary<string, string>(),
            [new AiDocument("document", Trim(text.Value))], SummarySchema, DocRegPermissions.SystemCode, ResourceType: Document.EntityType, ResourceId: documentId.ToString("D")),
            ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new SummaryDraftDto(Str(call.Value.Output, "summaryAr") ?? string.Empty, Str(call.Value.Output, "summaryEn") ?? string.Empty,
            call.Value.UsageId);
    }

    /// <summary>Duplicate / conflict check of a submission's draft against published documents the reviewer may see.</summary>
    public async Task<Result<DuplicateCheckDto>> DuplicatesAsync(Guid requestId, CancellationToken ct)
    {
        if (await requests.GetAsync(requestId, context.User, ct).ConfigureAwait(false) is null
            || await db.Versions.AsNoTracking().FirstOrDefaultAsync(v => v.RequestId == requestId, ct).ConfigureAwait(false) is not { } version)
        {
            return Error.NotFound();
        }

        var document = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == version.DocumentId, ct).ConfigureAwait(false);
        var draft = await documents.TextOfAsync(version, ct).ConfigureAwait(false) ?? string.Empty;
        var candidates = await search.SimilarAsync($"{document.NameAr} {document.NameEn} {document.Description} {Trim(draft, 2000)}", document.Id, context.User, 5, ct)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return new DuplicateCheckDto([], 0, null);
        }

        var docs = new List<AiDocument> { new("draft", Trim(draft)) };
        var rows = await db.Documents.AsNoTracking().Where(d => candidates.Select(c => c.Id).Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            docs.Add(new AiDocument(row.Id.ToString("D"), $"{row.DocumentNo} {row.NameEn ?? row.NameAr}\n{Trim(await CurrentTextOfAsync(row, ct).ConfigureAwait(false) ?? row.Description ?? string.Empty, 3000)}"));
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(DocRegAiFeatures.Duplicates, new Dictionary<string, string>(), docs, DuplicatesSchema,
            DocRegPermissions.SystemCode, ResourceType: DocumentVersion.EntityType, ResourceId: version.Id.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var findings = new List<OverlapDto>();
        if (call.Value.Output.TryGetProperty("findings", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (Guid.TryParse(Str(item, "documentId"), out var id) && rows.FirstOrDefault(r => r.Id == id) is { } match)
                {
                    findings.Add(new OverlapDto(id, match.DocumentNo, match.NameAr, match.NameEn, Str(item, "kind") is "duplicate" or "conflict" ? Str(item, "kind")! : "related",
                        Str(item, "explanation") ?? string.Empty));
                }
            }
        }

        return new DuplicateCheckDto(findings, rows.Count, call.Value.UsageId);
    }

    public async Task<Result<SummaryDraftDto>> WhatChangedAsync(Guid documentId, Guid from, Guid to, CancellationToken ct)
    {
        var compare = await documents.CompareAsync(documentId, from, to, ct).ConfigureAwait(false);
        if (compare.IsFailure)
        {
            return compare.Error;
        }

        var diff = new StringBuilder();
        foreach (var part in compare.Value.Parts.Where(p => p.Kind != "same"))
        {
            diff.Append(part.Kind == "added" ? "[added] " : "[removed] ").AppendLine(part.Text);
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(DocRegAiFeatures.WhatChanged,
            new Dictionary<string, string> { ["from"] = compare.Value.FromLabel ?? "?", ["to"] = compare.Value.ToLabel ?? "?" },
            [new AiDocument("differences", Trim(diff.Length == 0 ? "No textual differences." : diff.ToString()))], SummarySchema, DocRegPermissions.SystemCode,
            ResourceType: Document.EntityType, ResourceId: documentId.ToString("D")), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new SummaryDraftDto(Str(call.Value.Output, "summaryAr") ?? string.Empty, Str(call.Value.Output, "summaryEn") ?? string.Empty,
            call.Value.UsageId);
    }

    /// <summary>Q&amp;A over published documents the user may see (retrieval by the visibility rule, ADR 0083), with links to the sources.</summary>
    public async Task<Result<AnswerDto>> AskAsync(QuestionInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Question) || input.Question.Length > 1000)
        {
            return Error.Validation([new FieldError("question", "validation.notEmpty", "validation.notEmpty")]);
        }

        var hits = await search.SimilarAsync(input.Question, null, context.User, 5, ct).ConfigureAwait(false);
        var ids = hits.Select(h => h.Id).ToList();
        var rows = await (await visibility.VisibleAsync(context.User, ct).ConfigureAwait(false)).Where(d => ids.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        var docs = new List<AiDocument>();
        foreach (var row in rows)
        {
            var text = await CurrentTextOfAsync(row, ct).ConfigureAwait(false) ?? row.Description ?? string.Empty;
            docs.Add(new AiDocument(row.Id.ToString("D"), $"{row.DocumentNo} — {row.NameEn ?? row.NameAr}\n{Trim(text, PassageLength)}"));
        }

        if (docs.Count == 0)
        {
            docs.Add(new AiDocument("none", "No published document matches the question."));
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(DocRegAiFeatures.Qa, new Dictionary<string, string> { ["question"] = input.Question.Trim() }, docs,
            AnswerSchema, DocRegPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var sources = Strings(call.Value.Output, "sources").Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => rows.Any(r => r.Id == g)).Distinct()
            .Select(g => rows.First(r => r.Id == g)).Select(r => new AnswerSourceDto(r.Id, r.DocumentNo, r.NameAr, r.NameEn, Contracts.DocumentResource.Link(r.Id))).ToList();
        return new AnswerDto(Str(call.Value.Output, "answer") ?? string.Empty, sources, call.Value.UsageId);
    }

    private async Task<Result<string>> CurrentTextAsync(Guid documentId, CancellationToken ct)
    {
        var document = await visibility.FindAsync(documentId, context.User, ct).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        var text = await CurrentTextOfAsync(document, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? Error.Rule("docreg.ai.noText", "docreg.errors.noText") : text;
    }

    private async Task<string?> CurrentTextOfAsync(Document document, CancellationToken ct) =>
        document.CurrentVersionId is { } id && await db.Versions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct).ConfigureAwait(false) is { } version
            ? await documents.TextOfAsync(version, ct).ConfigureAwait(false)
            : null;

    private async Task<IReadOnlyList<string>> CodesAsync(string type, CancellationToken ct) =>
        (await lookups.GetValuesAsync(type, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();

    private static string Trim(string text, int max = 20_000) => text.Length > max ? text[..max] : text;

    private static string? Pick(JsonElement o, string name, IReadOnlyList<string> allowed) => Str(o, name) is { } v && allowed.Contains(v) ? v : null;

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Take(10).ToList()
            : [];

    private static readonly JsonElement ClassifySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"level":{"type":["string","null"]},"docType":{"type":["string","null"]},"sensitivity":{"type":["string","null"]},
        "reasons":{"type":"array","items":{"type":"string"}}},"required":["level","docType","sensitivity","reasons"]}
        """);

    private static readonly JsonElement SummarySchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"summaryAr":{"type":"string"},"summaryEn":{"type":"string"}},"required":["summaryAr","summaryEn"]}
        """);

    private static readonly JsonElement DuplicatesSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"findings":{"type":"array","items":{"type":"object","properties":{"documentId":{"type":"string"},
        "kind":{"type":"string","enum":["duplicate","conflict","related"]},"explanation":{"type":"string"}},"required":["documentId","kind","explanation"]}}},
        "required":["findings"]}
        """);

    private static readonly JsonElement AnswerSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"answer":{"type":"string"},"sources":{"type":"array","items":{"type":"string"}}},"required":["answer","sources"]}
        """);
}
