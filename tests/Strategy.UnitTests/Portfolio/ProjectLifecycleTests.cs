using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;

namespace Strategy.UnitTests.Portfolio;

/// <summary>The project / initiative state machine (docs/design/portfolio.md §16, ADR 0070): every allowed and every forbidden pair.</summary>
public class ProjectLifecycleTests
{
    private static readonly StageTemplate Project = StageTemplate.ProjectDefault;
    private static readonly StageTemplate Initiative = StageTemplate.InitiativeDefault;

    public static TheoryData<string, string, string> ProjectTable() => new()
    {
        { SubStates.Ownership, ProjectActions.TakeOwnership, SubStates.CharterDraft },
        { SubStates.CharterDraft, ProjectActions.SubmitCharter, SubStates.CharterApproval },
        { SubStates.CharterApproval, ProjectActions.CharterApproved, SubStates.Lpo },
        { SubStates.CharterApproval, ProjectActions.ApprovalReturned, SubStates.CharterDraft },
        { SubStates.Lpo, ProjectActions.LpoUploaded, SubStates.TeamFormation },
        { SubStates.TeamFormation, ProjectActions.TeamFormed, SubStates.PlanningDraft },
        { SubStates.PlanningDraft, ProjectActions.SubmitPlanning, SubStates.PlanningApproval },
        { SubStates.PlanningApproval, ProjectActions.PlanningApproved, SubStates.Execution },
        { SubStates.PlanningApproval, ProjectActions.ApprovalReturned, SubStates.PlanningDraft },
        { SubStates.Execution, ProjectActions.AchievementComplete, SubStates.ClosureDraft },
        { SubStates.ClosureDraft, ProjectActions.SubmitClosure, SubStates.ClosureApproval },
        { SubStates.ClosureApproval, ProjectActions.ClosureApproved, SubStates.ImpactDraft },
        { SubStates.ClosureApproval, ProjectActions.ApprovalReturned, SubStates.ClosureDraft },
        { SubStates.ImpactDraft, ProjectActions.SubmitImpact, SubStates.ImpactApproval },
        { SubStates.ImpactApproval, ProjectActions.ImpactApproved, SubStates.Done },
        { SubStates.ImpactApproval, ProjectActions.ApprovalReturned, SubStates.ImpactDraft },
        { SubStates.Completion, ProjectActions.ConfirmCompletion, SubStates.Done },
    };

    [Theory]
    [MemberData(nameof(ProjectTable))]
    public void Allowed_project_transitions(string from, string action, string to)
    {
        var next = ProjectLifecycle.Next(ProjectStatuses.Active, from, action, Project);
        next.IsSuccess.ShouldBeTrue();
        next.Value.ShouldBe(to);
    }

    [Fact]
    public void Every_other_pair_is_refused()
    {
        var allowed = ProjectTable().Select(r => (r.Data.Item1, r.Data.Item2)).ToHashSet();
        foreach (var state in SubStates.All)
        {
            foreach (var action in ProjectActions.All)
            {
                var next = ProjectLifecycle.Next(ProjectStatuses.Active, state, action, Project);
                next.IsSuccess.ShouldBe(allowed.Contains((state, action)), $"{state} × {action}");
                if (next.IsFailure)
                {
                    next.Error.MessageKey.ShouldBe("portfolio.errors.notAllowedInStage");
                }
            }
        }
    }

    [Theory]
    [InlineData(ProjectStatuses.OnHold)]
    [InlineData(ProjectStatuses.Cancelled)]
    [InlineData(ProjectStatuses.Completed)]
    public void Lifecycle_actions_need_an_active_project(string status)
    {
        foreach (var action in ProjectActions.All)
        {
            foreach (var state in SubStates.All)
            {
                ProjectLifecycle.Next(status, state, action, Project).IsFailure.ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void Initiative_follows_its_lighter_template()
    {
        // Q-P2: Take Ownership → Planning → Execution → (100 %) PM Office confirms → Completed.
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.Ownership, ProjectActions.TakeOwnership, Initiative).Value.ShouldBe(SubStates.PlanningDraft);
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.Execution, ProjectActions.AchievementComplete, Initiative).Value.ShouldBe(SubStates.Completion);
        ProjectLifecycle.StageOf(SubStates.Ownership, Initiative).ShouldBe(ProjectStages.Planning);
        ProjectLifecycle.StageOf(SubStates.Completion, Initiative).ShouldBe(ProjectStages.Execution);
        Initiative.Path().ShouldBe([ProjectStages.Planning, ProjectStages.Execution, ProjectStages.Completed]);
    }

    [Fact]
    public void Impact_switched_off_goes_from_closure_to_completed()
    {
        var noImpact = Project with { Impact = false };
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.ClosureApproval, ProjectActions.ClosureApproved, noImpact).Value.ShouldBe(SubStates.Done);
        noImpact.Path().ShouldBe([ProjectStages.Initiation, ProjectStages.Planning, ProjectStages.Execution, ProjectStages.Closure, ProjectStages.Completed]);
        Project.Path().ShouldBe(ProjectStages.ProjectPath);
    }

