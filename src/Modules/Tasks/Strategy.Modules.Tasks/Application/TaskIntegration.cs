using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Contracts;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Tasks.Application;

/// <summary>"Next action required + who" for a task (current-status engine), with the current user's actions from the transition table.</summary>
internal sealed class TaskCurrentStatus(TaskVisibility visibility, TaskReader reader, IRequestContext context) : ICurrentStatusProvider
{
    public string ResourceType => TaskPermissions.ResourceType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var user = context.User;
        var task = await visibility.FindAsync(resourceId, user, tracked: false, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return null;
        }

        var people = await reader.PeopleAsync([task.AssigneeEmployeeId, task.CreatorEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName Name(Guid id) => new(null, people.GetValueOrDefault(id)?.NameAr ?? people.GetValueOrDefault(id)?.NameEn,
            people.GetValueOrDefault(id)?.NameEn ?? people.GetValueOrDefault(id)?.NameAr);
        var (isCreator, isAssignee) = TaskReader.Roles(task, user);
        var actions = TaskWorkflow.Allowed(task.Template, task.Status, isCreator, isAssignee)
            .Select(a => new CurrentStatusAction(a, $"tasks.actions.{a}", a switch
            {
                TaskActions.Approve or TaskActions.Accept or TaskActions.Submit or TaskActions.Complete or TaskActions.Start or TaskActions.Assign => "primary",
                TaskActions.Reject or TaskActions.Cancel => "danger",
                _ => "secondary",
            }))
            .ToList();
        var empty = new Dictionary<string, BilingualText>();
        var (key, tone, actor, actors) = task.Status switch
        {
            TaskStatuses.Draft => ("tasks.currentStatus.draft", "neutral", "tasks.currentStatus.creator", new[] { Name(task.CreatorEmployeeId) }),
            TaskStatuses.Assigned => ("tasks.currentStatus.assigned", "info", "tasks.currentStatus.assignee", new[] { Name(task.AssigneeEmployeeId) }),
            TaskStatuses.ToDo => ("tasks.currentStatus.toDo", "info", "tasks.currentStatus.assignee", new[] { Name(task.AssigneeEmployeeId) }),
            TaskStatuses.InProgress => ("tasks.currentStatus.inProgress", "info", "tasks.currentStatus.assignee", new[] { Name(task.AssigneeEmployeeId) }),
            TaskStatuses.OnHold => ("tasks.currentStatus.onHold", "warning", "tasks.currentStatus.assignee", new[] { Name(task.AssigneeEmployeeId) }),
            TaskStatuses.Submitted => ("tasks.currentStatus.submitted", "info", "tasks.currentStatus.creator", new[] { Name(task.CreatorEmployeeId) }),
            TaskStatuses.Rejected => ("tasks.currentStatus.rejected", "danger", "tasks.currentStatus.creator", new[] { Name(task.CreatorEmployeeId) }),
            TaskStatuses.Approved => ("tasks.currentStatus.approved", "success", (string?)null, Array.Empty<PersonName>()),
            TaskStatuses.Done => ("tasks.currentStatus.done", "success", null, []),
            _ => ("tasks.currentStatus.cancelled", "neutral", null, []),
        };
        var parameters = task.Status switch
        {
            TaskStatuses.OnHold => new Dictionary<string, BilingualText>
            {
                ["reason"] = new(task.Periods.Where(p => p.HoldReason != null).OrderBy(p => p.EndedAt).LastOrDefault()?.HoldReason,
                    task.Periods.Where(p => p.HoldReason != null).OrderBy(p => p.EndedAt).LastOrDefault()?.HoldReason),
            },
            TaskStatuses.Rejected => new Dictionary<string, BilingualText> { ["reason"] = new(task.RejectionReason, task.RejectionReason) },
            _ => empty,
        };
        return new CurrentStatus(key, parameters, tone, actor, actors, actions.Where(a => a.Code != TaskActions.CreateSubtask).ToList(), task.Status);
    }
}

/// <summary>My Work → Tasks (SPEC §0.4 level 1): assigned to me needing my action, created by me awaiting my approval, overdue.</summary>
internal sealed class TaskMyWork(TaskVisibility visibility, IClock clock) : IMyWorkSource
{
    public string SystemCode => TaskPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } employee)
        {
            return [];
        }

        var userId = user.UserId() ?? Guid.Empty;
        var now = clock.UtcNow;
        var tasks = await (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => (t.AssigneeEmployeeId == employee && (t.Status == TaskStatuses.Assigned || t.Status == TaskStatuses.ToDo || t.Status == TaskStatuses.InProgress
                    || t.Status == TaskStatuses.OnHold))
                || ((t.CreatorEmployeeId == employee || t.CreatorUserId == userId) && (t.Status == TaskStatuses.Submitted || t.Status == TaskStatuses.Rejected)))
            .OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).Take(50)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return tasks.Select(t => new MyWorkItem(
            TaskPermissions.SystemCode, TaskPermissions.ResourceType, t.Id, t.Number, t.TitleAr, t.TitleEn,
            t.AssigneeEmployeeId != employee ? (t.Status == TaskStatuses.Rejected ? "tasks.myWork.rejected" : "tasks.myWork.awaitingApproval")
                : TaskIndicators.Of(t.Status, t.DueAt, t.DueSoonAt, now) == TaskIndicators.Overdue ? "tasks.myWork.overdue"
                : t.Status is TaskStatuses.Assigned ? "tasks.myWork.toAccept" : "tasks.myWork.assigned",
            $"/tasks/{t.Id:D}", t.AssignedAt ?? t.CreatedAt, t.DueDate)).ToList();
    }
}

