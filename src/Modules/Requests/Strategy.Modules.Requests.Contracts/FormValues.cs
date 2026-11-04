using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Strategy.Modules.Requests.Contracts;

/// <summary>Typed reading of form values (a JSON object keyed by field code). Values are validated before handlers see them.</summary>
public sealed class FormValues
{
    public FormValues(JsonObject? values) => Json = values ?? [];

    public static FormValues Empty => new(null);

    public JsonObject Json { get; }

    public bool Has(string code) => Json[code] is { } node && !(node is JsonValue v && v.GetValueKind() == JsonValueKind.Null)
        && !(node is JsonValue s && s.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(s.GetValue<string>()));

    public string? Text(string code)
    {
        var node = Json[code];
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null,
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public bool Flag(string code) => Json[code] is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    public decimal? Number(string code) => Json[code] is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.Number => value.GetValue<decimal>(),
            JsonValueKind.String when decimal.TryParse(value.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        }
        : null;

    public Guid? Id(string code) => Guid.TryParse(Text(code), out var id) ? id : null;

    public DateOnly? Date(string code) =>
        DateOnly.TryParseExact(Text(code), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public IReadOnlyList<string> Codes(string code) => Json[code] is JsonArray array
        ? array.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>()).ToList()
        : [];

    public IReadOnlyList<Guid> Ids(string code)
    {
        if (Json[code] is JsonArray array)
        {
            return array.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String)
                .Select(v => Guid.TryParse(v.GetValue<string>(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        }

        return Id(code) is { } single ? [single] : [];
    }

    public IReadOnlyList<FormValues> Rows(string code) => Json[code] is JsonArray array
        ? array.OfType<JsonObject>().Select(o => new FormValues(o)).ToList()
        : [];
}
