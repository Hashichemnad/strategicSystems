using Strategy.Modules.Teams.Domain;

namespace Strategy.UnitTests.Teams;

/// <summary>The team lifecycle table (ADR 0046): every allowed pair reaches its status, every other (status, action) pair is refused.</summary>
public class TeamLifecycleTests
{
    private static readonly (string From, string Action, string To)[] Expected =
    [
        (TeamStatuses.Draft, TeamActions.Submit, TeamStatuses.PendingApproval),
        (TeamStatuses.Draft, TeamActions.Activate, TeamStatuses.Active),
        (TeamStatuses.Draft, TeamActions.Cancel, TeamStatuses.Cancelled),
        (TeamStatuses.PendingApproval, TeamActions.FormationApproved, TeamStatuses.Active),
        (TeamStatuses.PendingApproval, TeamActions.FormationReturned, TeamStatuses.Draft),
        (TeamStatuses.PendingApproval, TeamActions.FormationRejected, TeamStatuses.Cancelled),
        (TeamStatuses.PendingApproval, TeamActions.Withdraw, TeamStatuses.Cancelled),
        (TeamStatuses.Active, TeamActions.Deactivate, TeamStatuses.Inactive),
        (TeamStatuses.Inactive, TeamActions.Reactivate, TeamStatuses.Active),
        (TeamStatuses.Active, TeamActions.OpenEvaluation, TeamStatuses.PendingEvaluation),
        (TeamStatuses.Inactive, TeamActions.OpenEvaluation, TeamStatuses.PendingEvaluation),
        (TeamStatuses.PendingEvaluation, TeamActions.EvaluationSubmitted, TeamStatuses.EvaluationWaitingApproval),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationReturned, TeamStatuses.PendingEvaluation),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationApprovedContinue, TeamStatuses.Active),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationApprovedClose, TeamStatuses.Closed),
        (TeamStatuses.PendingEvaluation, TeamActions.EvaluationCancelled, TeamStatuses.Active),
        (TeamStatuses.Active, TeamActions.Close, TeamStatuses.Closed),
        (TeamStatuses.Inactive, TeamActions.Close, TeamStatuses.Closed),
        (TeamStatuses.Active, TeamActions.Replace, TeamStatuses.Replaced),
        (TeamStatuses.Inactive, TeamActions.Replace, TeamStatuses.Replaced),
        (TeamStatuses.PendingEvaluation, TeamActions.Replace, TeamStatuses.Replaced),
    ];

    private static readonly string[] Actions =
    [
        TeamActions.Submit, TeamActions.Activate, TeamActions.FormationApproved, TeamActions.FormationReturned, TeamActions.FormationRejected, TeamActions.Withdraw,
        TeamActions.Cancel, TeamActions.Deactivate, TeamActions.Reactivate, TeamActions.OpenEvaluation, TeamActions.EvaluationSubmitted, TeamActions.EvaluationReturned,
        TeamActions.EvaluationApprovedContinue, TeamActions.EvaluationApprovedClose, TeamActions.EvaluationCancelled, TeamActions.Close, TeamActions.Replace,
    ];

    [Fact]
    public void Code_table_equals_the_specified_table() => TeamLifecycle.Transitions.ShouldBe(Expected, ignoreOrder: true);

    [Fact]
    public void Every_status_action_pair_is_either_allowed_with_its_target_or_refused_without_change()
    {
        var checkedPairs = 0;
        foreach (var from in TeamStatuses.All)
        {
            foreach (var action in Actions)
            {
                var team = TeamIn(from);
                var result = team.Apply(action, "r", DateTimeOffset.UnixEpoch);
                var expected = Expected.Where(e => e.From == from && e.Action == action).Select(e => e.To).FirstOrDefault();
                if (expected is null)
                {
                    result.IsFailure.ShouldBeTrue($"{from} --{action}--> should be refused");
                    result.Error.Code.ShouldBe("teams.invalidTransition");
                    team.Status.ShouldBe(from);
                }
                else
                {
                    result.IsSuccess.ShouldBeTrue($"{from} --{action}--> {expected}");
                    team.Status.ShouldBe(expected);
                }

                checkedPairs++;
            }
        }

        checkedPairs.ShouldBe(TeamStatuses.All.Count * Actions.Length);
    }

    [Fact]
    public void Finished_statuses_stamp_the_closing_time_and_clear_the_pending_closure()
    {
        var team = TeamIn(TeamStatuses.Active);
        team.RequestCloseAfterEvaluation();
        team.Apply(TeamActions.Close, "done", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)).IsSuccess.ShouldBeTrue();
        team.ClosedAt.ShouldNotBeNull();
        team.CloseAfterEvaluation.ShouldBeFalse();
        team.StatusReason.ShouldBe("done");
    }

    [Theory]
    [InlineData(EvaluationTriggers.Auto, true, EvaluationTriggers.Annual)]
    [InlineData(EvaluationTriggers.Auto, false, EvaluationTriggers.EndDate)]
    [InlineData(EvaluationTriggers.LinkedRecordClosed, false, EvaluationTriggers.LinkedRecordClosed)]
    [InlineData(EvaluationTriggers.None, true, EvaluationTriggers.None)]
    public void Effective_trigger_of_a_team(string typeTrigger, bool permanent, string expected) =>
        EvaluationTriggers.Effective(typeTrigger, permanent).ShouldBe(expected);

    /// <summary>A team walked to <paramref name="status"/> through allowed transitions only.</summary>
    internal static Team TeamIn(string status)
    {
        var team = Team.Create("work-team", permanent: false, null, null);
        string[] path = status switch
        {
            TeamStatuses.Draft => [],
            TeamStatuses.PendingApproval => [TeamActions.Submit],
            TeamStatuses.Active => [TeamActions.Activate],
            TeamStatuses.Inactive => [TeamActions.Activate, TeamActions.Deactivate],
            TeamStatuses.PendingEvaluation => [TeamActions.Activate, TeamActions.OpenEvaluation],
            TeamStatuses.EvaluationWaitingApproval => [TeamActions.Activate, TeamActions.OpenEvaluation, TeamActions.EvaluationSubmitted],
            TeamStatuses.Closed => [TeamActions.Activate, TeamActions.Close],
            TeamStatuses.Replaced => [TeamActions.Activate, TeamActions.Replace],
            TeamStatuses.Cancelled => [TeamActions.Cancel],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        foreach (var step in path)
        {
            team.Apply(step).IsSuccess.ShouldBeTrue();
        }

        team.Status.ShouldBe(status);
        return team;
    }
}

