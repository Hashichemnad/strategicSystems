using Strategy.Platform.Reporting;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>
/// Accumulates additive facts so that every (metric, month, unit, partition, dimension values) combination is emitted once — SQL groups that
/// collapse after in-memory mapping (e.g. the first category code) are summed here.
/// </summary>
internal sealed class TaskFacts
{
    private readonly Dictionary<string, (MetricFact Fact, decimal Value, decimal Weight)> _facts = new(StringComparer.Ordinal);

    public void Add(string metric, DateOnly month, Guid? unitId, IReadOnlyDictionary<string, string> dimensions, decimal value, decimal weight = 0m,
        string partition = ReportingPartitions.Public)
    {
        var key = string.Join('\u001f', new[] { metric, month.ToString("O", System.Globalization.CultureInfo.InvariantCulture), unitId?.ToString("D") ?? "-", partition }
            .Concat(dimensions.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + "=" + d.Value)));
        _facts[key] = _facts.TryGetValue(key, out var existing)
            ? (existing.Fact, existing.Value + value, existing.Weight + weight)
            : (new MetricFact(metric, month, unitId, partition, dimensions, 0m), value, weight);
    }

    public IReadOnlyList<MetricFact> ToList() => _facts.Values.Select(f => f.Fact with { Value = f.Value, Weight = f.Weight }).ToList();

    public static IReadOnlyDictionary<string, string> Dims(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    public static string Bool(bool value) => value ? "true" : "false";

    public static IReadOnlyDictionary<string, DimensionLabel> Labels(params (string Code, string Ar, string En)[] values) =>
        values.ToDictionary(v => v.Code, v => new DimensionLabel(v.Ar, v.En), StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, DimensionLabel> YesNo = Labels(("true", "نعم", "Yes"), ("false", "لا", "No"));

    /// <summary>The months from <paramref name="from"/> to <paramref name="to"/> (first days), inclusive.</summary>
    public static IEnumerable<DateOnly> Months(DateOnly from, DateOnly to)
    {
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            yield return m;
        }
    }

    /// <summary>The UTC start of <paramref name="month"/>.</summary>
    public static DateTimeOffset Start(DateOnly month) => new(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
