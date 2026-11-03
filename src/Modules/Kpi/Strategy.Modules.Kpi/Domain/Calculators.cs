using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.Kernel.Expressions;

namespace Strategy.Modules.Kpi.Domain;

/// <summary>Performance bands on capped performance (SPEC §2.7). Thresholds are ratios (0.95 = 95 %).</summary>
public static class RagBands
{
    public const decimal DefaultGreen = 0.95m;
    public const decimal DefaultAmber = 0.80m;

    public static string Band(decimal? capped, decimal green = DefaultGreen, decimal amber = DefaultAmber) => capped switch
    {
        null => RagCodes.None,
        { } c when c >= green => RagCodes.Green,
        { } c when c >= amber => RagCodes.Amber,
        _ => RagCodes.Red,
    };
}

/// <summary>A period of a measuring year: its code, dates and order.</summary>
public sealed record PeriodSpec(string Code, DateOnly Start, DateOnly End, int Sort);

/// <summary>A period target (single or range).</summary>
public sealed record PeriodTarget(decimal? Target, decimal? RangeMin, decimal? RangeMax);

/// <summary>Result periods per measuring period (SPEC §2.4 K1 step 3) and default period targets (design note §5.4).</summary>
public static class PeriodSchedule
{
    public static IReadOnlyList<string> CodesFor(string measuringPeriod) => measuringPeriod switch
    {
        MeasuringPeriods.Quarterly => [PeriodCodes.Q1, PeriodCodes.Q2, PeriodCodes.Q3, PeriodCodes.Q4],
        MeasuringPeriods.HalfYearly => [PeriodCodes.H1, PeriodCodes.H2],
        MeasuringPeriods.Annual => [PeriodCodes.FullYear],
        MeasuringPeriods.TwoYear => [PeriodCodes.TwoYear],
        _ => throw new ArgumentOutOfRangeException(nameof(measuringPeriod), measuringPeriod, null),
    };

    /// <summary>The measuring period a value period belongs to (validation: the period must match the plan's measuring period).</summary>
    public static string? MeasuringPeriodOf(string periodCode) => periodCode switch
    {
        PeriodCodes.Q1 or PeriodCodes.Q2 or PeriodCodes.Q3 or PeriodCodes.Q4 => MeasuringPeriods.Quarterly,
        PeriodCodes.H1 or PeriodCodes.H2 => MeasuringPeriods.HalfYearly,
        PeriodCodes.FullYear => MeasuringPeriods.Annual,
        PeriodCodes.TwoYear => MeasuringPeriods.TwoYear,
        _ => null,
    };

    public static PeriodSpec Spec(int year, string code)
    {
        DateOnly D(int y, int m, int d) => new(y, m, d);
        return code switch
        {
            PeriodCodes.Q1 => new(code, D(year, 1, 1), D(year, 3, 31), 1),
            PeriodCodes.Q2 => new(code, D(year, 4, 1), D(year, 6, 30), 2),
            PeriodCodes.Q3 => new(code, D(year, 7, 1), D(year, 9, 30), 3),
            PeriodCodes.Q4 => new(code, D(year, 10, 1), D(year, 12, 31), 4),
            PeriodCodes.H1 => new(code, D(year, 1, 1), D(year, 6, 30), 1),
            PeriodCodes.H2 => new(code, D(year, 7, 1), D(year, 12, 31), 2),
            PeriodCodes.FullYear => new(code, D(year, 1, 1), D(year, 12, 31), 1),
            PeriodCodes.TwoYear => new(code, D(year, 1, 1), D(year + 1, 12, 31), 1),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };
    }

    public static IReadOnlyList<PeriodSpec> For(int year, string measuringPeriod) => CodesFor(measuringPeriod).Select(c => Spec(year, c)).ToList();

