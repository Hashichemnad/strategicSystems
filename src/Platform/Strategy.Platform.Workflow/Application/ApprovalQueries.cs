using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

public sealed record MyWorkTaskDto(
    Guid TaskId, uint TaskVersion, Guid InstanceId, string ProcessKey, string ProcessNameAr, string ProcessNameEn, string SystemCode, string ResourceType,
    Guid ResourceId, string? Number, string? TitleAr, string? TitleEn, string Link, string? StepNameAr, string? StepNameEn, string? SubmitterName,
    DateTimeOffset SubmittedAt, DateTimeOffset OpenedAt, DateTimeOffset? DueAt, bool Overdue, PersonName? OnBehalfOf, int Attempt);

public sealed record MySubmissionDto(
    Guid InstanceId, string ProcessKey, string ProcessNameAr, string ProcessNameEn, string SystemCode, string? Number, string? TitleAr, string? TitleEn,
    string Link, string Status, string? StepNameAr, string? StepNameEn, DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt, int Attempt);

public sealed record MyWorkCountsDto(int PendingApprovals, int Overdue, int Delegated, int ActionItems, int InProgressSubmissions);

public sealed record MyWorkDto(
    MyWorkCountsDto Counts, IReadOnlyList<MyWorkTaskDto> Approvals, IReadOnlyList<MyWorkItem> ActionItems, IReadOnlyList<MySubmissionDto> Submissions);

public sealed record ApprovalActionOptionDto(string Code, string Outcome, string? LabelAr, string? LabelEn, bool CommentRequired);

public sealed record MyTaskDto(Guid TaskId, uint Version, IReadOnlyList<ApprovalActionOptionDto> Actions, IReadOnlyList<PersonName> OnBehalfOf, bool SelfAssigned);

public sealed record StepStateDto(
    int Sort, string Code, string? NameAr, string? NameEn, string State, IReadOnlyList<PersonName> Approvers, string? Outcome, PersonName? ActedBy,
    PersonName? OnBehalfOf, string? Comment, DateTimeOffset? CompletedAt, DateTimeOffset? DueAt, bool Overdue);

public sealed record HistoryDto(
    Guid Id, DateTimeOffset OccurredAt, string Event, string? StepNameAr, string? StepNameEn, PersonName? Actor, PersonName? OnBehalfOf, string? Comment,
    JsonElement? Data);

public sealed record InstanceDto(
    Guid Id, string ProcessKey, string ProcessNameAr, string ProcessNameEn, string Status, int Attempt, DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt,
    PersonName? Submitter, string? Comment, IReadOnlyList<StepStateDto> Steps, IReadOnlyList<HistoryDto> History, MyTaskDto? MyTask);

public sealed record SubjectApprovalsDto(string ResourceType, Guid ResourceId, IReadOnlyList<InstanceDto> Instances);

public static class StepStates
{
    public const string Done = "done";
    public const string Current = "current";
    public const string Skipped = "skipped";
    public const string Upcoming = "upcoming";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
    public const string NotReached = "notReached";
}

