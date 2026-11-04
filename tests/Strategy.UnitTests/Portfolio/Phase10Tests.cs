using Strategy.Modules.Portfolio.Ai;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;

namespace Strategy.UnitTests.Portfolio;

/// <summary>Agile progress formula (SPEC §1.13, ADR 0091): Σ epic weight × approved-done points ÷ epic points.</summary>
public class AgileProgressCalculatorTests
{
    private static readonly Guid E1 = Guid.NewGuid();
    private static readonly Guid E2 = Guid.NewGuid();

    [Fact]
    public void Progress_is_the_weighted_share_of_approved_done_points()
    {
        EpicProgressInput[] epics = [new(E1, 0.6m), new(E2, 0.4m)];
        StoryProgressInput[] stories =
        [
            new(E1, 5, Done: true, ApprovedDone: true),
            new(E1, 5, Done: true, ApprovedDone: false),
            new(E2, 3, Done: true, ApprovedDone: true),
            new(E2, 1, Done: false, ApprovedDone: false),
        ];

        // E1: 5 of 10 approved = 0.5 × 0.6 = 0.30 ; E2: 3 of 4 = 0.75 × 0.4 = 0.30 → 60 %.
        AgileProgressCalculator.Progress(epics, stories).ShouldBe(0.6m);

        // Pending (done, not yet approved): E1 10 of 10 → 0.6 ; E2 0.75 × 0.4 → 0.9.
        AgileProgressCalculator.Progress(epics, stories, approvedOnly: false).ShouldBe(0.9m);
    }

    [Fact]
    public void Cancelled_stories_and_epics_do_not_count_and_an_epic_without_points_contributes_nothing()
    {
        EpicProgressInput[] epics = [new(E1, 0.5m), new(E2, 0.5m, Cancelled: true)];
        StoryProgressInput[] stories = [new(E1, 8, true, true), new(E1, 8, false, false, Cancelled: true), new(E2, 5, true, true)];
        AgileProgressCalculator.Progress(epics, stories).ShouldBe(0.5m);
        AgileProgressCalculator.EpicFraction(Guid.NewGuid(), stories, true).ShouldBe(0m);
    }

    [Fact]
    public void Progress_is_capped_at_100_percent_and_hybrid_milestones_scale_by_their_weight()
    {
        EpicProgressInput[] epics = [new(E1, 0.7m), new(E2, 0.7m)];
        StoryProgressInput[] stories = [new(E1, 1, true, true), new(E2, 1, true, true)];
        AgileProgressCalculator.Progress(epics, stories).ShouldBe(1m);

        // A 30 % milestone delivered in sprints, its epics 50 / 50, one epic done → 15 % of the project.
        EpicProgressInput[] mine = [new(E1, 0.5m), new(E2, 0.5m)];
        StoryProgressInput[] done = [new(E1, 3, true, false), new(E2, 3, false, false)];
        AgileProgressCalculator.MilestoneAchieve(0.3m, mine, done, approvedOnly: false).ShouldBe(0.15m);
        AgileProgressCalculator.MilestoneAchieve(0.3m, mine, done, approvedOnly: true).ShouldBe(0m);
    }

    [Theory]
    [InlineData(Methodologies.Agile, null, AgileApprovalModes.Monthly, AgileApprovalModes.Monthly)]
    [InlineData(Methodologies.Agile, null, AgileApprovalModes.Sprint, AgileApprovalModes.Sprint)]
    [InlineData(Methodologies.Agile, AgileApprovalModes.Sprint, AgileApprovalModes.Monthly, AgileApprovalModes.Sprint)]
    [InlineData(Methodologies.Agile, AgileApprovalModes.Monthly, AgileApprovalModes.Sprint, AgileApprovalModes.Monthly)]
    [InlineData(Methodologies.Hybrid, AgileApprovalModes.Sprint, AgileApprovalModes.Sprint, AgileApprovalModes.Monthly)]
    [InlineData(Methodologies.Waterfall, null, AgileApprovalModes.Sprint, AgileApprovalModes.Monthly)]
    public void Approval_mode_is_the_project_override_or_the_installation_default_and_hybrid_is_always_monthly(
        string methodology, string? project, string installation, string expected) =>
        AgileApprovalModes.Effective(methodology, project, installation).ShouldBe(expected);
}

