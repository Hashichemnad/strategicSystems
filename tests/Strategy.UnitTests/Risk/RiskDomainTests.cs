using Strategy.Modules.Risk.Ai;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;

namespace Strategy.UnitTests.Risk;

/// <summary>The shared scoring engine (ADR 0100, CLAUDE.md §15): multiplicative, additive and custom formula; bands with gaps; project risks.</summary>
public class RiskScoringTests
{
    private static FrameworkContent With(string method, string? formula = null, IReadOnlyList<RatingBand>? bands = null) =>
        DefaultFramework.Content with { ScoringMethod = method, Formula = formula, Bands = bands ?? DefaultFramework.Content.Bands };

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 6)]
    [InlineData(4, 4, 16)]
    [InlineData(4, 5, 20)]
    [InlineData(5, 5, 25)]
    public void Multiplicative_is_likelihood_times_impact(int l, int i, decimal expected) =>
        new RiskScorer(With(ScoringMethods.Multiplicative)).Score(l, i).ShouldBe(expected);

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(5, 4, 9)]
    [InlineData(5, 5, 10)]
    public void Additive_is_likelihood_plus_impact(int l, int i, decimal expected) =>
        new RiskScorer(With(ScoringMethods.Additive)).Score(l, i).ShouldBe(expected);

    [Theory]
    [InlineData(5, 5, 0, 25)]
    [InlineData(5, 5, 8, 5)]
    [InlineData(4, 3, 5, 6)]
    [InlineData(2, 3, 10, 0)]
    public void Custom_formula_uses_the_control_factor(int l, int i, decimal c, decimal expected) =>
        new RiskScorer(With(ScoringMethods.Custom, "(L * I) * (1 - C/10)")).Score(l, i, c).ShouldBe(expected);

    [Fact]
    public void Custom_formula_with_functions_rounds_to_two_decimals() =>
        new RiskScorer(With(ScoringMethods.Custom, "L * I / 3")).Score(2, 5).ShouldBe(3.33m);

    [Theory]
    [InlineData(1, "low")]
    [InlineData(4, "low")]
    [InlineData(4.01, "medium")]
    [InlineData(5, "medium")]
    [InlineData(9, "medium")]
    [InlineData(10, "high")]
    [InlineData(16, "high")]
    [InlineData(17, "critical")]
    [InlineData(19.5, "critical")]
    [InlineData(20, "critical")]
    [InlineData(25, "critical")]
    [InlineData(0, "low")]
    [InlineData(30, "critical")]
    public void Default_bands_and_gaps_map_to_the_higher_band(decimal score, string band) =>
        new RiskScorer(DefaultFramework.Content).Band(score).Code.ShouldBe(band);

    [Fact]
    public void Bands_follow_the_configured_ranges_for_additive_scoring()
    {
        RatingBand[] bands =
        [
            new("low", "منخفض", "Low", "#16a34a", 2, 4, ResidualStatuses.NoIssue),
            new("medium", "متوسط", "Medium", "#eab308", 5, 6, ResidualStatuses.ContinuousReview),
            new("high", "مرتفع", "High", "#f97316", 7, 8, ResidualStatuses.ActiveManagement),
            new("critical", "حرج", "Critical", "#dc2626", 9, 10, ResidualStatuses.ActiveManagement),
        ];
        var scorer = new RiskScorer(With(ScoringMethods.Additive, bands: bands) with
        {
            ReviewFrequencyMonths = new Dictionary<string, int> { ["low"] = 6, ["medium"] = 3, ["high"] = 1, ["critical"] = 1 },
        });
        scorer.Band(scorer.Score(5, 4)).Code.ShouldBe("critical");
        scorer.Band(scorer.Score(2, 3)).Code.ShouldBe("medium");
        scorer.ResidualStatusOf("low").ShouldBe(ResidualStatuses.NoIssue);
    }

    [Fact]
    public void The_matrix_has_every_cell_with_its_band()
    {
        var matrix = new RiskScorer(DefaultFramework.Content).Matrix();
        matrix.Count.ShouldBe(25);
        matrix.Single(c => c.Likelihood == 5 && c.Impact == 4).ShouldBe(new MatrixCell(5, 4, 20, "critical"));
        matrix.Single(c => c.Likelihood == 2 && c.Impact == 2).Band.ShouldBe("low");
    }

    [Theory]
    [InlineData(5, 5, null, 25, "critical", 25, "critical")]
    [InlineData(5, 5, 10, 25, "critical", 2.5, "low")]
    [InlineData(4, 4, 6, 16, "high", 8, "medium")]
    [InlineData(4, 5, 1, 20, "critical", 20, "critical")]
    public void Project_risks_use_the_control_reduction_rule(int p, int i, int? c, decimal inherent, string inherentBand, decimal residual, string residualBand)
    {
        var (score, band, res, resBand) = new RiskScorer(DefaultFramework.Content).ScoreProject(p, i, c);
        score.ShouldBe(inherent);
        band.Code.ShouldBe(inherentBand);
        res.ShouldBe(residual);
        resBand.Code.ShouldBe(residualBand);
    }

    [Fact]
    public void Project_risks_follow_a_custom_formula_with_C_when_configured()
    {
        var content = With(ScoringMethods.Custom, "(L * I) * (1 - C/10)") with { ProjectResidualRule = ProjectResidualRules.Formula };
        var (inherent, _, residual, band) = new RiskScorer(content).ScoreProject(5, 4, 5);
        inherent.ShouldBe(20m);
        residual.ShouldBe(10m);
        band.Code.ShouldBe("high");
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "effective" }, "effective")]
    [InlineData(new[] { "effective", "ineffective" }, "ineffective")]
    [InlineData(new[] { "effective", "effective", "partially-effective" }, "effective")]
    [InlineData(new[] { "effective", "partially-effective", "ineffective" }, "partially-effective")]
    public void Overall_control_effectiveness_is_the_lower_median(string[] ratings, string? expected) =>
        new RiskScorer(DefaultFramework.Content).OverallEffectiveness(ratings).ShouldBe(expected);

    [Theory]
    [InlineData("critical", 1)]
    [InlineData("high", 1)]
    [InlineData("medium", 3)]
    [InlineData("low", 6)]
    public void Review_frequency_by_rating(string band, int months) => new RiskScorer(DefaultFramework.Content).ReviewMonths(band).ShouldBe(months);
}