    [Fact]
    public void Charter_without_lpo_or_team_goes_straight_to_planning()
    {
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.CharterApproval, ProjectActions.CharterApproved, Project with { Lpo = false })
            .Value.ShouldBe(SubStates.TeamFormation);
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.CharterApproval, ProjectActions.CharterApproved, Project with { Lpo = false, TeamFormation = false })
            .Value.ShouldBe(SubStates.PlanningDraft);
        ProjectLifecycle.Next(ProjectStatuses.Active, SubStates.Lpo, ProjectActions.LpoUploaded, Project with { TeamFormation = false }).Value.ShouldBe(SubStates.PlanningDraft);
    }

    [Theory]
    [InlineData(ProjectStatuses.Active, ProjectStatusActions.Hold, ProjectStatuses.OnHold)]
    [InlineData(ProjectStatuses.OnHold, ProjectStatusActions.Resume, ProjectStatuses.Active)]
    [InlineData(ProjectStatuses.Active, ProjectStatusActions.Cancel, ProjectStatuses.Cancelled)]
    [InlineData(ProjectStatuses.OnHold, ProjectStatusActions.Cancel, ProjectStatuses.Cancelled)]
    [InlineData(ProjectStatuses.Active, ProjectStatusActions.Complete, ProjectStatuses.Completed)]
    public void Status_transitions(string from, string action, string to) => ProjectLifecycle.NextStatus(from, action).Value.ShouldBe(to);

    [Fact]
    public void Status_forbidden_pairs()
    {
        var allowed = new HashSet<(string, string)>
        {
            (ProjectStatuses.Active, ProjectStatusActions.Hold), (ProjectStatuses.OnHold, ProjectStatusActions.Resume), (ProjectStatuses.Active, ProjectStatusActions.Cancel),
            (ProjectStatuses.OnHold, ProjectStatusActions.Cancel), (ProjectStatuses.Active, ProjectStatusActions.Complete),
        };
        foreach (var status in new[] { ProjectStatuses.Active, ProjectStatuses.OnHold, ProjectStatuses.Cancelled, ProjectStatuses.Completed })
        {
            foreach (var action in ProjectStatusActions.All)
            {
                ProjectLifecycle.NextStatus(status, action).IsSuccess.ShouldBe(allowed.Contains((status, action)), $"{status} × {action}");
            }
        }
    }

    [Theory]
    [InlineData(SubStates.Ownership, ProjectStages.Initiation)]
    [InlineData(SubStates.CharterApproval, ProjectStages.Initiation)]
    [InlineData(SubStates.Lpo, ProjectStages.Initiation)]
    [InlineData(SubStates.TeamFormation, ProjectStages.Initiation)]
    [InlineData(SubStates.PlanningApproval, ProjectStages.Planning)]
    [InlineData(SubStates.Execution, ProjectStages.Execution)]
    [InlineData(SubStates.ClosureDraft, ProjectStages.Closure)]
    [InlineData(SubStates.ImpactApproval, ProjectStages.Impact)]
    [InlineData(SubStates.Done, ProjectStages.Completed)]
    public void Stage_of_each_sub_state(string subState, string stage) => ProjectLifecycle.StageOf(subState, Project).ShouldBe(stage);

    [Fact]
    public void Content_is_frozen_while_a_stage_approval_runs()
    {
        ProjectLifecycle.CanEditContent(ProjectStatuses.Active, SubStates.CharterDraft).ShouldBeTrue();
        ProjectLifecycle.CanEditContent(ProjectStatuses.Active, SubStates.Execution).ShouldBeTrue();
        ProjectLifecycle.CanEditContent(ProjectStatuses.Active, SubStates.CharterApproval).ShouldBeFalse();
        ProjectLifecycle.CanEditContent(ProjectStatuses.OnHold, SubStates.CharterDraft).ShouldBeFalse();
        ProjectLifecycle.CanPlanMilestones(ProjectStatuses.Active, SubStates.Execution).ShouldBeFalse("execution changes come from monthly updates");
        ProjectLifecycle.CanPlanMilestones(ProjectStatuses.Active, SubStates.PlanningDraft).ShouldBeTrue();
    }

    [Fact]
    public void Template_rules()
    {
        StageTemplate.ProjectDefault.Problems().ShouldBeEmpty();
        StageTemplate.InitiativeDefault.Problems().ShouldBeEmpty();
        new StageTemplate(false, true, false, true, true).Problems().ShouldContain("portfolio.errors.templateNeedsCharter");
        new StageTemplate(true, true, true, false, true).Problems().ShouldContain("portfolio.errors.templateImpactNeedsClosure");
        PortfolioSettings.Validate(new PortfolioSettings { InitiativeTemplate = new StageTemplate(false, false, true, false, false) })
            .ShouldContain(e => e.Field == "initiativeTemplate");
    }
}

