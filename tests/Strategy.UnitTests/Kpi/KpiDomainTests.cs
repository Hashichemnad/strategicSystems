using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;

namespace Strategy.UnitTests.Kpi;

/// <summary>SPEC §2.4 K5 — the minimum performance table, exactly (actual and capped), plus the "cannot be calculated" cases.</summary>
public class PerformanceCalculatorTests
{
    public static TheoryData<int, string, string?, decimal?, decimal?, decimal?, decimal, decimal?, decimal?> SpecTable => new()
    {
        { 1, TargetTypes.SingleValue, Directions.Increase, 0m, null, null, 0m, 1.00m, 1.00m },
        { 1, TargetTypes.SingleValue, Directions.Decrease, 0m, null, null, 0m, 1.00m, 1.00m },
        { 1, TargetTypes.SingleValue, Directions.Matching, 0m, null, null, 0m, 1.00m, 1.00m },
        { 2, TargetTypes.SingleValue, Directions.Increase, 0m, null, null, 5m, null, null },
        { 2, TargetTypes.SingleValue, Directions.Decrease, 0m, null, null, 5m, null, null },
        { 2, TargetTypes.SingleValue, Directions.Matching, 0m, null, null, 5m, null, null },
        { 3, TargetTypes.SingleValue, Directions.Increase, 80m, null, null, 100m, 1.25m, 1.00m },
        { 4, TargetTypes.SingleValue, Directions.Increase, 80m, null, null, 0m, 0m, 0m },
        { 5, TargetTypes.SingleValue, Directions.Decrease, 10m, null, null, 5m, 2.00m, 1.00m },
        { 6, TargetTypes.SingleValue, Directions.Decrease, 10m, null, null, 0m, 1.00m, 1.00m },
        { 7, TargetTypes.SingleValue, Directions.Decrease, 10m, null, null, 20m, 0.50m, 0.50m },
        { 8, TargetTypes.SingleValue, Directions.Matching, 50m, null, null, 40m, 0.80m, 0.80m },
        { 9, TargetTypes.SingleValue, Directions.Matching, 50m, null, null, 62.5m, 0.80m, 0.80m },
        { 10, TargetTypes.Range, null, null, 85m, 95m, 92m, 1.00m, 1.00m },
        { 11, TargetTypes.Range, null, null, 85m, 95m, 68m, 0.80m, 0.80m },
        { 12, TargetTypes.Range, null, null, 85m, 95m, 100m, 0.95m, 0.95m },
        { 13, TargetTypes.Range, null, null, 0m, 5m, 3m, 1.00m, 1.00m },
        { 14, TargetTypes.Range, null, null, 0m, 5m, 10m, 0.50m, 0.50m },
    };

    [Theory]
    [MemberData(nameof(SpecTable))]
    public void Spec_K5_table_row(int row, string type, string? direction, decimal? target, decimal? min, decimal? max, decimal achieve, decimal? actual, decimal? capped)
    {
        var performance = PerformanceCalculator.Calculate(type, direction, target, min, max, achieve);
        performance.Actual.ShouldBe(actual, $"row {row} actual");
        performance.Capped.ShouldBe(capped, $"row {row} capped");
    }

    [Fact]
    public void Missing_value_or_target_cannot_be_calculated()
    {
        PerformanceCalculator.Calculate(TargetTypes.SingleValue, Directions.Increase, 80m, null, null, null).ShouldBe(Performance.None);
        PerformanceCalculator.Calculate(TargetTypes.SingleValue, Directions.Increase, null, null, null, 5m).ShouldBe(Performance.None);
        PerformanceCalculator.Calculate(TargetTypes.Range, null, null, 5m, null, 5m).ShouldBe(Performance.None);
        PerformanceCalculator.Calculate(TargetTypes.Range, null, null, 9m, 5m, 5m).ShouldBe(Performance.None);
        PerformanceCalculator.Calculate(TargetTypes.Range, null, null, 0m, 5m, -1m).ShouldBe(Performance.None);
        PerformanceCalculator.Calculate(TargetTypes.SingleValue, "unknown", 10m, null, null, 5m).ShouldBe(Performance.None);
    }