public class EvaluationLifecycleTests
{
    private static readonly string[] Statuses =
        [EvaluationStatuses.Open, EvaluationStatuses.Submitted, EvaluationStatuses.Returned, EvaluationStatuses.Approved, EvaluationStatuses.Cancelled];

    private static readonly string[] Actions =
        [EvaluationActions.Save, EvaluationActions.Submit, EvaluationActions.Return, EvaluationActions.Approve, EvaluationActions.Cancel];

    [Fact]
    public void Only_the_table_transitions_are_allowed()
    {
        var allowed = new HashSet<(string, string)>
        {
            (EvaluationStatuses.Open, EvaluationActions.Save), (EvaluationStatuses.Returned, EvaluationActions.Save), (EvaluationStatuses.Open, EvaluationActions.Submit),
            (EvaluationStatuses.Returned, EvaluationActions.Submit), (EvaluationStatuses.Submitted, EvaluationActions.Return),
            (EvaluationStatuses.Submitted, EvaluationActions.Approve), (EvaluationStatuses.Open, EvaluationActions.Cancel), (EvaluationStatuses.Returned, EvaluationActions.Cancel),
        };
        foreach (var from in Statuses)
        {
            foreach (var action in Actions)
            {
                (EvaluationLifecycle.Next(from, action) is not null).ShouldBe(allowed.Contains((from, action)), $"{from} --{action}-->");
            }
        }

        EvaluationLifecycle.Next(EvaluationStatuses.Submitted, EvaluationActions.Return).ShouldBe(EvaluationStatuses.Returned);
        EvaluationLifecycle.Next(EvaluationStatuses.Submitted, EvaluationActions.Approve).ShouldBe(EvaluationStatuses.Approved);
    }

