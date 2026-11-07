using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Workflow.Domain;

/// <summary>JSON options of the rule / custom-action columns (web defaults: camelCase).</summary>
internal static class WorkflowJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}

/// <summary>One saved configuration of a process (ADR 0024). Never edited after it is saved; a change is a new version.</summary>
[Audited(EntityType)]
public sealed class ProcessVersion : AuditableEntity
{
    public const string EntityType = "workflow.process-version";

    private ProcessVersion()
    {
    }

    public string ProcessKey { get; private set; } = string.Empty;

    public int VersionNo { get; private set; }

    public string? Note { get; private set; }

    public List<ProcessStep> Steps { get; private set; } = [];

    public static ProcessVersion Create(string processKey, int versionNo, string? note, IEnumerable<ApprovalStepDefinition> steps)
    {
        var version = new ProcessVersion { ProcessKey = processKey, VersionNo = versionNo, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
        var sort = 1;
        foreach (var step in steps)
        {
            version.Steps.Add(ProcessStep.Create(version.Id, sort++, step));
        }

        return version;
    }

    public IReadOnlyList<ProcessStep> OrderedSteps => [.. Steps.OrderBy(s => s.Sort)];
}

/// <summary>A step of a <see cref="ProcessVersion"/>. Rule and escalation are stored as JSON of <see cref="ApproverRule"/>.</summary>
[Audited(EntityType)]
[AuditParent(ProcessVersion.EntityType, nameof(VersionId))]
public sealed class ProcessStep : Entity
{
    public const string EntityType = "workflow.process-step";

    private ProcessStep()
    {
    }

    public Guid VersionId { get; private set; }

    public int Sort { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string Rule { get; private set; } = "{}";

    public string? Condition { get; private set; }

    public List<string> Actions { get; private set; } = [];

    public string CustomActions { get; private set; } = "[]";

    public bool CommentOnApprove { get; private set; }

    public bool CommentOnReject { get; private set; }

    public bool CommentOnReturn { get; private set; }

    public int? SlaHours { get; private set; }

    public string? Escalation { get; private set; }

    public string OnNoApprover { get; private set; } = NoApproverBehaviours.Block;

    public bool ExcludeSubmitter { get; private set; }

    public string? NotificationTemplate { get; private set; }

    public string SlaMode { get; private set; } = SlaModes.Working;

    public string BusinessHoursSource { get; private set; } = BusinessHoursSources.Default;

    public Guid? BusinessHoursId { get; private set; }

    public static ProcessStep Create(Guid versionId, int sort, ApprovalStepDefinition d) => new()
    {
        VersionId = versionId,
        Sort = sort,
        Code = d.Code.Trim(),
        NameAr = Clean(d.NameAr),
        NameEn = Clean(d.NameEn),
        Rule = WorkflowJson.Write(d.Rule),
        Condition = Clean(d.Condition),
        Actions = [.. d.EffectiveActions],
        CustomActions = WorkflowJson.Write(d.CustomActions ?? []),
        CommentOnApprove = d.CommentOnApprove,
        CommentOnReject = d.CommentOnReject,
        CommentOnReturn = d.CommentOnReturn,
        SlaHours = d.SlaHours,
        Escalation = d.Escalation is null ? null : WorkflowJson.Write(d.Escalation),
        OnNoApprover = d.OnNoApprover,
        ExcludeSubmitter = d.ExcludeSubmitter,
        NotificationTemplate = Clean(d.NotificationTemplate),
        SlaMode = d.SlaMode,
        BusinessHoursSource = d.BusinessHoursSource,
        BusinessHoursId = d.BusinessHoursSource == BusinessHoursSources.Specific ? d.BusinessHoursId : null,
    };

    public ApprovalStepDefinition ToDefinition() => new(
        Code, NameAr, NameEn, WorkflowJson.Read<ApproverRule>(Rule), Condition, Actions,
        WorkflowJson.Read<List<CustomApprovalAction>>(CustomActions), CommentOnApprove, CommentOnReject, CommentOnReturn, SlaHours,
        Escalation is null ? null : WorkflowJson.Read<ApproverRule>(Escalation), OnNoApprover, ExcludeSubmitter, NotificationTemplate,
        SlaMode, BusinessHoursSource, BusinessHoursId);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Instance lifecycle (design note §2): only an instance in progress moves; every other status is final.</summary>
public static class InstanceLifecycle
{
    public static readonly IReadOnlyDictionary<string, string> FinalStatusOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ApprovalActions.Approve] = ApprovalInstanceStatuses.Approved,
        [ApprovalActions.Reject] = ApprovalInstanceStatuses.Rejected,
        [ApprovalActions.Return] = ApprovalInstanceStatuses.Returned,
        ["cancel"] = ApprovalInstanceStatuses.Cancelled,
    };

