using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;

namespace Strategy.UnitTests.Cap;

/// <summary>The remark state machine (docs/design/cap.md §4.1): every allowed and every forbidden transition.</summary>
public class RemarkLifecycleTests
{
    public static TheoryData<string, string, string?> Table()
    {
        var allowed = new Dictionary<(string, string), string>
        {
            [(CapRemarkStatuses.Assigned, RemarkActions.Accept)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Assigned, RemarkActions.Reject)] = CapRemarkStatuses.Rejected,
            [(CapRemarkStatuses.Assigned, RemarkActions.RejectForConfirmation)] = CapRemarkStatuses.RejectionPending,
            [(CapRemarkStatuses.RejectionPending, RemarkActions.ConfirmRejection)] = CapRemarkStatuses.Rejected,
            [(CapRemarkStatuses.RejectionPending, RemarkActions.SendBack)] = CapRemarkStatuses.Assigned,
            [(CapRemarkStatuses.Planning, RemarkActions.Submit)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Correction, RemarkActions.Submit)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Validation, RemarkActions.ToImplementation)] = CapRemarkStatuses.Implementation,
            [(CapRemarkStatuses.Validation, RemarkActions.ToCorrection)] = CapRemarkStatuses.Correction,
            [(CapRemarkStatuses.Validation, RemarkActions.ToPlanning)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Validation, RemarkActions.ToValidation)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Implementation, RemarkActions.Complete)] = CapRemarkStatuses.Completed,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToVerification)] = CapRemarkStatuses.Verification,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToPlanning)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToCorrection)] = CapRemarkStatuses.Correction,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToValidation)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Verification, RemarkActions.Effective)] = CapRemarkStatuses.Completed,
            [(CapRemarkStatuses.Verification, RemarkActions.NotEffective)] = CapRemarkStatuses.Planning,
        };
        foreach (var open in new[]
                 {
                     CapRemarkStatuses.Assigned, CapRemarkStatuses.RejectionPending, CapRemarkStatuses.Planning, CapRemarkStatuses.Validation,
                     CapRemarkStatuses.Correction, CapRemarkStatuses.Implementation, CapRemarkStatuses.Verification,
                 })
        {
            allowed[(open, RemarkActions.Cancel)] = CapRemarkStatuses.Cancelled;
        }

        var data = new TheoryData<string, string, string?>();
        foreach (var status in CapRemarkStatuses.All)
        {
            foreach (var action in RemarkActions.All)
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
        RemarkLifecycle.Next(from, action).ShouldBe(to);
        RemarkLifecycle.Allows(from, action).ShouldBe(to is not null);
    }

    [Theory]
    [InlineData(CapRemarkStatuses.Assigned, RemarkStages.Analysis)]
    [InlineData(CapRemarkStatuses.RejectionPending, RemarkStages.Analysis)]
    [InlineData(CapRemarkStatuses.Planning, RemarkStages.CorrectiveAction)]
    [InlineData(CapRemarkStatuses.Validation, RemarkStages.CorrectiveAction)]
    [InlineData(CapRemarkStatuses.Correction, RemarkStages.CorrectiveAction)]
    [InlineData(CapRemarkStatuses.Implementation, RemarkStages.Implementation)]
    [InlineData(CapRemarkStatuses.Verification, RemarkStages.Verification)]
    [InlineData(CapRemarkStatuses.Completed, RemarkStages.Closed)]
    [InlineData(CapRemarkStatuses.Rejected, RemarkStages.Closed)]
    [InlineData(CapRemarkStatuses.Cancelled, RemarkStages.Closed)]
    public void Stage_of_each_status(string status, string stage) => RemarkStages.Of(status).ShouldBe(stage);
}

/// <summary>Remark status derived from its actions after each decision (docs/design/cap.md §4.1). Pure, table-tested.</summary>
public class RemarkProgressTests
{
    private static RemarkProgress.ActionState A(string validation, string status = ActionStatuses.NotStarted) => new(validation, status);

    [Fact]
    public void Any_pending_keeps_validation() =>
        RemarkProgress.Derive(CapRemarkStatuses.Validation, [A(ValidationStatuses.Approved), A(ValidationStatuses.Pending)], false).ShouldBe(CapRemarkStatuses.Validation);

    [Fact]
    public void A_returned_action_asks_for_correction() =>
        RemarkProgress.Derive(CapRemarkStatuses.Validation, [A(ValidationStatuses.Approved), A(ValidationStatuses.Returned)], false).ShouldBe(CapRemarkStatuses.Correction);

    [Fact]
    public void All_rejected_goes_back_to_planning() =>
        RemarkProgress.Derive(CapRemarkStatuses.Validation, [A(ValidationStatuses.Rejected), A(ValidationStatuses.Rejected)], false).ShouldBe(CapRemarkStatuses.Planning);

    [Fact]
    public void Approved_and_rejected_goes_to_implementation() =>
        RemarkProgress.Derive(CapRemarkStatuses.Validation, [A(ValidationStatuses.Approved), A(ValidationStatuses.Rejected)], false).ShouldBe(CapRemarkStatuses.Implementation);