    /// <summary>
    /// Default period targets from the annual target: a <c>sum</c> rule splits a single target equally (4 decimals, remainder on the last
    /// period); <c>average</c> rules and ranges repeat the annual values.
    /// </summary>
    public static IReadOnlyList<PeriodTarget> DefaultTargets(string aggregationRule, string targetType, decimal? target, decimal? rangeMin, decimal? rangeMax, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (targetType == TargetTypes.Range)
        {
            if (aggregationRule == AggregationRules.Sum && rangeMin is { } min && rangeMax is { } max)
            {
                var mins = Split(min, count);
                var maxes = Split(max, count);
                return Enumerable.Range(0, count).Select(i => new PeriodTarget(null, mins[i], maxes[i])).ToList();
            }

            return Enumerable.Repeat(new PeriodTarget(null, rangeMin, rangeMax), count).ToList();
        }

        if (aggregationRule == AggregationRules.Sum && target is { } t)
        {
            return Split(t, count).Select(v => new PeriodTarget(v, null, null)).ToList();
        }

        return Enumerable.Repeat(new PeriodTarget(target, null, null), count).ToList();
    }

    private static decimal[] Split(decimal total, int count)
    {
        var part = Math.Round(total / count, 4, MidpointRounding.ToZero);
        var parts = Enumerable.Repeat(part, count).ToArray();
        parts[^1] = total - (part * (count - 1));
        return parts;
    }
}

/// <summary>A submission-window rule (ADR 0060).</summary>
public sealed record WindowRule(string PeriodCode, int OpensDaysAfterEnd, int DueDaysAfterOpen);

/// <summary>Period windows (SPEC K2, Appendix A Q-K2): opens the day after the period ends, due on the 15th; December periods due 31 January.</summary>
public static class WindowRules
{
    public static readonly IReadOnlyList<WindowRule> Defaults =
    [
        new(PeriodCodes.Q1, 1, 14), new(PeriodCodes.Q2, 1, 14), new(PeriodCodes.Q3, 1, 14), new(PeriodCodes.Q4, 1, 30),
        new(PeriodCodes.H1, 1, 14), new(PeriodCodes.H2, 1, 30), new(PeriodCodes.FullYear, 1, 30), new(PeriodCodes.TwoYear, 1, 30),
    ];

    public static (DateOnly OpensOn, DateOnly DueOn) Window(PeriodSpec period, IReadOnlyList<WindowRule> rules)
    {
        ArgumentNullException.ThrowIfNull(period);
        var rule = rules.FirstOrDefault(r => r.PeriodCode == period.Code) ?? Defaults.First(r => r.PeriodCode == period.Code);
        var opens = period.End.AddDays(rule.OpensDaysAfterEnd);
        return (opens, opens.AddDays(rule.DueDaysAfterOpen));
    }

    /// <summary>Late = submitted after the due moment; days late = calendar days after the due date (at least 1 when late).</summary>
    public static (bool Late, int DaysLate) Lateness(DateTimeOffset submittedAt, DateOnly submittedOn, DateTimeOffset? dueAt, DateOnly? dueOn)
    {
        if (dueAt is not { } due || submittedAt <= due)
        {
            return (false, 0);
        }

        var days = dueOn is { } d ? submittedOn.DayNumber - d.DayNumber : 0;
        return (true, Math.Max(1, days));
    }
}

/// <summary>One period of a plan as the aggregation sees it.</summary>
public sealed record PeriodValue(decimal? Target, decimal? RangeMin, decimal? RangeMax, decimal? Achieve, bool NotApplicable, bool Approved);

/// <summary>Aggregated achieved value and the comparable target of the counted periods.</summary>
public sealed record AggregateValue(decimal? Achieved, decimal? Target, decimal? RangeMin, decimal? RangeMax, int Counted, decimal? Last);

/// <summary>
/// Annual aggregation by Measuring Method (SPEC K5, design note §5.3): approved, not N/A periods with a value count; <c>sum</c> adds values and
/// targets, <c>average</c> averages them; the annual target is used when the counted periods have no period targets.
/// </summary>
public static class AnnualAggregator
{
    public static AggregateValue Aggregate(
        string rule, string targetType, IReadOnlyList<PeriodValue> periods, decimal? annualTarget, decimal? annualMin, decimal? annualMax)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var counted = periods.Where(p => p.Approved && !p.NotApplicable && p.Achieve is not null).ToList();
        if (counted.Count == 0)
        {
            return new AggregateValue(null, annualTarget, annualMin, annualMax, 0, null);
        }