/// <summary>Sprint lifecycle (docs/design/portfolio.md §30): every allowed and every forbidden pair.</summary>
public class SprintLifecycleTests
{
    public static TheoryData<string, string, string> Table() => new()
    {
        { SprintStatuses.Planned, SprintActions.Edit, SprintStatuses.Planned },
        { SprintStatuses.Planned, SprintActions.Start, SprintStatuses.Active },
        { SprintStatuses.Planned, SprintActions.Cancel, SprintStatuses.Cancelled },
        { SprintStatuses.Active, SprintActions.Edit, SprintStatuses.Active },
        { SprintStatuses.Active, SprintActions.Close, SprintStatuses.Closed },
        { SprintStatuses.Closed, SprintActions.EditReview, SprintStatuses.Closed },
        { SprintStatuses.Closed, SprintActions.Submit, SprintStatuses.InApproval },
        { SprintStatuses.InApproval, SprintActions.Approve, SprintStatuses.Approved },
        { SprintStatuses.InApproval, SprintActions.Return, SprintStatuses.Closed },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Allowed(string from, string action, string to) => SprintLifecycle.Next(from, action).Value.ShouldBe(to);

    [Fact]
    public void Every_other_pair_is_refused()
    {
        var allowed = Table().Select(r => (r.Data.Item1, r.Data.Item2)).ToHashSet();
        foreach (var status in SprintStatuses.All)
        {
            foreach (var action in SprintActions.All.Where(a => !allowed.Contains((status, a))))
            {
                var next = SprintLifecycle.Next(status, action);
                next.IsFailure.ShouldBeTrue($"{status} × {action}");
                next.Error!.MessageKey.ShouldBe("portfolio.errors.sprintNotAllowed");
            }
        }
    }
}

/// <summary>Board rules (ADR 0090): WIP limits, approved-done lock, configuration.</summary>
public class BoardRulesTests
{
    private static readonly BoardConfig Board = new(
    [
        new("to-do", "انتظار", "To do", BoardCategories.ToDo),
        new("doing", "تنفيذ", "Doing", BoardCategories.Doing, WipLimit: 2),
        new("done", "منجز", "Done", BoardCategories.Done),
    ]);

    [Fact]
    public void A_full_column_refuses_a_new_story_but_a_move_inside_it_is_fine()
    {
        BoardRules.CheckMove(Board, "to-do", "doing", storiesInTarget: 1, approvedDone: false).ShouldBeNull();
        BoardRules.CheckMove(Board, "to-do", "doing", storiesInTarget: 2, approvedDone: false)!.MessageKey.ShouldBe("portfolio.errors.wipLimit");
        BoardRules.CheckMove(Board, "doing", "doing", storiesInTarget: 2, approvedDone: false).ShouldBeNull();
        BoardRules.CheckMove(Board, "to-do", "done", storiesInTarget: 50, approvedDone: false).ShouldBeNull();
    }

    [Fact]
    public void An_approved_done_story_stays_in_a_done_column_and_unknown_columns_are_refused()
    {
        BoardRules.CheckMove(Board, "done", "doing", 0, approvedDone: true)!.MessageKey.ShouldBe("portfolio.errors.storyLocked");
        BoardRules.CheckMove(Board, "done", "done", 0, approvedDone: true).ShouldBeNull();
        BoardRules.CheckMove(Board, "done", "nowhere", 0, approvedDone: false)!.Kind.ShouldBe(Strategy.Platform.Kernel.Results.ErrorKind.Validation);
    }

    [Fact]
    public void Configuration_needs_unique_codes_a_todo_and_a_done_column_and_sane_wip_limits()
    {
        BoardConfig.Default.Problems().ShouldBeEmpty();
        Board.Problems().ShouldBeEmpty();
        new BoardConfig([new("a", null, "A", BoardCategories.Doing), new("b", null, "B", BoardCategories.Done)]).Problems()
            .ShouldContain(e => e.Code == "portfolio.errors.boardNeedsTodoAndDone");
        new BoardConfig([new("a", null, "A", BoardCategories.ToDo), new("a", null, "B", BoardCategories.Done)]).Problems().ShouldContain(e => e.Code == "validation.duplicate");
        new BoardConfig([new("a", null, "A", BoardCategories.ToDo), new("b", null, "B", BoardCategories.Done, WipLimit: 0)]).Problems()
            .ShouldContain(e => e.Field == "columns[1].wipLimit");
        BoardConfig.FromJson(BoardConfig.Default.ToJson()).Columns.Count.ShouldBe(4);
    }

