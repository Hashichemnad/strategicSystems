using System.Security.Claims;

namespace Strategy.Platform.Reporting;

/// <summary>How a metric behaves over time (ADR 0142).</summary>
public enum MetricKind
{
    /// <summary>State at the end of the period (open findings, projects by health). A period reads its last month.</summary>
    Stock = 1,

    /// <summary>Events in the period (results approved, actions completed). A period sums its months.</summary>
    Flow = 2,
}

/// <summary>How fact components combine (ADR 0141). Facts only hold additive parts, so any set of units recombines exactly.</summary>
public enum MetricAggregation
{
    /// <summary>Σ <see cref="MetricFact.Value"/> (counts, amounts).</summary>
    Sum = 1,

    /// <summary>Σ <see cref="MetricFact.Value"/> / Σ <see cref="MetricFact.Weight"/> (rates, averages, weighted scores). Null when Σ weight = 0.</summary>
    Ratio = 2,
}

/// <summary>Display kind of a metric's value.</summary>
public enum MetricValueKind
{
    Count = 1,

    /// <summary>A ratio stored as 1.0 = 100 % (CLAUDE.md §6).</summary>
    Ratio = 2,

    /// <summary>Money in the installation currency.</summary>
    Money = 3,

    /// <summary>A performance score stored as 1.0 = 100 %.</summary>
    Score = 4,

    Hours = 5,

    Days = 6,
}

/// <summary>Fact partitions for records restricted beyond the data scope (ADR 0141).</summary>
public static class ReportingPartitions
{
    /// <summary>Visible to everybody whose data scope covers the unit.</summary>
    public const string Public = "public";

    /// <summary>Confidential records (e.g. confidential tasks): only participants, record by record — never served from the cache.</summary>
    public const string Confidential = "confidential";

    /// <summary>Cost / budget figures (Portfolio cost visibility, ADR 0098).</summary>
    public const string Costs = "costs";

    /// <summary>Records only an office sees (e.g. findings of draft audit reports).</summary>
    public const string Office = "office";

    /// <summary>Document sensitivity level <paramref name="code"/>.</summary>
    public static string Sensitivity(string code) => "sensitivity:" + code;
}

/// <summary>Well-known dimension codes shared across systems, so cross-system widgets can line up.</summary>
public static class ReportingDimensions
{
    public const string Status = "status";
    public const string Stage = "stage";
    public const string Priority = "priority";
    public const string Type = "type";
    public const string Health = "health";
    public const string Band = "band";
    public const string Severe = "severe";
    public const string Overdue = "overdue";
    public const string OnTime = "onTime";
    public const string AgeBucket = "age";
    public const string Source = "source";
    public const string Objective = "objective";
    public const string Perspective = "perspective";
    public const string Category = "category";

    /// <summary>Age buckets in days used by every ageing analysis: 0-30, 31-90, 91-180, 181-365, 365+.</summary>
    public static string AgeBucketOf(int days) => days switch
    {
        <= 30 => "0-30",
        <= 90 => "31-90",
        <= 180 => "91-180",
        <= 365 => "181-365",
        _ => "365+",
    };

    public static readonly IReadOnlyList<string> AgeBuckets = ["0-30", "31-90", "91-180", "181-365", "365+"];
}

/// <summary>
/// A dashboard metric declared by its owning module (docs/design/reporting.md §2.1).
/// </summary>
/// <remarks><c>Code</c>: <c>&lt;system&gt;.&lt;metric&gt;</c>, stable.</remarks>
/// <remarks><c>SystemCode</c>: Owning system.</remarks>
/// <remarks><c>ScopePermission</c>: 
/// The record read permission whose data scope decides which records count for a viewer (the permission the module's reader uses).
/// The fact's <see cref="MetricFact.UnitId"/> must be the unit column of that permission's scope map.
/// </remarks>
/// <remarks><c>Dimensions</c>: Breakdowns a widget may use (codes; values are codes too, labels come from <paramref name="DimensionLookups"/>).</remarks>
/// <remarks><c>HigherIsBetter</c>: Direction for deltas and RAG; null = neutral.</remarks>
/// <remarks><c>DrillLink</c>: SPA link of the system view for drill-down, with <c>{unit}</c>, <c>{from}</c>, <c>{to}</c> and <c>{dim:&lt;code&gt;}</c> placeholders.</remarks>
/// <remarks><c>DimensionLookups</c>: Dimension → lookup type (settings) for labels.</remarks>
/// <remarks><c>DimensionLabels</c>: Dimension → value code → AR / EN label, for fixed codes that are not lookups (e.g. status codes).</remarks>
public sealed record MetricDefinition(
    string Code,
    string SystemCode,
    string LabelAr,
    string LabelEn,
    MetricKind Kind,
    MetricAggregation Aggregation,
    MetricValueKind ValueKind,
    string ScopePermission,
    IReadOnlyList<string> Dimensions,
    bool? HigherIsBetter = null,
    string? DrillLink = null,
    IReadOnlyDictionary<string, string>? DimensionLookups = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, DimensionLabel>>? DimensionLabels = null);

/// <summary>An AR / EN label of a fixed dimension value.</summary>
public sealed record DimensionLabel(string Ar, string En);

/// <summary>
/// One additive fact: the records of one owning unit (null = no unit, visible only with scope All) and partition in one month, for one
/// combination of dimension values.
/// </summary>
public sealed record MetricFact(
    string Metric,
    DateOnly Month,
    Guid? UnitId,
    string Partition,
    IReadOnlyDictionary<string, string> Dimensions,
    decimal Value,
    decimal Weight = 0m);

/// <summary>
/// What to compute. <see cref="User"/> null: every record (the cache, run by the refresh job as the system). Otherwise only the records
/// that user may read (live path, ADR 0141) — the module applies the same reader / scope as its own screens.
/// Stock metrics are computed for the state now and stamped with <see cref="CurrentMonth"/> (only when it is inside the range); flow metrics
/// for every month of the range from the records' dates.
/// </summary>
public sealed record MetricComputation(
    IReadOnlyCollection<string> Metrics,
    DateOnly FromMonth,
    DateOnly ToMonth,
    DateOnly CurrentMonth,
    DateOnly Today,
    ClaimsPrincipal? User)
{
    public bool Wants(string metric) => Metrics.Contains(metric);

    public bool CoversCurrentMonth => CurrentMonth >= FromMonth && CurrentMonth <= ToMonth;

    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly MonthOf(DateTimeOffset at) => new(at.Year, at.Month, 1);

    public bool InRange(DateOnly month) => month >= FromMonth && month <= ToMonth;
}

/// <summary>Implemented once per module, in its <c>Reporting/</c> folder, on its own DbContext (ADR 0140).</summary>
public interface IReportingMetricSource
{
    string SystemCode { get; }

    IReadOnlyList<MetricDefinition> Metrics { get; }

    Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken);

    /// <summary>
    /// Restricted partitions (besides <see cref="ReportingPartitions.Public"/>) whose records the user may read <b>in full</b> within their
    /// data scope (e.g. the Audit Office reads <see cref="ReportingPartitions.Office"/>; a cleared user reads a sensitivity level).
    /// </summary>
    Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>
/// Labels of dimension values kept in module tables (e.g. KPI strategic objectives, team types), for codes neither a lookup nor fixed labels
/// cover. Optional; implemented by the module that owns the codes.
/// </summary>
public interface IReportingDimensionLabelSource
{
    string SystemCode { get; }

    Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
}