/// <summary>Framework validation (ADR 0100): scales, bands, formula on every cell.</summary>
public class FrameworkRulesTests
{
    [Fact]
    public void The_default_framework_is_valid() => FrameworkRules.Validate(DefaultFramework.Content).ShouldBeEmpty();

    [Theory]
    [InlineData("L * X", "formula")]
    [InlineData("L * I / (C - C)", "formula")]
    [InlineData("L - I * 2", "formula")]
    [InlineData("L * ", "formula")]
    public void An_invalid_custom_formula_is_refused(string formula, string field) =>
        FrameworkRules.Validate(DefaultFramework.Content with { ScoringMethod = ScoringMethods.Custom, Formula = formula }).ShouldContain(e => e.Field == field);

    [Fact]
    public void A_valid_custom_formula_is_accepted() =>
        FrameworkRules.Validate(DefaultFramework.Content with { ScoringMethod = ScoringMethods.Custom, Formula = "(L * I) * (1 - C/10)" }).ShouldBeEmpty();

    [Fact]
    public void Overlapping_bands_are_refused()
    {
        var bands = DefaultFramework.Content.Bands.ToList();
        bands[1] = bands[1] with { Min = 4 };
        FrameworkRules.Validate(DefaultFramework.Content with { Bands = bands }).ShouldContain(e => e.Code == "risk.errors.bandsOverlap");
    }