/// <summary>Gates (docs/design/portfolio.md §17): the charter minimums and the planning weights.</summary>
public class ProjectGateTests
{
    private static GateFacts Ready() => new(true, true, 1, 1, 1, 3, 2, 1m, 1, 4, 1m, 0, 0);

    [Fact]
    public void Charter_passes_with_the_minimums() => ProjectGates.Failure(ProjectGates.Charter(Ready())).ShouldBeNull();

    [Theory]
    [InlineData("kpis")]
    [InlineData("risks")]
    [InlineData("stakeholders")]
    [InlineData("members")]
    [InlineData("high-level-plan")]
    [InlineData("pm")]
    public void Charter_blocks_each_missing_minimum(string missing)
    {
        var f = Ready();
        f = missing switch
        {
            "kpis" => f with { ActiveKpis = 0 },
            "risks" => f with { ActiveRisks = 0 },
            "stakeholders" => f with { ActiveStakeholders = 0 },
            "members" => f with { ProposedMembers = 2 },
            "high-level-plan" => f with { HighLevelWeight = 0.9m },
            _ => f with { HasPm = false },
        };
        var error = ProjectGates.Failure(ProjectGates.Charter(f));
        error.ShouldNotBeNull();
        error.Fields!.Select(e => e.Field).ShouldBe([$"gate.{missing}"]);
    }

    [Theory]
    [InlineData(0.9999, false)]
    [InlineData(1.0, true)]
    [InlineData(1.0001, false)]
    public void Planning_needs_weights_of_exactly_100(decimal total, bool passes)
    {
        var error = ProjectGates.Failure(ProjectGates.Planning(Ready() with { DetailedWeight = total }));
        (error is null).ShouldBe(passes);
        if (!passes)
        {
            error!.Fields!.Single().MessageKey.ShouldBe("portfolio.errors.weightsNot100");
            error.Fields!.Single().Params!["total"].ShouldBe(decimal.Round(total * 100, 2));
        }
    }

    [Fact]
    public void Planning_reports_every_problem_together()
    {
        var error = ProjectGates.Failure(ProjectGates.Planning(Ready() with { InScopeItems = 0, DetailedLeaves = 0, LeavesWithBadDates = 1, DeliverablesIncomplete = 2 }));
        error!.Fields!.Select(f => f.Field).ShouldBe(["gate.scope-items", "gate.weights", "gate.dates", "gate.deliverables"], ignoreOrder: true);
    }
}

/// <summary>Progress (Q-P6, ADR 0071): performance, linear planned-to-date, status with the 5-point tolerance.</summary>
public class ProgressCalculatorTests
{
    private static readonly DateOnly Jan1 = new(2027, 1, 1);

    private static MilestoneProgressInput M(decimal weight, decimal approved, int startDay, int endDay, bool cancelled = false) =>
        new(weight, approved, Jan1.AddDays(startDay), Jan1.AddDays(endDay), cancelled);

    [Fact]
    public void Performance_sums_approved_achievement_of_active_leaves()
    {
        ProgressCalculator.Performance([M(0.5m, 0.5m, 0, 9), M(0.3m, 0.1m, 10, 19), M(0.2m, 0.2m, 0, 5, cancelled: true)]).ShouldBe(0.6m);
        ProgressCalculator.Performance([M(0.6m, 0.6m, 0, 1), M(0.6m, 0.6m, 0, 1)]).ShouldBe(1m, "capped at 100 %");
    }