    [Fact]
    public void Capped_never_exceeds_one_and_ratios_are_rounded_to_four_decimals()
    {
        var p = PerformanceCalculator.Calculate(TargetTypes.SingleValue, Directions.Increase, 3m, null, null, 10m);
        p.Actual.ShouldBe(3.3333m);
        p.Capped.ShouldBe(1m);
    }
}

public class RagBandsTests
{
    [Theory]
    [InlineData(1.0, "green")]
    [InlineData(0.95, "green")]
    [InlineData(0.9499, "amber")]
    [InlineData(0.80, "amber")]
    [InlineData(0.7999, "red")]
    [InlineData(0, "red")]
    public void Default_bands_on_capped_performance(double capped, string band) => RagBands.Band((decimal)capped).ShouldBe(band);

    [Fact]
    public void Null_is_not_reported_and_overrides_apply()
    {
        RagBands.Band(null).ShouldBe(RagCodes.None);
        RagBands.Band(0.85m, green: 0.85m, amber: 0.7m).ShouldBe(RagCodes.Green);
    }
}

public class AnnualAggregatorTests
{
    private static PeriodValue V(decimal? target, decimal? achieve, bool approved = true, bool na = false) => new(target, null, null, achieve, na, approved);

    [Fact]
    public void Cumulative_adds_values_and_the_targets_of_the_same_periods()
    {
        var periods = new[] { V(25, 30), V(25, 20), V(25, null, approved: false), V(25, null, approved: false) };
        var aggregate = AnnualAggregator.Aggregate(AggregationRules.Sum, TargetTypes.SingleValue, periods, 100, null, null);
        aggregate.Achieved.ShouldBe(50m);
        aggregate.Target.ShouldBe(50m);
        aggregate.Counted.ShouldBe(2);
        AnnualAggregator.Performance(AggregationRules.Sum, TargetTypes.SingleValue, Directions.Increase, periods, 100, null, null).Actual.ShouldBe(1m);
    }

    [Fact]
    public void Average_averages_reported_periods_and_excludes_not_applicable()
    {
        var periods = new[] { V(90, 80), V(90, null, na: true), V(90, 100), V(90, 999, approved: false) };
        var aggregate = AnnualAggregator.Aggregate(AggregationRules.Average, TargetTypes.SingleValue, periods, 90, null, null);
        aggregate.Achieved.ShouldBe(90m);
        aggregate.Target.ShouldBe(90m);
        aggregate.Counted.ShouldBe(2);
        aggregate.Last.ShouldBe(100m);
    }

    [Fact]
    public void Only_approved_results_count_and_nothing_approved_means_no_performance()
    {
        var periods = new[] { V(10, 10, approved: false) };
        AnnualAggregator.Aggregate(AggregationRules.Sum, TargetTypes.SingleValue, periods, 40, null, null).Counted.ShouldBe(0);
        AnnualAggregator.Performance(AggregationRules.Sum, TargetTypes.SingleValue, Directions.Increase, periods, 40, null, null).ShouldBe(Performance.None);
    }

    [Fact]
    public void Missing_period_targets_fall_back_to_the_annual_target_and_ranges_aggregate_bounds()
    {
        var single = AnnualAggregator.Aggregate(AggregationRules.Sum, TargetTypes.SingleValue, [V(null, 30)], 100, null, null);
        single.Target.ShouldBe(100m);
        var ranges = new[] { new PeriodValue(null, 40, 50, 45, false, true), new PeriodValue(null, 40, 50, 60, false, true) };
        var range = AnnualAggregator.Aggregate(AggregationRules.Average, TargetTypes.Range, ranges, null, 40, 50);
        (range.RangeMin, range.RangeMax, range.Achieved).ShouldBe((40m, 50m, 52.5m));
    }
}

public class PeriodScheduleTests
{
    [Theory]
    [InlineData(MeasuringPeriods.Quarterly, "q1,q2,q3,q4")]
    [InlineData(MeasuringPeriods.HalfYearly, "h1,h2")]
    [InlineData(MeasuringPeriods.Annual, "fy")]
    [InlineData(MeasuringPeriods.TwoYear, "ty")]
    public void Periods_per_measuring_period(string measuringPeriod, string codes)
    {
        string.Join(',', PeriodSchedule.For(2026, measuringPeriod).Select(p => p.Code)).ShouldBe(codes);
        PeriodSchedule.For(2026, measuringPeriod).All(p => PeriodSchedule.MeasuringPeriodOf(p.Code) == measuringPeriod).ShouldBeTrue();
    }