    [Fact]
    public void A_band_without_review_frequency_is_refused()
    {
        var content = DefaultFramework.Content with { ReviewFrequencyMonths = new Dictionary<string, int> { ["low"] = 6 } };
        FrameworkRules.Validate(content).ShouldContain(e => e.Code == "risk.errors.reviewFrequency");
    }

    [Fact]
    public void A_scale_must_start_at_one_without_gaps()
    {
        var levels = DefaultFramework.Content.Likelihood.Where(l => l.Value != 3).ToList();
        FrameworkRules.Validate(DefaultFramework.Content with { Likelihood = levels }).ShouldContain(e => e.Field == "likelihood");
    }
}

/// <summary>Ratings, appetite, escalation and Top-10 (design §5.4).</summary>
public class RiskRatingTests
{
    private static RiskRecord Risk(int l, int i, int? rl = null, int? ri = null, int? tl = null, int? ti = null, IReadOnlyList<string>? categories = null, string number = "GRD26-R001") =>
        RiskRecord.Create(number, 2026, new RiskValues("خطر", "Risk", "cause", "event", "consequence", null, new DateOnly(2026, 1, 10), null, RiskTypes.Internal,
            categories ?? ["operational"], false, false, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, l, i, null, rl, ri, tl, ti, null, null, false));

    [Fact]
    public void Inherent_residual_target_and_status_are_derived()
    {
        var scorer = new RiskScorer(DefaultFramework.Content);
        var risk = Risk(5, 4, rl: 3, ri: 3, tl: 2, ti: 2);
        var r = RiskRater.Compute(risk, scorer, Guid.Empty, ["effective"], appetite: 8);
        r.InherentScore.ShouldBe(20);
        r.InherentBand.ShouldBe("critical");
        r.ResidualScore.ShouldBe(9);
        r.ResidualBand.ShouldBe("medium");
        r.ResidualStatus.ShouldBe(ResidualStatuses.ContinuousReview);
        r.TargetScore.ShouldBe(4);
        r.TargetBand.ShouldBe("low");
        r.ControlEffectiveness.ShouldBe("effective");
        r.WithinAppetite.ShouldBe(false);
    }

    [Fact]
    public void Without_residual_ratings_the_residual_equals_the_inherent_and_appetite_is_unknown()
    {
        var r = RiskRater.Compute(Risk(2, 2), new RiskScorer(DefaultFramework.Content), Guid.Empty, [], appetite: null);
        r.ResidualScore.ShouldBe(4);
        r.ResidualBand.ShouldBe("low");
        r.WithinAppetite.ShouldBeNull();
    }

    [Fact]
    public void A_custom_formula_with_C_reduces_the_residual_by_control_effectiveness()
    {
        var scorer = new RiskScorer(DefaultFramework.Content with { ScoringMethod = ScoringMethods.Custom, Formula = "(L * I) * (1 - C/10)" });
        var r = RiskRater.Compute(Risk(5, 5), scorer, Guid.Empty, ["effective"], null);
        r.InherentScore.ShouldBe(25);
        r.ResidualScore.ShouldBe(5);
        r.ResidualBand.ShouldBe("medium");
    }

    [Fact]
    public void The_residual_status_override_wins_with_its_justification()
    {
        var risk = Risk(5, 5);
        risk.OfficeUpdate(null, ResidualStatuses.ContinuousReview, "Accepted by the committee", false, null);
        risk.ApplyRatings(RiskRater.Compute(risk, new RiskScorer(DefaultFramework.Content), Guid.Empty, [], null));
        risk.ResidualStatus.ShouldBe(ResidualStatuses.ContinuousReview);
        risk.ResidualStatusJustification.ShouldBe("Accepted by the committee");
    }

