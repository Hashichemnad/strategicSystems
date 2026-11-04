namespace Strategy.Modules.Kpi.Domain;

/// <summary>Measuring periods of a card / plan (SPEC §2.3 A).</summary>
public static class MeasuringPeriods
{
    public const string Quarterly = "quarterly";
    public const string HalfYearly = "half_yearly";
    public const string Annual = "annual";
    public const string TwoYear = "two_year";

    public static readonly IReadOnlyList<string> All = [Quarterly, HalfYearly, Annual, TwoYear];
}

/// <summary>Value periods of a result (SPEC §2.3 C).</summary>
public static class PeriodCodes
{
    public const string Q1 = "q1";
    public const string Q2 = "q2";
    public const string Q3 = "q3";
    public const string Q4 = "q4";
    public const string H1 = "h1";
    public const string H2 = "h2";
    public const string FullYear = "fy";
    public const string TwoYear = "ty";

    public static readonly IReadOnlyList<string> All = [Q1, Q2, Q3, Q4, H1, H2, FullYear, TwoYear];
}

public static class CardStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}

/// <summary>Quantitative or descriptive (qualitative, scored on a scale) KPI.</summary>
public static class Natures
{
    public const string Quantitative = "quantitative";
    public const string Qualitative = "qualitative";

    public static readonly IReadOnlyList<string> All = [Quantitative, Qualitative];
}

/// <summary>Cascading level of a card (SPEC §2.7; employee KPIs are out of scope).</summary>
public static class CardLevels
{
    public const string Organisation = "organisation";
    public const string Department = "department";
    public const string Section = "section";

    public static readonly IReadOnlyList<string> All = [Organisation, Department, Section];
}

/// <summary>Strategy hierarchy kinds (ADR 0062): vision → perspective (pillar) → objective.</summary>
public static class ElementKinds
{
    public const string Vision = "vision";
    public const string Perspective = "perspective";
    public const string Objective = "objective";

    public static readonly IReadOnlyList<string> All = [Vision, Perspective, Objective];

    public static string? ParentKindOf(string kind) => kind switch
    {
        Perspective => Vision,
        Objective => Perspective,
        _ => null,
    };
}

public static class PlanStatuses
{
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";
}

/// <summary>Aggregation rules a Measuring Method code maps to (Settings → KPI).</summary>
public static class AggregationRules
{
    public const string Sum = "sum";
    public const string Average = "average";

    public static readonly IReadOnlyList<string> All = [Sum, Average];
}

/// <summary>Unit code that means "percentage" (seeded in <c>kpi.unit</c>).</summary>
public static class Units
{
    public const string Percentage = "percentage";
}

public static class ChangeStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Withdrawn = "withdrawn";
}