    [Fact]
    public void Period_dates_cover_the_year()
    {
        PeriodSchedule.Spec(2026, PeriodCodes.Q1).ShouldBe(new PeriodSpec(PeriodCodes.Q1, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 1));
        PeriodSchedule.Spec(2026, PeriodCodes.H2).End.ShouldBe(new DateOnly(2026, 12, 31));
        PeriodSchedule.Spec(2026, PeriodCodes.TwoYear).End.ShouldBe(new DateOnly(2027, 12, 31));
    }

    [Fact]
    public void Default_period_targets_split_cumulative_and_repeat_average()
    {
        var split = PeriodSchedule.DefaultTargets(AggregationRules.Sum, TargetTypes.SingleValue, 100, null, null, 3);
        split.Select(t => t.Target).ShouldBe([33.3333m, 33.3333m, 33.3334m]);
        split.Sum(t => t.Target!.Value).ShouldBe(100m);
        PeriodSchedule.DefaultTargets(AggregationRules.Average, TargetTypes.SingleValue, 90, null, null, 4).ShouldAllBe(t => t.Target == 90m);
        PeriodSchedule.DefaultTargets(AggregationRules.Average, TargetTypes.Range, null, 85, 95, 2).ShouldAllBe(t => t.RangeMin == 85m && t.RangeMax == 95m);
        PeriodSchedule.DefaultTargets(AggregationRules.Sum, TargetTypes.Range, null, 40, 80, 4).ShouldAllBe(t => t.RangeMin == 10m && t.RangeMax == 20m);
    }
}

/// <summary>Appendix A Q-K2 defaults (ADR 0060).</summary>
public class WindowRulesTests
{
    [Theory]
    [InlineData(PeriodCodes.Q1, "2026-04-01", "2026-04-15")]
    [InlineData(PeriodCodes.Q2, "2026-07-01", "2026-07-15")]
    [InlineData(PeriodCodes.Q3, "2026-10-01", "2026-10-15")]
    [InlineData(PeriodCodes.Q4, "2027-01-01", "2027-01-31")]
    [InlineData(PeriodCodes.H1, "2026-07-01", "2026-07-15")]
    [InlineData(PeriodCodes.H2, "2027-01-01", "2027-01-31")]
    [InlineData(PeriodCodes.FullYear, "2027-01-01", "2027-01-31")]
    [InlineData(PeriodCodes.TwoYear, "2028-01-01", "2028-01-31")]
    public void Q_K2_default_windows(string period, string opens, string due)
    {
        var (opensOn, dueOn) = WindowRules.Window(PeriodSchedule.Spec(2026, period), WindowRules.Defaults);
        opensOn.ShouldBe(DateOnly.Parse(opens, System.Globalization.CultureInfo.InvariantCulture));
        dueOn.ShouldBe(DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Edited_rules_are_used()
    {
        var (opens, due) = WindowRules.Window(PeriodSchedule.Spec(2026, PeriodCodes.Q1), [new WindowRule(PeriodCodes.Q1, 5, 10)]);
        (opens, due).ShouldBe((new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 15)));
    }

    [Fact]
    public void Late_is_after_the_due_moment_with_calendar_days()
    {
        var dueAt = new DateTimeOffset(2026, 4, 15, 15, 30, 0, TimeSpan.FromHours(4));
        WindowRules.Lateness(dueAt, new DateOnly(2026, 4, 15), dueAt, new DateOnly(2026, 4, 15)).ShouldBe((false, 0));
        WindowRules.Lateness(dueAt.AddMinutes(1), new DateOnly(2026, 4, 15), dueAt, new DateOnly(2026, 4, 15)).ShouldBe((true, 1));
        WindowRules.Lateness(dueAt.AddDays(3), new DateOnly(2026, 4, 18), dueAt, new DateOnly(2026, 4, 15)).ShouldBe((true, 3));
        WindowRules.Lateness(dueAt, new DateOnly(2026, 4, 15), null, null).ShouldBe((false, 0));
    }
}

/// <summary>The result lifecycle (ADR 0061): every allowed pair reaches its status, every other (status, action) pair is refused.</summary>
public class ResultLifecycleTests
{
    private static readonly (string From, string Action, string To)[] Expected =
    [
        (ResultStatuses.NotOpen, ResultActions.OpenWindow, ResultStatuses.Open),
        (ResultStatuses.Open, ResultActions.Submit, ResultStatuses.PendingApproval),
        (ResultStatuses.Returned, ResultActions.Submit, ResultStatuses.PendingApproval),
        (ResultStatuses.PendingApproval, ResultActions.Recall, ResultStatuses.Open),
        (ResultStatuses.PendingApproval, ResultActions.Approved, ResultStatuses.Approved),
        (ResultStatuses.PendingApproval, ResultActions.Returned, ResultStatuses.Returned),
        (ResultStatuses.Approved, ResultActions.Reopen, ResultStatuses.Open),
        (ResultStatuses.NotOpen, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Open, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Returned, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Locked, ResultActions.Reopen, ResultStatuses.Open),
    ];

