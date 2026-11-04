using System.Text.Json;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.IpRegistry.Ai;

/// <summary>AI of the IP Registry (SPEC §7.5): suggestions and drafts only.</summary>
public static class IpAiFeatures
{
    public const string Suggest = "ip-suggest";
    public const string Similarity = "ip-similarity";
    public const string MoetDescription = "ip-moet-description";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(Suggest, "اقتراح نوع الملكية والحماية", "Suggest property and protection type", IpPermissions.AiSuggest),
        new(Similarity, "فحص تشابه الملكية الفكرية", "IP similarity check", IpPermissions.AiSimilarity, NeedsProvider: false),
        new(MoetDescription, "وصف العمل لنموذج الوزارة", "MOET description of the work", IpPermissions.AiMoet),
    ];
}

public sealed record TypeSuggestionDto(string? PropertyType, string? ProtectionType, IReadOnlyList<string> Reasons, Guid UsageId);

public sealed record SimilarItemDto(string ResourceType, Guid Id, string? Number, string? TitleAr, string? TitleEn, string Link, double Score);

public sealed record SimilarityDto(IReadOnlyList<SimilarItemDto> Items);

public sealed record MoetDraftDto(string DescriptionAr, string DescriptionEn, Guid UsageId);

public sealed record IpSuggestInput(string? Title, string? Description);

internal sealed class IpAiService(AiGateway gateway, IpService ip, IpSearchSource search, IDocumentDirectory documents, ILookupService lookups, IRequestContext context)
{
    public async Task<Result<TypeSuggestionDto>> SuggestAsync(IpSuggestInput input, CancellationToken ct)
    {
        var text = $"{input.Title}\n{input.Description}".Trim();
        if (text.Length < 10 || text.Length > 20_000)
        {
            return Error.Validation([new FieldError("description", "validation.notEmpty", "validation.notEmpty")]);
        }

        var properties = (await lookups.GetValuesAsync(IpPermissions.PropertyTypeLookup, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var protections = (await lookups.GetValuesAsync(IpPermissions.ProtectionTypeLookup, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(IpAiFeatures.Suggest,
            new Dictionary<string, string> { ["propertyTypes"] = string.Join(", ", properties), ["protectionTypes"] = string.Join(", ", protections) },
            [new AiDocument("work", text)], SuggestSchema, IpPermissions.SystemCode), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var o = call.Value.Output;
        return new TypeSuggestionDto(Str(o, "propertyType") is { } p && properties.Contains(p) ? p : null,
            Str(o, "protectionType") is { } q && protections.Contains(q) ? q : null,
            o.TryGetProperty("reasons", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Take(3).ToList() : [],
            call.Value.UsageId);
    }

    /// <summary>Similar IP records and published documents the user may see (no model needed: the visibility rules of both registers apply).</summary>
    public async Task<Result<SimilarityDto>> SimilarityAsync(IpSuggestInput input, Guid? excludeId, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(IpAiFeatures.Similarity, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var text = $"{input.Title} {input.Description}";
        var records = await search.SimilarAsync(text, excludeId, context.User, 5, ct).ConfigureAwait(false);
        var docs = await documents.SearchAsync(text, context.User, 5, ct).ConfigureAwait(false);
        await gateway.RecordAsync(ready.Value, "ip.record", excludeId?.ToString("D"), true, null, 0, 0, 0, ct, new { records = records.Count, documents = docs.Count })
            .ConfigureAwait(false);
        return new SimilarityDto([
            .. records.Select(r => new SimilarItemDto(r.ResourceType, r.Id, r.Number, r.TitleAr, r.TitleEn, r.Link, r.Score)),
            .. docs.Select(d => new SimilarItemDto(DocumentResource.Type, d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Link, 0.5)),
        ]);
    }

    public async Task<Result<MoetDraftDto>> MoetDescriptionAsync(Guid recordId, CancellationToken ct)
    {
        var record = await ip.GetAsync(recordId, ct).ConfigureAwait(false);
        if (record.IsFailure)
        {
            return record.Error;
        }

        var r = record.Value;
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(IpAiFeatures.MoetDescription,
            new Dictionary<string, string> { ["title"] = r.NameEn ?? r.NameAr ?? r.Number },
            [new AiDocument("work", $"{r.NameAr}\n{r.NameEn}\n{r.Description}")], MoetSchema, IpPermissions.SystemCode, ResourceType: "ip.record",
            ResourceId: recordId.ToString("D")), ct).ConfigureAwait(false);
        return call.IsFailure ? call.Error : new MoetDraftDto(Str(call.Value.Output, "descriptionAr") ?? string.Empty, Str(call.Value.Output, "descriptionEn") ?? string.Empty,
            call.Value.UsageId);
    }

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly JsonElement SuggestSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"propertyType":{"type":["string","null"]},"protectionType":{"type":["string","null"]},"reasons":{"type":"array","items":{"type":"string"}}},
        "required":["propertyType","protectionType","reasons"]}
        """);

    private static readonly JsonElement MoetSchema = ToolRegistrationExtensions.Schema("""
        {"type":"object","properties":{"descriptionAr":{"type":"string"},"descriptionEn":{"type":"string"}},"required":["descriptionAr","descriptionEn"]}
        """);
}