    [Fact]
    public void Planned_to_date_is_linear_within_each_milestone()
    {
        var leaves = new[] { M(0.4m, 0, 0, 9), M(0.6m, 0, 10, 19) };
        ProgressCalculator.PlannedToDate(leaves, Jan1.AddDays(-1)).ShouldBe(0m, "before any start");
        ProgressCalculator.PlannedToDate(leaves, Jan1).ShouldBe(0.04m, "day 1 of 10");
        ProgressCalculator.PlannedToDate(leaves, Jan1.AddDays(4)).ShouldBe(0.2m, "day 5 of 10");
        ProgressCalculator.PlannedToDate(leaves, Jan1.AddDays(9)).ShouldBe(0.4m, "on the end date the full weight is due");
        ProgressCalculator.PlannedToDate(leaves, Jan1.AddDays(14)).ShouldBe(0.7m);
        ProgressCalculator.PlannedToDate(leaves, Jan1.AddDays(40)).ShouldBe(1m);
    }

    [Theory]
    [InlineData(0.15, ProgressCalculator.OnTrack)] // 5 points below plan = at the tolerance
    [InlineData(0.1499, ProgressCalculator.Delayed)] // more than 5 points below
    [InlineData(0.30, ProgressCalculator.OnTrack)]
    public void Status_uses_the_tolerance(decimal approved, string status) =>
        ProgressCalculator.Status([M(1m, approved, 0, 49)], Jan1.AddDays(9), 0.05m).ShouldBe(status); // planned 0.2

    [Fact]
    public void A_milestone_past_its_end_below_100_is_delayed_even_when_on_plan()
    {
        var leaves = new[] { M(0.1m, 0.05m, 0, 4), M(0.9m, 0.9m, 0, 100) };
        ProgressCalculator.Status(leaves, Jan1.AddDays(5), 0.5m).ShouldBe(ProgressCalculator.Delayed);
        ProgressCalculator.Status([M(0.1m, 0.1m, 0, 4), M(0.9m, 0.9m, 0, 100)], Jan1.AddDays(5), 0.05m).ShouldBe(ProgressCalculator.OnTrack);
    }

    [Theory]
    [InlineData(0.1, 0.2, 0.5)]
    [InlineData(0.2, 0.2, 1)]
    [InlineData(0, 0, 0)]
    public void Completion_rate(decimal achieve, decimal weight, decimal rate) => ProgressCalculator.CompletionRate(achieve, weight).ShouldBe(rate);
}

/// <summary>Monthly update rules for one milestone (SPEC §1.4 C, ADR 0071).</summary>
public class MilestoneRulesTests
{
    private static readonly DateOnly Today = new(2027, 3, 10);

    private static List<string> Errors(decimal achieve, decimal weight = 0.2m, decimal approved = 0, DateOnly? end = null, DateOnly? actualStart = null,
        DateOnly? actualEnd = null, string? causality = null, string? corrective = null, int evidence = 0) =>
        MilestoneRules.ValidateProgress(new MilestoneProgressValues(achieve, actualStart, actualEnd, causality, corrective, null), weight, approved,
            end ?? Today.AddDays(30), Today, evidence).Select(e => e.MessageKey).ToList();

    [Fact]
    public void Achieve_stays_within_the_weight() => Errors(0.21m, actualStart: Today).ShouldContain("portfolio.errors.achieveAboveWeight");

    [Fact]
    public void Approved_achievement_is_locked() => Errors(0.05m, approved: 0.1m, actualStart: Today).ShouldContain("portfolio.errors.achieveBelowApproved");

    [Fact]
    public void First_achievement_needs_an_actual_start() => Errors(0.05m).ShouldBe(["portfolio.errors.actualStartRequired"]);

    [Fact]
    public void Complete_needs_actual_end_and_evidence()
    {
        Errors(0.2m, actualStart: Today.AddDays(-5)).ShouldBe(["portfolio.errors.actualEndRequired", "portfolio.errors.evidenceRequired"], ignoreOrder: true);
        Errors(0.2m, actualStart: Today.AddDays(-5), actualEnd: Today, evidence: 1).ShouldBeEmpty();
    }

    [Fact]
    public void Delayed_needs_causality_and_corrective_actions()
    {
        var pastEnd = Today.AddDays(-1);
        Errors(0.1m, end: pastEnd, actualStart: Today.AddDays(-20))
            .ShouldBe(["portfolio.errors.causalityRequired", "portfolio.errors.correctiveActionsRequired"], ignoreOrder: true);
        Errors(0.1m, end: pastEnd, actualStart: Today.AddDays(-20), causality: "Vendor late", corrective: "Second shift").ShouldBeEmpty();
        Errors(0.1m, end: Today, actualStart: Today.AddDays(-20)).ShouldBeEmpty("not past the planned end yet");
    }

