using System.Globalization;
using System.Text.Json;

namespace Strategy.Platform.AI.Application;

/// <summary>
/// Validates model output against the JSON schema subset the prompts emit (ADR 0029): <c>type</c> (single or list), <c>properties</c>,
/// <c>required</c>, <c>additionalProperties: false</c>, <c>enum</c>, <c>items</c>, <c>maxItems</c>, <c>maxLength</c>, <c>minimum</c>,
/// <c>maximum</c>. In-house so no further dependency is needed; anything outside the subset is not used by our schemas.
/// </summary>
public static class JsonSchemaValidator
{
    public const int MaxErrors = 20;

    public static IReadOnlyList<string> Validate(JsonElement schema, JsonElement value)
    {
        var errors = new List<string>();
        Check(schema, value, "$", errors);
        return errors;
    }

    private static void Check(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (errors.Count >= MaxErrors || schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("type", out var type) && !TypeMatches(type, value))
        {
            errors.Add($"{path}: expected {type}, got {value.ValueKind}");
            return;
        }

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(a => JsonElement.DeepEquals(a, value)))
        {
            errors.Add($"{path}: value not allowed");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(schema, value, path, errors);
                break;
            case JsonValueKind.Array:
                if (schema.TryGetProperty("maxItems", out var maxItems) && value.GetArrayLength() > maxItems.GetInt32())
                {
                    errors.Add($"{path}: more than {maxItems.GetInt32()} items");
                }

                if (schema.TryGetProperty("items", out var items))
                {
                    var i = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Check(items, item, $"{path}[{i++}]", errors);
                    }
                }

                break;
            case JsonValueKind.String:
                if (schema.TryGetProperty("maxLength", out var maxLength) && value.GetString()!.Length > maxLength.GetInt32())
                {
                    errors.Add($"{path}: longer than {maxLength.GetInt32()} characters");
                }

                break;
            case JsonValueKind.Number:
                var number = value.GetDouble();
                if (schema.TryGetProperty("minimum", out var min) && number < min.GetDouble())
                {
                    errors.Add($"{path}: below {min.GetDouble().ToString(CultureInfo.InvariantCulture)}");
                }

                if (schema.TryGetProperty("maximum", out var max) && number > max.GetDouble())
                {
                    errors.Add($"{path}: above {max.GetDouble().ToString(CultureInfo.InvariantCulture)}");
                }

                break;
        }
    }

    private static void CheckObject(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var properties = schema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!value.TryGetProperty(name, out _))
                {
                    errors.Add($"{path}.{name}: required");
                }
            }
        }

        var closed = schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False;
        foreach (var property in value.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                Check(propertySchema, property.Value, $"{path}.{property.Name}", errors);
            }
            else if (closed)
            {
                errors.Add($"{path}.{property.Name}: not allowed");
            }
        }
    }

    private static bool TypeMatches(JsonElement type, JsonElement value) => type.ValueKind == JsonValueKind.Array
        ? type.EnumerateArray().Any(t => Is(t.GetString(), value))
        : Is(type.GetString(), value);

    private static bool Is(string? type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };
}

/// <summary>Finds the JSON object in a model reply (code fences and chatter around it are ignored).</summary>
public static class JsonOutput
{
    public static bool TryParse(string? text, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        while (start >= 0 && end > start)
        {
            try
            {
                using var document = JsonDocument.Parse(text[start..(end + 1)]);
                value = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                end = text.LastIndexOf('}', end - 1);
            }
        }

        return false;
    }
}
