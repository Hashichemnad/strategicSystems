using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Tasks.Domain;

public static class GroupCompletionRules
{
    public const string All = "all";
    public const string Any = "any";

    public static readonly IReadOnlyList<string> Codes = [All, Any];
}

/// <summary>Ties the tasks of one multi-assignment (SPEC §11.4).</summary>
[Audited(EntityType)]
public sealed class AssignmentGroup : AuditableEntity
{
    public const string EntityType = "tasks.assignment_group";

    private AssignmentGroup()
    {
    }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public Guid CreatorEmployeeId { get; private set; }

    public Guid? CreatorUserId { get; private set; }

    public string CompletionRule { get; private set; } = GroupCompletionRules.All;

    public bool Confidential { get; private set; }

    public static AssignmentGroup Create(string? titleAr, string? titleEn, Guid creatorEmployeeId, Guid? creatorUserId, string completionRule, bool confidential) =>
        new()
        {
            TitleAr = titleAr?.Trim(),
            TitleEn = titleEn?.Trim(),
            CreatorEmployeeId = creatorEmployeeId,
            CreatorUserId = creatorUserId,
            CompletionRule = completionRule,
            Confidential = confidential,
        };

    /// <summary>"Finished" for a member: approved (Full) or done (Simple). Cancelled members do not count either way.</summary>
    public static bool IsFinished(string status) => status is TaskStatuses.Approved or TaskStatuses.Done;

    /// <summary>Group complete: all non-cancelled members finished (at least one), or any one finished.</summary>
    public static bool IsComplete(string rule, IReadOnlyCollection<string> memberStatuses)
    {
        var active = memberStatuses.Where(s => s != TaskStatuses.Cancelled).ToList();
        return rule == GroupCompletionRules.Any
            ? active.Any(IsFinished)
            : active.Count > 0 && active.All(IsFinished);
    }
}

public static class CommentKinds
{
    public const string Comment = "comment";
    public const string GroupMessage = "group-message";
}

/// <summary>A comment / communication on a task with @mentions and attachments (task files by id).</summary>
[Audited(EntityType)]
[AuditParent(TaskItem.EntityType, nameof(TaskId))]
public sealed class TaskComment : AuditableEntity
{
    public const string EntityType = "tasks.comment";
    public const int TextMaxLength = 4000;

    private TaskComment()
    {
    }

    public Guid TaskId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public string Kind { get; private set; } = CommentKinds.Comment;

    public Guid[] Mentions { get; private set; } = [];

    public Guid[] FileIds { get; private set; } = [];

    public Guid? AuthorEmployeeId { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public static TaskComment Create(Guid taskId, string text, string kind, IEnumerable<Guid> mentions, IEnumerable<Guid> fileIds, Guid? authorEmployeeId, Guid? authorUserId) =>
        new()
        {
            TaskId = taskId,
            Text = text.Trim(),
            Kind = kind,
            Mentions = [.. mentions.Distinct()],
            FileIds = [.. fileIds.Distinct()],
            AuthorEmployeeId = authorEmployeeId,
            AuthorUserId = authorUserId,
        };
}

/// <summary>A follow-up employee (SPEC §11.2): may read the task, comment and (when allowed) upload. Removed, never deleted.</summary>
[Audited(EntityType)]
[AuditParent(TaskItem.EntityType, nameof(TaskId))]
public sealed class FollowUp : AuditableEntity
{
    public const string EntityType = "tasks.follow_up";

    private FollowUp()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid? AddedByEmployeeId { get; private set; }

    public bool CanUpload { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset? RemovedAt { get; private set; }

    public static FollowUp Create(Guid taskId, Guid employeeId, Guid? addedBy, bool canUpload) =>
        new() { TaskId = taskId, EmployeeId = employeeId, AddedByEmployeeId = addedBy, CanUpload = canUpload };

    public void Remove(DateTimeOffset at)
    {
        Active = false;
        RemovedAt = at;
    }
}

[Audited(EntityType)]
[AuditParent(TaskItem.EntityType, nameof(TaskId))]
public sealed class ChecklistItem : AuditableEntity
{
    public const string EntityType = "tasks.checklist_item";
    public const int TextMaxLength = 500;