    /// <summary>The status after <paramref name="transition"/> (<c>approve</c> of the last step, <c>reject</c>, <c>return</c>, <c>cancel</c>), or an error.</summary>
    public static Result<string> Next(string status, string transition)
    {
        if (status != ApprovalInstanceStatuses.InProgress)
        {
            return Error.Rule("workflow.instance.closed", "workflow.errors.instanceClosed");
        }

        return FinalStatusOf.TryGetValue(transition, out var next)
            ? next
            : Error.Rule("workflow.instance.unknownTransition", "workflow.errors.unknownAction");
    }
}

[Audited(EntityType)]
public sealed class ApprovalInstance : AuditableEntity
{
    public const string EntityType = "workflow.instance";

    private ApprovalInstance()
    {
    }

    public string ProcessKey { get; private set; } = string.Empty;

    public Guid VersionId { get; private set; }

    public string ResourceType { get; private set; } = string.Empty;

    public Guid ResourceId { get; private set; }

    public string? SubjectNumber { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string Link { get; private set; } = string.Empty;

    public Guid? SubjectOwnerEmployeeId { get; private set; }

    public Guid? SubjectOwnerUserId { get; private set; }

    public Guid? SubjectUnitId { get; private set; }

    public Guid? SubmitterUserId { get; private set; }

    public Guid? SubmitterEmployeeId { get; private set; }

    public string? SubmitterName { get; private set; }

    /// <summary>Condition variables (built-in and the module's) as submitted, JSON.</summary>
    public string Variables { get; private set; } = "{}";

    public string Status { get; private set; } = ApprovalInstanceStatuses.InProgress;

    /// <summary>Sort of the step in progress (0 before the first step is entered).</summary>
    public int CurrentSort { get; private set; }

    public int Attempt { get; private set; } = 1;

    public Guid? PreviousInstanceId { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? Comment { get; private set; }

    public static ApprovalInstance Create(
        ApprovalProcessDefinition process, Guid versionId, ApprovalSubject subject, Guid? submitterUserId, Guid? submitterEmployeeId, string? submitterName,
        string variablesJson, ApprovalInstance? previous, DateTimeOffset now, string? comment) => new()
        {
            ProcessKey = process.Key,
            VersionId = versionId,
            ResourceType = subject.ResourceType,
            ResourceId = subject.ResourceId,
            SubjectNumber = subject.Number,
            TitleAr = subject.TitleAr,
            TitleEn = subject.TitleEn,
            Link = process.LinkFor(subject.ResourceId),
            SubjectOwnerEmployeeId = subject.OwnerEmployeeId,
            SubjectOwnerUserId = subject.OwnerUserId,
            SubjectUnitId = subject.UnitId,
            SubmitterUserId = submitterUserId,
            SubmitterEmployeeId = submitterEmployeeId,
            SubmitterName = submitterName,
            Variables = variablesJson,
            Attempt = previous is null ? 1 : previous.Attempt + 1,
            PreviousInstanceId = previous?.Id,
            SubmittedAt = now,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        };

    public void EnterStep(int sort) => CurrentSort = sort;

    public Result Finish(string transition, DateTimeOffset now)
    {
        var next = InstanceLifecycle.Next(Status, transition);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        CompletedAt = now;
        return Result.Success();
    }

    /// <summary>The record's owner / unit changed while the approval runs (bulk responsibility reassignment, ADR 0168).</summary>
    public void UpdateSubject(Guid? ownerEmployeeId, Guid? unitId)
    {
        SubjectOwnerEmployeeId = ownerEmployeeId ?? SubjectOwnerEmployeeId;
        SubjectUnitId = unitId ?? SubjectUnitId;
    }

    public ApprovalSubject Subject => new(ResourceType, ResourceId, SubjectNumber, TitleAr, TitleEn, SubjectOwnerEmployeeId, SubjectOwnerUserId, SubjectUnitId);
}

public static class ApprovalTaskStatuses
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

[Audited(EntityType)]
[AuditParent(ApprovalInstance.EntityType, nameof(InstanceId))]
public sealed class ApprovalTask : AuditableEntity
{
    public const string EntityType = "workflow.task";
    public const int CommentMaxLength = 4000;

    private ApprovalTask()
    {
    }

    public Guid InstanceId { get; private set; }

    public int StepSort { get; private set; }

    public string StepCode { get; private set; } = string.Empty;

    public string? StepNameAr { get; private set; }

    public string? StepNameEn { get; private set; }

    public string Status { get; private set; } = ApprovalTaskStatuses.Pending;

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>How the due time was counted (<see cref="SlaModes"/>) and, for working hours, with which business hours (snapshot).</summary>
    public string? SlaMode { get; private set; }

    public Guid? BusinessHoursId { get; private set; }

    public string? BusinessHoursNameAr { get; private set; }

    public string? BusinessHoursNameEn { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Action code taken (approve, reject, return, or a custom action code).</summary>
    public string? Outcome { get; private set; }

    public Guid? ActedByUserId { get; private set; }

    public string? ActedByName { get; private set; }

    public Guid? OnBehalfOfUserId { get; private set; }

    public string? OnBehalfOfName { get; private set; }

    public string? Comment { get; private set; }

    public List<TaskAssignee> Assignees { get; private set; } = [];

    /// <summary>Opens a task; <paramref name="due"/> is computed by the engine through the business-hours calculator (ADR 0027).</summary>
    public static ApprovalTask Open(Guid instanceId, ProcessStep step, DateTimeOffset now, TaskDue? due) => new()
    {
        InstanceId = instanceId,
        StepSort = step.Sort,
        StepCode = step.Code,
        StepNameAr = step.NameAr,
        StepNameEn = step.NameEn,
        OpenedAt = now,
        DueAt = due?.DueAt,
        SlaMode = due?.SlaMode,
        BusinessHoursId = due?.BusinessHoursId,
        BusinessHoursNameAr = due?.NameAr,
        BusinessHoursNameEn = due?.NameEn,
    };

    public IEnumerable<TaskAssignee> ActiveAssignees => Assignees.Where(a => a.Active);

    public Result Complete(string outcome, Guid? actorId, string? actorName, Guid? onBehalfOfId, string? onBehalfOfName, string? comment, DateTimeOffset now)
    {
        if (Status != ApprovalTaskStatuses.Pending)
        {
            return Error.Rule("workflow.task.closed", "workflow.errors.taskClosed");
        }

        Status = ApprovalTaskStatuses.Completed;
        Outcome = outcome;
        ActedByUserId = actorId;
        ActedByName = actorName;
        OnBehalfOfUserId = onBehalfOfId;
        OnBehalfOfName = onBehalfOfId is null ? null : onBehalfOfName;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        CompletedAt = now;
        return Result.Success();
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == ApprovalTaskStatuses.Pending)
        {
            Status = ApprovalTaskStatuses.Cancelled;
            CompletedAt = now;
        }
    }

    public void MarkEscalated(DateTimeOffset now) => EscalatedAt = now;
}

/// <summary>A task's due time and how it was counted.</summary>
public sealed record TaskDue(DateTimeOffset DueAt, string SlaMode, Guid? BusinessHoursId, string? NameAr, string? NameEn);

public static class AssigneeSources
{
    public const string Rule = "rule";
    public const string Escalation = "escalation";
    public const string Transfer = "transfer";
}

[Audited(EntityType)]
[AuditParent(ApprovalTask.EntityType, nameof(TaskId))]
public sealed class TaskAssignee : AuditableEntity
{
    public const string EntityType = "workflow.task-assignee";

    private TaskAssignee()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string? Name { get; private set; }

    public string Source { get; private set; } = AssigneeSources.Rule;

    public bool Active { get; private set; } = true;

    public DateTimeOffset AddedAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public string? RemovedReason { get; private set; }

    public static TaskAssignee Create(Guid taskId, Guid userId, Guid? employeeId, string? name, string source, DateTimeOffset now) => new()
    {
        TaskId = taskId,
        UserId = userId,
        EmployeeId = employeeId,
        Name = name,
        Source = source,
        AddedAt = now,
    };

    public void Remove(string reason, DateTimeOffset now)
    {
        Active = false;
        RemovedAt = now;
        RemovedReason = reason.Length > 1000 ? reason[..1000] : reason;
    }
}

public static class HistoryEvents
{
    public const string Submitted = "submitted";
    public const string Resubmitted = "resubmitted";
    public const string StepEntered = "step-entered";
    public const string StepSkipped = "step-skipped";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Escalated = "escalated";
    public const string Reminded = "reminded";
    public const string Rerouted = "rerouted";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
    public const string ConditionError = "condition-error";
}

/// <summary>
/// The approval timeline (approval panel). Not audited field by field: each row records an action whose task / instance changes are in
/// the audit trail already.
/// </summary>
[NotAudited]
public sealed class HistoryEntry : Entity
{
    private HistoryEntry()
    {
    }

    public Guid InstanceId { get; private set; }

    /// <summary>Insertion order (several rows of one action share the same time).</summary>
    public long Seq { get; private set; }

    public Guid? TaskId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Event { get; private set; } = string.Empty;

    public string? StepNameAr { get; private set; }

    public string? StepNameEn { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ActorName { get; private set; }

    public Guid? OnBehalfOfUserId { get; private set; }

    public string? OnBehalfOfName { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>Small detail object (JSON): custom action code, skip reason, condition, new assignees…</summary>
    public string? Data { get; private set; }

    public static HistoryEntry Create(
        Guid instanceId, Guid? taskId, string @event, DateTimeOffset at, string? stepNameAr = null, string? stepNameEn = null, Guid? actorId = null,
        string? actorName = null, Guid? onBehalfOfId = null, string? onBehalfOfName = null, string? comment = null, object? data = null) => new()
        {
            InstanceId = instanceId,
            TaskId = taskId,
            Event = @event,
            OccurredAt = at,
            StepNameAr = stepNameAr,
            StepNameEn = stepNameEn,
            ActorUserId = actorId,
            ActorName = actorName,
            OnBehalfOfUserId = onBehalfOfId,
            OnBehalfOfName = onBehalfOfName,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            Data = data is null ? null : WorkflowJson.Write(data),
        };
}

/// <summary>A remembered response for an <c>Idempotency-Key</c> (ADR 0026). Not business data; removed after 24 hours.</summary>
[NotAudited]
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public bool Completed { get; set; }

    public int StatusCode { get; set; }

    public string? ContentType { get; set; }

    public byte[]? Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