    [Fact]
    public void Only_open_and_returned_evaluations_are_editable()
    {
        EvaluationStatuses.IsEditable(EvaluationStatuses.Open).ShouldBeTrue();
        EvaluationStatuses.IsEditable(EvaluationStatuses.Returned).ShouldBeTrue();
        EvaluationStatuses.IsEditable(EvaluationStatuses.Submitted).ShouldBeFalse();
        EvaluationStatuses.IsEditable(EvaluationStatuses.Approved).ShouldBeFalse();
    }
}

/// <summary>Template validation (ADR 0041): weights, levels, data ranges and grade bands without gaps.</summary>
public class TemplateRulesTests
{
    private static readonly TemplateContent Default = DefaultTemplate.Content;

    [Fact]
    public void The_seeded_default_template_is_valid_and_totals_100()
    {
        TemplateRules.Validate(Default).ShouldBeEmpty();
        Default.Criteria.Sum(c => c.Weight).ShouldBe(100m);
        Default.MinPoints.ShouldBe(3);
        Default.MaxPoints.ShouldBe(15);
    }

    [Fact]
    public void Weights_must_total_100()
    {
        var content = Default with { Criteria = [.. Default.Criteria.Take(4)] };
        TemplateRules.Validate(content).ShouldContain(e => e.Field == "criteria" && e.Code == "teams.errors.weightsTotal");
    }

    [Fact]
    public void A_level_cannot_score_more_than_the_criterion_weight()
    {
        var first = Default.Criteria[0];
        var broken = first with { Levels = [first.Levels[0] with { Score = 30 }, .. first.Levels.Skip(1)] };
        var content = Default with { Criteria = [broken, .. Default.Criteria.Skip(1)] };
        TemplateRules.Validate(content).ShouldContain(e => e.Field == "criteria[0].levels[0].score");
    }

    [Theory]
    [InlineData(12, 15, 9, 10, 3, 8, "teams.errors.bandsGap")] // 11 missing: the Salesforce "8 vs 9"-style hole
    [InlineData(12, 15, 8, 11, 3, 8, "teams.errors.bandsOverlap")]
    [InlineData(12, 14, 9, 11, 3, 8, "teams.errors.bandsCover")]
    [InlineData(12, 15, 9, 11, 4, 8, "teams.errors.bandsCover")]
    public void Grade_bands_must_cover_the_whole_scale_without_gaps_or_overlaps(int aMin, int aMax, int bMin, int bMax, int cMin, int cMax, string error)
    {
        GradeBand[] bands = [new("a", "أ", "A", aMin, aMax), new("b", "ب", "B", bMin, bMax), new("c", "ج", "C", cMin, cMax)];
        TemplateRules.ValidateBands(bands, 3, 15).ShouldContain(e => e.Code == error);
    }

    [Fact]
    public void Contiguous_bands_are_valid_and_a_band_with_min_above_max_is_refused()
    {
        TemplateRules.ValidateBands(Default.GradeBands, 3, 15).ShouldBeEmpty();
        TemplateRules.ValidateBands([new("x", "س", "X", 9, 3)], 3, 15).ShouldContain(e => e.Code == "teams.errors.bandInvalid");
    }