    internal static KpiResult ResultIn(string status)
    {
        var plan = AnnualPlan.Create(Guid.CreateVersion7(), 2026, MeasuringPeriods.Quarterly, "number", Directions.Increase, false, null, null);
        plan.SetTarget(TargetTypes.SingleValue, 100, null, null);
        var result = KpiResult.Create(plan, PeriodSchedule.Spec(2026, PeriodCodes.Q1), new PeriodTarget(25, null, null), null, null);
        typeof(KpiResult).GetProperty(nameof(KpiResult.Status))!.SetValue(result, status);
        return result;
    }

    [Fact]
    public void Code_table_equals_the_specified_table() => ResultLifecycle.Transitions.ShouldBe(Expected, ignoreOrder: true);

    [Fact]
    public void Every_status_action_pair_is_either_allowed_with_its_target_or_refused_without_change()
    {
        var checkedPairs = 0;
        foreach (var from in ResultStatuses.All)
        {
            foreach (var action in ResultActions.All)
            {
                var result = ResultIn(from);
                var applied = result.Apply(action, DateTimeOffset.UnixEpoch, null, "r");
                var expected = Expected.Where(e => e.From == from && e.Action == action).Select(e => e.To).FirstOrDefault();
                if (expected is null)
                {
                    applied.IsFailure.ShouldBeTrue($"{from} --{action}--> should be refused");
                    applied.Error.Code.ShouldBe("kpi.invalidTransition");
                    result.Status.ShouldBe(from);
                }
                else
                {
                    applied.IsSuccess.ShouldBeTrue($"{from} --{action}--> {expected}");
                    result.Status.ShouldBe(expected);
                }

                checkedPairs++;
            }
        }

        checkedPairs.ShouldBe(ResultStatuses.All.Count * ResultActions.All.Count);
    }

    [Fact]
    public void Counters_and_stamps_follow_the_actions()
    {
        var result = ResultIn(ResultStatuses.Open);
        var at = new DateTimeOffset(2026, 4, 10, 8, 0, 0, TimeSpan.Zero);
        var user = Guid.CreateVersion7();
        result.Apply(ResultActions.Submit, at, user).IsSuccess.ShouldBeTrue();
        (result.SubmittedAt, result.SubmittedBy).ShouldBe((at, user));
        result.Apply(ResultActions.Returned, at).IsSuccess.ShouldBeTrue();
        result.ReturnCount.ShouldBe(1);
        result.Apply(ResultActions.Submit, at, user).IsSuccess.ShouldBeTrue();
        result.Apply(ResultActions.Approved, at, user).IsSuccess.ShouldBeTrue();
        result.ApprovedAt.ShouldBe(at);
        result.Apply(ResultActions.Reopen, at, user, "wrong evidence").IsSuccess.ShouldBeTrue();
        (result.ReopenedCount, result.ApprovedAt, result.StatusReason).ShouldBe((1, (DateTimeOffset?)null, "wrong evidence"));
        ResultStatuses.IsOpenForUpdate(result.Status).ShouldBeTrue();
    }

