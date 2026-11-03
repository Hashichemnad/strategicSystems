using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;

namespace Strategy.UnitTests.Tasks;

/// <summary>Timesheet maths, indicators, groups, recurrence, dependency cycles and settings validation.</summary>
public class TaskDomainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Timesheet_durations_are_correct_across_pauses_and_returns()
    {
        var task = TaskItem.Create(TaskWorkflowTests.Content(TaskTemplates.Full), "TASK-1", Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), null, false, T0);
        Act(task, TaskActions.Accept, T0.AddHours(1));                                   // period 1 opens (accepted 1 h after assignment)
        Act(task, TaskActions.Pause, T0.AddHours(3), reason: "waiting for data");       // period 1: 2 h, hold reason
        Act(task, TaskActions.Start, T0.AddHours(5));                                    // period 2 opens (2 h on hold, not counted)
        Act(task, TaskActions.Submit, T0.AddHours(6).AddMinutes(30));                    // period 2: 1.5 h
        Act(task, TaskActions.Return, T0.AddHours(7), comment: "add the annex");         // period 3 opens
        Act(task, TaskActions.Submit, T0.AddHours(7).AddMinutes(45));                    // period 3: 45 min

        task.Periods.Count.ShouldBe(3);
        task.Periods.Select(p => p.DurationSeconds).ShouldBe([7200L, 5400L, 2700L]);
        task.Periods[0].HoldReason.ShouldBe("waiting for data");
        task.Periods[0].EndEvent.ShouldBe(TaskActions.Pause);
        task.Periods[1].HoldReason.ShouldBeNull();
        task.ActualSeconds.ShouldBe(7200L + 5400L + 2700L);
        task.TimeToAcceptSeconds.ShouldBe(3600L);
        task.TimeToCompleteSeconds.ShouldBe((long)TimeSpan.FromHours(6.75).TotalSeconds);
        task.OpenPeriod.ShouldBeNull();
    }

    [Fact]
    public void Cancelling_closes_the_running_period_and_a_second_start_does_not_open_two()
    {
        var task = TaskItem.Create(TaskWorkflowTests.Content(TaskTemplates.Simple), "TASK-2", Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), null, false, T0);
        Act(task, TaskActions.Start, T0.AddMinutes(10));
        task.TimeToAcceptSeconds.ShouldBe(600L);
        Act(task, TaskActions.Cancel, T0.AddMinutes(70), reason: "no longer needed");
        task.Periods.Single().DurationSeconds.ShouldBe(3600L);
        task.ActualSeconds.ShouldBe(3600L);
        task.CancelReason.ShouldBe("no longer needed");
        task.ClosedAt.ShouldBe(T0.AddMinutes(70));
    }

    [Fact]
    public void Reassign_moves_the_task_to_the_new_assignee_and_resets_acceptance()
    {
        var task = TaskItem.Create(TaskWorkflowTests.Content(TaskTemplates.Full), "TASK-3", Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), null, false, T0);
        Act(task, TaskActions.Reject, T0.AddHours(1), reason: "not my unit");
        task.RejectionReason.ShouldBe("not my unit");
        var other = Guid.CreateVersion7();
        var unit = Guid.CreateVersion7();
        task.Act(TaskActions.Reassign, true, false, T0.AddHours(2), new TaskActInput(NewAssigneeEmployeeId: other, NewAssigneeUnitId: unit), new TaskGuards())
            .IsSuccess.ShouldBeTrue();
        task.Status.ShouldBe(TaskStatuses.Assigned);
        task.AssigneeEmployeeId.ShouldBe(other);
        task.UnitId.ShouldBe(unit);
        task.AssignedAt.ShouldBe(T0.AddHours(2));
        task.RejectionReason.ShouldBeNull();
    }

    [Fact]
    public void On_time_flag_compares_completion_with_the_due_time()
    {
        var task = TaskItem.Create(TaskWorkflowTests.Content(TaskTemplates.Full), "TASK-4", Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), null, false, T0);
        task.SetDeadlines(Guid.CreateVersion7(), T0.AddHours(5), T0.AddHours(1), T0.AddDays(3));
        Act(task, TaskActions.Accept, T0.AddHours(1));
        Act(task, TaskActions.Submit, T0.AddHours(6));
        task.CompletedOnTime.ShouldBe(false);
        Act(task, TaskActions.Return, T0.AddHours(7), comment: "again");
        task.CompletedOnTime.ShouldBeNull();
    }

    [Theory]
    [InlineData(TaskStatuses.InProgress, -2, -1, TaskIndicators.Overdue)]
    [InlineData(TaskStatuses.InProgress, 2, -1, TaskIndicators.DueSoon)]
    [InlineData(TaskStatuses.InProgress, 48, 24, TaskIndicators.OnTime)]
    [InlineData(TaskStatuses.OnHold, -2, -3, TaskIndicators.OnHold)]
    [InlineData(TaskStatuses.Approved, -2, -3, null)]
    public void Indicator(string status, int dueInHours, int soonInHours, string? expected) =>
        TaskIndicators.Of(status, T0.AddHours(dueInHours), T0.AddHours(soonInHours), T0).ShouldBe(expected);

    [Fact]
    public void Indicator_without_due_date_is_empty_unless_on_hold()
    {
        TaskIndicators.Of(TaskStatuses.Assigned, null, null, T0).ShouldBeNull();
        TaskIndicators.Of(TaskStatuses.OnHold, null, null, T0).ShouldBe(TaskIndicators.OnHold);
    }

    [Fact]
    public void Group_completion_rules()
    {
        string[] oneOfThree = [TaskStatuses.Approved, TaskStatuses.InProgress, TaskStatuses.Assigned];
        AssignmentGroup.IsComplete(GroupCompletionRules.All, oneOfThree).ShouldBeFalse();
        AssignmentGroup.IsComplete(GroupCompletionRules.Any, oneOfThree).ShouldBeTrue();
        AssignmentGroup.IsComplete(GroupCompletionRules.All, [TaskStatuses.Approved, TaskStatuses.Done, TaskStatuses.Cancelled]).ShouldBeTrue();
        AssignmentGroup.IsComplete(GroupCompletionRules.All, [TaskStatuses.Cancelled]).ShouldBeFalse();
        oneOfThree.Count(AssignmentGroup.IsFinished).ShouldBe(1);
    }

    [Fact]
    public void Recurrence_dates_and_ends()
    {
        var anchor = new DateOnly(2026, 1, 31);
        var monthly = new RecurrenceRule(RecurrenceFrequencies.Monthly, 1, Count: 3);
        monthly.IsValid.ShouldBeTrue();
        monthly.Next(anchor, 2).ShouldBe(new DateOnly(2026, 2, 28));
        monthly.Next(anchor, 3).ShouldBe(new DateOnly(2026, 3, 31));
        monthly.Next(anchor, 4).ShouldBeNull();

        var weekly = new RecurrenceRule(RecurrenceFrequencies.Weekly, 2, Until: new DateOnly(2026, 2, 20));
        weekly.Next(anchor, 2).ShouldBe(new DateOnly(2026, 2, 14));
        weekly.Next(anchor, 3).ShouldBeNull();

        new RecurrenceRule(RecurrenceFrequencies.Daily, 3).Next(anchor, 2).ShouldBe(new DateOnly(2026, 2, 3));
        new RecurrenceRule("yearly").IsValid.ShouldBeFalse();
        new RecurrenceRule(RecurrenceFrequencies.Daily, 13).IsValid.ShouldBeFalse();
        RecurrenceRule.FromJson(monthly.ToJson()).ShouldBe(monthly);
    }

    [Fact]
    public void Dependency_cycles_are_detected()
    {
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), c = Guid.CreateVersion7();
        var edges = new Dictionary<Guid, IReadOnlyList<Guid>> { [b] = [a], [c] = [b] }; // c waits for b, b waits for a
        TaskDependency.CreatesCycle(a, c, edges).ShouldBeTrue();
        TaskDependency.CreatesCycle(a, a, edges).ShouldBeTrue();
        TaskDependency.CreatesCycle(c, a, edges).ShouldBeFalse();
    }

    [Fact]
    public void Settings_validation_and_template_map()
    {
        var defaults = new TaskSettings();
        TaskSettings.Validate(defaults).ShouldBeEmpty();
        defaults.TemplateOf("personal").ShouldBe(TaskTemplates.Simple);
        defaults.TemplateOf("manager-assignment").ShouldBe(TaskTemplates.Full);
        (defaults with { ConfidentialCategories = ["hr"] }).IsConfidentialCategory("hr").ShouldBeTrue();
        TaskSettings.Validate(defaults with { FeedbackScaleMax = 2, AssignScope = "x", Digest = "hourly" }).Select(e => e.Field)
            .ShouldBe(["assignScope", "digest", "feedbackScaleMax"], ignoreOrder: true);
    }

    private static void Act(TaskItem task, string action, DateTimeOffset at, string? reason = null, string? comment = null)
    {
        var result = task.Act(action, true, true, at, new TaskActInput(reason, comment, action == TaskActions.Approve ? 3 : null), new TaskGuards());
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error!.Code : action);
    }
}
