using System.Security.Claims;

namespace Strategy.Platform.Reporting;

/// <summary>Type of a reportable field (ADR 0190). <see cref="Id"/>: a record id (keys of related records).</summary>
public enum RecordFieldType
{
    Id = 1,
    Text = 2,
    Number = 3,
    Money = 4,

    /// <summary>1.0 = 100 %.</summary>
    Ratio = 5,
    Date = 6,
    DateTime = 7,
    Bool = 8,

    /// <summary>A code; labels from <see cref="RecordFieldDefinition.LookupType"/> or <see cref="RecordFieldDefinition.Choices"/>.</summary>
    Lookup = 9,

    /// <summary>An organisation unit id.</summary>
    Unit = 10,

    /// <summary>An employee id.</summary>
    Employee = 11,
}

/// <summary>A fixed code of a <see cref="RecordFieldType.Lookup"/> field that is not a Settings picklist (e.g. a lifecycle status).</summary>
public sealed record RecordFieldChoice(string Code, string LabelAr, string LabelEn);

/// <summary>
/// A field of a reportable record type. <paramref name="Permission"/>: extra permission (without a record) the reader needs for the field;
/// without it the field is not offered and is refused in queries.
/// </summary>
public sealed record RecordFieldDefinition(
    string Code,
    string LabelAr,
    string LabelEn,
    RecordFieldType Type,
    string? Permission = null,
    string? LookupType = null,
    IReadOnlyList<RecordFieldChoice>? Choices = null);

/// <summary>
/// A relationship from one record type to another: target rows whose <paramref name="TargetField"/> equals this row's
/// <paramref name="SourceField"/>. <paramref name="Many"/>: a collection (e.g. project → milestones); otherwise at most one (milestone → project).
/// </summary>
public sealed record RecordRelationship(
    string Name, string LabelAr, string LabelEn, string TargetType, string SourceField, string TargetField, bool Many);

/// <summary>
/// A record type exposed for reporting and analytics feeds (ADR 0190). <paramref name="Code"/> is the resource type (e.g.
/// <c>portfolio.project</c>); <paramref name="EntitySet"/> the feed name (<c>Projects</c>). Every type also has the implicit fields
/// <see cref="RecordFields.Id"/> and <see cref="RecordFields.ModifiedAt"/>.
/// </summary>
public sealed record ReportableRecordType(
    string Code,
    string SystemCode,
    string EntitySet,
    string LabelAr,
    string LabelEn,
    string ReadPermission,
    IReadOnlyList<RecordFieldDefinition> Fields,
    IReadOnlyList<RecordRelationship> Relationships,
    string? Link = null)
{
    /// <summary>The declared field (implicit ones included), or null.</summary>
    public RecordFieldDefinition? Field(string code) =>
        code == RecordFields.Id ? RecordFields.IdField
        : code == RecordFields.ModifiedAt ? RecordFields.ModifiedAtField
        : Fields.FirstOrDefault(f => f.Code == code);
}

public static class RecordFields
{
    public const string Id = "id";
    public const string ModifiedAt = "modifiedAt";

    public static readonly RecordFieldDefinition IdField = new(Id, "المعرّف", "Id", RecordFieldType.Id);

    public static readonly RecordFieldDefinition ModifiedAtField = new(ModifiedAt, "آخر تعديل", "Modified at", RecordFieldType.DateTime);
}

public enum RecordCompare
{
    Eq = 1,
    Ne = 2,
    Gt = 3,
    Ge = 4,
    Lt = 5,
    Le = 6,
}

public enum RecordTextMatch
{
    Contains = 1,
    StartsWith = 2,
    EndsWith = 3,
}

/// <summary>
/// A neutral filter tree. Values are typed by the field: string (Text, Lookup), decimal (Number, Money, Ratio), Guid (Id, Unit, Employee),
/// DateOnly (Date), DateTimeOffset (DateTime), bool (Bool) — or null.
/// </summary>
public abstract record RecordFilter;

public sealed record RecordFilterAnd(RecordFilter Left, RecordFilter Right) : RecordFilter;

public sealed record RecordFilterOr(RecordFilter Left, RecordFilter Right) : RecordFilter;

public sealed record RecordFilterNot(RecordFilter Inner) : RecordFilter;

public sealed record RecordFilterCompare(string Field, RecordCompare Operator, object? Value) : RecordFilter;

public sealed record RecordFilterText(string Field, RecordTextMatch Match, string Value) : RecordFilter;

public sealed record RecordFilterIn(string Field, IReadOnlyList<object?> Values) : RecordFilter;

public sealed record RecordSort(string Field, bool Descending = false);

/// <summary>Keyset position: rows after (<paramref name="ModifiedAt"/>, <paramref name="Id"/>) in (modifiedAt, id) order.</summary>
public sealed record RecordCursor(DateTimeOffset ModifiedAt, Guid Id);

public enum RecordQueryMode
{
    /// <summary>Every readable record.</summary>
    Current = 1,

    /// <summary>Only records in the type's "deleted" condition (cancelled / withdrawn / inactive) — the deleted feed.</summary>
    Deleted = 2,
}

/// <summary>
/// A validated query (the consumer checked every field against the reader's allowed fields). <paramref name="ChangedSince"/>: only rows
/// with <c>modifiedAt</c> after it. <paramref name="After"/>: keyset paging in (modifiedAt, id) order (sort is then ignored).
/// </summary>
public sealed record RecordQuery(
    string RecordType,
    IReadOnlyList<string> Fields,
    RecordFilter? Filter,
    IReadOnlyList<RecordSort> OrderBy,
    int Skip,
    int Take,
    bool IncludeTotal = false,
    DateTimeOffset? ChangedSince = null,
    RecordCursor? After = null,
    RecordQueryMode Mode = RecordQueryMode.Current);

/// <summary>Raw values (codes, ids, numbers, dates) in <see cref="Fields"/> order.</summary>
public sealed record RecordPage(IReadOnlyList<string> Fields, IReadOnlyList<IReadOnlyList<object?>> Rows, long? Total);

/// <summary>Implemented by each module (in its own DbContext, on the same scoped reader as its screens).</summary>
public interface IReportableRecordSource
{
    string SystemCode { get; }

    IReadOnlyList<ReportableRecordType> RecordTypes { get; }

    /// <summary>Only rows <paramref name="user"/> may read.</summary>
    Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public static class ReportableRecordServiceCollectionExtensions
{
    /// <summary>Registers a module's reportable record source (ADR 0190).</summary>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddReportableRecordSource<TSource>(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        where TSource : class, IReportableRecordSource =>
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<IReportableRecordSource, TSource>(services);
}