/// <summary>Task attachments (purpose <c>tasks.attachment</c>): read with the task, upload with the upload permission while the task is open.</summary>
internal sealed class TaskFiles(TaskVisibility visibility, TasksDbContext db) : IFileOwnerAccess
{
    public static readonly FilePurpose Purpose = new("tasks.attachment", TaskPermissions.ResourceType, TaskPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public string OwnerType => TaskPermissions.ResourceType;

    public string AttachmentPurpose => Purpose.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await LoadAsync(user, ownerId, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await LoadAsync(user, ownerId, cancellationToken).ConfigureAwait(false) is { IsOpen: true } task
        && await visibility.HasAsync(task, user, TaskPermissions.Upload, cancellationToken).ConfigureAwait(false);

    private async Task<TaskItem?> LoadAsync(ClaimsPrincipal user, string ownerId, CancellationToken ct)
    {
        if (!Guid.TryParse(ownerId, out var id))
        {
            return null;
        }

        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        return task is not null && await visibility.CanReadAsync(task, user, ct).ConfigureAwait(false) ? task : null;
    }
}

/// <summary>Tasks as link sources (the "from" side of <c>/api/v1/links/to/…</c>): badge with status for readers only.</summary>
internal sealed class TaskLinkTargets(TaskVisibility visibility) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        TaskPermissions.ResourceType, TaskPermissions.SystemCode, "مهمة", "Task", [], TaskPermissions.Read, "/tasks/{id}");

    public string ResourceType => TaskPermissions.ResourceType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var tasks = await (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return tasks.ToDictionary(t => t.Id, t => new LinkTarget(t.Id, t.Number, t.TitleAr, t.TitleEn, t.Status, null, null, ToneOf(t.Status), TaskStatuses.IsClosed(t.Status)));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await visibility.FindAsync(id, user, tracked: false, cancellationToken).ConfigureAwait(false) is { } task
        && await visibility.HasAsync(task, user, permission, cancellationToken).ConfigureAwait(false);

    public static string ToneOf(string status) => status switch
    {
        TaskStatuses.Approved or TaskStatuses.Done => "success",
        TaskStatuses.Rejected => "danger",
        TaskStatuses.OnHold => "warning",
        TaskStatuses.Cancelled or TaskStatuses.Draft => "neutral",
        _ => "info",
    };
}

/// <summary>Global search, similar records and "Ask the system" over tasks — only what <see cref="TaskVisibility"/> allows (confidential included).</summary>
internal sealed class TaskSearchSource(TaskVisibility visibility, IRequestContext context) : IRecordSearchSource
{
    public string ResourceType => TaskPermissions.ResourceType;

