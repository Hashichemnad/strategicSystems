using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Strategy.Platform.Integration.Sync.Salesforce;

namespace Strategy.Platform.Integration.Sync;

/// <summary>Why a record could not be mapped (→ exceptions queue, ADR 0205).</summary>
public sealed record MappingIssue(string Reason, string? Field, string? Value, string MessageKey);

/// <summary>Resolves Salesforce Ids during mapping (cached per run by the engine).</summary>
public interface IMappingResolver
{
    /// <summary>The internal record of the Salesforce Id for the target type, or null when not synced (yet).</summary>
    Task<Guid?> ReferenceAsync(string targetType, string externalId, CancellationToken cancellationToken);

    /// <summary>The employee matched to a User / Contact Id, or null.</summary>
    Task<Guid?> PersonAsync(string externalId, CancellationToken cancellationToken);

    Task<bool> IsActiveLookupCodeAsync(string lookupType, string code, CancellationToken cancellationToken);
}

/// <summary>The mapped values of one record (target field code → converted value) and the issues found.</summary>
public sealed record MappedRecord(Dictionary<string, object?> Values, IReadOnlyList<MappingIssue> Issues, string Hash)
{
    public bool Ok => Issues.Count == 0;
}

/// <summary>
/// Maps a source record to the fields of a sync target (docs/design/salesforce-sync.md §3): kinds, value maps, transforms, conversions
/// into the target field kinds, required checks and defaults. Deterministic; the hash covers the mapped source values and the mapping.
/// </summary>
public static partial class FieldMapper
{
    private static readonly string[] DateTimeFormats =
        ["yyyy-MM-dd'T'HH:mm:ss.fffzzz", "yyyy-MM-dd'T'HH:mm:ss.fffK", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.fff'+0000'", "yyyy-MM-dd'T'HH:mm:ss.fffZ"];

    public static async Task<MappedRecord> MapAsync(
        SourceRecord record, IReadOnlyList<FieldMapping> fields, IReadOnlyDictionary<string, JsonElement> defaults, SyncTargetDefinition target,
        IMappingResolver resolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolver);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var issues = new List<MappingIssue>();
        foreach (var mapping in fields)
        {
            var field = target.Field(mapping.Target);
            if (field is null)
            {
                issues.Add(new(ExceptionReasons.UnknownTarget, mapping.Target, null, "integration.sync.exceptions.unknownTarget"));
                continue;
            }

            var raw = mapping.Kind == FieldMappingKinds.Constant ? mapping.Value : mapping.Source is null ? null : record[mapping.Source];
            var (value, issue) = await MapOneAsync(mapping, field, raw, resolver, cancellationToken).ConfigureAwait(false);
            if (issue is not null)
            {
                issues.Add(issue);
                continue;
            }

            if (value is not null || !values.ContainsKey(field.Code))
            {
                values[field.Code] = value;
            }
        }

        foreach (var (code, json) in defaults)
        {
            if (target.Field(code) is { } field && (!values.TryGetValue(code, out var current) || current is null))
            {
                var text = json.ValueKind == JsonValueKind.String ? json.GetString() : json.ValueKind is JsonValueKind.Null ? null : json.GetRawText();
                var (value, issue) = Convert(field, text);
                if (issue is null)
                {
                    values[code] = value;
                }
            }
        }

        foreach (var field in target.Fields.Where(f => f.Required))
        {
            if ((!values.TryGetValue(field.Code, out var v) || v is null) && !issues.Any(i => i.Field == field.Code))
            {
                issues.Add(new(ExceptionReasons.RequiredMissing, field.Code, null, "integration.sync.exceptions.requiredMissing"));
            }
        }

        foreach (var mapping in fields.Where(m => m.Required))
        {
            if ((!values.TryGetValue(mapping.Target, out var v) || v is null) && !issues.Any(i => i.Field == mapping.Target))
            {
                issues.Add(new(ExceptionReasons.RequiredMissing, mapping.Target, null, "integration.sync.exceptions.requiredMissing"));
            }
        }

        return new MappedRecord(values, issues, Hash(record, fields, defaults));
    }