        var sum = rule == AggregationRules.Sum;
        decimal? Combine(IEnumerable<decimal?> values)
        {
            var list = values.ToList();
            if (list.Any(v => v is null))
            {
                return null;
            }

            var total = list.Sum(v => v!.Value);
            return sum ? total : Math.Round(total / list.Count, 4, MidpointRounding.AwayFromZero);
        }

        var achieved = Combine(counted.Select(p => p.Achieve));
        decimal? target = null, min = null, max = null;
        if (targetType == TargetTypes.Range)
        {
            min = Combine(counted.Select(p => p.RangeMin)) ?? annualMin;
            max = Combine(counted.Select(p => p.RangeMax)) ?? annualMax;
        }
        else
        {
            target = Combine(counted.Select(p => p.Target)) ?? annualTarget;
        }

        return new AggregateValue(achieved, target, min, max, counted.Count, counted[^1].Achieve);
    }

    public static Performance Performance(string rule, string targetType, string direction, IReadOnlyList<PeriodValue> periods, decimal? annualTarget, decimal? annualMin, decimal? annualMax)
    {
        var aggregate = Aggregate(rule, targetType, periods, annualTarget, annualMin, annualMax);
        return aggregate.Counted == 0
            ? Contracts.Performance.None
            : PerformanceCalculator.Calculate(targetType, direction, aggregate.Target, aggregate.RangeMin, aggregate.RangeMax, aggregate.Achieved);
    }
}

/// <summary>A node of a scorecard: its weight within the parent, its own score (KPI capped performance) and its children.</summary>
public sealed record ScoreNode(decimal? Weight, decimal? OwnScore, IReadOnlyList<ScoreNode> Children)
{
    public static ScoreNode Leaf(decimal? weight, decimal? score) => new(weight, score, []);
}

/// <summary>Weighted roll-up of capped performance (ADR 0062): missing scores left out and weights re-normalised; null weights are equal.</summary>
public static class ScorecardCalculator
{
    public static decimal? WeightedAverage(IEnumerable<(decimal? Weight, decimal? Score)> items)
    {
        var scored = items.Where(i => i.Score is not null).ToList();
        if (scored.Count == 0)
        {
            return null;
        }

        var anyWeight = scored.Any(i => i.Weight is > 0);
        decimal totalWeight = 0, total = 0;
        foreach (var (weight, score) in scored)
        {
            var w = anyWeight ? Math.Max(0, weight ?? 0) : 1m;
            totalWeight += w;
            total += w * Math.Min(1m, score!.Value);
        }

        return totalWeight == 0 ? null : Math.Round(total / totalWeight, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>The node's own score (a KPI with approved results), else the weighted average of its children (cascading / objectives).</summary>
    public static decimal? Score(ScoreNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.OwnScore is { } own)
        {
            return Math.Min(1m, own);
        }

        return node.Children.Count == 0 ? null : WeightedAverage(node.Children.Select(c => (c.Weight, Score(c))));
    }
}

/// <summary>A submitted result as the data-quality score sees it.</summary>
public sealed record SubmissionFacts(bool Late, bool EverReturned, bool HasEvidence, bool NotApplicable);

public sealed record DataQuality(int Submitted, int Due, decimal? OnTimeRate, decimal? RejectionRate, decimal? EvidenceCompleteness, decimal? Score);

/// <summary>Data-quality score per department (SPEC §2.7, design note §5.7).</summary>
public static class DataQualityCalculator
{
    public static DataQuality Calculate(IReadOnlyList<SubmissionFacts> submitted, int due)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        if (submitted.Count == 0)
        {
            return new DataQuality(0, due, null, null, null, null);
        }

        decimal n = submitted.Count;
        var onTime = R(submitted.Count(s => !s.Late) / n);
        var rejection = R(submitted.Count(s => s.EverReturned) / n);
        var values = submitted.Where(s => !s.NotApplicable).ToList();
        decimal? completeness = values.Count == 0 ? null : R(values.Count(s => s.HasEvidence) / (decimal)values.Count);
        var parts = new List<decimal> { onTime, 1 - rejection };
        if (completeness is { } c)
        {
            parts.Add(c);
        }

