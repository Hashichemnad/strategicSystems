namespace Strategy.Modules.Tasks.Domain;

/// <summary>Workflow templates (SPEC §11.3, ADR 0036). Chosen per task type in Settings → Tasks and snapshotted on the task.</summary>
public static class TaskTemplates
{
    public const string Full = "full";
    public const string Simple = "simple";

    public static readonly IReadOnlyList<string> All = [Full, Simple];
}

/// <summary>Status codes. Names per template are translation keys (<c>tasks.status.&lt;template&gt;.&lt;code&gt;</c>).</summary>
public static class TaskStatuses
{
    public const string Draft = "draft";
    public const string Assigned = "assigned";
    public const string InProgress = "in_progress";
    public const string OnHold = "on_hold";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string ToDo = "to_do";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, Assigned, InProgress, OnHold, Submitted, Approved, Rejected, ToDo, Done, Cancelled];

    public static readonly IReadOnlyList<string> Closed = [Approved, Done, Cancelled];

    public static readonly IReadOnlyList<string> Open = [Draft, Assigned, InProgress, OnHold, Submitted, Rejected, ToDo];

    /// <summary>Statuses in which the task's time counts against the due date (draft and rejected wait for the creator).</summary>
    public static readonly IReadOnlyList<string> Running = [Assigned, InProgress, OnHold, Submitted, ToDo];

    /// <summary>Kanban columns / stage stepper per template.</summary>
    public static IReadOnlyList<string> StagesOf(string template) => template == TaskTemplates.Simple
        ? [ToDo, InProgress, OnHold, Done]
        : [Assigned, InProgress, OnHold, Submitted, Approved];

    public static bool IsClosed(string status) => Closed.Contains(status);
}

public static class TaskActions
{
    public const string Assign = "assign";
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string Reassign = "reassign";
    public const string Pause = "pause";
    public const string Start = "start";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Return = "return";
    public const string Complete = "complete";
    public const string Cancel = "cancel";
    public const string CreateSubtask = "create-subtask";

    public static readonly IReadOnlyList<string> All = [Assign, Accept, Reject, Reassign, Pause, Start, Submit, Approve, Return, Complete, Cancel, CreateSubtask];
}

/// <summary>Who performs a transition: the creator (assigner) or the assignee (SPEC §11.3 "By" column).</summary>
public enum TaskActor
{
    Creator = 1,
    Assignee = 2,
}

/// <summary>One row of a transition table. <c>To</c> null: the action creates something without moving the task (sub-task).</summary>
public sealed record TaskTransition(string Template, string From, string Action, TaskActor By, string? To);

/// <summary>
/// The two lifecycles as explicit transition tables (CLAUDE.md §5). Every allowed transition is a row; anything else is refused.
/// Unit tests enumerate every (template, status, action, actor) combination against these rows.
/// </summary>
public static class TaskWorkflow
{
    private const string Full = TaskTemplates.Full;
    private const string Simple = TaskTemplates.Simple;

    public static readonly IReadOnlyList<TaskTransition> Transitions =
    [
        // Full (SPEC §11.3)
        new(Full, TaskStatuses.Draft, TaskActions.Assign, TaskActor.Creator, TaskStatuses.Assigned),
        new(Full, TaskStatuses.Assigned, TaskActions.Accept, TaskActor.Assignee, TaskStatuses.InProgress),
        new(Full, TaskStatuses.Assigned, TaskActions.Reject, TaskActor.Assignee, TaskStatuses.Rejected),
        new(Full, TaskStatuses.Rejected, TaskActions.Reassign, TaskActor.Creator, TaskStatuses.Assigned),
        new(Full, TaskStatuses.InProgress, TaskActions.Pause, TaskActor.Assignee, TaskStatuses.OnHold),
        new(Full, TaskStatuses.OnHold, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        new(Full, TaskStatuses.InProgress, TaskActions.Submit, TaskActor.Assignee, TaskStatuses.Submitted),
        new(Full, TaskStatuses.Submitted, TaskActions.Approve, TaskActor.Creator, TaskStatuses.Approved),
        new(Full, TaskStatuses.Submitted, TaskActions.Return, TaskActor.Creator, TaskStatuses.InProgress),
        new(Full, TaskStatuses.Submitted, TaskActions.Reassign, TaskActor.Creator, TaskStatuses.Assigned),
        new(Full, TaskStatuses.Draft, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.Assigned, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.Rejected, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.InProgress, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.OnHold, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.Submitted, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Full, TaskStatuses.InProgress, TaskActions.CreateSubtask, TaskActor.Assignee, null),
        new(Full, TaskStatuses.OnHold, TaskActions.CreateSubtask, TaskActor.Assignee, null),

        // Simple (SPEC §11.3: To Do → In Progress → Done, optional pause, no acceptance or approval)
        new(Simple, TaskStatuses.Draft, TaskActions.Assign, TaskActor.Creator, TaskStatuses.ToDo),
        new(Simple, TaskStatuses.ToDo, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        new(Simple, TaskStatuses.InProgress, TaskActions.Pause, TaskActor.Assignee, TaskStatuses.OnHold),
        new(Simple, TaskStatuses.OnHold, TaskActions.Start, TaskActor.Assignee, TaskStatuses.InProgress),
        new(Simple, TaskStatuses.InProgress, TaskActions.Complete, TaskActor.Assignee, TaskStatuses.Done),
        new(Simple, TaskStatuses.Draft, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Simple, TaskStatuses.ToDo, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Simple, TaskStatuses.InProgress, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Simple, TaskStatuses.OnHold, TaskActions.Cancel, TaskActor.Creator, TaskStatuses.Cancelled),
        new(Simple, TaskStatuses.InProgress, TaskActions.CreateSubtask, TaskActor.Assignee, null),
        new(Simple, TaskStatuses.OnHold, TaskActions.CreateSubtask, TaskActor.Assignee, null),
    ];

    /// <summary>The status a new task starts in (draft when saved as a draft).</summary>
    public static string InitialStatus(string template, bool draft) =>
        draft ? TaskStatuses.Draft : template == TaskTemplates.Simple ? TaskStatuses.ToDo : TaskStatuses.Assigned;

    /// <summary>The row allowing <paramref name="action"/> from <paramref name="status"/> for one of the actor's roles, or null.</summary>
    public static TaskTransition? Find(string template, string status, string action, bool isCreator, bool isAssignee) =>
        Transitions.FirstOrDefault(t => t.Template == template && t.From == status && t.Action == action
            && ((t.By == TaskActor.Creator && isCreator) || (t.By == TaskActor.Assignee && isAssignee)));

    /// <summary>Actions the actor may take now (current-status banner, quick actions, MCP).</summary>
    public static IReadOnlyList<string> Allowed(string template, string status, bool isCreator, bool isAssignee) =>
        Transitions.Where(t => t.Template == template && t.From == status
                && ((t.By == TaskActor.Creator && isCreator) || (t.By == TaskActor.Assignee && isAssignee)))
            .Select(t => t.Action).Distinct().ToList();

    /// <summary>Does the action exist for the template at all (a known action that is not allowed now is a business-rule error)?</summary>
    public static bool Exists(string template, string action) => Transitions.Any(t => t.Template == template && t.Action == action);
}