    [Fact]
    public void Appetite_is_the_smallest_among_the_categories_and_their_ancestors()
    {
        var parent = Category.Create("financial");
        parent.Update("مالية", "Financial", null, 12, 10, true);
        var child = Category.Create("budget");
        child.Update("الميزانية", "Budget", parent.Id, null, 20, true);
        var other = Category.Create("reputation");
        other.Update("السمعة", "Reputation", null, 6, 30, true);
        RiskRater.Appetite([parent, child, other], ["budget"]).ShouldBe(12);
        RiskRater.Appetite([parent, child, other], ["budget", "reputation"]).ShouldBe(6);
        RiskRater.Appetite([parent, child, other], ["operational"]).ShouldBeNull();
    }

    [Fact]
    public void Critical_or_outside_appetite_risks_are_escalated_and_cleared_when_they_improve()
    {
        var scorer = new RiskScorer(DefaultFramework.Content);
        var settings = new RiskSettings();
        var risk = Risk(5, 5);
        risk.ApplyRatings(RiskRater.Compute(risk, scorer, Guid.Empty, [], null));
        RiskRater.ApplyEscalation(risk, settings, scorer, DateTimeOffset.UtcNow);
        risk.Escalated.ShouldBeTrue();
        risk.EscalationReason.ShouldBe(EscalationReasons.Critical);
        risk.Edit(risk.Values() with { InherentLikelihood = 2, InherentImpact = 2 });
        risk.ApplyRatings(RiskRater.Compute(risk, scorer, Guid.Empty, [], 2));
        RiskRater.ApplyEscalation(risk, settings, scorer, DateTimeOffset.UtcNow);
        risk.EscalationReason.ShouldBe(EscalationReasons.OutsideAppetite);
        risk.ApplyRatings(RiskRater.Compute(risk, scorer, Guid.Empty, [], 10));
        RiskRater.ApplyEscalation(risk, settings, scorer, DateTimeOffset.UtcNow);
        risk.Escalated.ShouldBeFalse();
    }

    [Fact]
    public void Top10_puts_flagged_risks_first_by_rank_then_residual_score()
    {
        var scorer = new RiskScorer(DefaultFramework.Content);
        RiskRecord Rated(int l, int i, string number, bool top = false, int? rank = null)
        {
            var r = Risk(l, i, number: number);
            r.ApplyRatings(RiskRater.Compute(r, scorer, Guid.Empty, [], null));
            r.OfficeUpdate(null, null, null, top, rank);
            return r;
        }

        var ranked = Top10.Rank([Rated(5, 5, "A"), Rated(2, 2, "B", true, 2), Rated(3, 3, "C", true, 1), Rated(4, 4, "D"), Rated(4, 4, "E")], 4);
        ranked.Select(r => r.Number).ShouldBe(["C", "B", "A", "D"]);
    }
}

