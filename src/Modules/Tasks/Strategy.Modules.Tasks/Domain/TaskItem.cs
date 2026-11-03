using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Tasks.Domain;

public static class TaskPriorities
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static readonly IReadOnlyList<string> All = [High, Medium, Low];
}

/// <summary>Computed indicator (SPEC §11.5): on time / due soon / overdue / on hold; null when the task is closed or has no due date.</summary>
public static class TaskIndicators
{
    public const string OnTime = "on_time";
    public const string DueSoon = "due_soon";
    public const string Overdue = "overdue";
    public const string OnHold = "on_hold";

    public static readonly IReadOnlyList<string> All = [OnTime, DueSoon, Overdue, OnHold];

    public static string? Of(string status, DateTimeOffset? dueAt, DateTimeOffset? dueSoonAt, DateTimeOffset now)
    {
        if (TaskStatuses.IsClosed(status))
        {
            return null;
        }

        if (status == TaskStatuses.OnHold)
        {
            return OnHold;
        }

        if (dueAt is null)
        {
            return null;
        }

        return now > dueAt ? Overdue : dueSoonAt is { } soon && now >= soon ? DueSoon : OnTime;
    }
}

/// <summary>Values the creator gives a new task (shared by every task of a multi-assignment).</summary>
public sealed record TaskContent(
    string? TitleAr,
    string? TitleEn,
    string? Description,
    string TypeCode,
    string Template,
    string SourceCode,
    string? CategoryCode,
    string Priority,
    bool Confidential,
    DateOnly? PlannedStart,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    string? LinkedType,
    Guid? LinkedId);

/// <summary>Inputs of a transition beyond the action itself.</summary>
public sealed record TaskActInput(
    string? Reason = null, string? Comment = null, int? FeedbackPoint = null, Guid? NewAssigneeEmployeeId = null, Guid? NewAssigneeUnitId = null);

/// <summary>Facts the handler loads before a transition (sub-tasks, predecessors, settings).</summary>
public sealed record TaskGuards(int OpenSubtasks = 0, int OpenPredecessors = 0, int FeedbackScaleMax = 5);

/// <summary>
/// A task (SPEC §11.5, <c>TASK-NNNNN</c>). One assignee per task; multi-assignment creates one task per assignee tied by an assignment group.
/// The lifecycle is <see cref="TaskWorkflow"/>; the timesheet periods are part of the aggregate.
/// </summary>
[Audited(EntityType)]
public sealed class TaskItem : AuditableEntity
{
    public const string EntityType = "tasks.task";
    public const int TitleMaxLength = 300;
    public const int DescriptionMaxLength = 8000;
    public const int ReasonMaxLength = 2000;

    private readonly List<TimesheetPeriod> _periods = [];

    private TaskItem()
    {
    }

