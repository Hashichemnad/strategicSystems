using System.Globalization;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>
/// Formats raw report values for people (Reporting 2.0): code labels (lookups, label sources, fixed choices — ADR 0209), unit and employee names
/// (resolved once per run), ratios as percentages, money with two decimals, booleans and dates in the invariant ISO form used by every export, rich
/// text as plain text. Codes never change. Shared by the report builder, report types, charts, dossiers and their exports.
/// </summary>
public sealed class ReportValueFormatter(
    ILookupService lookups, IOrgDirectory org, IEmployeeDirectory employees, IEnumerable<IReportingDimensionLabelSource> labelSources)
{
    private readonly Dictionary<string, Dictionary<string, (string? Ar, string? En)>> _lookups = new(StringComparer.Ordinal);
    private readonly Dictionary<FieldLabelSource, Dictionary<string, (string? Ar, string? En)>> _sourced = [];
    private readonly Dictionary<Guid, UnitReference?> _units = [];
    private readonly Dictionary<Guid, EmployeeReference> _people = [];

    /// <summary>Loads the labels the values need (lookups and label sources of the given fields, every unit / employee id among the values).</summary>
    public async Task PrepareAsync(IEnumerable<(DatasetField Field, IEnumerable<object?> Values)> columns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var employeeIds = new HashSet<Guid>();
        foreach (var (field, values) in columns)
        {
            if (IsCoded(field))
            {
                if (field.LookupType is { } type)
                {
                    if (!_lookups.ContainsKey(type))
                    {
                        var list = await lookups.GetValuesAsync(type, includeInactive: true, ct).ConfigureAwait(false);
                        _lookups[type] = list.ToDictionary(v => v.Code, v => (v.LabelAr, v.LabelEn), StringComparer.Ordinal);
                    }
                }
                else if (field.LabelSource is { } labelSource)
                {
                    await PrepareSourceAsync(labelSource, values, ct).ConfigureAwait(false);
                }

                continue;
            }

            switch (field.Type)
            {
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

    private async Task PrepareSourceAsync(FieldLabelSource labelSource, IEnumerable<object?> values, CancellationToken ct)
    {
        if (!_sourced.TryGetValue(labelSource, out var known))
        {
            _sourced[labelSource] = known = new Dictionary<string, (string? Ar, string? En)>(StringComparer.Ordinal);
        }

        var missing = values.OfType<string>().Where(c => c.Length > 0 && !known.ContainsKey(c)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var found = new Dictionary<string, DimensionLabel>(StringComparer.Ordinal);
        foreach (var source in labelSources.Where(s => s.SystemCode == labelSource.SystemCode))
        {
            foreach (var (code, label) in await source.LabelsAsync(labelSource.Dimension, missing, ct).ConfigureAwait(false))
            {
                found.TryAdd(code, label);
            }
        }

        foreach (var code in missing)
        {
            known[code] = found.TryGetValue(code, out var l) ? (l.Ar, l.En) : (null, null);
        }
    }

    /// <summary>A field whose string values are codes with labels (ADR 0209): lookup fields, and text fields that declare labels.</summary>
    private static bool IsCoded(DatasetField field) =>
        field.Type == DatasetFieldType.Lookup || (field.Type == DatasetFieldType.Text && field.HasCodeLabels);

    /// <summary>The label of a code (Arabic when <paramref name="ar"/>), with fallback to the other language, then to the code.</summary>
    private string? CodeLabel(DatasetField field, string code, bool ar)
    {
        (string? Ar, string? En) label = (null, null);
        if (field.LookupType is { } type)
        {
            if (_lookups.TryGetValue(type, out var map) && map.TryGetValue(code, out var l))
            {
                label = l;
            }
        }
        else if (field.LabelSource is { } source)
        {
            if (_sourced.TryGetValue(source, out var map) && map.TryGetValue(code, out var l))
            {
                label = l;
            }
        }

        if (Blank(label.Ar) && Blank(label.En) && field.Choices?.FirstOrDefault(c => c.Code == code) is { } choice)
        {
            label = (choice.LabelAr, choice.LabelEn);
        }

        var (first, second) = ar ? (label.Ar, label.En) : (label.En, label.Ar);
        return !Blank(first) ? first : !Blank(second) ? second : code;
    }

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

    public string? Format(DatasetField field, object? value, string language)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (value is null)
        {
            return null;
        }

        var ar = language == "ar";
        if (value is string code && IsCoded(field))
        {
            return CodeLabel(field, code, ar);
        }

        return field.Type switch
        {
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