    [Fact]
    public void Data_backed_levels_must_cover_their_range_without_holes()
    {
        var closure = Default.Criteria.Single(c => c.DataSource == CriterionDataSources.RecommendationsClosure);
        TemplateRules.RangesContiguous(closure.Levels, closure.DataSource!).ShouldBeTrue();
        var holed = closure.Levels.Select(l => l.Code == "medium" ? l with { Min = 85 } : l).ToList();
        TemplateRules.RangesContiguous(holed, closure.DataSource!).ShouldBeFalse();
        var delay = Default.Criteria.Single(c => c.DataSource == CriterionDataSources.MinutesDelay);
        TemplateRules.RangesContiguous(delay.Levels, delay.DataSource!).ShouldBeTrue();
        TemplateRules.RangesContiguous(delay.Levels.Select(l => l.Code == "poor" ? l with { Max = 500 } : l).ToList(), delay.DataSource!).ShouldBeFalse();
    }

    [Fact]
    public void Questions_must_be_between_one_and_ten()
    {
        TemplateRules.Validate(Default with { Questions = [] }).ShouldContain(e => e.Field == "questions");
    }
}

/// <summary>Suggested levels from data (ADR 0045).</summary>
public class EvaluationSuggestionsTests
{
    private static readonly TeamCriterion Closure = DefaultTemplate.Content.Criteria.Single(c => c.Code == "recommendations");
    private static readonly TeamCriterion Governance = DefaultTemplate.Content.Criteria.Single(c => c.Code == "governance");

    [Theory]
    [InlineData(12, 13, "medium", 92.31)] // SPEC example: 12 of 13 closed = 92 %
    [InlineData(19, 20, "high", 95)]
    [InlineData(20, 20, "high", 100)]
    [InlineData(8, 10, "medium", 80)]
    [InlineData(79, 100, "low", 79)]
    [InlineData(0, 5, "low", 0)]
    public void Recommendations_closure_percent_maps_to_its_level(int closed, int counted, string level, double percent)
    {
        var (suggested, basis) = EvaluationSuggestions.Suggest(Closure, new CycleActivity(closed, counted, []));
        suggested.ShouldBe(level);
        basis!.Value.ShouldBe((decimal)percent);
        basis.Count.ShouldBe(closed);
        basis.Total.ShouldBe(counted);
    }

    [Theory]
    [InlineData(new[] { 10.0, 20.0 }, "timely", 15)]
    [InlineData(new[] { 24.0 }, "timely", 24)]
    [InlineData(new[] { 20.0, 40.0 }, "late", 30)] // SPEC example "average minutes delay 30 h"
    [InlineData(new[] { 72.0 }, "late", 72)]
    [InlineData(new[] { 100.0, 50.0 }, "poor", 75)]
    public void Average_minutes_delay_maps_to_its_level(double[] delays, string level, double average)
    {
        var (suggested, basis) = EvaluationSuggestions.Suggest(Governance, new CycleActivity(0, 0, delays.Select(d => (decimal)d).ToList()));
        suggested.ShouldBe(level);
        basis!.Value.ShouldBe((decimal)average);
    }

    [Fact]
    public void No_data_gives_no_suggestion_and_judgement_criteria_are_never_suggested()
    {
        EvaluationSuggestions.Suggest(Closure, new CycleActivity(0, 0, [])).Level.ShouldBeNull();
        EvaluationSuggestions.Suggest(Governance, new CycleActivity(3, 3, [])).Level.ShouldBeNull();
        var judgement = DefaultTemplate.Content.Criteria.Single(c => c.Code == "quality");
        EvaluationSuggestions.Suggest(judgement, new CycleActivity(5, 5, [1])).Level.ShouldBeNull();
    }
}

/// <summary>Team score, points, grades, evidence rule and submission checks.</summary>
public class EvaluationCalculatorTests
{
    private static readonly TemplateContent T = DefaultTemplate.Content;

    private static List<CriterionResult> All(params string[] levels) =>
        T.Criteria.Select((c, i) => new CriterionResult(c.Code, levels[i], null, null, null, null, null)).ToList();

