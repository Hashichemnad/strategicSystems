using System.Text.Json;
using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Requests.Ai;

public static class RequestAiFeatures
{
    public const string FormFill = "requests-form-fill";

    public static readonly IReadOnlyList<AiFeatureDefinition> All =
    [
        new(FormFill, "تعبئة نموذج الطلب من مستند", "Fill a request form from a document", RequestPermissions.AiExtract),
    ];
}

/// <summary>Values proposed by AI for the wizard: drafts only, never saved by the server (CLAUDE.md rule 6).</summary>
public sealed record FormFillDto(JsonObject Values, IReadOnlyList<string> Skipped, Guid UsageId);

/// <summary>Document-to-form for every request type (SPEC §5.1, §0.5): the type's simple fields are offered to the model; the answer is validated.</summary>
internal sealed class RequestAiService(
    AiGateway gateway, RequestTypeCache types, RequestReader reader, ILookupService lookups, IOcrEngine ocr, FormResolverFactory resolvers, IRequestContext context)
{
    private static readonly string[] Fillable = [FieldTypes.Text, FieldTypes.Textarea, FieldTypes.Number, FieldTypes.Currency, FieldTypes.Date, FieldTypes.Checkbox, FieldTypes.Picklist];

    public async Task<Result<FormFillDto>> FillAsync(string typeCode, byte[] content, string fileName, CancellationToken ct)
    {
        if (types.Find(typeCode) is not { } type || !await reader.CanSubmitAsync(context.User, type, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var ready = await gateway.PrepareAsync(RequestAiFeatures.FormFill, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var recognition = ready.Value.Settings.Recognition;
        var options = recognition.Enabled && await ocr.IsAvailableAsync(recognition.Languages, ct).ConfigureAwait(false)
            ? new OcrOptions(ocr, recognition.Languages, recognition.MaxPages)
            : null;
        var text = await DocumentTextExtractor.ExtractAsync(content, fileName, options, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var fields = type.Form.AllFields.Where(f => Fillable.Contains(f.Type)).ToList();
        var described = new JsonArray();
        var properties = new JsonObject();
        foreach (var field in fields)
        {
            var item = new JsonObject { ["code"] = field.Code, ["type"] = field.Type, ["label"] = field.LabelEn ?? field.LabelAr };
            if (field.Type == FieldTypes.Picklist)
            {
                item["allowed"] = new JsonArray((await lookups.GetValuesAsync(field.LookupType ?? string.Empty, false, ct).ConfigureAwait(false))
                    .Select(v => (JsonNode?)JsonValue.Create(v.Code)).ToArray());
            }

            described.Add(item);
            properties[field.Code] = field.Type switch
            {
                FieldTypes.Number or FieldTypes.Currency => new JsonObject { ["type"] = new JsonArray("number", "null") },
                FieldTypes.Checkbox => new JsonObject { ["type"] = new JsonArray("boolean", "null") },
                _ => new JsonObject { ["type"] = new JsonArray("string", "null") },
            };
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["values"] = new JsonObject { ["type"] = "object", ["properties"] = properties } },
            ["required"] = new JsonArray("values"),
        };
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(RequestAiFeatures.FormFill,
            new Dictionary<string, string> { ["formName"] = type.NameEn ?? type.Code, ["fields"] = described.ToJsonString() },
            [new AiDocument("document", text.Value.Text)], JsonSerializer.SerializeToElement(schema), RequestPermissions.SystemCode, IsUpload: true,
            AuditDetail: new { type = type.Code, characters = text.Value.Text.Length }), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var proposed = call.Value.Output.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(v.GetRawText()) as JsonObject ?? []
            : [];
        var kept = new JsonObject();
        var skipped = new List<string>();
        foreach (var (key, node) in proposed)
        {
            if (fields.All(f => f.Code != key) || FormDataValidator.IsEmpty(node))
            {
                continue;
            }

            var single = new FormSchema { Sections = [new FormSection { Code = "one", Fields = [fields.First(f => f.Code == key) with { Required = false, VisibleWhen = null }] }] };
            var check = await FormDataValidator.ValidateAsync(single, new JsonObject { [key] = node!.DeepClone() }, false, resolvers.For(context.User, null), ct)
                .ConfigureAwait(false);
            if (check.IsValid && check.Values[key] is { } value)
            {
                kept[key] = value.DeepClone();
            }
            else
            {
                skipped.Add(key);
            }
        }

        return new FormFillDto(kept, skipped, call.Value.UsageId);
    }
}
