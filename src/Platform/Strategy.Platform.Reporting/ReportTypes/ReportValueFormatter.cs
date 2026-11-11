using System.Globalization;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>
/// Formats raw report values for people (Reporting 2.0): lookup labels, unit and employee names (resolved once per run), ratios as percentages,
/// money with two decimals, booleans and dates in the invariant ISO form used by every export, rich text as plain text. Codes never change.
/// </summary>
public sealed class ReportValueFormatter(ILookupService lookups, IOrgDirectory org, IEmployeeDirectory employees)
{
    private readonly Dictionary<string, Dictionary<string, (string? Ar, string? En)>> _lookups = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, UnitReference?> _units = [];
    private readonly Dictionary<Guid, EmployeeReference> _people = [];

    /// <summary>Loads the labels the values need (lookups of the given fields, every unit / employee id among the values).</summary>
    public async Task PrepareAsync(IEnumerable<(DatasetField Field, IEnumerable<object?> Values)> columns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var employeeIds = new HashSet<Guid>();
        foreach (var (field, values) in columns)
        {
            switch (field.Type)
            {
                case DatasetFieldType.Lookup when field.LookupType is { } type && !_lookups.ContainsKey(type):
                    var list = await lookups.GetValuesAsync(type, includeInactive: true, ct).ConfigureAwait(false);
                    _lookups[type] = list.ToDictionary(v => v.Code, v => (v.LabelAr, v.LabelEn), StringComparer.Ordinal);
                    break;
                case DatasetFieldType.Unit:
                    foreach (var id in values.OfType<Guid>().Where(id => !_units.ContainsKey(id)).Distinct().ToList())
                    {
                        _units[id] = await org.GetUnitAsync(id, ct).ConfigureAwait(false);
                    }

                    break;
                case DatasetFieldType.Employee:
                    employeeIds.UnionWith(values.OfType<Guid>().Where(id => !_people.ContainsKey(id)));
                    break;
            }
        }

        if (employeeIds.Count > 0)
        {
            foreach (var (id, person) in await employees.GetManyAsync(employeeIds, ct).ConfigureAwait(false))
            {
                _people[id] = person;
            }
        }
    }

    public string? Format(DatasetField field, object? value, string language)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (value is null)
        {
            return null;
        }

        var ar = language == "ar";
        return field.Type switch
        {
            DatasetFieldType.Lookup when value is string code && field.LookupType is { } type && _lookups.TryGetValue(type, out var map) && map.TryGetValue(code, out var l) =>
                (ar ? l.Ar ?? l.En : l.En ?? l.Ar) ?? code,
            DatasetFieldType.Unit when value is Guid g && _units.TryGetValue(g, out var u) && u is not null => (ar ? u.NameAr ?? u.NameEn : u.NameEn ?? u.NameAr) ?? u.Code,
            DatasetFieldType.Employee when value is Guid g && _people.TryGetValue(g, out var e) => (ar ? e.NameAr ?? e.NameEn : e.NameEn ?? e.NameAr) ?? e.EmployeeNo,
            DatasetFieldType.Unit or DatasetFieldType.Employee or DatasetFieldType.Reference => null,
            _ => FormatPlain(field.Type, value, ar),
        };
    }

    /// <summary>Type-only formatting (no labels): numbers, ratios, money, booleans, dates, rich text.</summary>
    public static string? FormatPlain(DatasetFieldType type, object? value, bool arabic)
    {
        if (value is null)
        {
            return null;
        }

        return type switch
        {
            DatasetFieldType.Ratio when ReportAggregation.ToDecimal(value) is { } r => (r * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %",
            DatasetFieldType.Money when ReportAggregation.ToDecimal(value) is { } m => m.ToString("#,0.00", CultureInfo.InvariantCulture),
            DatasetFieldType.Number when ReportAggregation.ToDecimal(value) is { } d => d.ToString("#,0.##", CultureInfo.InvariantCulture),
            DatasetFieldType.Bool => value is true ? (arabic ? "نعم" : "Yes") : (arabic ? "لا" : "No"),
            DatasetFieldType.Date when value is DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DatasetFieldType.Date when value is DateTime dt => DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DatasetFieldType.DateTime when value is DateTimeOffset t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DatasetFieldType.RichText when value is string html => RichText.ToPlainText(html),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
