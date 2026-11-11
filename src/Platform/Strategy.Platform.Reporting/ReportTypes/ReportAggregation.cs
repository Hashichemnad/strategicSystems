using System.Globalization;

namespace Strategy.Platform.Reporting.ReportTypes;

public static class SummaryFunctions
{
    public const string Sum = "sum";
    public const string Average = "avg";
    public const string Min = "min";
    public const string Max = "max";

    public static readonly IReadOnlyList<string> All = [Sum, Average, Min, Max];
}

public static class ChartTypes
{
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Pie = "pie";
    public const string Donut = "donut";
    public const string StackedBar = "stackedBar";

    public static readonly IReadOnlyList<string> All = [Bar, Line, Pie, Donut, StackedBar];
}

public static class HighlightTones
{
    public static readonly IReadOnlyList<string> All = ["good", "warning", "bad", "info"];
}

/// <summary>A summary formula: <see cref="Functions"/> of a numeric (or date, for min / max) column; count is always given.</summary>
public sealed record ReportSummary
{
    public string Column { get; init; } = string.Empty;

    public List<string> Functions { get; init; } = [];
}

/// <summary>
/// A chart on top of the table: categories from <see cref="Category"/>, optional <see cref="Series"/> (stacked bar / multi-line), value =
/// count of rows (<see cref="Measure"/> null) or <see cref="Aggregate"/> of <see cref="Measure"/>.
/// </summary>
public sealed record ReportChart
{
    public string Type { get; init; } = ChartTypes.Bar;

    public string Category { get; init; } = string.Empty;

    public string? Series { get; init; }

    public string? Measure { get; init; }

    public string Aggregate { get; init; } = SummaryFunctions.Sum;
}

/// <summary>Conditional highlighting: cells of <see cref="Column"/> matching the condition get <see cref="Tone"/>.</summary>
public sealed record ReportHighlight
{
    public string Column { get; init; } = string.Empty;

    public string Operator { get; init; } = ReportOperators.GreaterOrEqual;

    public List<string> Values { get; init; } = [];

    public string Tone { get; init; } = "bad";
}

/// <summary>A computed aggregate (raw value; the engine formats it by the column's type).</summary>
public sealed record AggregateValue(string Column, string Function, object? Value);

/// <summary>One group (subtotal) in tree order: level 1..3, the keys from level 1 to this level, row count and aggregates.</summary>
public sealed record GroupNode(int Level, IReadOnlyList<object?> Keys, int Count, IReadOnlyList<AggregateValue> Aggregates);

public sealed record ChartSeries(object? Key, IReadOnlyList<decimal> Values);

public sealed record ChartData(string Type, IReadOnlyList<object?> Categories, IReadOnlyList<ChartSeries> Series);

/// <summary>Sorting, grouping with subtotals, summary formulas, charts and highlighting on composed rows (ADR 0182; pure, unit-tested).</summary>
public static class ReportAggregation
{
    public static int Compare(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1, // empty values last
        (_, null) => -1,
        _ when IsNumber(a) && IsNumber(b) => ToDecimal(a)!.Value.CompareTo(ToDecimal(b)!.Value),
        (IComparable x, _) when a.GetType() == b.GetType() => x.CompareTo(b),
        _ => string.CompareOrdinal(ReportComposer.Key(a), ReportComposer.Key(b)),
    };

    public static bool IsNumber(object? v) => v is int or long or decimal or double or float or short;

    public static decimal? ToDecimal(object? v) => IsNumber(v) ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;

    /// <summary>Rows ordered by the group fields (in order), then by the sort fields.</summary>
    public static List<FlatRow> Sort(IEnumerable<FlatRow> rows, IReadOnlyList<string> groupBy, IReadOnlyList<ReportSort> sort)
    {
        var keys = groupBy.Select(g => (Field: g, Descending: sort.FirstOrDefault(s => s.Field == g)?.Descending ?? false))
            .Concat(sort.Where(s => !groupBy.Contains(s.Field)).Select(s => (s.Field, s.Descending))).ToList();
        var list = rows.ToList();
        if (keys.Count == 0)
        {
            return list;
        }

        // Stable sort keeps the composition order (primary order, then related order) for equal keys.
        return list.Select((r, i) => (r, i)).OrderBy(p => p, Comparer<(FlatRow Row, int Index)>.Create((x, y) =>
        {
            foreach (var (field, descending) in keys)
            {
                var a = x.Row.Value(field);
                var b = y.Row.Value(field);
                if (a is null != b is null)
                {
                    return a is null ? 1 : -1; // empty values last in both directions
                }

                var c = Compare(a, b);
                if (c != 0)
                {
                    return descending ? -c : c;
                }
            }

            return x.Index.CompareTo(y.Index);
        })).Select(p => p.r).ToList();
    }

    public static IReadOnlyList<AggregateValue> Aggregate(IReadOnlyList<FlatRow> rows, IReadOnlyList<ReportSummary> summaries)
    {
        var result = new List<AggregateValue>();
        foreach (var summary in summaries)
        {
            var values = rows.Select(r => r.Value(summary.Column)).Where(v => v is not null).ToList();
            var numbers = values.Select(ToDecimal).Where(d => d is not null).Select(d => d!.Value).ToList();
            foreach (var function in summary.Functions)
            {
                object? value = function switch
                {
                    SummaryFunctions.Sum => numbers.Sum(),
                    SummaryFunctions.Average => numbers.Count == 0 ? null : Math.Round(numbers.Average(), 6),
                    SummaryFunctions.Min => values.Count == 0 ? null : values.OrderBy(v => v, Comparer<object?>.Create(Compare)).First(),
                    SummaryFunctions.Max => values.Count == 0 ? null : values.OrderBy(v => v, Comparer<object?>.Create(Compare)).Last(),
                    _ => null,
                };
                result.Add(new AggregateValue(summary.Column, function, value));
            }
        }

        return result;
    }