    private ChecklistItem()
    {
    }

    public Guid TaskId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public int Sort { get; private set; }

    public bool Done { get; private set; }

    public Guid? DoneByEmployeeId { get; private set; }

    public DateTimeOffset? DoneAt { get; private set; }

    public bool Active { get; private set; } = true;

    public static ChecklistItem Create(Guid taskId, string text, int sort) => new() { TaskId = taskId, Text = text.Trim(), Sort = sort };

    public void Update(string? text, bool? done, Guid? by, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Text = text.Trim();
        }

        if (done is { } value && value != Done)
        {
            Done = value;
            DoneByEmployeeId = value ? by : null;
            DoneAt = value ? now : null;
        }
    }

    public void Remove() => Active = false;
}

/// <summary>Finish-to-start dependency (ADR 0038): <see cref="TaskId"/> waits for <see cref="DependsOnTaskId"/>.</summary>
[Audited(EntityType)]
[AuditParent(TaskItem.EntityType, nameof(TaskId))]
public sealed class TaskDependency : AuditableEntity
{
    public const string EntityType = "tasks.dependency";

    private TaskDependency()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid DependsOnTaskId { get; private set; }

    public bool Active { get; private set; } = true;

    public static TaskDependency Create(Guid taskId, Guid dependsOn) => new() { TaskId = taskId, DependsOnTaskId = dependsOn };

    public void Remove() => Active = false;

    /// <summary>Would adding <paramref name="taskId"/> → <paramref name="dependsOn"/> close a cycle in <paramref name="edges"/> (task → predecessors)?</summary>
    public static bool CreatesCycle(Guid taskId, Guid dependsOn, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> edges)
    {
        if (taskId == dependsOn)
        {
            return true;
        }

        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>([dependsOn]);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == taskId)
            {
                return true;
            }

            if (seen.Add(current) && edges.TryGetValue(current, out var next))
            {
                foreach (var n in next)
                {
                    stack.Push(n);
                }
            }
        }

        return false;
    }
}

/// <summary>A timeline entry (append-only): what happened, from which status to which, by whom, with the comment or reason.</summary>
[NotAudited]
public sealed class TaskEvent : Entity
{
    private TaskEvent()
    {
    }

    public Guid TaskId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Event { get; private set; } = string.Empty;

    public string? FromStatus { get; private set; }

    public string? ToStatus { get; private set; }

    public Guid? ActorEmployeeId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Comment { get; private set; }

    public string? Data { get; private set; }

    public static TaskEvent Create(
        Guid taskId, DateTimeOffset at, string @event, string? from, string? to, Guid? actorEmployeeId, Guid? actorUserId, string? comment = null, object? data = null) =>
        new()
        {
            TaskId = taskId,
            OccurredAt = at,
            Event = @event,
            FromStatus = from,
            ToStatus = to,
            ActorEmployeeId = actorEmployeeId,
            ActorUserId = actorUserId,
            Comment = comment,
            Data = data is null ? null : JsonSerializer.Serialize(data, JsonSerializerOptions.Web),
        };
}

public static class TaskEvents
{
    public const string Created = "created";
    public const string Drafted = "drafted";
    public const string SubtaskCreated = "subtask-created";
    public const string DueChanged = "due-changed";
    public const string Edited = "edited";
    public const string FollowUpAdded = "follow-up-added";
    public const string FollowUpRemoved = "follow-up-removed";
    public const string Reminded = "reminded";
    public const string AcceptReminded = "accept-reminded";
    public const string Overdue = "overdue";
    public const string Escalated = "escalated";
    public const string GroupMessage = "group-message";
    public const string Transferred = "transferred";
    public const string ReturnedToCreator = "returned-to-creator";
}
