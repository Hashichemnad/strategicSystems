using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Modules.Requests.Domain;

/// <summary>Checks of references a form value makes (lookups, people, units, files, records) — implemented by the application layer.</summary>
public interface IFormReferenceResolver
{
    Task<bool> IsActiveLookupAsync(string lookupType, string code, CancellationToken ct);

    Task<bool> EmployeeExistsAsync(Guid id, CancellationToken ct);

    Task<bool> UnitExistsAsync(Guid id, CancellationToken ct);

    /// <summary>The file belongs to the request being validated (not deleted).</summary>
    Task<bool> IsRequestFileAsync(Guid fileId, CancellationToken ct);

    /// <summary>The user may read the record (Links registry).</summary>
    Task<bool> CanReadRecordAsync(string targetType, Guid id, CancellationToken ct);
}

/// <summary>Result of a data validation: the normalised values (hidden fields dropped) and the field errors.</summary>
public sealed record FormValidationResult(JsonObject Values, IReadOnlyList<FieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Validates an admin-defined schema before it is saved (docs/design/requests.md §2.2).</summary>
public static partial class FormSchemaValidator
{
    public const int MaxFields = 100;
    public const int MaxColumns = 20;

    public static List<FieldError> Validate(FormSchema schema, string path = "form", IReadOnlySet<string>? knownTargetTypes = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var errors = new List<FieldError>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sectionCodes = new HashSet<string>(StringComparer.Ordinal);
        var earlier = new Dictionary<string, FormField>(StringComparer.Ordinal);
        var count = 0;
        for (var s = 0; s < schema.Sections.Count; s++)
        {
            var section = schema.Sections[s];
            if (!CodePattern().IsMatch(section.Code ?? string.Empty) || !sectionCodes.Add(section.Code ?? string.Empty))
            {
                errors.Add(Error($"{path}.sections[{s}].code", "requests.errors.code"));
            }

            for (var f = 0; f < section.Fields.Count; f++)
            {
                var field = section.Fields[f];
                var fieldPath = $"{path}.sections[{s}].fields[{f}]";
                count++;
                ValidateField(field, fieldPath, errors, seen, earlier, knownTargetTypes, inTable: false);
                earlier[field.Code] = field;
            }
        }

        if (count > MaxFields)
        {
            errors.Add(Error(path, "requests.errors.tooManyFields", new Dictionary<string, object?> { ["max"] = MaxFields }));
        }

        return errors;
    }

    private static void ValidateField(FormField field, string path, List<FieldError> errors, HashSet<string> seen, Dictionary<string, FormField> earlier,
        IReadOnlySet<string>? knownTargetTypes, bool inTable)
    {
        if (!CodePattern().IsMatch(field.Code ?? string.Empty))
        {
            errors.Add(Error($"{path}.code", "requests.errors.code"));
        }
        else if (!seen.Add(field.Code ?? string.Empty))
        {
            errors.Add(Error($"{path}.code", "requests.errors.duplicateCode"));
        }

        if (!(inTable ? FieldTypes.Columns : FieldTypes.All).Contains(field.Type))
        {
            errors.Add(Error($"{path}.type", "requests.errors.fieldType"));
            return;
        }

        if (string.IsNullOrWhiteSpace(field.LabelAr) && string.IsNullOrWhiteSpace(field.LabelEn))
        {
            errors.Add(Error($"{path}.labelEn", "requests.errors.label"));
        }

        if (field.Type is FieldTypes.Picklist or FieldTypes.MultiPicklist && string.IsNullOrWhiteSpace(field.LookupType))
        {
            errors.Add(Error($"{path}.lookupType", "validation.notEmpty"));
        }

        if (field.Type == FieldTypes.Record && (string.IsNullOrWhiteSpace(field.TargetType) || (knownTargetTypes is not null && !knownTargetTypes.Contains(field.TargetType))))
        {
            errors.Add(Error($"{path}.targetType", "validation.unknownCode"));
        }

        if (field.MaxLength is < 1 or > 20000 || field.Decimals is < 0 or > 4 || (field.Min is { } min && field.Max is { } max && min > max))
        {
            errors.Add(Error(path, "requests.errors.fieldOptions"));
        }

        if (field.Type == FieldTypes.Secret && field.Format is not null && field.Format != SecretFormats.EmiratesId)
        {
            errors.Add(Error($"{path}.format", "validation.unknownCode"));
        }

        if (field.Type == FieldTypes.Table)
        {
            var columns = field.Columns ?? [];
            if (columns.Count == 0 || columns.Count > MaxColumns || field.MinRows is < 0 || (field.MinRows is { } a && field.MaxRows is { } b && a > b))
            {
                errors.Add(Error($"{path}.columns", "requests.errors.tableColumns"));
            }

            var columnCodes = new HashSet<string>(StringComparer.Ordinal);
            var columnEarlier = new Dictionary<string, FormField>(StringComparer.Ordinal);
            for (var c = 0; c < columns.Count; c++)
            {
                ValidateField(columns[c], $"{path}.columns[{c}]", errors, columnCodes, columnEarlier, knownTargetTypes, inTable: true);
                columnEarlier[columns[c].Code] = columns[c];
            }
        }

        ValidateCondition(field.VisibleWhen, $"{path}.visibleWhen", errors, earlier);
        ValidateCondition(field.RequiredWhen, $"{path}.requiredWhen", errors, earlier);
    }

    private static void ValidateCondition(FieldCondition? condition, string path, List<FieldError> errors, Dictionary<string, FormField> earlier)
    {
        if (condition is null)
        {
            return;
        }

        if (!earlier.ContainsKey(condition.Field ?? string.Empty) || !FieldCondition.Ops.Contains(condition.Op)
            || (condition.Op == "in" && condition.Value is not JsonArray))
        {
            errors.Add(Error(path, "requests.errors.condition"));
        }
    }

    internal static FieldError Error(string field, string key, IReadOnlyDictionary<string, object?>? parameters = null) => new(field, key, key, parameters);

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]{0,49}$")]
    internal static partial Regex CodePattern();
}