    [Fact]
    public void Not_applicable_entry_clears_the_value_and_is_excluded_from_performance()
    {
        var result = ResultIn(ResultStatuses.Open);
        result.SaveEntry(new ResultEntry(30, null, null, null, null, null, true, "No activity this quarter"), 30,
            PerformanceCalculator.Calculate(TargetTypes.SingleValue, Directions.Increase, 25, null, null, 30));
        (result.Achieve, result.ActualPerformance, result.CappedPerformance, result.Achieved).ShouldBe(((decimal?)null, (decimal?)null, (decimal?)null, (bool?)null));
        result.NaReason.ShouldBe("No activity this quarter");
    }
}

/// <summary>Weighted scorecards on capped performance (ADR 0062).</summary>
public class ScorecardCalculatorTests
{
    [Fact]
    public void Weighted_average_uses_capped_values_and_renormalises_missing_scores()
    {
        ScorecardCalculator.WeightedAverage([(3m, 1.25m), (1m, 0.6m)]).ShouldBe(0.9m); // 1.25 counts as 1.0
        ScorecardCalculator.WeightedAverage([(3m, 0.8m), (1m, null)]).ShouldBe(0.8m);
        ScorecardCalculator.WeightedAverage([(null, 0.8m), (null, 0.6m)]).ShouldBe(0.7m);
        ScorecardCalculator.WeightedAverage([(null, null)]).ShouldBeNull();
    }

    [Fact]
    public void One_over_achiever_cannot_hide_an_under_performer()
    {
        // Actual 300 % and 40 %: averaging actual would show 170 %; capped gives 70 %.
        ScorecardCalculator.WeightedAverage([(null, 3m), (null, 0.4m)]).ShouldBe(0.7m);
    }

    [Fact]
    public void Tree_rolls_up_objectives_perspectives_and_cascaded_kpis()
    {
        var parentWithoutResults = new ScoreNode(1m, null, [ScoreNode.Leaf(1m, 1m), ScoreNode.Leaf(1m, 0.5m)]); // cascaded 0.75
        var objectiveA = new ScoreNode(2m, null, [parentWithoutResults, ScoreNode.Leaf(1m, 0.9m)]); // (0.75 + 0.9) / 2 = 0.825
        var objectiveB = new ScoreNode(1m, null, [ScoreNode.Leaf(null, 0.6m)]);
        var perspective = new ScoreNode(null, null, [objectiveA, objectiveB]); // (2 × 0.825 + 0.6) / 3 = 0.75
        ScorecardCalculator.Score(objectiveA).ShouldBe(0.825m);
        ScorecardCalculator.Score(perspective).ShouldBe(0.75m);
        ScorecardCalculator.Score(new ScoreNode(null, 0.4m, [ScoreNode.Leaf(null, 1m)])).ShouldBe(0.4m); // own results win over cascading
    }
}

public class DataQualityCalculatorTests
{
    [Fact]
    public void Score_is_the_average_of_on_time_non_rejection_and_evidence_completeness()
    {
        var facts = new[]
        {
            new SubmissionFacts(false, false, true, false),
            new SubmissionFacts(true, false, true, false),
            new SubmissionFacts(false, true, false, false),
            new SubmissionFacts(false, false, false, true), // N/A: no evidence expected
        };
        var quality = DataQualityCalculator.Calculate(facts, 5);
        quality.OnTimeRate.ShouldBe(0.75m);
        quality.RejectionRate.ShouldBe(0.25m);
        quality.EvidenceCompleteness.ShouldBe(0.6667m);
        quality.Score.ShouldBe(0.7222m);
        DataQualityCalculator.Calculate([], 3).Score.ShouldBeNull();
    }
}

public class AnomalyRulesTests
{
    [Fact]
    public void Flags_jumps_copies_negatives_percentages_and_extremes()
    {
        AnomalyRules.Flags(200, [100], "number", false, 100).ShouldContain(AnomalyRules.Jump);
        AnomalyRules.Flags(120, [100], "number", false, 100).ShouldBeEmpty();
        AnomalyRules.Flags(100, [90, 100], "number", false, 100).ShouldBe([AnomalyRules.Identical]);
        AnomalyRules.Flags(-1, [], "number", false, 10).ShouldContain(AnomalyRules.Negative);
        AnomalyRules.Flags(-1, [], "number", true, 10).ShouldBeEmpty();
        AnomalyRules.Flags(130, [], "percentage", false, 100).ShouldContain(AnomalyRules.AbovePercentage);
        AnomalyRules.Flags(400, [], "number", false, 100).ShouldContain(AnomalyRules.FarBeyondTarget);
        AnomalyRules.Flags(null, [1], "number", false, 100).ShouldBeEmpty();
    }
}

public class SmartRulesTests
{
    [Fact]
    public void A_complete_card_passes_and_a_vague_one_fails_the_right_checks()
    {
        var good = SmartRules.Check(new SmartInput("نسبة رضا المتعاملين", "Customer satisfaction rate", "satisfied ÷ surveyed × 100", "percentage",
            MeasuringPeriods.Quarterly, Directions.Increase, 90, null, null, Guid.CreateVersion7(), null, null));
        good.ShouldAllBe(c => c.Passed);
        var vague = SmartRules.Check(new SmartInput(null, "Quality", null, "percentage", null, null, null, null, null, null, null, null));
        vague.Where(c => !c.Passed).Select(c => c.Code).ShouldBe(["specific", "equation", "unitConsistent", "direction", "timeBound", "target", "owner"], ignoreOrder: true);
    }
}

public class TargetSuggesterTests
{
    [Fact]
    public void Trend_extrapolates_history_and_the_cycle_target_is_the_floor_for_increase()
    {
        var history = new List<(int, decimal)> { (2023, 70), (2024, 75), (2025, 80) };
        var trend = TargetSuggester.Suggest(2026, Directions.Increase, history, 60, null, null);
        trend.Trend.ShouldBe(85m);
        trend.Suggested.ShouldBe(85m);
        TargetSuggester.Suggest(2026, Directions.Increase, history, 60, null, 90).Suggested.ShouldBe(90m);
        TargetSuggester.Suggest(2026, Directions.Increase, history, 60, null, 82).Suggested.ShouldBe(85m);
        trend.Reasons.Select(r => r.Key).ShouldContain("kpi.ai.targets.trend");
    }