/// <summary>Read side: My Work (SPEC §0.3), the approval panel of a record, the current-status endpoint.</summary>
internal sealed partial class ApprovalQueries(
    WorkflowDbContext db,
    ProcessRegistry registry,
    ApprovalEngine engine,
    IRequestContext context,
    IDelegationQuery delegations,
    IEmployeeDirectory employees,
    IUserDirectory users,
    ISystemState systems,
    IEnumerable<IMyWorkSource> sources,
    IEnumerable<ICurrentStatusProvider> statusProviders,
    IClock clock,
    ILogger<ApprovalQueries> logger)
{
    public async Task<MyWorkDto> MyWorkAsync(CancellationToken ct)
    {
        var user = context.User;
        var userId = user.UserId() ?? Guid.Empty;
        var now = clock.UtcNow;

        // Pending tasks I am assigned to, then tasks of people who delegated to me (covering the process).
        var mine = await PendingTasksAsync(a => a.UserId == userId, ct).ConfigureAwait(false);
        var delegated = new List<(ApprovalTask Task, TaskAssignee For)>();
        if (user.EmployeeIdOf() is { } employeeId)
        {
            var grants = await delegations.GetDelegationsToAsync(employeeId, clock.Today, ct).ConfigureAwait(false);
            var delegators = grants.Select(g => g.DelegatorEmployeeId).Distinct().ToList();
            if (delegators.Count > 0)
            {
                var candidates = await PendingTasksAsync(a => a.EmployeeId != null && delegators.Contains(a.EmployeeId.Value), ct).ConfigureAwait(false);
                var candidateInstances = await db.Instances.AsNoTracking().Where(i => candidates.Select(c => c.InstanceId).Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, i => new { i.ProcessKey, i.SubmitterUserId }, ct).ConfigureAwait(false);
                foreach (var task in candidates.Where(t => mine.All(m => m.Id != t.Id)))
                {
                    // Never the delegate's own request (no self-approval through a delegation).
                    if (!candidateInstances.TryGetValue(task.InstanceId, out var facts) || facts.SubmitterUserId == userId)
                    {
                        continue;
                    }

                    var forWhom = task.ActiveAssignees.FirstOrDefault(a => a.UserId != userId && a.EmployeeId is { } e
                        && grants.Any(g => g.DelegatorEmployeeId == e && g.Covers(facts.ProcessKey)));
                    if (forWhom is not null)
                    {
                        delegated.Add((task, forWhom));
                    }
                }
            }
        }

        var allTasks = mine.Select(t => (Task: t, For: (TaskAssignee?)null)).Concat(delegated.Select(d => (d.Task, For: (TaskAssignee?)d.For))).ToList();
        var instanceIds = allTasks.Select(t => t.Task.InstanceId).Distinct().ToList();
        var instances = await db.Instances.AsNoTracking().Where(i => instanceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        var onBehalf = await engine.PeopleAsync(delegated.Select(d => (d.For.UserId, d.For.EmployeeId, d.For.Name)), ct).ConfigureAwait(false);
        var approvals = allTasks
            .Where(t => instances.ContainsKey(t.Task.InstanceId) && registry.Find(instances[t.Task.InstanceId].ProcessKey) is not null)
            .Select(t =>
            {
                var instance = instances[t.Task.InstanceId];
                var process = registry.Find(instance.ProcessKey)!;
                return new MyWorkTaskDto(t.Task.Id, t.Task.Version, instance.Id, process.Key, process.NameAr, process.NameEn, process.SystemCode,
                    instance.ResourceType, instance.ResourceId, instance.SubjectNumber, instance.TitleAr, instance.TitleEn, instance.Link, t.Task.StepNameAr,
                    t.Task.StepNameEn, instance.SubmitterName, instance.SubmittedAt, t.Task.OpenedAt, t.Task.DueAt, t.Task.DueAt is { } due && due < now,
                    t.For is null ? null : onBehalf.FirstOrDefault(p => p.UserId == t.For.UserId), instance.Attempt);
            })
            .OrderByDescending(t => t.Overdue).ThenBy(t => t.DueAt ?? DateTimeOffset.MaxValue).ThenBy(t => t.OpenedAt)
            .ToList();

        var submissions = await db.Instances.AsNoTracking()
            .Where(i => i.SubmitterUserId == userId && (i.Status == ApprovalInstanceStatuses.InProgress || i.CompletedAt > now.AddDays(-30)))
            .OrderByDescending(i => i.SubmittedAt).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var stepNames = await StepNamesAsync(submissions, ct).ConfigureAwait(false);
        var submissionDtos = submissions.Where(i => registry.Find(i.ProcessKey) is not null).Select(i =>
        {
            var process = registry.Find(i.ProcessKey)!;
            var step = stepNames.GetValueOrDefault((i.VersionId, i.CurrentSort));
            return new MySubmissionDto(i.Id, i.ProcessKey, process.NameAr, process.NameEn, process.SystemCode, i.SubjectNumber, i.TitleAr, i.TitleEn, i.Link, i.Status,
                i.Status == ApprovalInstanceStatuses.InProgress ? step.Ar : null, i.Status == ApprovalInstanceStatuses.InProgress ? step.En : null,
                i.SubmittedAt, i.CompletedAt, i.Attempt);
        }).ToList();

        var items = new List<MyWorkItem>();
        foreach (var source in sources)
        {
            if (!await systems.IsEnabledAsync(source.SystemCode, ct).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                items.AddRange(await source.GetItemsAsync(user, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failing card must not break the home page.
                LogSourceFailed(logger, source.SystemCode, ex);
            }
        }

        return new MyWorkDto(
            new MyWorkCountsDto(approvals.Count, approvals.Count(a => a.Overdue), delegated.Count, items.Count,
                submissionDtos.Count(s => s.Status == ApprovalInstanceStatuses.InProgress)),
            approvals, items, submissionDtos);
    }

    private async Task<List<ApprovalTask>> PendingTasksAsync(System.Linq.Expressions.Expression<Func<TaskAssignee, bool>> who, CancellationToken ct)
    {
        var taskIds = db.Assignees.AsNoTracking().Where(a => a.Active).Where(who).Select(a => a.TaskId);
        return await db.Tasks.AsNoTracking().Include(t => t.Assignees)
            .Where(t => t.Status == ApprovalTaskStatuses.Pending && taskIds.Contains(t.Id))
            .OrderBy(t => t.OpenedAt).Take(200).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<(Guid, int), (string? Ar, string? En)>> StepNamesAsync(IReadOnlyCollection<ApprovalInstance> instances, CancellationToken ct)
    {
        var versionIds = instances.Select(i => i.VersionId).Distinct().ToList();
        return (await db.Steps.AsNoTracking().Where(s => versionIds.Contains(s.VersionId)).Select(s => new { s.VersionId, s.Sort, s.NameAr, s.NameEn })
            .ToListAsync(ct).ConfigureAwait(false)).ToDictionary(s => (s.VersionId, s.Sort), s => (s.NameAr, s.NameEn));
    }

    /// <summary>The approval panel: every attempt for the record (newest first). 404 when the user may not see it.</summary>
    public async Task<Result<SubjectApprovalsDto>> SubjectAsync(string resourceType, Guid resourceId, CancellationToken ct)
    {
        var instances = await db.Instances.AsNoTracking().Where(i => i.ResourceType == resourceType && i.ResourceId == resourceId)
            .OrderByDescending(i => i.SubmittedAt).Take(50).ToListAsync(ct).ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return new SubjectApprovalsDto(resourceType, resourceId, []);
        }

        if (!await engine.CanSeeAsync(instances[0], context.User, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var ids = instances.Select(i => i.Id).ToList();
        var tasks = await db.Tasks.AsNoTracking().Include(t => t.Assignees).Where(t => ids.Contains(t.InstanceId)).ToListAsync(ct).ConfigureAwait(false);
        var history = await db.History.AsNoTracking().Where(h => ids.Contains(h.InstanceId)).OrderBy(h => h.OccurredAt).ThenBy(h => h.Seq)
            .ToListAsync(ct).ConfigureAwait(false);
        var versionIds = instances.Select(i => i.VersionId).Distinct().ToList();
        var steps = await db.Steps.AsNoTracking().Where(s => versionIds.Contains(s.VersionId)).ToListAsync(ct).ConfigureAwait(false);
        var people = await NamesAsync(tasks, history, instances, ct).ConfigureAwait(false);
        PersonName? Person(Guid? userId, string? fallback) => userId is { } id ? people.GetValueOrDefault(id) ?? new PersonName(id, fallback, fallback) : null;

        var now = clock.UtcNow;
        var result = new List<InstanceDto>();
        foreach (var instance in instances)
        {
            var process = registry.Find(instance.ProcessKey);
            var instanceTasks = tasks.Where(t => t.InstanceId == instance.Id).ToList();
            var instanceHistory = history.Where(h => h.InstanceId == instance.Id).ToList();
            var skipped = instanceHistory.Where(h => h.Event == HistoryEvents.StepSkipped).Select(h => StepCodeOf(h.Data)).ToHashSet(StringComparer.Ordinal);
            var stepStates = steps.Where(s => s.VersionId == instance.VersionId).OrderBy(s => s.Sort).Select(s =>
            {
                var task = instanceTasks.Where(t => t.StepSort == s.Sort).OrderByDescending(t => t.OpenedAt).FirstOrDefault();
                string state;
                if (task is not null)
                {
                    state = task.Status switch
                    {
                        ApprovalTaskStatuses.Pending => StepStates.Current,
                        ApprovalTaskStatuses.Cancelled => StepStates.Cancelled,
                        _ => OutcomeState(process, s, task.Outcome),
                    };
                }
                else if (skipped.Contains(s.Code))
                {
                    state = StepStates.Skipped;
                }
                else
                {
                    state = instance.Status == ApprovalInstanceStatuses.InProgress && s.Sort > instance.CurrentSort ? StepStates.Upcoming : StepStates.NotReached;
                }

                var approvers = task?.Assignees.Where(a => task.Status != ApprovalTaskStatuses.Pending || a.Active)
                    .Select(a => Person(a.UserId, a.Name)!).ToList() ?? [];
                return new StepStateDto(s.Sort, s.Code, s.NameAr, s.NameEn, state, approvers, task?.Outcome, Person(task?.ActedByUserId, task?.ActedByName),
                    Person(task?.OnBehalfOfUserId, task?.OnBehalfOfName), task?.Comment, task?.CompletedAt, task?.DueAt,
                    task is { Status: ApprovalTaskStatuses.Pending, DueAt: { } due } && due < now);
            }).ToList();

            MyTaskDto? myTask = null;
            var pending = instanceTasks.FirstOrDefault(t => t.Status == ApprovalTaskStatuses.Pending);
            if (pending is not null && instance.Status == ApprovalInstanceStatuses.InProgress
                && await engine.ActingAsAsync(pending, instance, context.User, null, ct).ConfigureAwait(false) is { } acting)
            {
                myTask = await MyTaskAsync(pending, instance, steps.First(s => s.VersionId == instance.VersionId && s.Sort == pending.StepSort), acting, ct)
                    .ConfigureAwait(false);
            }

            result.Add(new InstanceDto(instance.Id, instance.ProcessKey, process?.NameAr ?? instance.ProcessKey, process?.NameEn ?? instance.ProcessKey, instance.Status,
                instance.Attempt, instance.SubmittedAt, instance.CompletedAt, Person(instance.SubmitterUserId, instance.SubmitterName), instance.Comment, stepStates,
                instanceHistory.OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Seq).Select(h => new HistoryDto(h.Id, h.OccurredAt, h.Event, h.StepNameAr, h.StepNameEn,
                    Person(h.ActorUserId, h.ActorName), Person(h.OnBehalfOfUserId, h.OnBehalfOfName), h.Comment,
                    h.Data is null ? null : JsonDocument.Parse(h.Data).RootElement.Clone())).ToList(),
                myTask));
        }

        return new SubjectApprovalsDto(resourceType, resourceId, result);
    }

    private async Task<MyTaskDto> MyTaskAsync(ApprovalTask task, ApprovalInstance instance, ProcessStep step, ActingAs acting, CancellationToken ct)
    {
        var definition = step.ToDefinition();
        bool Required(string outcome) => outcome switch
        {
            ApprovalActions.Approve => definition.CommentOnApprove,
            ApprovalActions.Reject => definition.CommentOnReject,
            _ => definition.CommentOnReturn,
        };

        var actions = definition.EffectiveActions.Select(a => new ApprovalActionOptionDto(a, a, null, null, Required(a)))
            .Concat((definition.CustomActions ?? []).Select(c => new ApprovalActionOptionDto(c.Code, c.Outcome, c.LabelAr, c.LabelEn, Required(c.Outcome))))
            .ToList();

        // A delegate may act for any covered assignee; list them so the panel can offer "on behalf of".
        var forWhom = new List<TaskAssignee>();
        var self = task.ActiveAssignees.Any(a => a.UserId == context.User.UserId());
        if (context.User.EmployeeIdOf() is { } employeeId)
        {
            var grants = (await delegations.GetDelegationsToAsync(employeeId, clock.Today, ct).ConfigureAwait(false))
                .Where(g => g.Covers(instance.ProcessKey)).Select(g => g.DelegatorEmployeeId).ToHashSet();
            forWhom.AddRange(task.ActiveAssignees.Where(a => a.UserId != context.User.UserId() && a.EmployeeId is { } e && grants.Contains(e)));
        }

        if (!self && forWhom.Count == 0)
        {
            forWhom.Add(acting.Assignee);
        }

        var names = await engine.PeopleAsync(forWhom.Select(a => (a.UserId, a.EmployeeId, a.Name)), ct).ConfigureAwait(false);
        return new MyTaskDto(task.Id, task.Version, actions, names, self);
    }

    private static string OutcomeState(ApprovalProcessDefinition? process, ProcessStep step, string? outcome)
    {
        _ = process;
        var resolved = step.ToDefinition().CustomActions?.FirstOrDefault(c => c.Code == outcome)?.Outcome ?? outcome;
        return resolved switch
        {
            ApprovalActions.Reject => StepStates.Rejected,
            ApprovalActions.Return => StepStates.Returned,
            _ => StepStates.Done,
        };
    }

    private static string StepCodeOf(string? data)
    {
        if (data is null)
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(data);
        return document.RootElement.TryGetProperty("step", out var step) ? step.GetString() ?? string.Empty : string.Empty;
    }

    private async Task<Dictionary<Guid, PersonName>> NamesAsync(
        IReadOnlyCollection<ApprovalTask> tasks, IReadOnlyCollection<HistoryEntry> history, IReadOnlyCollection<ApprovalInstance> instances, CancellationToken ct)
    {
        var userIds = tasks.SelectMany(t => t.Assignees.Select(a => a.UserId).Append(t.ActedByUserId ?? Guid.Empty).Append(t.OnBehalfOfUserId ?? Guid.Empty))
            .Concat(history.SelectMany(h => new[] { h.ActorUserId ?? Guid.Empty, h.OnBehalfOfUserId ?? Guid.Empty }))
            .Concat(instances.Select(i => i.SubmitterUserId ?? Guid.Empty))
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var found = await users.GetManyAsync(userIds, ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(found.Values.Where(u => u.EmployeeId is not null).Select(u => u.EmployeeId!.Value).Distinct().ToList(), ct)
            .ConfigureAwait(false);
        return found.Values.ToDictionary(u => u.Id, u => u.EmployeeId is { } e && people.TryGetValue(e, out var p)
            ? new PersonName(u.Id, p.NameAr ?? p.NameEn, p.NameEn ?? p.NameAr)
            : new PersonName(u.Id, u.UserName, u.UserName));
    }

    public async Task<Result<CurrentStatus>> StatusAsync(string resourceType, Guid resourceId, CancellationToken ct)
    {
        var provider = statusProviders.FirstOrDefault(p => p.ResourceType == resourceType);
        if (provider is null)
        {
            return Error.NotFound();
        }

        var status = await provider.GetAsync(resourceId, ct).ConfigureAwait(false);
        return status is null ? Error.NotFound() : status;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "My Work source {SystemCode} failed")]
    private static partial void LogSourceFailed(ILogger logger, string systemCode, Exception exception);
}