    [Fact]
    public void Actual_end_cannot_be_in_the_future_or_before_the_start() =>
        Errors(0.2m, actualStart: Today, actualEnd: Today.AddDays(1), evidence: 1).ShouldBe(["portfolio.errors.actualEndInFuture"]);
}

/// <summary>Default project risk scoring (ADR 0074): 5 × 5, bands at 4/5, 9/10, 16/17; control evaluation lowers the residual.</summary>
public class RiskScoringTests
{
    [Theory]
    [InlineData(1, 4, RiskBands.Low)]
    [InlineData(1, 5, RiskBands.Medium)]
    [InlineData(3, 3, RiskBands.Medium)]
    [InlineData(2, 5, RiskBands.High)]
    [InlineData(4, 4, RiskBands.High)]
    [InlineData(3, 5, RiskBands.High)]
    [InlineData(4, 5, RiskBands.Critical)]
    [InlineData(5, 5, RiskBands.Critical)]
    public void Inherent_bands(int probability, int impact, string band)
    {
        var score = DefaultRiskScoring.Score(probability, impact, null);
        score.Inherent.ShouldBe(probability * impact);
        score.InherentBand.ShouldBe(band);
        score.ResidualBand.ShouldBe(band, "no control evaluation: residual = inherent");
    }

    [Theory]
    [InlineData(17, RiskBands.Critical)]
    [InlineData(16, RiskBands.High)]
    [InlineData(10, RiskBands.High)]
    [InlineData(9, RiskBands.Medium)]
    [InlineData(5, RiskBands.Medium)]
    [InlineData(4, RiskBands.Low)]
    public void Band_boundaries(decimal score, string band) => DefaultRiskScoring.Band(score).ShouldBe(band);

    [Fact]
    public void Control_evaluation_reduces_the_residual()
    {
        var strong = DefaultRiskScoring.Score(5, 5, 10);
        strong.Inherent.ShouldBe(25);
        strong.Residual.ShouldBe(2.5m);
        strong.ResidualBand.ShouldBe(RiskBands.Low);
        DefaultRiskScoring.Score(5, 5, 1).Residual.ShouldBe(25m, "a weak control removes nothing");
        DefaultRiskScoring.Score(4, 4, 6).Residual.ShouldBe(8m);
    }
}

/// <summary>Change request lifecycle (W7, ADR 0073).</summary>
public class ChangeLifecycleTests
{
    public static TheoryData<string, string, string> Table() => new()
    {
        { ChangeStatuses.Draft, ChangeActions.Edit, ChangeStatuses.Draft },
        { ChangeStatuses.Draft, ChangeActions.Submit, ChangeStatuses.UnderReview },
        { ChangeStatuses.Draft, ChangeActions.Cancel, ChangeStatuses.Cancelled },
        { ChangeStatuses.UnderReview, ChangeActions.Approve, ChangeStatuses.Approved },
        { ChangeStatuses.UnderReview, ChangeActions.Reject, ChangeStatuses.Rejected },
        { ChangeStatuses.UnderReview, ChangeActions.Return, ChangeStatuses.Draft },
        { ChangeStatuses.UnderReview, ChangeActions.Cancel, ChangeStatuses.Cancelled },
        { ChangeStatuses.Approved, ChangeActions.Apply, ChangeStatuses.Completed },
        { ChangeStatuses.Approved, ChangeActions.Cancel, ChangeStatuses.Cancelled },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Allowed(string from, string action, string to) => ChangeLifecycle.Next(from, action).Value.ShouldBe(to);

    [Fact]
    public void Everything_else_is_refused_and_nothing_applies_before_approval()
    {
        var allowed = Table().Select(r => (r.Data.Item1, r.Data.Item2)).ToHashSet();
        foreach (var status in ChangeStatuses.All)
        {
            foreach (var action in ChangeActions.All)
            {
                ChangeLifecycle.Allows(status, action).ShouldBe(allowed.Contains((status, action)), $"{status} × {action}");
            }
        }

        ChangeLifecycle.Allows(ChangeStatuses.UnderReview, ChangeActions.Apply).ShouldBeFalse();
        ChangeLifecycle.Allows(ChangeStatuses.Draft, ChangeActions.Apply).ShouldBeFalse();
    }
}