    /// <summary>Groups in tree order (each group followed by its sub-groups) over rows already sorted by the group fields.</summary>
    public static List<GroupNode> Groups(IReadOnlyList<FlatRow> sorted, IReadOnlyList<string> groupBy, IReadOnlyList<ReportSummary> summaries)
    {
        var result = new List<GroupNode>();
        void Walk(IReadOnlyList<FlatRow> rows, int level, List<object?> keys)
        {
            if (level > groupBy.Count)
            {
                return;
            }

            var field = groupBy[level - 1];
            var start = 0;
            while (start < rows.Count)
            {
                var key = rows[start].Value(field);
                var end = start;
                while (end < rows.Count && Compare(rows[end].Value(field), key) == 0)
                {
                    end++;
                }

                var slice = rows.Skip(start).Take(end - start).ToList();
                var path = new List<object?>(keys) { key };
                result.Add(new GroupNode(level, path, slice.Count, Aggregate(slice, summaries)));
                Walk(slice, level + 1, path);
                start = end;
            }
        }

        if (groupBy.Count > 0)
        {
            Walk(sorted, 1, []);
        }

        return result;
    }

    public static ChartData Chart(IReadOnlyList<FlatRow> rows, ReportChart chart)
    {
        var categories = rows.Select(r => r.Value(chart.Category)).Distinct(KeyComparer.Instance).OrderBy(v => v, Comparer<object?>.Create(Compare)).ToList();
        var seriesKeys = chart.Series is null
            ? [null]
            : rows.Select(r => r.Value(chart.Series)).Distinct(KeyComparer.Instance).OrderBy(v => v, Comparer<object?>.Create(Compare)).ToList();
        var series = seriesKeys.Select(s => new ChartSeries(s, categories.Select(c =>
        {
            var slice = rows.Where(r => KeyComparer.Instance.Equals(r.Value(chart.Category), c)
                && (chart.Series is null || KeyComparer.Instance.Equals(r.Value(chart.Series), s))).ToList();
            if (chart.Measure is null)
            {
                return (decimal)slice.Count;
            }

            var value = Aggregate(slice, [new ReportSummary { Column = chart.Measure, Functions = [chart.Aggregate] }])[0].Value;
            return ToDecimal(value) ?? 0m;
        }).ToList())).ToList();
        return new ChartData(chart.Type, categories, series);
    }

    /// <summary>The tone of a cell, or null (first matching rule wins).</summary>
    public static string? Tone(object? value, string column, IReadOnlyList<ReportHighlight> highlights)
    {
        foreach (var h in highlights.Where(h => h.Column == column))
        {
            if (Matches(value, h.Operator, h.Values))
            {
                return h.Tone;
            }
        }

        return null;
    }

    public static bool Matches(object? value, string op, IReadOnlyList<string> values)
    {
        if (op == ReportOperators.IsEmpty)
        {
            return value is null || (value is string s && s.Length == 0);
        }

        if (op == ReportOperators.IsNotEmpty)
        {
            return !(value is null || (value is string s2 && s2.Length == 0));
        }

        if (value is null || values.Count == 0)
        {
            return false;
        }

        int Cmp(string target)
        {
            if (ToDecimal(value) is { } d)
            {
                return decimal.TryParse(target, NumberStyles.Number, CultureInfo.InvariantCulture, out var t) ? d.CompareTo(t) : 1;
            }

            return value switch
            {
                DateOnly date when DateOnly.TryParse(target, CultureInfo.InvariantCulture, out var td) => date.CompareTo(td),
                DateTimeOffset dto when DateTimeOffset.TryParse(target, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var tt) => dto.CompareTo(tt),
                bool b => string.Equals(b ? "true" : "false", target, StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                _ => string.Compare(ReportComposer.Key(value), target, StringComparison.OrdinalIgnoreCase),
            };
        }

        return op switch
        {
            ReportOperators.Equal => Cmp(values[0]) == 0,
            ReportOperators.NotEqual => Cmp(values[0]) != 0,
            ReportOperators.GreaterOrEqual => Cmp(values[0]) >= 0,
            ReportOperators.LessOrEqual => Cmp(values[0]) <= 0,
            ReportOperators.Between => values.Count > 1 && Cmp(values[0]) >= 0 && Cmp(values[1]) <= 0,
            ReportOperators.In => values.Any(v => Cmp(v) == 0),
            ReportOperators.Contains => ReportComposer.Key(value).Contains(values[0], StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private sealed class KeyComparer : IEqualityComparer<object?>
    {
        public static readonly KeyComparer Instance = new();

        public new bool Equals(object? x, object? y) => Compare(x, y) == 0;

        public int GetHashCode(object? obj) => ToDecimal(obj) is { } d ? d.GetHashCode() : ReportComposer.Key(obj).GetHashCode(StringComparison.Ordinal);
    }
}
