namespace Strategy.Modules.Kpi.Contracts;

/// <summary>Target types of an annual plan / period (SPEC §2.4 K5): one value, or a range chosen explicitly.</summary>
public static class TargetTypes
{
    public const string SingleValue = "single";
    public const string Range = "range";

    public static readonly IReadOnlyList<string> All = [SingleValue, Range];
}

/// <summary>Measuring directions of single-value targets (the old "Proximity Better" is merged into Matching Better).</summary>
public static class Directions
{
    public const string Increase = "increase";
    public const string Decrease = "decrease";
    public const string Matching = "matching";

    public static readonly IReadOnlyList<string> All = [Increase, Decrease, Matching];
}

/// <summary>Actual (uncapped) and capped performance as ratios (1.0 = 100 %). Null = cannot be calculated.</summary>
public readonly record struct Performance(decimal? Actual, decimal? Capped)
{
    public static readonly Performance None = new(null, null);

    public bool IsCalculated => Actual is not null;
}

/// <summary>
/// The one performance calculation (SPEC §2.4 K5, CLAUDE.md §15) used by results, plans, scorecards, dashboards, reports, API, MCP and other
/// modules. A literal implementation of the SPEC pseudo-code; ratios rounded to 4 decimals (0.01 %).
/// </summary>
public static class PerformanceCalculator
{
    private const int Decimals = 4;

    public static Performance Calculate(string targetType, string? direction, decimal? target, decimal? rangeMin, decimal? rangeMax, decimal? achieve)
    {
        if (achieve is not { } a)
        {
            return Performance.None;
        }

        decimal? actual;
        if (targetType == TargetTypes.Range)
        {
            if (rangeMin is not { } min || rangeMax is not { } max || min > max)
            {
                return Performance.None;
            }

            if (min <= a && a <= max)
            {
                actual = 1m;
            }
            else if (a < min)
            {
                actual = min > 0 ? a / min : null; // below a zero / negative minimum: cannot be calculated
            }
            else
            {
                actual = a == 0 ? 1m : max / a;
            }
        }
        else
        {
            if (target is not { } t)
            {
                return Performance.None;
            }

            if (t == 0)
            {
                actual = a == 0 ? 1m : null;
            }
            else
            {
                actual = direction switch
                {
                    Directions.Increase => a / t,
                    Directions.Decrease => a == 0 ? 1m : t / a,
                    Directions.Matching => a == t ? 1m : a > t ? t / a : a / t,
                    _ => null,
                };
            }
        }

        if (actual is not { } value)
        {
            return Performance.None;
        }

        value = Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
        return new Performance(value, Math.Min(value, 1m));
    }
}
