using System.Text.Json;
using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Contracts;

namespace Strategy.Modules.Requests.Domain;

/// <summary>
/// <c>secret</c> field values (ADR 0086): stored as <c>{"$enc": cipher, "last": "3"}</c>, shown as a mask, decrypted only for handlers.
/// A client sending the mask back keeps the stored value.
/// </summary>
public static class SecretValues
{
    public const string EncryptedKey = "$enc";
    public const string LastKey = "last";

    public static string Mask(string? format, string? last) => format == SecretFormats.EmiratesId
        ? $"784-****-*******-{last}"
        : $"********{last}";

    public static string MaskOf(FormField field, string plain) =>
        Mask(field.Format, field.Format == SecretFormats.EmiratesId ? plain[^1..] : plain.Length > 2 ? plain[^2..] : string.Empty);

    public static bool LooksMasked(string value) => value.Contains('*', StringComparison.Ordinal);

    /// <summary>Stored → masked strings (API responses, events, audit-safe copies).</summary>
    public static JsonObject ToMasked(FormSchema schema, JsonObject stored) => Transform(schema, stored, (field, node) =>
        node is JsonObject o && o[LastKey] is { } last ? JsonValue.Create(Mask(field.Format, last.GetValue<string>())) : node?.DeepClone());

    /// <summary>Stored → plain (server side only).</summary>
    public static JsonObject ToPlain(FormSchema schema, JsonObject stored, Func<string, string?> unprotect) => Transform(schema, stored, (_, node) =>
        node is JsonObject o && o[EncryptedKey] is { } cipher ? JsonValue.Create(unprotect(cipher.GetValue<string>())) : node?.DeepClone());

    /// <summary>Plain (validated) → stored.</summary>
    public static JsonObject ToStored(FormSchema schema, JsonObject plain, Func<string, string> protect) => Transform(schema, plain, (field, node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String
            ? new JsonObject
            {
                [EncryptedKey] = protect(v.GetValue<string>()),
                [LastKey] = field.Format == SecretFormats.EmiratesId ? v.GetValue<string>()[^1..] : v.GetValue<string>().Length > 2 ? v.GetValue<string>()[^2..] : string.Empty,
            }
            : node?.DeepClone());

    /// <summary>Incoming values: a masked string equal to the mask of a currently stored value is replaced by that plain value.</summary>
    public static JsonObject RestoreMasked(FormSchema schema, JsonObject incoming, JsonObject currentPlain)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        Collect(schema, currentPlain, known);
        return Transform(schema, incoming, (_, node) =>
            node is JsonValue v && v.GetValueKind() == JsonValueKind.String && LooksMasked(v.GetValue<string>())
                ? known.TryGetValue(v.GetValue<string>(), out var plain) ? JsonValue.Create(plain) : null
                : node?.DeepClone());
    }

    private static void Collect(FormSchema schema, JsonObject plain, Dictionary<string, string> known)
    {
        foreach (var field in schema.AllFields)
        {
            if (field.Type == FieldTypes.Secret && plain[field.Code] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
            {
                known.TryAdd(MaskOf(field, v.GetValue<string>()), v.GetValue<string>());
            }
            else if (field.Type == FieldTypes.Table && plain[field.Code] is JsonArray rows)
            {
                var columns = new FormSchema { Sections = [new FormSection { Code = "row", Fields = field.Columns ?? [] }] };
                foreach (var row in rows.OfType<JsonObject>())
                {
                    Collect(columns, row, known);
                }
            }
        }
    }

    private static JsonObject Transform(FormSchema schema, JsonObject source, Func<FormField, JsonNode?, JsonNode?> secret)
    {
        var result = new JsonObject();
        foreach (var (key, node) in source)
        {
            var field = schema.Find(key);
            if (field?.Type == FieldTypes.Secret)
            {
                var value = secret(field, node);
                if (value is not null)
                {
                    result[key] = value;
                }
            }
            else if (field?.Type == FieldTypes.Table && node is JsonArray rows)
            {
                var columns = new FormSchema { Sections = [new FormSection { Code = "row", Fields = field.Columns ?? [] }] };
                result[key] = new JsonArray(rows.Select(r => (JsonNode?)(r is JsonObject row ? Transform(columns, row, secret) : r?.DeepClone())).ToArray());
            }
            else
            {
                result[key] = node?.DeepClone();
            }
        }

        return result;
    }
}