    [Fact]
    public void Team_score_is_the_sum_of_the_selected_levels()
    {
        EvaluationCalculator.TeamScore(T, All("high", "radical", "no-feedback", "timely", "shared")).ShouldBe(100m);
        EvaluationCalculator.TeamScore(T, All("medium", "steering", "feedback", "late", "few")).ShouldBe(50m);
        EvaluationCalculator.TeamScore(T, All("low", "unenforceable", "not-delivered", "poor", "disputes")).ShouldBe(0m);
        EvaluationCalculator.TeamScore(T, All("high", "radical", "no-feedback", "timely", "shared").Take(4).ToList()).ShouldBeNull();
    }

    [Theory]
    [InlineData(89.99, false)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    public void Evidence_is_required_at_or_above_the_threshold(double score, bool required) =>
        EvaluationCalculator.EvidenceRequired(T, (decimal)score).ShouldBe(required);

    [Theory]
    [InlineData(5, 5, 5, 15, "very-active")]
    [InlineData(4, 4, 4, 12, "very-active")]
    [InlineData(4, 4, 3, 11, "active")]
    [InlineData(3, 3, 3, 9, "active")]
    [InlineData(3, 3, 2, 8, "not-active")]
    [InlineData(1, 1, 1, 3, "not-active")]
    public void Points_and_grade_bands(int a, int b, int c, int points, string grade)
    {
        var answers = new Dictionary<string, int> { ["contribution"] = a, ["commitment"] = b, ["communication"] = c };
        EvaluationCalculator.Points(T, answers).ShouldBe(points);
        EvaluationCalculator.Grade(T, points)!.Code.ShouldBe(grade);
    }

    [Fact]
    public void Points_need_every_answer_in_range()
    {
        EvaluationCalculator.Points(T, new Dictionary<string, int> { ["contribution"] = 5, ["commitment"] = 5 }).ShouldBeNull();
        EvaluationCalculator.Points(T, new Dictionary<string, int> { ["contribution"] = 6, ["commitment"] = 5, ["communication"] = 5 }).ShouldBeNull();
    }

    [Fact]
    public void Submission_needs_levels_override_justification_evidence_at_threshold_and_every_member()
    {
        var member = Guid.CreateVersion7();
        var full = All("high", "radical", "no-feedback", "timely", "shared");
        EvaluationCalculator.ValidateSubmission(T, full, 0, [(member, 12)]).ShouldContain(e => e.Field == "evidence" && e.Code == "teams.errors.evidenceRequired");
        EvaluationCalculator.ValidateSubmission(T, full, 1, [(member, 12)]).ShouldBeEmpty();
        EvaluationCalculator.ValidateSubmission(T, full, 1, [(member, null)]).ShouldContain(e => e.Code == "teams.errors.memberIncomplete");
        var overridden = full.Select(r => r.Code == "recommendations" ? r with { SuggestedLevel = "medium" } : r).ToList();
        EvaluationCalculator.ValidateSubmission(T, overridden, 1, []).ShouldContain(e => e.Field == "criteria[0].overrideJustification");
        var justified = overridden.Select(r => r.Code == "recommendations" ? r with { OverrideJustification = "Two more closed after the cut-off" } : r).ToList();
        EvaluationCalculator.ValidateSubmission(T, justified, 1, []).ShouldBeEmpty();
        var low = All("medium", "steering", "feedback", "late", "few");
        EvaluationCalculator.ValidateSubmission(T, low, 0, []).ShouldBeEmpty(); // 50 < 90: no evidence needed
        EvaluationCalculator.ValidateSubmission(T, All("high", null!, "no-feedback", "timely", "shared"), 1, []).ShouldContain(e => e.Field == "criteria[1].level");
    }

    [Fact]
    public void Inconsistency_flags()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var none = new List<CriterionResult>();
        InconsistencyRules.Flags(T, none, [(a, 15, 1m), (b, 15, 1m)]).ShouldContain(f => f.Code == InconsistencyRules.AllTopScores);
        InconsistencyRules.Flags(T, none, [(a, 10, 1m), (b, 10, 1m), (c, 10, 1m)]).ShouldContain(f => f.Code == InconsistencyRules.AllEqual);
        InconsistencyRules.Flags(T, none, [(a, 14, 0.25m), (b, 9, 1m)]).ShouldContain(f => f.Code == InconsistencyRules.TopGradeLowAttendance && f.MemberEvaluationId == a);
        InconsistencyRules.Flags(T, none, [(a, 5, 0.95m), (b, 10, 0.5m)]).ShouldContain(f => f.Code == InconsistencyRules.LowGradeHighAttendance && f.MemberEvaluationId == a);
        InconsistencyRules.Flags(T, none, [(a, 12, 0.8m), (b, 9, 0.8m)]).ShouldBeEmpty();
        var far = T.Criteria.Select(cr => new CriterionResult(cr.Code, cr.Code == "recommendations" ? "high" : null, null, null,
            cr.Code == "recommendations" ? "low" : null, null, "x")).ToList();
        InconsistencyRules.Flags(T, far, []).ShouldContain(f => f.Code == InconsistencyRules.FarFromSuggestion);
    }
}

