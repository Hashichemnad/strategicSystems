using Strategy.Modules.Tasks.Domain;

namespace Strategy.UnitTests.Tasks;

/// <summary>Administrative transfer when an employee leaves (ADR 0040): the status each open task lands in, for both ways.</summary>
public class TaskTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TaskTemplates.Full, TaskStatuses.Draft, TaskStatuses.Draft)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Assigned, TaskStatuses.Assigned)]
    [InlineData(TaskTemplates.Full, TaskStatuses.InProgress, TaskStatuses.Assigned)]
    [InlineData(TaskTemplates.Full, TaskStatuses.OnHold, TaskStatuses.Assigned)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Rejected, TaskStatuses.Assigned)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Submitted, TaskStatuses.Submitted)]
    [InlineData(TaskTemplates.Simple, TaskStatuses.ToDo, TaskStatuses.ToDo)]
    [InlineData(TaskTemplates.Simple, TaskStatuses.InProgress, TaskStatuses.ToDo)]
    [InlineData(TaskTemplates.Simple, TaskStatuses.OnHold, TaskStatuses.ToDo)]
    public void Transfer_to_a_new_assignee(string template, string from, string expected)
    {
        var task = TaskWorkflowTests.TaskIn(template, from);
        var newAssignee = Guid.CreateVersion7();

        task.TransferTo(newAssignee, null, Now).IsSuccess.ShouldBeTrue();

        task.Status.ShouldBe(expected);
        task.AssigneeEmployeeId.ShouldBe(newAssignee);
        task.OpenPeriod.ShouldBeNull();
        if (from is TaskStatuses.InProgress)
        {
            task.Periods[^1].EndEvent.ShouldBe(TaskEvents.Transferred);
        }
    }

    [Theory]
    [InlineData(TaskTemplates.Full, TaskStatuses.Draft, TaskStatuses.Draft)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Assigned, TaskStatuses.Rejected)]
    [InlineData(TaskTemplates.Full, TaskStatuses.InProgress, TaskStatuses.Rejected)]
    [InlineData(TaskTemplates.Full, TaskStatuses.OnHold, TaskStatuses.Rejected)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Rejected, TaskStatuses.Rejected)]
    [InlineData(TaskTemplates.Full, TaskStatuses.Submitted, TaskStatuses.Submitted)]
    [InlineData(TaskTemplates.Simple, TaskStatuses.InProgress, TaskStatuses.Rejected)]
    public void Return_to_the_creator_for_a_decision(string template, string from, string expected)
    {
        var task = TaskWorkflowTests.TaskIn(template, from);

        task.ReturnToCreator("assignee left", Now).IsSuccess.ShouldBeTrue();

        task.Status.ShouldBe(expected);
        task.OpenPeriod.ShouldBeNull();
        if (expected == TaskStatuses.Rejected && from != TaskStatuses.Rejected)
        {
            task.RejectionReason.ShouldBe("assignee left");
            TaskWorkflow.Allowed(TaskTemplates.Full, task.Status, isCreator: true, isAssignee: false).ShouldContain(TaskActions.Reassign);
        }
    }

    [Theory]
    [InlineData(TaskStatuses.Approved)]
    [InlineData(TaskStatuses.Cancelled)]
    public void Closed_tasks_are_never_moved(string status)
    {
        var task = TaskWorkflowTests.TaskIn(TaskTemplates.Full, status);
        task.TransferTo(Guid.CreateVersion7(), null, Now).IsFailure.ShouldBeTrue();
        task.ReturnToCreator("x", Now).IsFailure.ShouldBeTrue();
        task.Status.ShouldBe(status);
    }
}