    [Fact]
    public void A_story_records_start_and_done_and_leaving_done_clears_it()
    {
        var story = UserStory.Create(Guid.NewGuid(), "P-US1", new StoryValues(Guid.NewGuid(), "Story", null, null, null, null, 5, "must", null, null), "to-do", 1);
        var t0 = new DateTimeOffset(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);
        story.Move("doing", BoardCategories.Doing, 1, t0);
        story.StartedAt.ShouldBe(t0);
        story.Move("done", BoardCategories.Done, 1, t0.AddDays(2));
        story.DoneAt.ShouldBe(t0.AddDays(2));
        story.Accepted.ShouldBeTrue();
        story.Move("doing", BoardCategories.Doing, 1, t0.AddDays(3));
        story.DoneAt.ShouldBeNull();
        story.StartedAt.ShouldBe(t0);
        story.ApproveDone();
        story.ApprovedDone.ShouldBeFalse();
    }
}

/// <summary>Agile metrics (SPEC §1.13): burndown, burnup, cumulative flow, cycle time, velocity.</summary>
public class AgileMetricsTests
{
    private static readonly DateOnly Day1 = new(2027, 3, 1);

    private static StoryHistory Story(string code, int points, params (int Day, string Category)[] moves) =>
        new(Guid.NewGuid(), code, points, Day1, moves.Select(m => (Day1.AddDays(m.Day), m.Category)).ToList(),
            moves.FirstOrDefault(m => m.Category != BoardCategories.ToDo) is { Category: not null } s ? new DateTimeOffset(Day1.AddDays(s.Day), TimeOnly.MinValue, TimeSpan.Zero) : null,
            moves.LastOrDefault(m => m.Category == BoardCategories.Done) is { Category: not null } d ? new DateTimeOffset(Day1.AddDays(d.Day), TimeOnly.MinValue, TimeSpan.Zero) : null);

    [Fact]
    public void Burndown_counts_remaining_committed_points_at_the_end_of_each_day_with_an_ideal_line()
    {
        StoryHistory[] stories =
        [
            Story("A", 5, (1, BoardCategories.Doing), (2, BoardCategories.Done)),
            Story("B", 3, (3, BoardCategories.Done)),
            Story("C", 2),
        ];
        var points = AgileMetrics.Burndown(Day1, Day1.AddDays(4), stories);
        points.Select(p => p.Remaining).ShouldBe([10m, 10m, 5m, 2m, 2m]);
        points.Select(p => p.Ideal).ShouldBe([10m, 7.5m, 5m, 2.5m, 0m]);

        // The days after "until" are future days (-1).
        AgileMetrics.Burndown(Day1, Day1.AddDays(4), stories, until: Day1.AddDays(2)).Select(p => p.Remaining).ShouldBe([10m, 10m, 5m, -1m, -1m]);
    }

    [Fact]
    public void Burnup_cumulative_flow_cycle_time_and_velocity()
    {
        StoryHistory[] stories =
        [
            Story("A", 5, (1, BoardCategories.Doing), (3, BoardCategories.Done)),
            Story("B", 3, (2, BoardCategories.Doing)),
        ];
        AgileMetrics.Burnup(stories, [("S1", Day1.AddDays(2)), ("S2", Day1.AddDays(5))]).Select(p => (p.Done, p.Scope)).ShouldBe([(0, 8), (5, 8)]);
        var flow = AgileMetrics.CumulativeFlow(stories, Day1, Day1.AddDays(3));
        flow.Select(f => (f.ToDo, f.Doing, f.Done)).ShouldBe([(2, 0, 0), (1, 1, 0), (0, 2, 0), (0, 1, 1)]);
        var cycle = AgileMetrics.CycleTimes(stories);
        cycle.Single().Days.ShouldBe(2m);
        AgileMetrics.AverageCycleTime(cycle).ShouldBe(2m);
        AgileMetrics.Velocity([(2, "S2", 10, 7), (1, "S1", 8, 8)]).Select(v => v.Number).ShouldBe([1, 2]);
    }
}

/// <summary>Project health RAG (ADR 0093) at the boundaries of the default thresholds.</summary>
public class HealthCalculatorTests
{
    private static readonly HealthThresholds T = HealthThresholds.Default;