/// <summary>The risk state machine (ADR 0102): every allowed and every forbidden transition.</summary>
public class RiskLifecycleTests
{
    public static TheoryData<string, string, string?> Table()
    {
        var allowed = new Dictionary<(string, string), string>
        {
            [(RiskStatuses.Draft, RiskActions.Submit)] = RiskStatuses.UnderReview,
            [(RiskStatuses.Draft, RiskActions.Discard)] = RiskStatuses.Draft,
            [(RiskStatuses.UnderReview, RiskActions.Approve)] = RiskStatuses.Active,
            [(RiskStatuses.UnderReview, RiskActions.Return)] = RiskStatuses.Draft,
            [(RiskStatuses.UnderReview, RiskActions.Recall)] = RiskStatuses.Draft,
            [(RiskStatuses.UnderReview, RiskActions.Reject)] = RiskStatuses.Rejected,
            [(RiskStatuses.Active, RiskActions.RequestClosure)] = RiskStatuses.Active,
            [(RiskStatuses.Active, RiskActions.ClosureApproved)] = RiskStatuses.Closed,
            [(RiskStatuses.Active, RiskActions.ClosureRefused)] = RiskStatuses.Active,
            [(RiskStatuses.Closed, RiskActions.Reopen)] = RiskStatuses.Active,
            [(RiskStatuses.Rejected, RiskActions.Reopen)] = RiskStatuses.Draft,
        };
        var data = new TheoryData<string, string, string?>();
        foreach (var status in RiskStatuses.All)
        {
            foreach (var action in RiskActions.All)
            {
                data.Add(status, action, allowed.GetValueOrDefault((status, action)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Transition(string from, string action, string? to)
    {
        RiskLifecycle.Next(from, action).ShouldBe(to);
        RiskLifecycle.Allows(from, action).ShouldBe(to is not null);
    }

    [Fact]
    public void Return_keeps_the_comment_and_submit_clears_it()
    {
        var risk = RiskRecord.Create("X26-R001", 2026, new RiskValues(null, "Risk", "c", "e", "q", null, new DateOnly(2026, 1, 1), null, RiskTypes.Internal, ["operational"],
            false, false, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, 3, 3, null, null, null, null, null, null, null, false));
        risk.Apply(RiskActions.Submit, DateTimeOffset.UtcNow, null).IsSuccess.ShouldBeTrue();
        risk.Apply(RiskActions.Return, DateTimeOffset.UtcNow, null, "Clarify the cause").IsSuccess.ShouldBeTrue();
        risk.Returned.ShouldBeTrue();
        risk.ReturnComment.ShouldBe("Clarify the cause");
        risk.Apply(RiskActions.Submit, DateTimeOffset.UtcNow, null).IsSuccess.ShouldBeTrue();
        risk.Returned.ShouldBeFalse();
        risk.Apply(RiskActions.ClosureApproved, DateTimeOffset.UtcNow, null).IsFailure.ShouldBeTrue();
    }
}

/// <summary>KRI evaluation (ADR 0104), action progress, statement checks.</summary>
public class RiskChildrenTests
{
    [Theory]
    [InlineData(95, "above", 80.0, 90, "breach")]
    [InlineData(90, "above", 80.0, 90, "breach")]
    [InlineData(85, "above", 80.0, 90, "warning")]
    [InlineData(70, "above", 80.0, 90, "normal")]
    [InlineData(70, "above", null, 90, "normal")]
    [InlineData(0.4, "below", 0.7, 0.5, "breach")]
    [InlineData(0.6, "below", 0.7, 0.5, "warning")]
    [InlineData(0.9, "below", 0.7, 0.5, "normal")]
    public void Kri_levels(decimal value, string direction, double? warning, decimal breach, string level) =>
        KriEvaluator.Level(value, direction, warning is { } w ? (decimal)w : null, breach).ShouldBe(level);

    [Fact]
    public void Action_progress_completes_at_one_hundred_percent_and_is_overdue_after_the_planned_end()
    {
        var action = TreatmentAction.Create("ORA-0001", Guid.CreateVersion7(),
            new ActionValues(null, "Train staff", Guid.CreateVersion7(), [], new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null));
        action.IsOverdue(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        action.Progress(0.5m, null, null, new DateOnly(2026, 2, 1));
        action.Status.ShouldBe(ActionStatuses.InProgress);
        action.ActualStart.ShouldBe(new DateOnly(2026, 2, 1));
        action.Progress(1m, null, null, new DateOnly(2026, 3, 15));
        action.Status.ShouldBe(ActionStatuses.Completed);
        action.ActualEnd.ShouldBe(new DateOnly(2026, 3, 15));
        action.IsOverdue(new DateOnly(2026, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Vague_or_incomplete_statements_are_flagged()
    {
        RiskAiService.Flags(new StatementInput("Lack of resources", "Lack of resources", null))
            .ShouldBe(["short-cause", "short-event", "missing-consequence", "vague", "cause-equals-event"], ignoreOrder: true);
        RiskAiService.Flags(new StatementInput("Legacy payment system has no vendor support after 2026",
            "The payment platform fails during the salary run", "Salaries are paid late and staff complaints reach the media")).ShouldBeEmpty();
    }
}
