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

    /// <summary>A code; labels from <see cref="DatasetField.LookupType"/>, <see cref="DatasetField.LabelSource"/> or <see cref="DatasetField.Choices"/> (ADR 0209).</summary>
    Lookup = 8,

    /// <summary>An organisation unit id; shown as its name.</summary>
    Unit = 9,

    /// <summary>An employee id; shown as their name.</summary>
    Employee = 10,

    /// <summary>
    /// A record id (<see cref="Guid"/>) used as a relationship key (Reporting 2.0, ADR 0180): never offered as a column to people,
    /// filterable only with <c>eq</c> / <c>in</c> / <c>empty</c> / <c>notEmpty</c>; published in the reportable metadata for joins.
    /// </summary>
    Reference = 11,

    /// <summary>Sanitised rich text (HTML, Phase 19): every report, export and dossier shows its plain-text projection.</summary>
    RichText = 12,
}

/// <summary>
/// A report-builder field (ADR 0144). <paramref name="Permission"/>: extra permission the runner needs for this field (e.g. cost fields);
/// without it the field is not offered, dropped from columns and refused in filters, grouping and sorting.
/// A field holding codes (<see cref="DatasetFieldType.Lookup"/>, or a <see cref="DatasetFieldType.Text"/> field that holds codes) declares where
/// its labels come from (ADR 0209), in this order: <paramref name="LookupType"/> (a Settings picklist), <paramref name="LabelSource"/> (codes kept
/// in module tables or settings, e.g. request types, priority bands) or <paramref name="Choices"/> (fixed codes, e.g. lifecycle statuses).
/// Reports, charts, dossiers and exports show the label in the reader's language; a code no source knows is shown as it is.
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
    string? LookupType = null,
    IReadOnlyList<RecordFieldChoice>? Choices = null,
    FieldLabelSource? LabelSource = null)
{
    /// <summary>The field declares labels for its codes (a lookup type, a label source or fixed choices).</summary>
    public bool HasCodeLabels => LookupType is not null || LabelSource is not null || Choices is { Count: > 0 };
}

/// <summary>
/// Labels of codes kept in module tables or settings (ADR 0209): the <see cref="IReportingDimensionLabelSource"/> of
/// <paramref name="SystemCode"/> answers <paramref name="Dimension"/> (the same sources label dashboard breakdowns, Phase 17).
/// </summary>
public sealed record FieldLabelSource(string SystemCode, string Dimension)
{
    /// <summary>Names of systems (the access catalogue's AR / EN system names), for fields holding a system code.</summary>
    public static readonly FieldLabelSource SystemNames = new("reporting", "system");
}

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
    string? ResourceType = null)
{
    /// <summary>
    /// The code of the <see cref="DatasetFieldType.Reference"/> field holding a row's own id (the key related datasets join on), or null when
    /// the dataset is never a parent (ADR 0180).
    /// </summary>
    public string? KeyField { get; init; }

    /// <summary>
    /// Join-only datasets (e.g. approval history) have no scoped list of their own: the reporting engine reads them only through a relationship,
    /// restricted to the keys of parent rows the runner already sees, and never offers them as a primary object (ADR 0180).
    /// </summary>
    public bool JoinOnly { get; init; }
}

/// <summary>
/// A relationship between two datasets (Reporting 2.0, ADR 0180): a parent row matches the child rows whose <paramref name="ChildField"/>
/// equals the parent's <paramref name="ParentField"/> (usually the parent's key). Both fields are declared fields of their datasets, normally of
/// type <see cref="DatasetFieldType.Reference"/>. Declared by the module that owns the reference — across systems through the dataset codes
/// only (no project reference, no cross-schema SQL). The same relationships drive report types, record dossiers and (Phase 22) OData <c>$expand</c>.
/// </summary>
public sealed record DatasetRelationship(
    string Code,
    string ParentDataset,
    string ParentField,
    string ChildDataset,
    string ChildField,
    string LabelAr,
    string LabelEn);

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

    /// <summary>Relationships this module declares (from or to its datasets; ADR 0180). Default: none.</summary>
    IReadOnlyList<DatasetRelationship> Relationships => [];
}

/// <summary>
/// Read-only view of everything reportable (ADR 0180): datasets (record types), their fields and the relationships between them, with a
/// permission-checked runner. Used by report types, record dossiers and integrations (OData <c>$expand</c>, Phase 22). Every run goes through
/// the owning module's scoped query as <c>user</c>; callers must still drop fields whose <see cref="DatasetField.Permission"/> the user lacks.
/// </summary>
public interface IReportableMetadata
{
    IReadOnlyList<DatasetDefinition> Datasets { get; }

    IReadOnlyList<DatasetRelationship> Relationships { get; }

    DatasetDefinition? Dataset(string code);

    /// <summary>Relationships whose parent is <paramref name="parentDataset"/>.</summary>
    IReadOnlyList<DatasetRelationship> RelationshipsFrom(string parentDataset);

    /// <summary>Runs <paramref name="query"/> in the owning module for <paramref name="user"/>. Unknown dataset → <see cref="ArgumentException"/>.</summary>
    Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);
}