    private static HealthFacts Facts(
        decimal progress = 0.5m, decimal planned = 0.5m, int pastDue = 0, int overdueTasks = 0, decimal? budget = 1000m, decimal? actual = 500m, int scope = 0,
        bool scopeReview = false, string? band = RiskBands.Low, bool hasRisks = true, bool hasPlan = true) =>
        new(hasPlan, progress, planned, pastDue, overdueTasks, budget, actual, scope, scopeReview, band, hasRisks);

    [Theory]
    [InlineData(0.50, 0.55, 0, 0, Rag.Green)]
    [InlineData(0.50, 0.5501, 0, 0, Rag.Amber)]
    [InlineData(0.50, 0.65, 0, 0, Rag.Amber)]
    [InlineData(0.50, 0.6501, 0, 0, Rag.Red)]
    [InlineData(0.50, 0.50, 1, 0, Rag.Amber)]
    [InlineData(0.50, 0.50, 30, 0, Rag.Amber)]
    [InlineData(0.50, 0.50, 31, 0, Rag.Red)]
    [InlineData(0.50, 0.50, 0, 2, Rag.Amber)]
    public void Schedule(decimal progress, decimal planned, int pastDue, int overdueTasks, string expected) =>
        HealthCalculator.Schedule(Facts(progress, planned, pastDue, overdueTasks), T).ShouldBe(expected);

    [Theory]
    [InlineData(1000, 550, Rag.Green)]
    [InlineData(1000, 551, Rag.Amber)]
    [InlineData(1000, 650, Rag.Amber)]
    [InlineData(1000, 651, Rag.Red)]
    [InlineData(1000, 100, Rag.Green)]
    public void Cost_variance_is_actual_minus_earned_over_budget(decimal budget, decimal actual, string expected) =>
        HealthCalculator.Cost(Facts(budget: budget, actual: actual), T).ShouldBe(expected);

    [Fact]
    public void Grey_without_data_scope_and_risk_rules_and_overall_is_the_worst()
    {
        HealthCalculator.Cost(Facts(budget: null), T).ShouldBe(Rag.Grey);
        HealthCalculator.Cost(Facts(actual: null), T).ShouldBe(Rag.Grey);
        HealthCalculator.Schedule(Facts(hasPlan: false), T).ShouldBe(Rag.Grey);
        HealthCalculator.Scope(Facts(scope: 0), T).ShouldBe(Rag.Green);
        HealthCalculator.Scope(Facts(scope: 0, scopeReview: true), T).ShouldBe(Rag.Amber);
        HealthCalculator.Scope(Facts(scope: 1), T).ShouldBe(Rag.Amber);
        HealthCalculator.Scope(Facts(scope: 2), T).ShouldBe(Rag.Red);
        HealthCalculator.Risk(Facts(band: RiskBands.Critical)).ShouldBe(Rag.Red);
        HealthCalculator.Risk(Facts(band: RiskBands.High)).ShouldBe(Rag.Amber);
        HealthCalculator.Risk(Facts(band: RiskBands.Medium)).ShouldBe(Rag.Green);
        HealthCalculator.Risk(Facts(hasRisks: false)).ShouldBe(Rag.Grey);
        HealthCalculator.Worst([Rag.Green, Rag.Grey, Rag.Amber]).ShouldBe(Rag.Amber);
        HealthCalculator.Worst([Rag.Grey]).ShouldBe(Rag.Grey);
        var health = HealthCalculator.Calculate(Facts(band: RiskBands.Critical), T);
        health.Overall.Effective.ShouldBe(Rag.Red);
    }

