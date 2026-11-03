using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

/// <summary>Builds task DTOs with people's names, the indicator and the actions the current user may take.</summary>
internal sealed class TaskReader(
    TasksDbContext db, IEmployeeDirectory employees, TaskVisibility visibility, ILinkRegistry links, ISettingsStore settings, IClock clock)
{
    public async Task<IReadOnlyList<TaskListItemDto>> ListItemsAsync(IReadOnlyList<TaskItem> tasks, ClaimsPrincipal user, CancellationToken ct)
    {
        var people = await PeopleAsync(tasks.SelectMany(t => new[] { t.AssigneeEmployeeId, t.CreatorEmployeeId }), ct).ConfigureAwait(false);
        return tasks.Select(t => ListItem(t, people, user)).ToList();
    }

    public TaskListItemDto ListItem(TaskItem t, IReadOnlyDictionary<Guid, EmployeeReference> people, ClaimsPrincipal user)
    {
        var (isCreator, isAssignee) = Roles(t, user);
        return new TaskListItemDto(
            t.Id, t.Number, t.TitleAr, t.TitleEn, t.Template, t.Status, t.Priority, TaskIndicators.Of(t.Status, t.DueAt, t.DueSoonAt, clock.UtcNow), t.TypeCode,
            t.SourceCode, t.CategoryCode, t.Confidential, Person(t.AssigneeEmployeeId, people), Person(t.CreatorEmployeeId, people), t.PlannedStart, t.DueDate,
            t.DueAt, t.GroupId, t.ParentTaskId, t.LinkedType, t.LinkedId, t.PercentComplete, t.CreatedAt, t.UpdatedAt, t.Version,
            TaskWorkflow.Allowed(t.Template, t.Status, isCreator, isAssignee).Where(a => a != TaskActions.CreateSubtask).ToList());
    }

    public async Task<TaskDto> DetailAsync(TaskItem t, ClaimsPrincipal user, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var (isCreator, isAssignee) = Roles(t, user);
        var checklist = await db.Checklist.AsNoTracking().Where(c => c.TaskId == t.Id && c.Active).OrderBy(c => c.Sort).ThenBy(c => c.CreatedAt)
            .Select(c => new ChecklistItemDto(c.Id, c.Text, c.Sort, c.Done, c.DoneAt)).ToListAsync(ct).ConfigureAwait(false);
        var followUps = await db.FollowUps.AsNoTracking().Where(f => f.TaskId == t.Id && f.Active).OrderBy(f => f.CreatedAt).ToListAsync(ct).ConfigureAwait(false);

        var visible = await visibility.QueryAsync(user, ct).ConfigureAwait(false);
        var subtasks = await visible.Where(s => s.ParentTaskId == t.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var predecessorIds = await db.Dependencies.AsNoTracking().Where(d => d.TaskId == t.Id && d.Active).Select(d => d.DependsOnTaskId).ToListAsync(ct).ConfigureAwait(false);
        var predecessors = await db.Tasks.AsNoTracking().Where(p => predecessorIds.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false);
        var readable = (await visible.Where(p => predecessorIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        TaskRefDto? parent = null;
        if (t.ParentTaskId is { } parentId && await visible.FirstOrDefaultAsync(p => p.Id == parentId, ct).ConfigureAwait(false) is { } p)
        {
            parent = Ref(p, true);
        }

        GroupSummaryDto? group = null;
        if (t.GroupId is { } groupId && await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct).ConfigureAwait(false) is { } g)
        {
            var statuses = await db.Tasks.AsNoTracking().Where(m => m.GroupId == groupId).Select(m => m.Status).ToListAsync(ct).ConfigureAwait(false);
            group = new GroupSummaryDto(g.Id, g.TitleAr, g.TitleEn, statuses.Count(s => s != TaskStatuses.Cancelled), statuses.Count(AssignmentGroup.IsFinished),
                AssignmentGroup.IsComplete(g.CompletionRule, statuses));
        }

        LinkView? link = null;
        if (t.LinkedType is { } type && t.LinkedId is { } linkedId)
        {
            link = (await links.ResolveAsync([new LinkRef(type, linkedId)], user, ct).ConfigureAwait(false))[0];
        }

        var people = await PeopleAsync(
            new[] { t.AssigneeEmployeeId, t.CreatorEmployeeId }.Concat(followUps.Select(f => f.EmployeeId)).Concat(subtasks.SelectMany(s => new[] { s.AssigneeEmployeeId, s.CreatorEmployeeId }))
                .Concat(t.EnteredByEmployeeId is { } e ? [e] : []), ct).ConfigureAwait(false);
        var open = t.IsOpen;
        var canComment = await visibility.HasAsync(t, user, TaskPermissions.Comment, ct).ConfigureAwait(false);
        var canUpload = open && await visibility.HasAsync(t, user, TaskPermissions.Upload, ct).ConfigureAwait(false);
        var canFollowUps = open && await visibility.HasAsync(t, user, TaskPermissions.FollowUp, ct).ConfigureAwait(false);
        var feedbackMax = (await settings.GetAsync<TaskSettings>(ct).ConfigureAwait(false)).FeedbackScaleMax;
        var rule = RecurrenceRule.FromJson(t.Recurrence);
        var actual = t.ActualSeconds + (t.OpenPeriod is { } running ? (long)(now - running.StartedAt).TotalSeconds : 0);
        var actions = TaskWorkflow.Allowed(t.Template, t.Status, isCreator, isAssignee);
        return new TaskDto(
            t.Id, t.Number, t.TitleAr, t.TitleEn, t.Description, t.Template, t.Status, t.Priority, TaskIndicators.Of(t.Status, t.DueAt, t.DueSoonAt, now), t.TypeCode,
            t.SourceCode, t.CategoryCode, t.Confidential, Person(t.AssigneeEmployeeId, people), Person(t.CreatorEmployeeId, people),
            t.EnteredByEmployeeId is { } entered ? Person(entered, people) : null, t.PlannedStart, t.DueDate, t.DueAt, t.DueSoonAt, t.AssignedAt, t.AcceptedAt,
            t.CompletedAt, t.ApprovedAt, t.ClosedAt, t.RejectionReason, t.CancelReason, t.CompletionComment, t.FeedbackPoint, t.FeedbackComment, t.PercentComplete,
            t.EstimatedHours, actual, t.TimeToAcceptSeconds, t.TimeToCompleteSeconds, t.CompletedOnTime,
            rule is null ? null : new RecurrenceDto(rule.Frequency, rule.Interval, rule.Until, rule.Count, t.NextOccurrenceOn, t.OccurrenceCount),
            t.RecurrenceSourceId, link, parent, group, checklist, subtasks.Select(s => ListItem(s, people, user)).ToList(),
            predecessors.Select(d => Ref(d, readable.Contains(d.Id))).ToList(),
            followUps.Select(f => new FollowUpDto(f.EmployeeId, people.GetValueOrDefault(f.EmployeeId)?.NameAr, people.GetValueOrDefault(f.EmployeeId)?.NameEn, f.CanUpload, f.CreatedAt)).ToList(),
            actions.Where(a => a != TaskActions.CreateSubtask).ToList(),
            new TaskCapabilitiesDto(open && isCreator, canComment, canUpload, canFollowUps, open && (isCreator || isAssignee), open && isCreator,
                actions.Contains(TaskActions.CreateSubtask)),
            feedbackMax, t.CreatedAt, t.UpdatedAt, t.Version);
    }

    public static (bool IsCreator, bool IsAssignee) Roles(TaskItem t, ClaimsPrincipal user) =>
        (t.IsCreator(user.EmployeeIdOf(), user.UserId()), t.IsAssignee(user.EmployeeIdOf()));

    public async Task<IReadOnlyDictionary<Guid, EmployeeReference>> PeopleAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        await employees.GetManyAsync(ids.Distinct().ToList(), ct).ConfigureAwait(false);

    public static PersonDto Person(Guid id, IReadOnlyDictionary<Guid, EmployeeReference> people) =>
        new(id, people.GetValueOrDefault(id)?.NameAr, people.GetValueOrDefault(id)?.NameEn);

    /// <summary>A reference; titles only when the user may read the task (a predecessor outside the scope shows only its status).</summary>
    public static TaskRefDto Ref(TaskItem t, bool readable) =>
        new(t.Id, readable ? t.Number : null, readable ? t.TitleAr : null, readable ? t.TitleEn : null, t.Status, t.Template, TaskStatuses.IsClosed(t.Status));
}