/// <summary>
/// Validates and normalises submitted values against a schema (server side, every save). Drafts check types and lengths; submissions check
/// everything. Hidden fields are not required and are dropped; unknown keys are refused. Error paths: <c>fields.code</c>,
/// <c>fields.table[2].column</c>.
/// </summary>
public static partial class FormDataValidator
{
    public const int TextDefault = 500;
    public const int TextareaDefault = 4000;
    public const int RichTextDefault = 20000;

    /// <summary>Stored size of a rich-text value (HTML, ADR 0161); the field's maximum counts its visible text.</summary>
    public const int RichTextStoredMax = 40000;
    public const int SecretMax = 100;

    public static async Task<FormValidationResult> ValidateAsync(
        FormSchema schema, JsonObject? input, bool submitting, IFormReferenceResolver resolver, CancellationToken ct, string prefix = "fields")
    {
        ArgumentNullException.ThrowIfNull(schema);
        input ??= [];
        var errors = new List<FieldError>();
        var output = new JsonObject();
        var fields = schema.AllFields.ToList();
        foreach (var key in input.Select(p => p.Key))
        {
            if (fields.All(f => f.Code != key))
            {
                errors.Add(FormSchemaValidator.Error($"{prefix}.{key}", "requests.errors.unknownField"));
            }
        }

        foreach (var field in fields)
        {
            if (field.VisibleWhen is { } visible && !Holds(visible, output, input))
            {
                continue;
            }

            var required = field.Required || (field.RequiredWhen is { } rw && Holds(rw, output, input));
            var value = await NormaliseAsync(field, input[field.Code], submitting, submitting && required, $"{prefix}.{field.Code}", errors, resolver, ct).ConfigureAwait(false);
            if (value is not null)
            {
                output[field.Code] = value;
            }
        }

        return new FormValidationResult(output, errors);
    }