    [Fact]
    public void An_override_replaces_the_dimension_and_feeds_the_overall()
    {
        var overrides = new Dictionary<string, HealthOverride>
        {
            [HealthDimensions.Risk] = new(Rag.Green, "Mitigation contract signed", null, DateTimeOffset.UnixEpoch),
        };
        var health = HealthCalculator.Calculate(Facts(band: RiskBands.Critical), T, overrides);
        health.Risk.Auto.ShouldBe(Rag.Red);
        health.Risk.Effective.ShouldBe(Rag.Green);
        health.Risk.Override!.Justification.ShouldBe("Mitigation contract signed");
        health.Overall.Effective.ShouldBe(Rag.Green);
    }
}

/// <summary>Earned value (ADR 0094) on the worked example of the design note.</summary>
public class EarnedValueCalculatorTests
{
    [Fact]
    public void Worked_example()
    {
        var evm = EarnedValueCalculator.Calculate(1_000_000m, 0.40m, 0.30m, 400_000m);
        evm.Pv.ShouldBe(400_000m);
        evm.Ev.ShouldBe(300_000m);
        evm.Ac.ShouldBe(400_000m);
        evm.Sv.ShouldBe(-100_000m);
        evm.Cv.ShouldBe(-100_000m);
        evm.Spi.ShouldBe(0.75m);
        evm.Cpi.ShouldBe(0.75m);
        evm.Eac.ShouldBe(1_333_333.33m);
        evm.Etc.ShouldBe(933_333.33m);
        evm.Vac.ShouldBe(-333_333.33m);
        evm.Tcpi.ShouldBe(1.1667m);
    }

    [Fact]
    public void Zero_divisors_give_no_index()
    {
        var evm = EarnedValueCalculator.Calculate(500_000m, 0m, 0m, 0m);
        evm.Spi.ShouldBeNull();
        evm.Cpi.ShouldBeNull();
        evm.Eac.ShouldBeNull();
        evm.Etc.ShouldBeNull();
        evm.Vac.ShouldBeNull();
        evm.Tcpi.ShouldBe(1m);
    }
}

/// <summary>Baseline vs current vs actual (ADR 0094).</summary>
public class BaselineVarianceTests
{
    [Fact]
    public void Changed_added_and_removed_items_with_day_deltas()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        BaselineItem[] baseline =
        [
            new(a, "milestone", "P-1", "أ", "A", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), 0.5m, 1000m),
            new(b, "milestone", "P-2", "ب", "B", new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28), 0.5m, 2000m),
        ];
        CurrentPlanItem[] current =
        [
            new(new(a, "milestone", "P-1", "أ", "A", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 10), 0.4m, 1000m), new DateOnly(2027, 2, 12)),
            new(new(c, "milestone", "P-3", "ج", "C", new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31), 0.6m, null), null),
        ];
        var rows = BaselineVariance.Compare(baseline, current);
        var first = rows.Single(r => r.Id == a);
        first.Change.ShouldBe(BaselineVariance.Changed);
        first.EndVarianceDays.ShouldBe(10);
        first.ActualEndVarianceDays.ShouldBe(12);
        first.StartVarianceDays.ShouldBe(0);
        first.BaselineWeight.ShouldBe(0.5m);
        first.CurrentWeight.ShouldBe(0.4m);
        rows.Single(r => r.Id == b).Change.ShouldBe(BaselineVariance.Removed);
        rows.Single(r => r.Id == c).Change.ShouldBe(BaselineVariance.Added);
        BaselineVariance.PlannedToDate(baseline, new DateOnly(2027, 2, 1)).ShouldBe(0.5m + Math.Round(0.5m / 28m, 4));
    }
}

/// <summary>Planning gate for agile / hybrid (ADR 0090), configured checklists and settings validation.</summary>
public class Phase10GateAndSettingsTests
{
    private static GateFacts Facts(string methodology, AgileGateFacts agile, int releases = 1, int leaves = 0, decimal weight = 0) =>
        new(true, true, 1, 1, 1, 3, 1, 1m, 1, leaves, weight, 0, 0, methodology, releases, agile);

    [Fact]
    public void Agile_planning_needs_releases_epics_at_100_percent_sprint_length_and_definition_of_done_but_no_milestones()
    {
        var ok = ProjectGates.Planning(Facts(Methodologies.Agile, new AgileGateFacts(3, 1m, true, true, 0)));
        ok.All(i => i.Met).ShouldBeTrue();
        ok.ShouldNotContain(i => i.Code == "weights");
        var bad = ProjectGates.Planning(Facts(Methodologies.Agile, new AgileGateFacts(2, 0.9m, false, false, 0), releases: 0));
        bad.Where(i => !i.Met).Select(i => i.Code).ShouldBe(["releases", "epics", "sprint-length", "definition-of-done"], ignoreOrder: true);
    }