    [Fact]
    public void Never_weaker_than_the_last_result_and_baseline_without_history()
    {
        var falling = new List<(int, decimal)> { (2024, 90), (2025, 80) };
        TargetSuggester.Suggest(2026, Directions.Increase, falling, null, null, null).Suggested.ShouldBe(80m);
        TargetSuggester.Suggest(2026, Directions.Decrease, [(2024, 10m), (2025, 8m)], null, null, null).Suggested.ShouldBe(6m);
        TargetSuggester.Suggest(2026, Directions.Increase, [], 50, null, null).Suggested.ShouldBe(50m);
    }
}

public class CompositeFormulaTests
{
    private static readonly DataElement[] Elements = [new("served", "المخدومين", "Served"), new("total", "الإجمالي", "Total")];

    [Fact]
    public void Formula_is_applied_by_the_system()
    {
        CompositeFormula.Evaluate("served / total * 100", Elements, new Dictionary<string, decimal?> { ["served"] = 45, ["total"] = 60 }).ShouldBe((75m, (string?)null));
    }

    [Fact]
    public void Missing_elements_division_by_zero_and_bad_formulas_are_errors()
    {
        CompositeFormula.Evaluate("served / total * 100", Elements, new Dictionary<string, decimal?> { ["served"] = 45 }).Error.ShouldBe("kpi.errors.elementsRequired");
        CompositeFormula.Evaluate("served / total", Elements, new Dictionary<string, decimal?> { ["served"] = 1, ["total"] = 0 }).Error.ShouldBe("kpi.errors.formulaEvaluation");
        CompositeFormula.Evaluate("served / unknown", Elements, new Dictionary<string, decimal?> { ["served"] = 1, ["total"] = 1 }).Error.ShouldBe("kpi.errors.formulaInvalid");
        CompositeFormula.Compile("served > total", Elements).Success.ShouldBeFalse();
    }
}