    private static async Task<JsonNode?> NormaliseAsync(
        FormField field, JsonNode? raw, bool submitting, bool required, string path, List<FieldError> errors, IFormReferenceResolver resolver, CancellationToken ct)
    {
        var empty = IsEmpty(raw);
        if (empty)
        {
            if (required)
            {
                errors.Add(FormSchemaValidator.Error(path, "validation.notEmpty"));
            }

            return null;
        }

        switch (field.Type)
        {
            case FieldTypes.RichText:
                {
                    if (StringOf(raw) is not { } html)
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    // Sanitised HTML or plain text kept as it is (ADR 0161); the limit counts the visible text.
                    var clean = RichText.Sanitize(html);
                    var max = field.MaxLength ?? RichTextDefault;
                    if (RichText.LengthError(path, clean, max, RichTextStoredMax) is { } tooLong)
                    {
                        errors.Add(tooLong);
                        return null;
                    }

                    if (clean is null)
                    {
                        if (required)
                        {
                            errors.Add(FormSchemaValidator.Error(path, "validation.notEmpty"));
                        }

                        return null;
                    }

                    return JsonValue.Create(clean);
                }

            case FieldTypes.Text:
            case FieldTypes.Textarea:
                {
                    if (StringOf(raw) is not { } text)
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    var max = field.MaxLength ?? (field.Type == FieldTypes.Text ? TextDefault : TextareaDefault);
                    if (text.Trim().Length > max)
                    {
                        errors.Add(FormSchemaValidator.Error(path, "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max }));
                        return null;
                    }

                    return JsonValue.Create(text.Trim());
                }

            case FieldTypes.Secret:
                {
                    if (StringOf(raw) is not { } secret || secret.Trim().Length > SecretMax)
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    secret = secret.Trim();
                    if (field.Format == SecretFormats.EmiratesId && !EmiratesIdPattern().IsMatch(secret))
                    {
                        return Fail(path, "requests.errors.emiratesId", errors);
                    }

                    return JsonValue.Create(secret);
                }

            case FieldTypes.Number:
            case FieldTypes.Currency:
                {
                    if (NumberOf(raw) is not { } number)
                    {
                        return Fail(path, "requests.errors.number", errors);
                    }

                    var decimals = field.Decimals ?? (field.Type == FieldTypes.Currency ? 2 : 4);
                    if (decimal.Round(number, decimals) != number)
                    {
                        return Fail(path, "requests.errors.decimals", errors, new Dictionary<string, object?> { ["decimals"] = decimals });
                    }

                    if ((field.Min is { } min && number < min) || (field.Max is { } max && number > max))
                    {
                        return Fail(path, "requests.errors.range", errors, new Dictionary<string, object?> { ["min"] = field.Min, ["max"] = field.Max });
                    }

                    return JsonValue.Create(number);
                }

            case FieldTypes.Date:
                {
                    if (StringOf(raw) is not { } text || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        return Fail(path, "requests.errors.date", errors);
                    }

                    return JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

            case FieldTypes.Checkbox:
                {
                    if (raw is not JsonValue v || v.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    var flag = v.GetValue<bool>();
                    if (required && !flag)
                    {
                        return Fail(path, "requests.errors.mustConfirm", errors);
                    }

                    return JsonValue.Create(flag);
                }

            case FieldTypes.Picklist:
                {
                    if (StringOf(raw) is not { } code || !await resolver.IsActiveLookupAsync(field.LookupType ?? string.Empty, code, ct).ConfigureAwait(false))
                    {
                        return Fail(path, "validation.unknownCode", errors);
                    }

                    return JsonValue.Create(code);
                }

            case FieldTypes.MultiPicklist:
                {
                    if (raw is not JsonArray array)
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    var codes = new JsonArray();
                    foreach (var item in array)
                    {
                        if (StringOf(item) is not { } code || !await resolver.IsActiveLookupAsync(field.LookupType ?? string.Empty, code, ct).ConfigureAwait(false))
                        {
                            return Fail(path, "validation.unknownCode", errors);
                        }

                        if (!codes.Any(c => c!.GetValue<string>() == code))
                        {
                            codes.Add(code);
                        }
                    }

                    if (required && codes.Count == 0)
                    {
                        return Fail(path, "validation.notEmpty", errors);
                    }

                    return codes;
                }

            case FieldTypes.Employee:
            case FieldTypes.Unit:
                {
                    if (!Guid.TryParse(StringOf(raw), out var id)
                        || !(field.Type == FieldTypes.Employee
                            ? await resolver.EmployeeExistsAsync(id, ct).ConfigureAwait(false)
                            : await resolver.UnitExistsAsync(id, ct).ConfigureAwait(false)))
                    {
                        return Fail(path, "validation.unknownCode", errors);
                    }

                    return JsonValue.Create(id.ToString("D"));
                }

            case FieldTypes.Record:
                {
                    if (!Guid.TryParse(StringOf(raw), out var id) || !await resolver.CanReadRecordAsync(field.TargetType ?? string.Empty, id, ct).ConfigureAwait(false))
                    {
                        return Fail(path, "requests.errors.record", errors);
                    }

                    return JsonValue.Create(id.ToString("D"));
                }

            case FieldTypes.File:
                {
                    var items = raw is JsonArray list ? list.ToList() : [raw];
                    if (!field.Multiple && items.Count > 1)
                    {
                        return Fail(path, "requests.errors.oneFile", errors);
                    }

                    var ids = new JsonArray();
                    foreach (var item in items)
                    {
                        if (!Guid.TryParse(StringOf(item), out var id) || !await resolver.IsRequestFileAsync(id, ct).ConfigureAwait(false))
                        {
                            return Fail(path, "requests.errors.file", errors);
                        }

                        ids.Add(id.ToString("D"));
                    }

                    return field.Multiple ? ids : JsonValue.Create(ids[0]!.GetValue<string>());
                }

            case FieldTypes.Table:
                {
                    if (raw is not JsonArray rows)
                    {
                        return Fail(path, "requests.errors.type", errors);
                    }

                    if ((required && rows.Count < Math.Max(1, field.MinRows ?? 1)) || (field.MaxRows is { } maxRows && rows.Count > maxRows))
                    {
                        return Fail(path, "requests.errors.rows", errors, new Dictionary<string, object?> { ["min"] = field.MinRows ?? 1, ["max"] = field.MaxRows });
                    }

                    var columns = new FormSchema { Sections = [new FormSection { Code = "row", Fields = field.Columns ?? [] }] };
                    var output = new JsonArray();
                    for (var i = 0; i < rows.Count; i++)
                    {
                        if (rows[i] is not JsonObject row)
                        {
                            return Fail($"{path}[{i}]", "requests.errors.type", errors);
                        }

                        var result = await ValidateAsync(columns, row, submitting, resolver, ct, $"{path}[{i}]").ConfigureAwait(false);
                        errors.AddRange(result.Errors);
                        output.Add(result.Values);
                    }

                    return output;
                }

            default:
                return Fail(path, "requests.errors.type", errors);
        }
    }

    /// <summary>Does the condition hold on the values seen so far (normalised) or, for later fields, the raw input?</summary>
    public static bool Holds(FieldCondition condition, JsonObject normalised, JsonObject raw)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var value = normalised[condition.Field] ?? raw[condition.Field];
        return condition.Op switch
        {
            "filled" => !IsEmpty(value),
            "empty" => IsEmpty(value),
            "eq" => Same(value, condition.Value),
            "ne" => !Same(value, condition.Value),
            "in" => condition.Value is JsonArray options && options.Any(o => Same(value, o)),
            _ => false,
        };
    }

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (IsEmpty(a) || IsEmpty(b))
        {
            return IsEmpty(a) && IsEmpty(b);
        }

        if (NumberOf(a) is { } x && NumberOf(b) is { } y)
        {
            return x == y;
        }

        return string.Equals(Scalar(a), Scalar(b), StringComparison.Ordinal);
    }

    private static string? Scalar(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => v.ToJsonString(),
        }
        : node?.ToJsonString();

    public static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.Null => true,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetValue<string>()),
        JsonArray a => a.Count == 0,
        _ => false,
    };

    private static string? StringOf(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static decimal? NumberOf(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            JsonValueKind.Number when v.TryGetValue<decimal>(out var d) => d,
            JsonValueKind.Number => decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null,
            JsonValueKind.String when decimal.TryParse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        }
        : null;

    private static JsonNode? Fail(string path, string key, List<FieldError> errors, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        errors.Add(FormSchemaValidator.Error(path, key, parameters));
        return null;
    }

    [GeneratedRegex(@"^784-\d{4}-\d{7}-\d$")]
    internal static partial Regex EmiratesIdPattern();
}
