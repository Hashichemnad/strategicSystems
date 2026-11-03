using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Platform.AI.Features;

/// <summary>One proposed value: never saved by the server; the review UI shows it as a draft (docs/design/ai.md §4.1).</summary>
public sealed record ExtractedFieldDto(string Name, string? Value, double Confidence, string? Source, bool SourceVerified, string? Location);

public sealed record ExtractionResultDto(
    string FormKey, string FileName, string DocumentKind, bool Truncated, string? Language, IReadOnlyList<ExtractedFieldDto> Fields,
    IReadOnlyList<string> Warnings, Guid UsageId);

public sealed record AiFormDto(string Key, string SystemCode, string NameAr, string NameEn, IReadOnlyList<AiFormField> Fields);

/// <summary>Document-to-form extraction: text extraction → prompt → validated JSON → post-processing against the form (ADR 0029, 0030).</summary>
public sealed class ExtractionService(
    IEnumerable<AiFormDefinition> forms, AiGateway gateway, IAccessService access, ISystemState systems, IRequestContext context)
{
    public const double UnverifiedConfidenceCap = 0.3;

    public async Task<Result<AiFormDto>> GetFormAsync(string formKey, CancellationToken ct)
    {
        var form = await FormAsync(formKey, ct).ConfigureAwait(false);
        return form.IsFailure ? form.Error : new AiFormDto(form.Value.Key, form.Value.SystemCode, form.Value.NameAr, form.Value.NameEn, form.Value.Fields);
    }

    public async Task<Result<ExtractionResultDto>> ExtractAsync(string formKey, byte[] content, string fileName, CancellationToken ct)
    {
        var form = await FormAsync(formKey, ct).ConfigureAwait(false);
        if (form.IsFailure)
        {
            return form.Error;
        }

        // Switches and limits before reading the file, so a disabled feature answers the same whatever is uploaded.
        var ready = await gateway.PrepareAsync(AiFeatures.Extract, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var text = DocumentTextExtractor.Extract(content, fileName);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var definition = form.Value;
        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            AiFeatures.Extract,
            new Dictionary<string, string>
            {
                ["formName"] = definition.NameEn,
                ["fields"] = DescribeFields(definition.Fields),
            },
            [new AiDocument("upload", text.Value.Text)],
            Schema(definition),
            definition.SystemCode,
            ResourceType: $"form:{definition.Key}",
            IsUpload: true,
            AuditDetail: new { form = definition.Key, fileName = Short(fileName), size = content.Length, sha256 = Convert.ToHexString(SHA256.HashData(content)), kind = text.Value.Kind }),
            ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var warnings = new List<string>();
        if (text.Value.Truncated)
        {
            warnings.Add("ai.extract.truncated");
        }

        var fields = PostProcess(definition, call.Value.Output, text.Value.Text, warnings);
        var language = call.Value.Output.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        return new ExtractionResultDto(definition.Key, Short(fileName), text.Value.Kind, text.Value.Truncated, language, fields, warnings, call.Value.UsageId);
    }

    private async Task<Result<AiFormDefinition>> FormAsync(string formKey, CancellationToken ct)
    {
        var form = forms.FirstOrDefault(f => f.Key == formKey);
        if (form is null || !await systems.IsEnabledAsync(form.SystemCode, ct).ConfigureAwait(false))
        {
            return Error.NotFound("ai.form.notFound");
        }

        return await access.CanAsync(context.User, form.Permission, cancellationToken: ct).ConfigureAwait(false) ? form : Error.Forbidden();
    }

    /// <summary>The output schema: one entry per proposed field, names limited to the form's fields.</summary>
    public static JsonElement Schema(AiFormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("language", "fields"),
            ["properties"] = new JsonObject
            {
                ["language"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("ar", "en", "mixed") },
                ["fields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = form.Fields.Count,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("name", "value", "confidence", "source"),
                        ["properties"] = new JsonObject
                        {
                            ["name"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. form.Fields.Select(f => (JsonNode)f.Name)]) },
                            ["value"] = new JsonObject { ["type"] = new JsonArray("string", "number", "null") },
                            ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                            ["source"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 400 },
                        },
                    },
                },
            },
        };
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string DescribeFields(IEnumerable<AiFormField> fields) => string.Join('\n', fields.Select(f =>
    {
        var line = new StringBuilder($"- {f.Name} ({f.Type}");
        if (f.MaxLength is { } max)
        {
            line.Append(CultureInfo.InvariantCulture, $", at most {max} characters");
        }

        if (f.Choices is { Count: > 0 } choices)
        {
            line.Append(", one of: ").Append(string.Join(", ", choices));
        }

        line.Append("): ").Append(f.LabelEn).Append(" / ").Append(f.LabelAr);
        if (!string.IsNullOrWhiteSpace(f.Hint))
        {
            line.Append(" — ").Append(f.Hint);
        }

        return line.ToString();
    }));

    /// <summary>Never trusts the model: unknown and duplicate fields dropped, values coerced, snippets verified against the document.</summary>
    public static IReadOnlyList<ExtractedFieldDto> PostProcess(AiFormDefinition form, JsonElement output, string documentText, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(warnings);
        var result = new List<ExtractedFieldDto>();
        if (!output.TryGetProperty("fields", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var sections = Sections(documentText);
        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var field = form.Fields.FirstOrDefault(f => f.Name == name);
            if (field is null || result.Any(r => r.Name == field.Name))
            {
                continue;
            }

            var value = Coerce(field, item.TryGetProperty("value", out var v) ? v : default, warnings);
            if (value is null)
            {
                continue;
            }

            var source = item.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()?.Trim() : null;
            var location = string.IsNullOrEmpty(source) ? null : Locate(sections, source);
            var verified = location is not null;
            var confidence = item.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? Math.Clamp(c.GetDouble(), 0, 1) : 0;
            if (!verified)
            {
                confidence = Math.Min(confidence, UnverifiedConfidenceCap);
                warnings.Add($"ai.extract.sourceNotFound:{field.Name}");
            }

            result.Add(new ExtractedFieldDto(field.Name, value, Math.Round(confidence, 2), source, verified, location?.Length == 0 ? null : location));
        }

        return result;
    }

    internal static string? Coerce(AiFormField field, JsonElement value, List<string> warnings)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var raw = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        switch (field.Type)
        {
            case AiFieldTypes.Number:
                var digits = new string(AsciiDigits(raw).Where(ch => char.IsAsciiDigit(ch) || ch is '.' or '-').ToArray());
                if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return number.ToString(CultureInfo.InvariantCulture);
                }

                warnings.Add($"ai.extract.invalidValue:{field.Name}");
                return null;
            case AiFieldTypes.Date:
                if (DateOnly.TryParse(AsciiDigits(raw), CultureInfo.InvariantCulture, out var date))
                {
                    return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }

                warnings.Add($"ai.extract.invalidValue:{field.Name}");
                return null;
            case AiFieldTypes.Choice:
                var choice = field.Choices?.FirstOrDefault(x => string.Equals(x, raw, StringComparison.OrdinalIgnoreCase));
                if (choice is null)
                {
                    warnings.Add($"ai.extract.invalidValue:{field.Name}");
                }

                return choice;
            default:
                if (field.MaxLength is { } max && raw.Length > max)
                {
                    warnings.Add($"ai.extract.clipped:{field.Name}");
                    return raw[..max].TrimEnd();
                }

                return raw;
        }
    }

    private static string AsciiDigits(string text) => new(text.Select(ch => ch switch
    {
        >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
        >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
        '٫' => '.',
        '٬' => ',',
        _ => ch,
    }).ToArray());

    /// <summary>The document split at its <c>[page n]</c> / <c>[slide n]</c> markers, each normalised for search.</summary>
    internal static IReadOnlyList<(string Location, string Text)> Sections(string text)
    {
        var sections = new List<(string, string)>();
        var current = string.Empty;
        var buffer = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("[page ", StringComparison.Ordinal) || line.StartsWith("[slide ", StringComparison.Ordinal))
            {
                sections.Add((current, SearchText.Normalise(buffer.ToString())));
                buffer.Clear();
                current = line.Trim('[', ']');
                continue;
            }

            buffer.Append(line).Append(' ');
        }

        sections.Add((current, SearchText.Normalise(buffer.ToString())));
        return sections;
    }

    /// <summary>Where the snippet appears ("page 2", "" when the document has no pages), or null when it is not in the document.</summary>
    internal static string? Locate(IReadOnlyList<(string Location, string Text)> sections, string snippet)
    {
        var needle = SearchText.Normalise(snippet.Replace("…", " ", StringComparison.Ordinal).Replace("...", " ", StringComparison.Ordinal));
        if (needle.Length < 2)
        {
            return null;
        }

        foreach (var (location, text) in sections)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return location;
            }
        }

        return null;
    }

    private static string Short(string fileName) => Path.GetFileName(fileName) is { Length: > 200 } name ? name[..200] : Path.GetFileName(fileName);
}
