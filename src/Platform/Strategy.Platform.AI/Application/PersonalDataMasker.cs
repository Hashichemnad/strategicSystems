using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Strategy.Platform.AI.Application;

/// <summary>
/// Reversible masking of personal data before text is sent to a provider (docs/design/ai.md §7): e-mail addresses, Emirates ID numbers,
/// phone numbers and the names of people the caller lists become placeholders (<c>[EMAIL_1]</c>, <c>[EID_1]</c>, <c>[PHONE_1]</c>,
/// <c>[PERSON_1]</c>); the same value always gets the same placeholder in one call, and <c>Unmask</c> restores them in the output.
/// </summary>
public sealed partial class PersonalDataMasker
{
    private readonly Dictionary<string, string> _toPlaceholder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toValue = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> _names;
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    public PersonalDataMasker(IEnumerable<string> peopleNames)
    {
        ArgumentNullException.ThrowIfNull(peopleNames);
        _names = peopleNames.Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 3).Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
    }

    public int Count => _toValue.Count;

    public string Mask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = Email().Replace(text, m => Placeholder("EMAIL", m.Value));
        result = EmiratesId().Replace(result, m => Placeholder("EID", m.Value));
        result = Phone().Replace(result, m => Placeholder("PHONE", m.Value));
        foreach (var name in _names)
        {
            result = Regex.Replace(result, Regex.Escape(name), m => Placeholder("PERSON", m.Value), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }

        return result;
    }

    public string Unmask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _toValue.Count == 0 ? text : PlaceholderPattern().Replace(text, m => _toValue.TryGetValue(m.Value, out var value) ? value : m.Value);
    }

    /// <summary>Restores placeholders in every string of a JSON value.</summary>
    public JsonElement Unmask(JsonElement value)
    {
        if (_toValue.Count == 0)
        {
            return value;
        }

        var node = JsonNode.Parse(value.GetRawText());
        node = Walk(node);
        using var document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private JsonNode? Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    obj[key] = Walk(obj[key]);
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Walk(array[i]);
                }

                return array;
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(Unmask(s));
            default:
                return node;
        }
    }

    private string Placeholder(string kind, string value)
    {
        if (_toPlaceholder.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var n = _counters[kind] = _counters.GetValueOrDefault(kind) + 1;
        var placeholder = $"[{kind}_{n}]";
        _toPlaceholder[value] = placeholder;
        _toValue[placeholder] = value;
        return placeholder;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // Emirates ID: 784-YYYY-NNNNNNN-N, with or without dashes / spaces.
    [GeneratedRegex(@"\b784[\- ]?\d{4}[\- ]?\d{7}[\- ]?\d\b")]
    private static partial Regex EmiratesId();

    // International (+971 …, 00971 …) and local numbers starting with 0 (05x…, 04…), 9+ digits with optional separators. Plain amounts
    // (no leading + / 0) are not phone numbers.
    [GeneratedRegex(@"(?<![\w\d])(?:\+|0)\d(?:[\s\-]?\d){7,13}(?![\w\d])")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\[(EMAIL|EID|PHONE|PERSON)_\d+\]")]
    private static partial Regex PlaceholderPattern();
}
