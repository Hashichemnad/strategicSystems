using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

/// <summary>One task to insert (one per assignee of a multi-assignment).</summary>
internal sealed record NewTaskSpec(
    TaskContent Content, Guid CreatorEmployeeId, Guid? CreatorUserId, Guid? EnteredByEmployeeId, Guid AssigneeEmployeeId, Guid? AssigneeUnitId, bool Draft,
    Guid? GroupId = null, Guid? ParentTaskId = null, IReadOnlyList<string>? Checklist = null, IReadOnlyList<Guid>? DependsOn = null, RecurrenceRule? Recurrence = null,
    Guid? RecurrenceSourceId = null);

/// <summary>
/// Inserts tasks inside the caller's open transaction: number, deadlines, record roles, link row, checklist, dependencies, timeline entry
/// and the assignee's notification (one place, used by New Task, sub-tasks, recurrence and AI drafts the user confirmed).
/// </summary>
internal sealed class TaskCreator(
    TasksDbContext db, INumberingService numbering, TaskDeadlines deadlines, IRecordRoleService recordRoles, ILinkStore linkStore, TaskNotifier notifier,
    IClock clock)
{
    public async Task<TaskItem> CreateAsync(NewTaskSpec spec, TaskSettings settings, Guid? actorEmployeeId, Guid? actorUserId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var number = await numbering.NextAsync(db, TaskPermissions.TaskNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false);
        var task = TaskItem.Create(spec.Content, number, spec.CreatorEmployeeId, spec.CreatorUserId, spec.EnteredByEmployeeId, spec.AssigneeEmployeeId,
            spec.AssigneeUnitId, spec.Draft, now, spec.GroupId, spec.ParentTaskId);
        if (spec.RecurrenceSourceId is { } source)
        {
            task.MarkOccurrence(source);
        }

        if (spec.Recurrence is { } rule && (spec.Content.PlannedStart ?? spec.Content.DueDate) is { } anchor)
        {
            task.SetRecurrence(rule.ToJson(), rule.Next(anchor, 2));
        }

        await deadlines.ApplyAsync(task, settings, ct).ConfigureAwait(false);
        await deadlines.ApplyAcceptReminderAsync(task, settings, ct).ConfigureAwait(false);
        db.Tasks.Add(task);

        var sort = 0;
        foreach (var text in spec.Checklist ?? [])
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                db.Checklist.Add(ChecklistItem.Create(task.Id, text, sort += 10));
            }
        }

        foreach (var predecessor in (spec.DependsOn ?? []).Distinct())
        {
            db.Dependencies.Add(TaskDependency.Create(task.Id, predecessor));
        }

        db.Events.Add(TaskEvent.Create(task.Id, now, spec.Draft ? TaskEvents.Drafted : TaskEvents.Created, null, task.Status, actorEmployeeId, actorUserId,
            data: spec.GroupId is { } g ? new { groupId = g } : null));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await GrantAsync(task, TaskPermissions.CreatorRole, spec.CreatorEmployeeId, ct).ConfigureAwait(false);
        await GrantAsync(task, TaskPermissions.AssigneeRole, spec.AssigneeEmployeeId, ct).ConfigureAwait(false);
        if (spec.EnteredByEmployeeId is { } enteredBy)
        {
            await GrantAsync(task, TaskPermissions.EnteredByRole, enteredBy, ct).ConfigureAwait(false);
        }

        if (task.LinkedType is { } type && task.LinkedId is { } linkedId)
        {
            await linkStore.AddAsync(LinkCapabilities.TaskAble, new LinkRef(TaskPermissions.ResourceType, task.Id), new LinkRef(type, linkedId), db, ct).ConfigureAwait(false);
        }

        if (!spec.Draft && spec.AssigneeEmployeeId != spec.CreatorEmployeeId)
        {
            await notifier.SendAsync(TaskNotifications.Assigned, task, [spec.AssigneeEmployeeId], await notifier.NameOfAsync(spec.CreatorEmployeeId, ct).ConfigureAwait(false),
                null, db, ct).ConfigureAwait(false);
        }

        return task;
    }

    public Task GrantAsync(TaskItem task, string role, Guid employeeId, CancellationToken ct) =>
        recordRoles.GrantInTransactionAsync(db, TaskPermissions.ResourceType, task.Id, RecordPrincipalKinds.Employee, employeeId, role, ct);

    public Task RevokeAsync(TaskItem task, string role, Guid employeeId, CancellationToken ct) =>
        recordRoles.RevokeInTransactionAsync(db, TaskPermissions.ResourceType, task.Id, RecordPrincipalKinds.Employee, employeeId, role, ct);

    /// <summary>Numbers new timesheet periods (TS-NNNNNNN) inside the open transaction.</summary>
    public async Task NumberPeriodsAsync(TaskItem task, CancellationToken ct)
    {
        foreach (var period in task.Periods.Where(p => p.Number is null))
        {
            period.SetNumber(await numbering.NextAsync(db, TaskPermissions.TimesheetNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false));
        }
    }
}
