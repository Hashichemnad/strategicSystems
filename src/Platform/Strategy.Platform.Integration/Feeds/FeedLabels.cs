using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Feeds;

/// <summary>
/// Labels of codes and ids in the caller's language (docs/design/integrations.md §3): Settings picklists, fixed choices, unit and employee
/// names; fallback to the other language, then to the code. One instance per request (caches what it has read).
/// </summary>
public sealed class FeedLabels(ILookupService lookups, IOrgDirectory org, IEmployeeDirectory employees)
{
    private readonly Dictionary<string, Dictionary<string, (string? Ar, string? En)>> _lookups = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (string? Ar, string? En)> _units = [];
    private readonly Dictionary<Guid, (string? Ar, string? En)> _employees = [];

    public static bool HasLabel(RecordFieldDefinition field) =>
        field.Type is RecordFieldType.Lookup or RecordFieldType.Unit or RecordFieldType.Employee;

    public static string LabelProperty(string field) => field + "Label";

    /// <summary>Reads every label the rows need (one call per lookup type, batched employees).</summary>
    public async Task PrepareAsync(IReadOnlyList<(RecordFieldDefinition Field, IEnumerable<object?> Values)> columns, CancellationToken ct)
    {
        foreach (var (field, values) in columns)
        {
            switch (field.Type)
            {
                case RecordFieldType.Lookup when field.LookupType is { } type && !_lookups.ContainsKey(type):
                    _lookups[type] = (await lookups.GetValuesAsync(type, includeInactive: true, ct).ConfigureAwait(false))
                        .ToDictionary(v => v.Code, v => (v.LabelAr, v.LabelEn), StringComparer.Ordinal);
                    break;
                case RecordFieldType.Unit:
                    foreach (var id in values.OfType<Guid>().Distinct().Where(id => !_units.ContainsKey(id)).ToList())
                    {
                        var unit = await org.GetUnitAsync(id, ct).ConfigureAwait(false);
                        _units[id] = (unit?.NameAr, unit?.NameEn);
                    }

                    break;
                case RecordFieldType.Employee:
                    var missing = values.OfType<Guid>().Distinct().Where(id => !_employees.ContainsKey(id)).ToList();
                    if (missing.Count > 0)
                    {
                        var found = await employees.GetManyAsync(missing, ct).ConfigureAwait(false);
                        foreach (var id in missing)
                        {
                            _employees[id] = found.TryGetValue(id, out var e) ? (e.NameAr, e.NameEn) : (null, null);
                        }
                    }

                    break;
            }
        }
    }

    public string? Label(RecordFieldDefinition field, object? value, string lang)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (value is null)
        {
            return null;
        }

        (string? Ar, string? En) pair = field.Type switch
        {
            RecordFieldType.Lookup when value is string code && field.LookupType is { } type && _lookups.TryGetValue(type, out var map) && map.TryGetValue(code, out var l) => l,
            RecordFieldType.Lookup when value is string code && field.Choices?.FirstOrDefault(c => c.Code == code) is { } choice => (choice.LabelAr, choice.LabelEn),
            RecordFieldType.Unit when value is Guid id && _units.TryGetValue(id, out var u) => u,
            RecordFieldType.Employee when value is Guid id && _employees.TryGetValue(id, out var e) => e,
            _ => (null, null),
        };
        var label = lang == "ar" ? pair.Ar ?? pair.En : pair.En ?? pair.Ar;
        return label ?? (value is string s ? s : null);
    }
}