/// <summary>Reward amounts from the matrix (grade × workload × role).</summary>
public class RewardCalculatorTests
{
    private static readonly RewardMatrixContent Matrix = new(
    [
        new("very-active", Workloads.High, "president", 5000),
        new("very-active", Workloads.High, "member", 3000),
        new("active", Workloads.High, "member", 1500),
        new("very-active", Workloads.Low, "member", 1000),
    ],
    ["very-active"], 100_000, "AED");

    [Theory]
    [InlineData("very-active", Workloads.High, "president", 5000, true)]
    [InlineData("very-active", Workloads.High, "member", 3000, true)]
    [InlineData("active", Workloads.High, "member", 1500, false)]
    [InlineData("very-active", Workloads.Low, "member", 1000, true)]
    [InlineData("active", Workloads.Low, "member", 0, false)] // no cell → 0
    [InlineData("not-active", Workloads.High, "president", 0, false)]
    public void Amount_comes_from_the_cell_and_certificate_from_the_grade(string grade, string workload, string role, int amount, bool certificate)
    {
        var (value, cert) = RewardCalculator.Calculate(Matrix, grade, workload, role);
        value.ShouldBe(amount);
        cert.ShouldBe(certificate);
    }

    [Fact]
    public void No_grade_means_no_reward() => RewardCalculator.Calculate(Matrix, null, Workloads.High, "member").ShouldBe((0m, false));

    [Fact]
    public void Matrix_validation_refuses_duplicates_unknown_codes_and_negative_amounts()
    {
        string[] grades = ["very-active", "active", "not-active"];
        string[] roles = ["president", "member"];
        RewardCalculator.Validate(Matrix, grades, roles).ShouldBeEmpty();
        RewardCalculator.Validate(Matrix with { Cells = [.. Matrix.Cells, Matrix.Cells[0]] }, grades, roles).ShouldContain(e => e.Field == "cells");
        RewardCalculator.Validate(Matrix with { Cells = [new("unknown", Workloads.High, "member", 1)] }, grades, roles).ShouldContain(e => e.Field == "cells[0]");
        RewardCalculator.Validate(Matrix with { Cells = [new("active", Workloads.High, "member", -1)] }, grades, roles).ShouldContain(e => e.Field == "cells[0]");
        RewardCalculator.Validate(Matrix with { CertificateGrades = ["gold"] }, grades, roles).ShouldContain(e => e.Field == "certificateGrades");
        RewardCalculator.Validate(Matrix with { AnnualBudget = -5 }, grades, roles).ShouldContain(e => e.Field == "annualBudget");
    }
}