    [Fact]
    public void Cancelled_actions_are_ignored() =>
        RemarkProgress.Derive(CapRemarkStatuses.Validation, [A(ValidationStatuses.Approved), A(ValidationStatuses.Pending, ActionStatuses.Cancelled)], false)
            .ShouldBe(CapRemarkStatuses.Implementation);

    [Fact]
    public void All_approved_actions_completed_completes_the_remark_or_waits_for_verification()
    {
        RemarkProgress.ActionState[] done = [A(ValidationStatuses.Approved, ActionStatuses.Completed), A(ValidationStatuses.Rejected)];
        RemarkProgress.Derive(CapRemarkStatuses.Implementation, done, false).ShouldBe(CapRemarkStatuses.Completed);
        RemarkProgress.Derive(CapRemarkStatuses.Implementation, done, true).ShouldBe(CapRemarkStatuses.Verification);
        RemarkProgress.Derive(CapRemarkStatuses.Implementation, [A(ValidationStatuses.Approved, ActionStatuses.Late)], false).ShouldBe(CapRemarkStatuses.Implementation);
    }

    [Theory]
    [InlineData(CapRemarkStatuses.Assigned)]
    [InlineData(CapRemarkStatuses.Planning)]
    [InlineData(CapRemarkStatuses.Completed)]
    public void Other_statuses_are_not_derived(string status) =>
        RemarkProgress.Derive(status, [A(ValidationStatuses.Approved, ActionStatuses.Completed)], false).ShouldBeNull();
}

/// <summary>Deterministic action quality flags shown to the PM Office (SPEC §9.8).</summary>
public class ActionQualityTests
{
    private static readonly DateOnly Today = new(2026, 9, 10);

    private static CapActionValues V(string details, Guid? assignee, DateOnly? start, DateOnly? end, bool budget = false, decimal? cost = null) =>
        new("إجراء", details, CapActionTypes.Corrective, budget, budget ? false : null, cost, null, assignee, start, end);

    [Fact]
    public void A_clear_action_has_no_flag() =>
        ActionQuality.Flags(V("Publish the approved complaints procedure on the intranet and train all twelve section staff", Guid.NewGuid(), Today, Today.AddDays(30)),
            Today, Today.AddDays(60)).ShouldBeEmpty();

    [Fact]
    public void Vague_short_unassigned_and_dates_are_flagged()
    {
        ActionQuality.Flags(V("Improve it", null, null, null), Today, null).ShouldBe([ActionQuality.TooShort, ActionQuality.Vague, ActionQuality.NoAssignee, ActionQuality.NoDates]);
        ActionQuality.Flags(V("Publish the procedure and train the section staff on it", Guid.NewGuid(), Today, Today.AddDays(-1)), Today, Today.AddDays(5))
            .ShouldBe([ActionQuality.EndBeforeStart, ActionQuality.EndInPast]);
        ActionQuality.Flags(V("Publish the procedure and train the section staff on it", Guid.NewGuid(), Today, Today.AddDays(90)), Today, Today.AddDays(60))
            .ShouldBe([ActionQuality.AfterRemarkDue]);
        ActionQuality.Flags(V("Buy and install the new queue management system in the hall", Guid.NewGuid(), Today, Today.AddDays(30), budget: true), Today, null)
            .ShouldBe([ActionQuality.NoCost]);
    }
}

/// <summary>Settings → CAP defaults and the sandboxed root-cause rule.</summary>
public class CapSettingsTests
{
    [Fact]
    public void Defaults_follow_the_spec()
    {
        var s = new CapSettings();
        (s.DueDays(CapPriorities.Critical), s.DueDays(CapPriorities.High), s.DueDays(CapPriorities.Medium), s.DueDays(CapPriorities.Low)).ShouldBe((30, 60, 90, 180));
        s.RejectionNeedsPmoConfirmation.ShouldBeFalse();
        s.EffectivenessVerification.ShouldBeFalse();
        CapSettings.Validate(s).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CapPriorities.Critical, CapSourceTypes.Workshop, true)]
    [InlineData(CapPriorities.High, CapSourceTypes.Workshop, true)]
    [InlineData(CapPriorities.Medium, CapSourceTypes.Workshop, false)]
    [InlineData(CapPriorities.Low, CapSourceTypes.InternalAudit, true)]
    [InlineData(CapPriorities.Low, CapSourceTypes.ExternalAudit, true)]
    [InlineData(CapPriorities.Low, CapSourceTypes.Benchmarking, false)]
    public void Root_cause_rule(string priority, string sourceType, bool required) =>
        new CapSettings().RootCauseRequired(priority, CapRemarkTypes.Observation, sourceType, null).ShouldBe(required);

    [Fact]
    public void An_invalid_rule_or_range_is_refused()
    {
        CapSettings.Validate(new CapSettings { RootCauseRule = "priority ==" }).ShouldContain(e => e.Field == "rootCauseRule");
        CapSettings.Validate(new CapSettings { MonthlyReminderDay = 31, KpiMissMode = "sometimes" }).Select(e => e.Field).ShouldBe(["monthlyReminderDay", "kpiMissMode"], ignoreOrder: true);
        new CapSettings { RootCauseRule = "" }.RootCauseRequired(CapPriorities.Critical, CapRemarkTypes.Observation, CapSourceTypes.Workshop, null).ShouldBeFalse();
    }
}
