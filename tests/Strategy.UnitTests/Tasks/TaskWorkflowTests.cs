using Strategy.Modules.Tasks.Domain;

namespace Strategy.UnitTests.Tasks;

/// <summary>
/// The Full and Simple transition tables (docs/design/tasks.md §3, SPEC §11.3): every allowed transition reaches its target, every other
/// (template, status, action, actor) combination is refused and leaves the task unchanged.
/// </summary>
public class TaskWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Creator = Guid.CreateVersion7();
    private static readonly Guid Assignee = Guid.CreateVersion7();

    /// <summary>The expected tables, written out independently of <see cref="TaskWorkflow.Transitions"/>.</summary>
    private static readonly (string Template, string From, string Action, TaskActor By, string? To)[] Table =
    [
        (TaskTemplates.Full, TaskStatuses.Draft, TaskActions.Assign, TaskActor.Creator, TaskStatuses.Assigned),
        (TaskTemplates.Full, TaskStatuses.Assigned, TaskActions.Accept, TaskActor.Assignee, TaskStatuses.InProgress),
        (TaskTemplates.Full, TaskStatuses.Assigned, TaskActions.Reject, TaskActor.Assignee, TaskStatuses.Rejected),
        (TaskTemplates.Full, TaskStatuses.Rejected, TaskActions.Reassign, TaskActor.Creator, TaskStatuses.Assigned),
        (TaskTemplates.Full, TaskStatuses.Rejected, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.InProgress, TaskActions.Pause, TaskActor.Assignee, TaskStatuses.OnHold),
        (TaskTemplates.Full, TaskStatuses.OnHold, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        (TaskTemplates.Full, TaskStatuses.InProgress, TaskActions.Submit, TaskActor.Assignee, TaskStatuses.Submitted),
        (TaskTemplates.Full, TaskStatuses.Submitted, TaskActions.Approve, TaskActor.Creator, TaskStatuses.Approved),
        (TaskTemplates.Full, TaskStatuses.Submitted, TaskActions.Return, TaskActor.Creator, TaskStatuses.InProgress),
        (TaskTemplates.Full, TaskStatuses.Submitted, TaskActions.Reassign, TaskActor.Creator, TaskStatuses.Assigned),
        (TaskTemplates.Full, TaskStatuses.Draft, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.Assigned, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.InProgress, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.OnHold, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.Submitted, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Full, TaskStatuses.InProgress, TaskActions.CreateSubtask, TaskActor.Assignee, null),
        (TaskTemplates.Full, TaskStatuses.OnHold, TaskActions.CreateSubtask, TaskActor.Assignee, null),
        (TaskTemplates.Simple, TaskStatuses.Draft, TaskActions.Assign, TaskActor.Creator, TaskStatuses.ToDo),
        (TaskTemplates.Simple, TaskStatuses.ToDo, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        (TaskTemplates.Simple, TaskStatuses.InProgress, TaskActions.Pause, TaskActor.Assignee, TaskStatuses.OnHold),
        (TaskTemplates.Simple, TaskStatuses.OnHold, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        (TaskTemplates.Simple, TaskStatuses.InProgress, TaskActions.Complete, TaskActor.Assignee, TaskStatuses.Done),
        (TaskTemplates.Simple, TaskStatuses.Draft, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Simple, TaskStatuses.ToDo, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Simple, TaskStatuses.InProgress, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Simple, TaskStatuses.OnHold, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        (TaskTemplates.Simple, TaskStatuses.InProgress, TaskActions.CreateSubtask, TaskActor.Assignee, null),
        (TaskTemplates.Simple, TaskStatuses.OnHold, TaskActions.CreateSubtask, TaskActor.Assignee, null),
    ];

    public static TheoryData<string, string, string, TaskActor, string?> Allowed
    {
        get
        {
            var data = new TheoryData<string, string, string, TaskActor, string?>();
            foreach (var row in Table)
            {
                data.Add(row.Template, row.From, row.Action, row.By, row.To);
            }

            return data;
        }
    }

    /// <summary>Every combination that is not in <see cref="Allowed"/>: must be refused.</summary>
    public static TheoryData<string, string, string, TaskActor> Forbidden
    {
        get
        {
            var allowed = Table.Select(row => (row.Template, row.From, row.Action, row.By)).ToHashSet();
            var data = new TheoryData<string, string, string, TaskActor>();
            foreach (var template in TaskTemplates.All)
            {
                foreach (var status in TaskStatuses.All)
                {
                    foreach (var action in TaskActions.All)
                    {
                        foreach (var actor in new[] { TaskActor.Creator, TaskActor.Assignee })
                        {
                            if (!allowed.Contains((template, status, action, actor)))
                            {
                                data.Add(template, status, action, actor);
                            }
                        }
                    }
                }
            }

            return data;
        }
    }

    [Fact]
    public void The_code_table_is_exactly_the_specified_table()
    {
        var expected = Table.ToHashSet();
        TaskWorkflow.Transitions.Select(t => (t.Template, t.From, t.Action, t.By, t.To)).ToHashSet().SetEquals(expected).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Allowed_transitions_reach_their_status(string template, string from, string action, TaskActor actor, string? to)
    {
        var task = TaskIn(template, from);

        var result = task.Act(action, actor == TaskActor.Creator, actor == TaskActor.Assignee, Now, Input(action), new TaskGuards());

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        task.Status.ShouldBe(to ?? from);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void Forbidden_transitions_are_refused_and_change_nothing(string template, string from, string action, TaskActor actor)
    {
        var task = TaskIn(template, from);

        var result = task.Act(action, actor == TaskActor.Creator, actor == TaskActor.Assignee, Now, Input(action), new TaskGuards());

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBeOneOf("tasks.task.actionNotAllowed", "tasks.task.unknownAction");
        task.Status.ShouldBe(from);
    }

    [Fact]
    public void Only_the_creator_approves_even_when_the_assignee_asks()
    {
        var task = TaskIn(TaskTemplates.Full, TaskStatuses.Submitted);

        task.Act(TaskActions.Approve, isCreator: false, isAssignee: true, Now, Input(TaskActions.Approve), new TaskGuards()).IsFailure.ShouldBeTrue();
        task.Act(TaskActions.Approve, isCreator: true, isAssignee: false, Now, Input(TaskActions.Approve), new TaskGuards()).IsSuccess.ShouldBeTrue();
        task.FeedbackPoint.ShouldBe(4);
        task.ApprovedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData(TaskActions.Reject, "reason")]
    [InlineData(TaskActions.Cancel, "reason")]
    public void Reasons_are_required(string action, string field)
    {
        var task = TaskIn(TaskTemplates.Full, TaskStatuses.Assigned);
        var result = task.Act(action, true, true, Now, new TaskActInput(), new TaskGuards());
        result.Error!.Fields!.ShouldContain(f => f.Field == field);
    }

    [Fact]
    public void Return_from_submitted_needs_a_comment_and_feedback_is_within_the_scale()
    {
        var task = TaskIn(TaskTemplates.Full, TaskStatuses.Submitted);
        task.Act(TaskActions.Return, true, false, Now, new TaskActInput(), new TaskGuards()).Error!.Fields!.ShouldContain(f => f.Field == "comment");
        task.Act(TaskActions.Approve, true, false, Now, new TaskActInput(FeedbackPoint: 6), new TaskGuards(FeedbackScaleMax: 5)).Error!.Fields!
            .ShouldContain(f => f.Field == "feedbackPoint");
        task.Act(TaskActions.Approve, true, false, Now, new TaskActInput(FeedbackPoint: 0), new TaskGuards()).IsFailure.ShouldBeTrue();
        task.Status.ShouldBe(TaskStatuses.Submitted);
    }

    [Fact]
    public void Open_subtasks_block_approval_and_completion_but_not_submission()
    {
        var full = TaskIn(TaskTemplates.Full, TaskStatuses.Submitted);
        full.Act(TaskActions.Approve, true, false, Now, Input(TaskActions.Approve), new TaskGuards(OpenSubtasks: 1)).Error!.Code.ShouldBe("tasks.task.openSubtasks");

        var simple = TaskIn(TaskTemplates.Simple, TaskStatuses.InProgress);
        simple.Act(TaskActions.Complete, false, true, Now, new TaskActInput(), new TaskGuards(OpenSubtasks: 2)).Error!.Code.ShouldBe("tasks.task.openSubtasks");

        var working = TaskIn(TaskTemplates.Full, TaskStatuses.InProgress);
        working.Act(TaskActions.Submit, false, true, Now, new TaskActInput(), new TaskGuards(OpenSubtasks: 2)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Open_predecessors_block_submission_and_completion()
    {
        var full = TaskIn(TaskTemplates.Full, TaskStatuses.InProgress);
        full.Act(TaskActions.Submit, false, true, Now, new TaskActInput(), new TaskGuards(OpenPredecessors: 1)).Error!.Code.ShouldBe("tasks.task.openPredecessors");

        var simple = TaskIn(TaskTemplates.Simple, TaskStatuses.InProgress);
        simple.Act(TaskActions.Complete, false, true, Now, new TaskActInput(), new TaskGuards(OpenPredecessors: 1)).Error!.Code.ShouldBe("tasks.task.openPredecessors");
    }

    [Fact]
    public void Allowed_lists_the_actions_of_each_role()
    {
        TaskWorkflow.Allowed(TaskTemplates.Full, TaskStatuses.Assigned, false, true).ShouldBe([TaskActions.Accept, TaskActions.Reject], ignoreOrder: true);
        TaskWorkflow.Allowed(TaskTemplates.Full, TaskStatuses.Assigned, true, false).ShouldBe([TaskActions.Cancel]);
        TaskWorkflow.Allowed(TaskTemplates.Full, TaskStatuses.Submitted, false, false).ShouldBeEmpty();
        TaskWorkflow.Allowed(TaskTemplates.Simple, TaskStatuses.ToDo, true, true).ShouldBe([TaskActions.Start, TaskActions.Cancel], ignoreOrder: true);
    }

    /// <summary>A task walked to <paramref name="status"/> through the transitions themselves (so its timesheet is consistent).</summary>
    internal static TaskItem TaskIn(string template, string status)
    {
        var draft = status == TaskStatuses.Draft;
        var task = TaskItem.Create(Content(template), "TASK-00001", Creator, null, null, Assignee, null, draft, Now.AddDays(-5));
        var path = (template, status) switch
        {
            (_, TaskStatuses.Draft) or (TaskTemplates.Full, TaskStatuses.Assigned) or (TaskTemplates.Simple, TaskStatuses.ToDo) => [],
            (TaskTemplates.Full, TaskStatuses.Rejected) => new[] { TaskActions.Reject },
            (TaskTemplates.Full, TaskStatuses.InProgress) => [TaskActions.Accept],
            (TaskTemplates.Full, TaskStatuses.OnHold) => [TaskActions.Accept, TaskActions.Pause],
            (TaskTemplates.Full, TaskStatuses.Submitted) => [TaskActions.Accept, TaskActions.Submit],
            (TaskTemplates.Full, TaskStatuses.Approved) => [TaskActions.Accept, TaskActions.Submit, TaskActions.Approve],
            (TaskTemplates.Full, TaskStatuses.Cancelled) => [TaskActions.Cancel],
            (TaskTemplates.Simple, TaskStatuses.InProgress) => [TaskActions.Start],
            (TaskTemplates.Simple, TaskStatuses.OnHold) => [TaskActions.Start, TaskActions.Pause],
            (TaskTemplates.Simple, TaskStatuses.Done) => [TaskActions.Start, TaskActions.Complete],
            (TaskTemplates.Simple, TaskStatuses.Cancelled) => [TaskActions.Cancel],
            _ => null,
        };

        if (path is null)
        {
            // A status the template never reaches (e.g. Submitted for Simple): set it directly through reflection for the forbidden checks.
            typeof(TaskItem).GetProperty(nameof(TaskItem.Status))!.SetValue(task, status);
            return task;
        }

        var at = Now.AddDays(-4);
        foreach (var action in path)
        {
            task.Act(action, true, true, at, Input(action), new TaskGuards()).IsSuccess.ShouldBeTrue(action);
            at = at.AddHours(1);
        }

        return task;
    }

    internal static TaskContent Content(string template) => new(
        "مهمة", "Task", null, "manager-assignment", template, "manager-assignment", null, TaskPriorities.Medium, false, null, null, null, null, null);

    private static TaskActInput Input(string action) => new(
        Reason: "سبب / reason", Comment: "تعليق / comment", FeedbackPoint: action == TaskActions.Approve ? 4 : null,
        NewAssigneeEmployeeId: action == TaskActions.Reassign ? Guid.CreateVersion7() : null);
}
