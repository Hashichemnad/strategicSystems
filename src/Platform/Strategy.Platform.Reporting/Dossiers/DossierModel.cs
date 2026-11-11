using Strategy.Platform.Reporting.ReportTypes;

namespace Strategy.Platform.Reporting.Dossiers;

public static class DossierSectionKinds
{
    public const string Fields = "fields";
    public const string Text = "text";
    public const string Related = "related";
    public const string Chart = "chart";
    public const string Trend = "trend";
    public const string Gantt = "gantt";
    public const string Matrix = "matrix";
    public const string Approvals = "approvals";
    public const string Attachments = "attachments";

    public static readonly IReadOnlyList<string> All = [Fields, Text, Related, Chart, Trend, Gantt, Matrix, Approvals, Attachments];
}

public static class DossierLanguages
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string Bilingual = "bilingual";

    public static readonly IReadOnlyList<string> All = [Arabic, English, Bilingual];
}

public sealed record DossierCover
{
    public bool Show { get; init; } = true;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    /// <summary>A record field shown under the title (qualified, e.g. <c>a.nameEn</c>).</summary>
    public string? SubtitleField { get; init; }

    public bool ShowOrganisation { get; init; } = true;

    public bool ShowDate { get; init; } = true;
}

/// <summary>A section is shown only when the record's field matches (operators as report filters, evaluated on the raw value).</summary>
public sealed record DossierCondition
{
    public string Field { get; init; } = string.Empty;

    public string Operator { get; init; } = ReportOperators.IsNotEmpty;

    public List<string> Values { get; init; } = [];
}

/// <summary>
/// One block of a dossier (ADR 0184). Fields are qualified by level: <c>a</c> = the record, <c>b</c>, <c>c</c>, <c>d</c> = the objects reached along
/// <see cref="Path"/> (relationship codes from the record's dataset, up to three).
/// </summary>
public sealed record DossierSection
{
    public string Key { get; init; } = string.Empty;

    public string? TitleAr { get; init; }

    public string? TitleEn { get; init; }

    public string Kind { get; init; } = DossierSectionKinds.Fields;

    /// <summary><c>fields</c>: record fields; <c>related</c>: table columns.</summary>
    public List<string>? Fields { get; init; }

    public string? TextAr { get; init; }

    public string? TextEn { get; init; }

    public List<string>? Path { get; init; }

    /// <summary>Filters on the last object of the path (qualified fields).</summary>
    public List<ReportFilter>? Filters { get; init; }

    public List<ReportSort>? Sort { get; init; }

    /// <summary>Rows of a related list (capped by Settings → Reporting).</summary>
    public int? Limit { get; init; }

    public ReportChart? Chart { get; init; }

    /// <summary><c>trend</c>: x axis field and the measured fields (one line each).</summary>
    public string? Category { get; init; }

    public List<string>? Measures { get; init; }

    /// <summary><c>gantt</c>: bar label (Arabic / English field), planned and actual dates.</summary>
    public string? LabelAr { get; init; }

    public string? LabelEn { get; init; }

    public string? Start { get; init; }

    public string? End { get; init; }

    public string? ActualStart { get; init; }

    public string? ActualEnd { get; init; }

    /// <summary><c>matrix</c>: likelihood and impact fields, matrix size (3–7).</summary>
    public string? Likelihood { get; init; }

    public string? Impact { get; init; }

    public int Size { get; init; } = 5;

    /// <summary><c>approvals</c>: the record type the approval engine uses (e.g. <c>portfolio.project</c>).</summary>
    public string? ResourceType { get; init; }

    /// <summary><c>attachments</c>: the file owner type (e.g. <c>portfolio.project</c>).</summary>
    public string? OwnerType { get; init; }

    public DossierCondition? Condition { get; init; }

    public bool PageBreak { get; init; }
}

public sealed record DossierDefinition
{
    public DossierCover Cover { get; init; } = new();

    /// <summary>The record's number and name fields (file names, record picker, page header).</summary>
    public string? NumberField { get; init; }

    public string? NameFieldAr { get; init; }

    public string? NameFieldEn { get; init; }

    public string? HeaderAr { get; init; }

    public string? HeaderEn { get; init; }

    public string? FooterAr { get; init; }

    public string? FooterEn { get; init; }

    public string Language { get; init; } = DossierLanguages.Arabic;

    public List<DossierSection> Sections { get; init; } = [];
}