    public string? Number { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Description { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    public string Template { get; private set; } = TaskTemplates.Full;

    public string SourceCode { get; private set; } = string.Empty;

    public string? CategoryCode { get; private set; }

    public string Priority { get; private set; } = TaskPriorities.Medium;

    public string Status { get; private set; } = TaskStatuses.Assigned;

    public bool Confidential { get; private set; }

    /// <summary>The assigner: approves the task (SPEC §11.12). For a task entered by a secretary, the manager it was entered for.</summary>
    public Guid CreatorEmployeeId { get; private set; }

    public Guid? CreatorUserId { get; private set; }

    /// <summary>The secretary who entered the task on behalf of the creator, if any.</summary>
    public Guid? EnteredByEmployeeId { get; private set; }

    public Guid AssigneeEmployeeId { get; private set; }

    /// <summary>The assignee's unit when assigned: unit data scopes and the business hours of the deadlines.</summary>
    public Guid? UnitId { get; private set; }

    public Guid? GroupId { get; private set; }

    public Guid? ParentTaskId { get; private set; }

    public string? LinkedType { get; private set; }

    public Guid? LinkedId { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? DueSoonAt { get; private set; }

    public DateTimeOffset? EscalateAt { get; private set; }

    public Guid? BusinessHoursId { get; private set; }

    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? AcceptRemindAt { get; private set; }

    /// <summary>Starting date-time (SPEC §11.3: accept = now).</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? CancelReason { get; private set; }

    public string? CompletionComment { get; private set; }

    public int? FeedbackPoint { get; private set; }

    public string? FeedbackComment { get; private set; }

    public int? PercentComplete { get; private set; }

    public decimal? EstimatedHours { get; private set; }

    /// <summary>Sum of closed timesheet periods.</summary>
    public long ActualSeconds { get; private set; }

    public long? TimeToAcceptSeconds { get; private set; }

    public long? TimeToCompleteSeconds { get; private set; }

    public bool? CompletedOnTime { get; private set; }

    /// <summary>Recurrence rule (JSON of <see cref="RecurrenceRule"/>) on the first task of a series.</summary>
    public string? Recurrence { get; private set; }

    public Guid? RecurrenceSourceId { get; private set; }

    public DateOnly? NextOccurrenceOn { get; private set; }

    public int OccurrenceCount { get; private set; }

    [NotAudited]
    public DateTimeOffset? ReminderSentAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? AcceptRemindedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? EscalatedAt { get; private set; }

    public IReadOnlyList<TimesheetPeriod> Periods => _periods;

    public bool IsOpen => !TaskStatuses.IsClosed(Status);

    public TimesheetPeriod? OpenPeriod => _periods.FirstOrDefault(p => p.EndedAt is null);

    public static TaskItem Create(
        TaskContent content, string? number, Guid creatorEmployeeId, Guid? creatorUserId, Guid? enteredByEmployeeId, Guid assigneeEmployeeId, Guid? unitId,
        bool draft, DateTimeOffset now, Guid? groupId = null, Guid? parentTaskId = null)
    {
        var task = new TaskItem
        {
            Number = number,
            CreatorEmployeeId = creatorEmployeeId,
            CreatorUserId = creatorUserId,
            EnteredByEmployeeId = enteredByEmployeeId,
            AssigneeEmployeeId = assigneeEmployeeId,
            UnitId = unitId,
            GroupId = groupId,
            ParentTaskId = parentTaskId,
            Template = content.Template,
            Status = TaskWorkflow.InitialStatus(content.Template, draft),
            LinkedType = content.LinkedType,
            LinkedId = content.LinkedId,
        };
        task.SetContent(content);
        if (!draft)
        {
            task.AssignedAt = now;
        }

        return task;
    }

    public bool IsCreator(Guid? employeeId, Guid? userId) =>
        (employeeId is { } e && e == CreatorEmployeeId) || (CreatorUserId is { } u && userId == u);

    public bool IsAssignee(Guid? employeeId) => employeeId is { } e && e == AssigneeEmployeeId;

    /// <summary>Editable content (creator, while the task is open). The template and the link are fixed after creation.</summary>
    public Result Update(TaskContent content)
    {
        if (!IsOpen)
        {
            return Error.Rule("tasks.task.closed", "tasks.errors.closed");
        }

        SetContent(content with { Template = Template, LinkedType = LinkedType, LinkedId = LinkedId });
        return Result.Success();
    }

    public void SetPercentComplete(int? percent) => PercentComplete = percent is null ? null : Math.Clamp(percent.Value, 0, 100);

    /// <summary>Deadlines from the business hours (ADR 0037). Clearing the due date clears every deadline and the reminder stamps.</summary>
    public void SetDeadlines(Guid? businessHoursId, DateTimeOffset? dueAt, DateTimeOffset? dueSoonAt, DateTimeOffset? escalateAt)
    {
        BusinessHoursId = businessHoursId;
        DueAt = dueAt;
        DueSoonAt = dueSoonAt;
        EscalateAt = escalateAt;
        ReminderSentAt = null;
        OverdueNotifiedAt = null;
        EscalatedAt = null;
    }

    public void SetDueDate(DateOnly? dueDate) => DueDate = dueDate;

    public void SetAcceptReminder(DateTimeOffset? at)
    {
        AcceptRemindAt = at;
        AcceptRemindedAt = null;
    }

    public void SetRecurrence(string? rule, DateOnly? nextOccurrenceOn)
    {
        Recurrence = rule;
        NextOccurrenceOn = nextOccurrenceOn;
        OccurrenceCount = rule is null ? 0 : 1;
    }

    public void MarkOccurrence(Guid sourceId) => RecurrenceSourceId = sourceId;

    public void AdvanceRecurrence(DateOnly? next)
    {
        NextOccurrenceOn = next;
        OccurrenceCount++;
    }

    public void SetNumber(string number) => Number ??= number;

    public void MarkReminderSent(DateTimeOffset at) => ReminderSentAt = at;

    public void MarkAcceptReminded(DateTimeOffset at) => AcceptRemindedAt = at;

    public void MarkOverdueNotified(DateTimeOffset at) => OverdueNotifiedAt = at;

    public void MarkEscalated(DateTimeOffset at) => EscalatedAt = at;

    /// <summary>
    /// Applies one transition of <see cref="TaskWorkflow"/> for an actor who is the creator and / or the assignee. Guards: reasons and
    /// comments where the SPEC requires them, the feedback point scale, open sub-tasks (approve / complete) and open predecessors (submit /
    /// complete). Timesheet periods open and close with the transition.
    /// </summary>
    public Result<TaskTransition> Act(string action, bool isCreator, bool isAssignee, DateTimeOffset now, TaskActInput input, TaskGuards guards)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(guards);
        var transition = TaskWorkflow.Find(Template, Status, action, isCreator, isAssignee);
        if (transition is null)
        {
            return TaskWorkflow.Exists(Template, action)
                ? Error.Rule("tasks.task.actionNotAllowed", "tasks.errors.actionNotAllowed", new Dictionary<string, object?> { ["action"] = action, ["status"] = Status })
                : Error.Rule("tasks.task.unknownAction", "tasks.errors.unknownAction", new Dictionary<string, object?> { ["action"] = action });
        }

        var reason = Clean(input.Reason);
        var comment = Clean(input.Comment);
        switch (action)
        {
            case TaskActions.Reject or TaskActions.Pause or TaskActions.Cancel when reason is null:
                return Error.Validation([new("reason", "validation.notEmpty", "validation.notEmpty")]);
            case TaskActions.Return or TaskActions.Reassign when comment is null && Status == TaskStatuses.Submitted:
                return Error.Validation([new("comment", "validation.notEmpty", "validation.notEmpty")]);
            case TaskActions.Reassign when input.NewAssigneeEmployeeId is null:
                return Error.Validation([new("assigneeEmployeeId", "validation.notEmpty", "validation.notEmpty")]);
            case TaskActions.Approve when input.FeedbackPoint is not { } point || point < 1 || point > guards.FeedbackScaleMax:
                return Error.Validation([new("feedbackPoint", "tasks.validation.feedbackPoint", "tasks.validation.feedbackPoint",
                    new Dictionary<string, object?> { ["max"] = guards.FeedbackScaleMax })]);
            case TaskActions.Approve or TaskActions.Complete when guards.OpenSubtasks > 0:
                return Error.Rule("tasks.task.openSubtasks", "tasks.errors.openSubtasks", new Dictionary<string, object?> { ["count"] = guards.OpenSubtasks });
            case TaskActions.Submit or TaskActions.Complete when guards.OpenPredecessors > 0:
                return Error.Rule("tasks.task.openPredecessors", "tasks.errors.openPredecessors", new Dictionary<string, object?> { ["count"] = guards.OpenPredecessors });
        }

        if (transition.To is null)
        {
            return transition; // creating a sub-task does not move the parent
        }

        switch (action)
        {
            case TaskActions.Assign:
                AssignedAt = now;
                break;
            case TaskActions.Accept:
                AcceptedAt = now;
                TimeToAcceptSeconds = AssignedAt is { } assigned ? (long)(now - assigned).TotalSeconds : null;
                OpenTimesheet(now);
                break;
            case TaskActions.Reject:
                RejectionReason = reason;
                break;
            case TaskActions.Reassign:
                CloseTimesheet(now, null, action);
                AssigneeEmployeeId = input.NewAssigneeEmployeeId!.Value;
                UnitId = input.NewAssigneeUnitId;
                AssignedAt = now;
                AcceptedAt = null;
                CompletedAt = null;
                RejectionReason = null;
                CompletionComment = null;
                break;
            case TaskActions.Pause:
                CloseTimesheet(now, reason, action);
                break;
            case TaskActions.Start:
                AcceptedAt ??= now;
                if (Template == TaskTemplates.Simple && TimeToAcceptSeconds is null && AssignedAt is { } since)
                {
                    TimeToAcceptSeconds = (long)(now - since).TotalSeconds;
                }

                OpenTimesheet(now);
                break;
            case TaskActions.Submit or TaskActions.Complete:
                CloseTimesheet(now, null, action);
                CompletedAt = now;
                CompletionComment = comment;
                TimeToCompleteSeconds = AcceptedAt is { } accepted ? (long)(now - accepted).TotalSeconds : null;
                CompletedOnTime = DueAt is null || now <= DueAt;
                if (action == TaskActions.Complete)
                {
                    ClosedAt = now;
                    PercentComplete = 100;
                }

                break;
            case TaskActions.Approve:
                ApprovedAt = now;
                ClosedAt = now;
                FeedbackPoint = input.FeedbackPoint;
                FeedbackComment = comment;
                PercentComplete = 100;
                break;
            case TaskActions.Return:
                CompletedAt = null;
                CompletedOnTime = null;
                OpenTimesheet(now);
                break;
            case TaskActions.Cancel:
                CloseTimesheet(now, null, action);
                CancelReason = reason;
                ClosedAt = now;
                break;
        }

        Status = transition.To;
        return transition;
    }

    /// <summary>
    /// Administrative transfer of an open task to a new assignee (ADR 0040, an employee leaves): drafts and submitted tasks keep their status;
    /// assigned / to-do tasks are re-assigned; work in progress, on hold or rejected goes back to Assigned (Full) / To Do (Simple) for the
    /// new assignee, the running timesheet period closed.
    /// </summary>
    public Result TransferTo(Guid newAssigneeEmployeeId, Guid? newUnitId, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Error.Rule("tasks.task.closed", "tasks.errors.closed");
        }

        CloseTimesheet(now, null, TaskEvents.Transferred);
        AssigneeEmployeeId = newAssigneeEmployeeId;
        UnitId = newUnitId;
        if (Status is TaskStatuses.InProgress or TaskStatuses.OnHold or TaskStatuses.Rejected or TaskStatuses.Assigned or TaskStatuses.ToDo)
        {
            Status = Template == TaskTemplates.Simple ? TaskStatuses.ToDo : TaskStatuses.Assigned;
            AssignedAt = now;
            AcceptedAt = null;
            RejectionReason = null;
        }

        return Result.Success();
    }

    /// <summary>The creator left: another employee becomes the task's creator (approver). Their own user is resolved through the record role.</summary>
    public void TakeOverAsCreator(Guid employeeId)
    {
        CreatorEmployeeId = employeeId;
        CreatorUserId = null;
    }

    /// <summary>
    /// Hands an open task back to its creator for a decision (ADR 0040): work not yet submitted becomes Rejected with the reason, so the
    /// creator reassigns or cancels it through the normal lifecycle; drafts and submitted tasks are already the creator's to act on.
    /// </summary>
    public Result ReturnToCreator(string reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Error.Rule("tasks.task.closed", "tasks.errors.closed");
        }

        if (Status is TaskStatuses.Assigned or TaskStatuses.ToDo or TaskStatuses.InProgress or TaskStatuses.OnHold)
        {
            CloseTimesheet(now, null, TaskEvents.Transferred);
            Status = TaskStatuses.Rejected;
            RejectionReason = reason;
        }

        return Result.Success();
    }

