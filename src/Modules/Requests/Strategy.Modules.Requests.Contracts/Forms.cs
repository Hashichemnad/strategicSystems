using System.Text.Json.Nodes;

namespace Strategy.Modules.Requests.Contracts;

/// <summary>Field types of the form builder (SPEC §5.1, docs/design/requests.md §2.2).</summary>
public static class FieldTypes
{
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string RichText = "richtext";
    public const string Number = "number";
    public const string Currency = "currency";
    public const string Date = "date";
    public const string Checkbox = "checkbox";
    public const string Picklist = "picklist";
    public const string MultiPicklist = "multipicklist";
    public const string Employee = "employee";
    public const string Unit = "unit";
    public const string File = "file";
    public const string Record = "record";
    public const string Table = "table";
    public const string Secret = "secret";

    public static readonly IReadOnlyList<string> All =
        [Text, Textarea, RichText, Number, Currency, Date, Checkbox, Picklist, MultiPicklist, Employee, Unit, File, Record, Table, Secret];

    /// <summary>Types allowed as table columns (no nested tables, files or rich text).</summary>
    public static readonly IReadOnlyList<string> Columns = [Text, Textarea, Number, Currency, Date, Checkbox, Picklist, Employee, Unit, Secret];

    /// <summary>Types offered to approval entry conditions as <c>f_&lt;code&gt;</c> variables (ADR 0081).</summary>
    public static readonly IReadOnlyList<string> Variables = [Number, Currency, Checkbox, Picklist];
}

/// <summary>Formats of <see cref="FieldTypes.Secret"/> fields.</summary>
public static class SecretFormats
{
    /// <summary><c>784-YYYY-NNNNNNN-N</c>, masked <c>784-****-*******-N</c> (ADR 0086).</summary>
    public const string EmiratesId = "emiratesId";
}

/// <summary>Condition on another field of the same form (visibility / required).</summary>
/// <param name="Field">Code of an earlier field.</param>
/// <param name="Op"><c>eq</c>, <c>ne</c>, <c>in</c>, <c>filled</c>, <c>empty</c>.</param>
/// <param name="Value">Compared value (<c>in</c>: an array).</param>
public sealed record FieldCondition(string Field, string Op, JsonNode? Value = null)
{
    public static readonly IReadOnlyList<string> Ops = ["eq", "ne", "in", "filled", "empty"];
}

/// <summary>One field of a form, mini-form or table (column).</summary>
public sealed record FormField
{
    public string Code { get; init; } = string.Empty;

    public string Type { get; init; } = FieldTypes.Text;

    public string? LabelAr { get; init; }

    public string? LabelEn { get; init; }

    public string? HelpAr { get; init; }

    public string? HelpEn { get; init; }

    public bool Required { get; init; }

    /// <summary>Defined in code by the owning module: the builder cannot remove or retype it.</summary>
    public bool System { get; init; }

    public int? MaxLength { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    public int? Decimals { get; init; }

    /// <summary>Lookup type code of picklists.</summary>
    public string? LookupType { get; init; }

    /// <summary>Linkable type of <see cref="FieldTypes.Record"/> fields (Links registry).</summary>
    public string? TargetType { get; init; }

    /// <summary>Columns of a table.</summary>
    public IReadOnlyList<FormField>? Columns { get; init; }

    public int? MinRows { get; init; }

    public int? MaxRows { get; init; }

    /// <summary>Secret format (<see cref="SecretFormats"/>).</summary>
    public string? Format { get; init; }

    /// <summary>File fields: more than one file.</summary>
    public bool Multiple { get; init; }

    /// <summary>File fields: allowed kinds (<c>pdf</c>, …); empty = any document kind.</summary>
    public IReadOnlyList<string>? Accept { get; init; }

    /// <summary><c>ar</c> / <c>en</c>: the value is the request's title in that language.</summary>
    public string? TitleOf { get; init; }

    /// <summary>A unit field whose value is the request's unit (approver resolution).</summary>
    public bool RequestUnit { get; init; }

    public FieldCondition? VisibleWhen { get; init; }

    public FieldCondition? RequiredWhen { get; init; }
}

public sealed record FormSection
{
    public string Code { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public bool System { get; init; }

    public IReadOnlyList<FormField> Fields { get; init; } = [];
}

/// <summary>A form: sections of fields (request form, approve form or action mini-form).</summary>
public sealed record FormSchema
{
    public IReadOnlyList<FormSection> Sections { get; init; } = [];

    public static FormSchema Of(params FormField[] fields) => new() { Sections = [new FormSection { Code = "main", Fields = fields }] };

    public IEnumerable<FormField> AllFields => Sections.SelectMany(s => s.Fields);

    public FormField? Find(string code) => AllFields.FirstOrDefault(f => f.Code == code);

    public bool IsEmpty => !AllFields.Any();
}