    public string SystemCode => TaskPermissions.SystemCode;

    public string ReadPermission => TaskPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = "%" + ListTasksHandler.EscapeLike(text.Trim()) + "%";
        var tasks = await (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => EF.Functions.ILike(t.TitleAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.TitleEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(t.Number ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.Description ?? string.Empty, pattern, "\\"))
            .OrderByDescending(t => t.UpdatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return tasks.Select(t => Hit(t, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var query = (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => excludeId == null || t.Id != excludeId);
        var hits = await query
            .Select(t => new { Task = t, Score = Math.Max(EF.Functions.TrigramsSimilarity(t.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(t.TitleAr ?? string.Empty, text)) })
            .Where(x => x.Score > 0.2)
            .OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        _ = context;
        return hits.Select(h => Hit(h.Task, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(TaskItem t, double score) =>
        new(TaskPermissions.ResourceType, t.Id, TaskPermissions.SystemCode, t.Number, t.TitleAr, t.TitleEn, t.Status, $"/tasks/{t.Id:D}", score);
}

/// <summary>A task's content for the approver brief, <c>get_record</c> and Ask (null outside <see cref="TaskVisibility"/>).</summary>
internal sealed class TaskRecordSource(TaskVisibility visibility, TaskReader reader) : IAiRecordSource
{
    public string ResourceType => TaskPermissions.ResourceType;

    public async Task<AiRecordContent?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var task = await visibility.FindAsync(id, user, tracked: false, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return null;
        }

        var people = await reader.PeopleAsync([task.AssigneeEmployeeId, task.CreatorEmployeeId], cancellationToken).ConfigureAwait(false);
        string? Name(Guid e) => people.GetValueOrDefault(e)?.NameEn ?? people.GetValueOrDefault(e)?.NameAr;
        return new AiRecordContent(TaskPermissions.ResourceType, task.Id, TaskPermissions.SystemCode, TaskItem.EntityType, task.Number, task.TitleAr, task.TitleEn,
            $"/tasks/{task.Id:D}",
            [
                new("TitleAr", "العنوان بالعربية", "Title (Arabic)", task.TitleAr),
                new("TitleEn", "العنوان بالإنجليزية", "Title (English)", task.TitleEn),
                new("Description", "الوصف", "Description", task.Description),
                new("Status", "الحالة", "Status", task.Status),
                new("Priority", "الأولوية", "Priority", task.Priority),
                new("DueDate", "تاريخ الاستحقاق", "Due date", task.DueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                new("Assignee", "المكلف", "Assignee", Name(task.AssigneeEmployeeId)),
                new("Creator", "المنشئ", "Creator", Name(task.CreatorEmployeeId)),
            ],
            new[] { Name(task.AssigneeEmployeeId), Name(task.CreatorEmployeeId) }.OfType<string>().ToList());
    }
}

/// <summary><see cref="ITaskDirectory"/> for other modules (counts only: a count reveals no content).</summary>
internal sealed class TaskDirectory(TasksDbContext db, IClock clock) : ITaskDirectory
{
    public async Task<LinkedTaskCounts> CountLinkedAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tasks = db.Tasks.AsNoTracking().Where(t => t.LinkedType == resourceType && t.LinkedId == resourceId);
        return new LinkedTaskCounts(
            await tasks.CountAsync(t => !TaskStatuses.Closed.Contains(t.Status), cancellationToken).ConfigureAwait(false),
            await tasks.CountAsync(t => !TaskStatuses.Closed.Contains(t.Status) && t.DueAt != null && t.DueAt < now, cancellationToken).ConfigureAwait(false),
            await tasks.CountAsync(cancellationToken).ConfigureAwait(false));
    }
}