/// <summary>Reporting periods, annual trigger dates and minutes delay.</summary>
public class TeamCalendarTests
{
    [Theory]
    [InlineData(Cadences.Monthly, "2026-05-17", "2026-05-01", "2026-05-31")]
    [InlineData(Cadences.Quarterly, "2026-05-17", "2026-04-01", "2026-06-30")]
    [InlineData(Cadences.Biannually, "2026-05-17", "2026-01-01", "2026-06-30")]
    [InlineData(Cadences.Biannually, "2026-11-03", "2026-07-01", "2026-12-31")]
    [InlineData(Cadences.Annually, "2026-11-03", "2026-01-01", "2026-12-31")]
    public void Reporting_period_of_a_day(string cadence, string day, string start, string end)
    {
        var period = TeamCalendar.PeriodOf(cadence, DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture))!.Value;
        period.Start.ShouldBe(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        period.End.ShouldBe(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Other_cadence_has_no_periodic_report() => TeamCalendar.PeriodOf(Cadences.Other, new DateOnly(2026, 1, 1)).ShouldBeNull();

    [Fact]
    public void Annual_cycle_is_due_on_and_after_the_trigger_date_for_the_previous_year()
    {
        TeamCalendar.AnnualCycleDue(new DateOnly(2027, 1, 1), 1, 1).ShouldBe(2026);
        TeamCalendar.AnnualCycleDue(new DateOnly(2027, 3, 14), 3, 15).ShouldBeNull();
        TeamCalendar.AnnualCycleDue(new DateOnly(2027, 3, 15), 3, 15).ShouldBe(2026);
        TeamCalendar.AnnualDate(2027, 2, 29).ShouldBe(new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void Annual_period_is_the_year_within_the_team_life()
    {
        TeamCalendar.AnnualPeriod(2026, new DateOnly(2026, 4, 1), null).ShouldBe((new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31)));
        TeamCalendar.AnnualPeriod(2026, null, new DateOnly(2026, 6, 30)).ShouldBe((new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        TeamCalendar.AnnualPeriod(2026, new DateOnly(2027, 1, 1), null).ShouldBeNull();
    }

    [Fact]
    public void Minutes_delay_is_elapsed_hours_from_the_meeting_end()
    {
        var end = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        Meeting.DelayHours(end, end.AddHours(30)).ShouldBe(30m);
        Meeting.DelayHours(end, end.AddMinutes(90)).ShouldBe(1.5m);
        Meeting.DelayHours(end, end.AddHours(-1)).ShouldBe(0m);
    }

    [Fact]
    public void Member_history_is_kept_on_leave_and_replace()
    {
        var teamId = Guid.CreateVersion7();
        var values = new MemberValues(Guid.CreateVersion7(), null, null, null, null, "member", null, null, null, new DateOnly(2026, 1, 1));
        var member = TeamMember.Create(teamId, values, 0);
        member.ActiveDuring(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1)).ShouldBeTrue();
        var successor = TeamMember.Create(teamId, values with { EmployeeId = Guid.CreateVersion7() }, 0);
        member.ReplaceWith(successor.Id, new DateOnly(2026, 6, 30), "Moved to another department");
        member.Status.ShouldBe(MemberStatuses.Replaced);
        member.ReplacedByMemberId.ShouldBe(successor.Id);
        member.ToDate.ShouldBe(new DateOnly(2026, 6, 30));
        member.ActiveDuring(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)).ShouldBeTrue();
        member.ActiveDuring(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)).ShouldBeFalse();
        var external = TeamMember.Create(teamId, new MemberValues(null, "Dr. Lina", "University", "lina@uni.example", null, "member", null, null, null, null), 1);
        external.IsExternal.ShouldBeTrue();
        external.Leave(new DateOnly(2026, 5, 1), "End of assignment");
        external.Status.ShouldBe(MemberStatuses.Left);
        external.LeaveReason.ShouldBe("End of assignment");
    }
}