    private static async Task<(object? Value, MappingIssue? Issue)> MapOneAsync(
        FieldMapping mapping, SyncTargetField field, string? raw, IMappingResolver resolver, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        switch (mapping.Kind)
        {
            case FieldMappingKinds.Picklist or FieldMappingKinds.RecordType:
                if (field.Kind == SyncFieldKinds.Codes)
                {
                    var codes = new List<string>();
                    foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (MapValue(mapping, part) is not { } code)
                        {
                            return (null, Unmapped(field, part));
                        }

                        if (field.LookupType is { } type && !await resolver.IsActiveLookupCodeAsync(type, code, cancellationToken).ConfigureAwait(false))
                        {
                            return (null, Invalid(field, code));
                        }

                        codes.Add(code);
                    }

                    return (codes.Distinct(StringComparer.Ordinal).ToArray(), null);
                }

                if (MapValue(mapping, raw) is not { } mapped)
                {
                    return (null, Unmapped(field, raw));
                }

                return await CheckCodeAsync(field, mapped, resolver, cancellationToken).ConfigureAwait(false);
            case FieldMappingKinds.Reference:
                var targetType = mapping.ReferenceType ?? field.ReferenceType;
                if (targetType is null)
                {
                    return (null, Invalid(field, raw));
                }

                var id = await resolver.ReferenceAsync(targetType, raw, cancellationToken).ConfigureAwait(false);
                return id is null
                    ? (null, new MappingIssue(ExceptionReasons.MissingParent, field.Code, raw, "integration.sync.exceptions.missingParent"))
                    : (id.Value, null);
            case FieldMappingKinds.Person:
                var employee = await resolver.PersonAsync(raw, cancellationToken).ConfigureAwait(false);
                return employee is null
                    ? (null, new MappingIssue(ExceptionReasons.UnknownUser, field.Code, raw, "integration.sync.exceptions.unknownUser"))
                    : (employee.Value, null);
            default:
                var transformed = Transform(mapping.Transform, raw);
                if (transformed is null)
                {
                    return (null, null);
                }

                var (value, issue) = Convert(field, transformed);
                if (issue is null && field.Kind is SyncFieldKinds.Lookup or SyncFieldKinds.Code && value is string code2)
                {
                    return await CheckCodeAsync(field, code2, resolver, cancellationToken).ConfigureAwait(false);
                }

                return (value, issue);
        }
    }

    private static async Task<(object?, MappingIssue?)> CheckCodeAsync(SyncTargetField field, string code, IMappingResolver resolver, CancellationToken cancellationToken)
    {
        if (field.Kind == SyncFieldKinds.Code && field.Choices is { } choices && !choices.Contains(code, StringComparer.Ordinal))
        {
            return (null, Invalid(field, code));
        }

        if (field.Kind == SyncFieldKinds.Lookup && field.LookupType is { } type && !await resolver.IsActiveLookupCodeAsync(type, code, cancellationToken).ConfigureAwait(false))
        {
            return (null, Invalid(field, code));
        }

        // A value map can fill a non-text field (picklist "3" → whole number 3, ADR 0212): the mapped value is converted to the target kind.
        if (field.Kind is SyncFieldKinds.Bool or SyncFieldKinds.Number or SyncFieldKinds.Ratio or SyncFieldKinds.WholeNumber or SyncFieldKinds.Date or SyncFieldKinds.DateTime)
        {
            return Convert(field, code);
        }

        return (code, null);
    }

    public static string? MapValue(FieldMapping mapping, string value)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Values is null)
        {
            return null;
        }

        if (mapping.Values.TryGetValue(value, out var code))
        {
            return code;
        }

        var match = mapping.Values.FirstOrDefault(p => string.Equals(p.Key, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match.Key is not null ? match.Value : mapping.Values.TryGetValue("*", out var fallback) ? fallback : null;
    }

    private static MappingIssue Unmapped(SyncTargetField field, string value) =>
        new(ExceptionReasons.UnmappedValue, field.Code, value, "integration.sync.exceptions.unmappedValue");

    private static MappingIssue Invalid(SyncTargetField field, string? value) =>
        new(ExceptionReasons.InvalidValue, field.Code, value, "integration.sync.exceptions.invalidValue");

    public static readonly IReadOnlyList<string> Transforms = ["trim", "upper", "lower", "percentToRatio", "ratio", "dateOnly", "htmlToText", "truncate", "yesNo"];

    public static bool IsKnownTransform(string? transform) =>
        transform is null || Transforms.Contains(transform.Split(':')[0], StringComparer.Ordinal)
        && (!transform.StartsWith("truncate", StringComparison.Ordinal) || (transform.Split(':') is [_, var n] && int.TryParse(n, CultureInfo.InvariantCulture, out var max) && max > 0));

    /// <summary>Applies a transform to a raw value (null = no value).</summary>
    public static string? Transform(string? transform, string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = transform?.Split(':') ?? [];
        return parts.FirstOrDefault() switch
        {
            null => raw,
            "trim" => raw.Trim(),
            "upper" => raw.Trim().ToUpperInvariant(),
            "lower" => raw.Trim().ToLowerInvariant(),
            "percentToRatio" => decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
                ? (p / 100m).ToString(CultureInfo.InvariantCulture) : raw,
            "ratio" => raw,
            "dateOnly" => raw.Length >= 10 ? raw[..10] : raw,
            "htmlToText" => HtmlToText(raw),
            "truncate" => parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var max) && raw.Length > max ? raw[..max] : raw,
            "yesNo" => raw.Trim().ToUpperInvariant() switch { "YES" or "Y" or "TRUE" or "نعم" => "true", "NO" or "N" or "FALSE" or "لا" => "false", _ => null },
            _ => raw,
        };
    }

    /// <summary>Converts a text value to the target field kind (invariant formats of the source API).</summary>
    public static (object? Value, MappingIssue? Issue) Convert(SyncTargetField field, string? text)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        switch (field.Kind)
        {
            case SyncFieldKinds.Text or SyncFieldKinds.Code or SyncFieldKinds.Lookup:
                return field.MaxLength is { } max && text.Length > max ? (null, Invalid(field, text[..Math.Min(text.Length, 100)])) : (text, null);
            case SyncFieldKinds.Date:
                return DateOnly.TryParseExact(text.Length >= 10 ? text[..10] : text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? (d, null) : (null, Invalid(field, text));
            case SyncFieldKinds.DateTime:
                return TryParseDateTime(text, out var dt) ? (dt, null) : (null, Invalid(field, text));
            case SyncFieldKinds.Number or SyncFieldKinds.Ratio:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (n, null) : (null, Invalid(field, text));
            case SyncFieldKinds.WholeNumber:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var i) && i == decimal.Truncate(i) && i is >= int.MinValue and <= int.MaxValue
                    ? ((int)i, null) : (null, Invalid(field, text));
            case SyncFieldKinds.Bool:
                return text.Trim().ToUpperInvariant() switch
                {
                    "TRUE" or "1" => (true, null),
                    "FALSE" or "0" => (false, null),
                    _ => (null, Invalid(field, text)),
                };
            case SyncFieldKinds.Codes:
                return (text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), null);
            case SyncFieldKinds.Person or SyncFieldKinds.Unit or SyncFieldKinds.Reference:
                return Guid.TryParse(text, out var g) ? (g, null) : (null, Invalid(field, text));
            default:
                return (null, Invalid(field, text));
        }
    }

    public static bool TryParseDateTime(string text, out DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalised = PlusOffset().Replace(text.Trim(), m => m.Groups[1].Value + ":" + m.Groups[2].Value); // +0000 → +00:00
        if (DateTimeOffset.TryParse(normalised, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value)
            || DateTimeOffset.TryParseExact(text.Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        return false;
    }

    /// <summary>SHA-256 of the mapped source values and of the mapping itself, so a mapping change re-applies records.</summary>
    public static string Hash(SourceRecord record, IReadOnlyList<FieldMapping> fields, IReadOnlyDictionary<string, JsonElement> defaults)
    {
        ArgumentNullException.ThrowIfNull(record);
        var used = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in fields.Select(f => f.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct())
        {
            used[source!] = record[source!];
        }

        var canonical = JsonSerializer.Serialize(new { used, fields, defaults = new SortedDictionary<string, JsonElement>(defaults.ToDictionary(), StringComparer.Ordinal) }, SyncJson.Options);
        return System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string HtmlToText(string html)
    {
        var text = Tags().Replace(html.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase).Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase), string.Empty);
        return WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary>External ID of a source record from a template (<c>{Id}</c>, <c>{Field}</c>, <c>{year:Field}</c>); null when a part is empty.</summary>
    public static string? ExternalId(string template, SourceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var missing = false;
        var id = Token().Replace(string.IsNullOrWhiteSpace(template) ? "{Id}" : template, m =>
        {
            var year = m.Groups[1].Success;
            var value = record[m.Groups[2].Value];
            if (string.IsNullOrWhiteSpace(value))
            {
                missing = true;
                return string.Empty;
            }

            return year ? (value.Length >= 4 ? value[..4] : value) : value;
        });
        return missing ? null : id;
    }

    [GeneratedRegex("([+-]\\d{2})(\\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex PlusOffset();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex("\\{(year:)?([A-Za-z0-9_.]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