    private void OpenTimesheet(DateTimeOffset now)
    {
        if (OpenPeriod is null)
        {
            _periods.Add(TimesheetPeriod.Open(Id, now));
        }
    }

    private void CloseTimesheet(DateTimeOffset now, string? holdReason, string endEvent)
    {
        OpenPeriod?.Close(now, holdReason, endEvent);
        ActualSeconds = _periods.Where(p => p.EndedAt is not null).Sum(p => p.DurationSeconds ?? 0);
    }

    private void SetContent(TaskContent content)
    {
        TitleAr = Clean(content.TitleAr);
        TitleEn = Clean(content.TitleEn);
        Description = Clean(content.Description);
        TypeCode = content.TypeCode;
        Template = content.Template;
        SourceCode = content.SourceCode;
        CategoryCode = Clean(content.CategoryCode);
        Priority = content.Priority;
        Confidential = content.Confidential;
        PlannedStart = content.PlannedStart;
        DueDate = content.DueDate;
        EstimatedHours = content.EstimatedHours;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A period of work on a task (<c>TS-NNNNNNN</c>): opened on accept / start / return, closed on pause (hold reason), submit, cancel.</summary>
[Audited(EntityType)]
[AuditParent(TaskItem.EntityType, nameof(TaskId))]
public sealed class TimesheetPeriod : AuditableEntity
{
    public const string EntityType = "tasks.timesheet_period";

    private TimesheetPeriod()
    {
    }

    public string? Number { get; private set; }

    public Guid TaskId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public long? DurationSeconds { get; private set; }

    public string? HoldReason { get; private set; }

    /// <summary>The action that closed the period (pause, submit, complete, cancel, reassign).</summary>
    public string? EndEvent { get; private set; }

    public static TimesheetPeriod Open(Guid taskId, DateTimeOffset at) => new() { TaskId = taskId, StartedAt = at };

    public void SetNumber(string number) => Number ??= number;

    public void Close(DateTimeOffset at, string? holdReason, string endEvent)
    {
        EndedAt = at < StartedAt ? StartedAt : at;
        DurationSeconds = (long)(EndedAt.Value - StartedAt).TotalSeconds;
        HoldReason = holdReason;
        EndEvent = endEvent;
    }
}
