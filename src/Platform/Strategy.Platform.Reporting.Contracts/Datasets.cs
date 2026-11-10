using System.Security.Claims;

namespace Strategy.Platform.Reporting;

public enum DatasetFieldType
{
    Text = 1,
    Number = 2,
    Money = 3,

    /// <summary>1.0 = 100 %.</summary>
    Ratio = 4,
    Date = 5,
    DateTime = 6,
    Bool = 7,

    /// <summary>A lookup code; labels from <see cref="DatasetField.LookupType"/> (or the reporting translations for fixed codes).</summary>
    Lookup = 8,

    /// <summary>An organisation unit id; shown as its name.</summary>
    Unit = 9,

    /// <summary>An employee id; shown as their name.</summary>
    Employee = 10,
}

/// <summary>
/// A report-builder field (ADR 0144). <paramref name="Permission"/>: extra permission the runner needs for this field (e.g. cost fields);
/// without it the field is not offered, dropped from columns and refused in filters, grouping and sorting.
/// </summary>
public sealed record DatasetField(
    string Code,
    string LabelAr,
    string LabelEn,
    DatasetFieldType Type,
    bool Filterable = true,
    bool Groupable = false,
    bool Sortable = true,
    string? Permission = null,
    string? LookupType = null);

/// <summary>
/// A report-builder object (one record type) declared by its module. <paramref name="Link"/>: record link with <c>{id}</c>.
/// <paramref name="ResourceType"/>: the record type of a row (external-reference columns, ADR 0164); null = the linkable type whose link
/// template equals <paramref name="Link"/>.
/// </summary>
public sealed record DatasetDefinition(
    string Code,
    string SystemCode,
    string LabelAr,
    string LabelEn,
    string ReadPermission,
    IReadOnlyList<DatasetField> Fields,
    string Link,
    string? ResourceType = null);

public static class ReportOperators
{
    public const string Equal = "eq";
    public const string NotEqual = "ne";
    public const string In = "in";
    public const string Contains = "contains";
    public const string GreaterOrEqual = "gte";
    public const string LessOrEqual = "lte";
    public const string Between = "between";
    public const string IsEmpty = "empty";
    public const string IsNotEmpty = "notEmpty";

    public static readonly IReadOnlyList<string> All = [Equal, NotEqual, In, Contains, GreaterOrEqual, LessOrEqual, Between, IsEmpty, IsNotEmpty];
}

/// <summary>One filter. Values are strings in invariant format (dates ISO, numbers with '.', bools true/false).</summary>
public sealed record ReportFilter(string Field, string Operator, IReadOnlyList<string> Values);

public sealed record ReportSort(string Field, bool Descending);

/// <summary>
/// A validated report run handed to the module: every field is declared and allowed for the runner (the engine checked it, ADR 0144).
/// <paramref name="GroupBy"/>: up to two fields; the result is then one row per group with <c>count</c> and sums of numeric columns.
/// </summary>
public sealed record ReportQuery(
    string Dataset,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ReportFilter> Filters,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<ReportSort> Sort,
    int Skip,
    int Take);

/// <summary>
/// Raw values in column order (codes, ids, numbers, dates); the engine formats labels. Grouped results carry the group fields, then
/// <c>count</c>, then the sums of numeric / money columns in <see cref="Columns"/> order.
/// </summary>
public sealed record ReportResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, int Total, IReadOnlyList<Guid>? RowIds = null);

public interface IReportingDatasetSource
{
    string SystemCode { get; }

    IReadOnlyList<DatasetDefinition> Datasets { get; }

    /// <summary>Runs on the module's scoped query for <paramref name="user"/> (same reader as its screens), in its own DbContext.</summary>
    Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);
}
