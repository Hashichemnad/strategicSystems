using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Tasks.Application;

/// <summary>
/// Tasks → Settings → Responsibilities (ADR 0168): a thin adapter over the existing <see cref="ReassignEmployeeTasks"/> command (ADR 0040), run
/// once per current assignee with the selected tasks: statuses, deadlines, timeline, record roles, audit and its own notices (the new assignee,
/// the creators and the previous assignee) stay the command's. The caller also needs <c>tasks.reassignment.manage</c> over the assignee.
/// Confidential tasks are listed without number or title when the caller may not read them.
/// </summary>
internal sealed class TaskAssigneeResponsibility(
    TasksDbContext db, IAccessService access, TaskVisibility visibility, IEmployeeDirectory employees, IDispatcher dispatcher, IResponsibilityNotifier notifier)
    : ResponsibilitySource<TaskItem>(notifier)
{
    public override string SystemCode => TaskPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("task-assignee", "المكلّف بالمهمة المفتوحة", "Open task assignee", Statuses:
    [
        new(TaskStatuses.Draft, "مسودة", "Draft"), new(TaskStatuses.Assigned, "مسندة", "Assigned"), new(TaskStatuses.InProgress, "قيد التنفيذ", "In progress"),
        new(TaskStatuses.OnHold, "معلّقة", "On hold"), new(TaskStatuses.Submitted, "مقدّمة", "Submitted"), new(TaskStatuses.Rejected, "مرفوضة", "Rejected"),
        new(TaskStatuses.ToDo, "للتنفيذ", "To do"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tasks = (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false)).Where(t => !TaskStatuses.Closed.Contains(t.Status));
        if (query.PersonId is { } person)
        {
            tasks = tasks.Where(t => t.AssigneeEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            tasks = tasks.Where(t => t.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            tasks = tasks.Where(t => t.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            // Confidential tasks never match on their text (their titles are not shown either).
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            tasks = tasks.Where(t => !t.Confidential && EF.Functions.ILike((t.Number ?? "") + " " + (t.TitleAr ?? "") + " " + (t.TitleEn ?? ""), pattern, "\\"));
        }

        var total = await tasks.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await tasks.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ResponsibilityRow>();
        foreach (var task in rows)
        {
            var row = Describe(task);
            result.Add(await visibility.CanReadAsync(task, user, cancellationToken).ConfigureAwait(false) ? row : row with { Number = null, TitleAr = null, TitleEn = null });
        }

        return new ResponsibilityPage(result, total);
    }

    protected override async Task<IReadOnlyList<TaskItem>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false)).Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(TaskItem record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.TitleAr, record.TitleEn, record.Status, record.AssigneeEmployeeId, record.UnitId, $"/tasks/{record.Id:D}");
    }

    protected override async Task<ResponsibilityOutcome> CheckAsync(TaskItem record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var shown = await visibility.CanReadAsync(record, user, cancellationToken).ConfigureAwait(false) ? row : row with { Number = null, TitleAr = null, TitleEn = null };
        if (TaskStatuses.Closed.Contains(record.Status))
        {
            return Blocked(shown, ResponsibilityMessages.NotEditable);
        }

        var assignee = await employees.GetAsync(record.AssigneeEmployeeId, cancellationToken).ConfigureAwait(false);
        var allowed = assignee is not null && await access.CanAsync(user, TaskPermissions.ReassignmentManage,
            new ResourceRef("org.employee", assignee.Id, OwnerEmployeeId: assignee.Id, UnitId: assignee.UnitId), cancellationToken).ConfigureAwait(false);
        return allowed ? Allowed(shown) : Blocked(shown, ResponsibilityMessages.Forbidden);
    }

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<TaskItem> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var outcomes = new List<ResponsibilityOutcome>();
        foreach (var group in batch.Records.GroupBy(t => t.AssigneeEmployeeId))
        {
            var ids = group.Select(t => t.Id).ToList();
            var done = await dispatcher.SendAsync(new ReassignEmployeeTasks(group.Key, ReassignmentModes.ToEmployee, batch.NewId, null, batch.Reason, ids), cancellationToken)
                .ConfigureAwait(false);
            foreach (var task in group)
            {
                var check = batch.OutcomeOf(task);
                outcomes.Add(done.IsSuccess && done.Value.TaskIds.Contains(task.Id)
                    ? check with { Outcome = ResponsibilityOutcomes.Moved, MessageKey = null }
                    : check with { Outcome = ResponsibilityOutcomes.Skipped, MessageKey = done.IsFailure ? done.Error.MessageKey : ResponsibilityMessages.ChangedMeanwhile });
            }
        }

        return outcomes;
    }

    private async Task<IQueryable<TaskItem>> ScopedAsync(ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Tasks : db.Tasks.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), TaskScope.Map, ct)
            .ConfigureAwait(false);
}