    [Fact]
    public void Hybrid_planning_needs_milestones_at_100_percent_and_each_sprint_milestone_epics_at_100_percent()
    {
        var items = ProjectGates.Planning(Facts(Methodologies.Hybrid, new AgileGateFacts(0, 0, true, true, 1), leaves: 2, weight: 1m));
        items.Single(i => i.Code == "weights").Met.ShouldBeTrue();
        items.Single(i => i.Code == "hybrid-epics").Met.ShouldBeFalse();
    }

    [Fact]
    public void Required_checklist_items_must_be_ticked_and_optional_ones_do_not_block()
    {
        IReadOnlyList<ChecklistItemDefinition> definitions = [new("schedule-reviewed", "أ", "A"), new("resources-confirmed", "ب", "B", Required: false)];
        GateChecklists.Default.Planning.ShouldAllBe(d => !d.Required);
        var ticks = new Dictionary<string, ChecklistTick> { ["schedule-reviewed"] = new(true, null, DateTimeOffset.UnixEpoch, null) };
        ProjectGates.Checklist(definitions, null).ShouldAllBe(i => !i.Met);
        ProjectGates.Checklist(definitions, ticks).ShouldAllBe(i => i.Met);
        ProjectGates.Checklist(definitions, ticks).Count.ShouldBe(1);
    }

    [Fact]
    public void Settings_validation_covers_the_phase_10_values()
    {
        PortfolioSettings.Validate(new PortfolioSettings()).ShouldBeEmpty();
        new PortfolioFeatures().EarnedValue.ShouldBeFalse();
        new PortfolioFeatures().ToMap().Count(f => f.Value).ShouldBe(FeatureCodes.AllCodes.Count - 1);
        var bad = new PortfolioSettings
        {
            AgileApprovalMode = "weekly",
            DefaultSprintLengthDays = 40,
            MethodologySuggestions = new Dictionary<string, string> { ["digital"] = "scrum" },
            Health = new HealthThresholds(ScheduleAmberGap: 0.2m, ScheduleRedGap: 0.1m),
            GateChecklists = GateChecklists.Default with { Charter = [new("x y", null, null)] },
        };
        PortfolioSettings.Validate(bad).Select(e => e.Field).ShouldBe(
            ["agileApprovalMode", "defaultSprintLengthDays", "methodologySuggestions.digital", "health.scheduleRedGap", "gateChecklists.charter[0].code",
                "gateChecklists.charter[0].labelEn"], ignoreOrder: true);
        new PortfolioSettings { MethodologySuggestions = new Dictionary<string, string> { ["digital"] = Methodologies.Agile } }.SuggestedMethodology("digital")
            .ShouldBe(Methodologies.Agile);
        new PortfolioSettings().SuggestedMethodology("digital").ShouldBe(Methodologies.Waterfall);
    }

    [Fact]
    public void Plan_generator_weights_are_normalised_to_exactly_100_percent()
    {
        var weights = ProjectAssistService.Normalise([15m, 15m, 40m, 20m, 10m]);
        weights.Sum().ShouldBe(1m);
        weights.ShouldBe([0.15m, 0.15m, 0.40m, 0.20m, 0.10m]);
        ProjectAssistService.Normalise([1m, 1m, 1m]).Sum().ShouldBe(1m);
        ProjectAssistService.Normalise([0m, 0m]).ShouldBe([0.5m, 0.5m]);
    }

    [Fact]
    public void Communication_frequencies_move_the_next_due_date()
    {
        CommunicationFrequencies.Next(CommunicationFrequencies.Weekly, new DateOnly(2027, 1, 1)).ShouldBe(new DateOnly(2027, 1, 8));
        CommunicationFrequencies.Next(CommunicationFrequencies.Quarterly, new DateOnly(2027, 1, 31)).ShouldBe(new DateOnly(2027, 4, 30));
        CommunicationFrequencies.Next(CommunicationFrequencies.Once, new DateOnly(2027, 1, 1)).ShouldBeNull();
        CommunicationFrequencies.FromText("Every week").ShouldBe(CommunicationFrequencies.Weekly);
        CommunicationFrequencies.FromText("شهري").ShouldBe(CommunicationFrequencies.Monthly);
    }
}