        return new DataQuality(submitted.Count, due, onTime, rejection, completeness, R(parts.Average()));
    }

    private static decimal R(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Anomaly flags on a result value (SPEC §2.9 — deterministic, advisory).</summary>
public static class AnomalyRules
{
    public const string Jump = "jump";
    public const string Identical = "identical";
    public const string Negative = "negative";
    public const string AbovePercentage = "abovePercentage";
    public const string FarBeyondTarget = "farBeyondTarget";

    public static IReadOnlyList<string> Flags(
        decimal? value, IReadOnlyList<decimal> previousApproved, string unitCode, bool allowsNegative, decimal? target, decimal jumpRatio = 0.5m)
    {
        ArgumentNullException.ThrowIfNull(previousApproved);
        var flags = new List<string>();
        if (value is not { } v)
        {
            return flags;
        }

        if (previousApproved.Count > 0)
        {
            var previous = previousApproved[^1];
            if (v == previous)
            {
                flags.Add(Identical);
            }
            else if (previous != 0 && Math.Abs(v - previous) / Math.Abs(previous) > jumpRatio)
            {
                flags.Add(Jump);
            }
        }

        if (v < 0 && !allowsNegative)
        {
            flags.Add(Negative);
        }

        if (unitCode == Units.Percentage && v > 100)
        {
            flags.Add(AbovePercentage);
        }

        if (target is { } t && t > 0 && v > t * 3)
        {
            flags.Add(FarBeyondTarget);
        }

        return flags;
    }
}

/// <summary>One check of the SMART quality review of a KPI card (SPEC §2.9 KPI card builder).</summary>
public sealed record SmartCheck(string Code, bool Passed);

/// <summary>What the SMART check reads from a card (or a draft).</summary>
public sealed record SmartInput(
    string? NameAr, string? NameEn, string? Equation, string? UnitCode, string? MeasuringPeriod, string? Direction, decimal? Target, decimal? RangeMin, decimal? RangeMax,
    Guid? ResponsibleUnitId, Guid? ResponsibleEmployeeId, string? Formula);

public static class SmartRules
{
    private static readonly string[] PercentWords = ["%", "percent", "نسبة", "÷", "/"];

    public static IReadOnlyList<SmartCheck> Check(SmartInput c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var name = $"{c.NameAr} {c.NameEn}".Trim();
        var equation = c.Equation ?? c.Formula ?? string.Empty;
        return
        [
            new("specific", name.Length >= 8 && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2),
            new("measurable", !string.IsNullOrWhiteSpace(c.UnitCode)),
            new("equation", !string.IsNullOrWhiteSpace(equation)),
            new("unitConsistent", c.UnitCode != Units.Percentage || PercentWords.Any(w => equation.Contains(w, StringComparison.OrdinalIgnoreCase))),
            new("direction", Directions.All.Contains(c.Direction ?? string.Empty)),
            new("timeBound", MeasuringPeriods.All.Contains(c.MeasuringPeriod ?? string.Empty)),
            new("target", c.Target is not null || (c.RangeMin is not null && c.RangeMax is not null)),
            new("owner", c.ResponsibleUnitId is not null || c.ResponsibleEmployeeId is not null),
        ];
    }
}

/// <summary>A target proposal with the reasons behind it (message keys with parameters).</summary>
public sealed record TargetReason(string Key, decimal? Value = null, int? Year = null);

public sealed record TargetProposal(decimal? Suggested, decimal? Trend, IReadOnlyList<TargetReason> Reasons);

/// <summary>Next-year target suggestion (SPEC §2.9): strategy-cycle target, else trend, else last result, else baseline — direction-aware.</summary>
public static class TargetSuggester
{
    public static TargetProposal Suggest(
        int year, string direction, IReadOnlyList<(int Year, decimal Achieved)> history, decimal? baseline, decimal? benchmark, decimal? cycleTarget)
    {
        ArgumentNullException.ThrowIfNull(history);
        var reasons = new List<TargetReason>();
        var ordered = history.OrderBy(h => h.Year).ToList();
        decimal? trend = null;
        if (ordered.Count >= 2)
        {
            var n = ordered.Count;
            var meanX = (decimal)ordered.Average(h => h.Year);
            var meanY = ordered.Average(h => h.Achieved);
            var sxx = ordered.Sum(h => (h.Year - meanX) * (h.Year - meanX));
            var slope = sxx == 0 ? 0 : ordered.Sum(h => (h.Year - meanX) * (h.Achieved - meanY)) / sxx;
            trend = Math.Round(meanY + (slope * (year - meanX)), 2, MidpointRounding.AwayFromZero);
            reasons.Add(new TargetReason("kpi.ai.targets.trend", trend, n));
        }

        (int Year, decimal Achieved)? last = ordered.Count > 0 ? ordered[^1] : null;
        if (last is { } l)
        {
            reasons.Add(new TargetReason("kpi.ai.targets.last", l.Achieved, l.Year));
        }

        if (baseline is { } b)
        {
            reasons.Add(new TargetReason("kpi.ai.targets.baseline", b));
        }

        if (benchmark is { } bm)
        {
            reasons.Add(new TargetReason("kpi.ai.targets.benchmark", bm));
        }

        decimal? suggested;
        if (cycleTarget is { } cycle)
        {
            reasons.Add(new TargetReason("kpi.ai.targets.cycle", cycle, year));
            suggested = trend is { } t && Better(direction, t, cycle) ? t : cycle;
        }
        else
        {
            suggested = trend ?? last?.Achieved ?? baseline;
            if (suggested is { } s && last is { } previous && !Better(direction, s, previous.Achieved) && s != previous.Achieved)
            {
                suggested = previous.Achieved; // never propose a target weaker than what was already achieved
                reasons.Add(new TargetReason("kpi.ai.targets.notBelowLast", previous.Achieved, previous.Year));
            }
        }

        return new TargetProposal(suggested, trend, reasons);
    }

    private static bool Better(string direction, decimal candidate, decimal reference) => direction switch
    {
        Directions.Increase => candidate > reference,
        Directions.Decrease => candidate < reference,
        _ => false,
    };
}

/// <summary>Composite KPIs (SPEC §2.7): the card formula over its data elements, in the shared expression sandbox.</summary>
public static class CompositeFormula
{
    public static IReadOnlyList<ExpressionVariable> Variables(IEnumerable<DataElement> elements) =>
        elements.Select(e => new ExpressionVariable(e.Code, ExpressionValueType.Number, e.NameAr, e.NameEn)).ToList();

    public static ExpressionCompilation Compile(string? formula, IReadOnlyList<DataElement> elements) =>
        ExpressionEngine.Compile(formula, Variables(elements), ExpressionValueType.Number);

    /// <summary>The value, or an error code (<c>kpi.errors.formula</c>: missing element, division by zero, not a number).</summary>
    public static (decimal? Value, string? Error) Evaluate(string formula, IReadOnlyList<DataElement> elements, IReadOnlyDictionary<string, decimal?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var compiled = Compile(formula, elements);
        if (!compiled.Success)
        {
            return (null, "kpi.errors.formulaInvalid");
        }

        if (elements.Any(e => !values.TryGetValue(e.Code, out var v) || v is null))
        {
            return (null, "kpi.errors.elementsRequired");
        }

        var evaluation = compiled.Expression!.Evaluate(values.ToDictionary(k => k.Key, k => (object?)k.Value));
        return evaluation is { Success: true, Value: decimal d }
            ? (Math.Round(d, 4, MidpointRounding.AwayFromZero), null)
            : (null, "kpi.errors.formulaEvaluation");
    }
}

/// <summary>A data element of a composite KPI (e.g. numerator / denominator).</summary>
public sealed record DataElement(string Code, string? NameAr, string? NameEn);

/// <summary>A level of a qualitative KPI's scale.</summary>
public sealed record ScaleLevel(decimal Value, string? LabelAr, string? LabelEn);
