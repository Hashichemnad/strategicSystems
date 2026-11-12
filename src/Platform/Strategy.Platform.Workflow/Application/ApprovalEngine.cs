using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

/// <summary>A decision on a task (API body).</summary>
public sealed record ActInput(string? Action, string? Comment, Guid? OnBehalfOfUserId, uint? Version);

/// <summary>Outcome of a decision.</summary>
public sealed record ActResult(Guid InstanceId, string InstanceStatus, Guid TaskId, string Outcome, string? NextStepNameAr, string? NextStepNameEn);

/// <summary>Who a user may act as on a task: themselves (an active assignee) or a delegate of one (ADR 0025, design note §6).</summary>
internal sealed record ActingAs(TaskAssignee Assignee, bool Delegated);

/// <summary>
/// The approval engine (CLAUDE.md §9, design note §2–§8): submission, decisions, step entry with conditions and approver resolution,
/// hooks, notifications, approver record roles, explicit audit events.
/// </summary>
internal sealed class ApprovalEngine(
    WorkflowDbContext db,
    ProcessRegistry registry,
    ApproverResolver resolver,
    OrgRelations relations,
    ISettingsStore settings,
    IClock clock,
    RequestContext requestContext,
    IAccessService access,
    IDelegationQuery delegations,
    IEmployeeDirectory employees,
    IRecordRoleService recordRoles,
    INotificationService notifications,
    IAuditSink auditSink,
    IBusinessHoursCalculator businessHours,
    IEnumerable<IApprovalHook> hooks,
    IEnumerable<Strategy.Platform.Kernel.Integration.IStandbyGuard> standby) : IApprovalEngine
{
    /// <summary>Standby (ADR 0206): no approval on a record that is read-only while its source system is primary.</summary>
    private async Task<Error?> ReadOnlyAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        foreach (var guard in standby)
        {
            if (await guard.IsActiveAsync(cancellationToken).ConfigureAwait(false)
                && (await guard.FindReadOnlyAsync([new(resourceType, resourceId)], cancellationToken).ConfigureAwait(false)).Values.FirstOrDefault() is { } source)
            {
                return Error.Conflict(Strategy.Platform.Kernel.Integration.StandbyErrors.Code, Strategy.Platform.Kernel.Integration.StandbyErrors.MessageKey) with
                {
                    Params = new Dictionary<string, object?> { ["source"] = source },
                };
            }
        }

        return null;
    }

    private readonly List<ApprovalEvent> _pendingEvents = [];
    private DateTimeOffset _lastStamp;

    // ------------------------------------------------------------------ submission

    public async Task<Result<ApprovalInstanceSummary>> SubmitAsync(ApprovalSubmission submission, object transactionContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var process = registry.Find(submission.ProcessKey);
        if (process is null || process.ResourceType != submission.Subject.ResourceType)
        {
            return Error.Rule("workflow.process.unknown", "workflow.errors.unknownProcess");
        }

        if (await ReadOnlyAsync(submission.Subject.ResourceType, submission.Subject.ResourceId, cancellationToken).ConfigureAwait(false) is { } readOnly)
        {
            return readOnly;
        }

        if (!await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SubmitAsync needs the module's DbContext with an open transaction (ADR 0023).");
        }

        var resourceId = submission.Subject.ResourceId;
        if (await db.Instances.AnyAsync(i => i.ProcessKey == process.Key && i.ResourceId == resourceId && i.Status == ApprovalInstanceStatuses.InProgress,
                cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("workflow.instance.alreadyInProgress", "workflow.errors.alreadyInProgress");
        }

        var version = await LatestVersionAsync(process.Key, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return Error.Rule("workflow.process.notConfigured", "workflow.errors.unknownProcess");
        }

        var moduleValues = VariableValues.FromModule(process, submission.Variables);
        if (moduleValues.IsFailure)
        {
            return moduleValues.Error;
        }

        var submitterEmployee = requestContext.User.EmployeeIdOf();
        var context = new ResolutionContext(process, submission.Subject, submitterEmployee, await settings.GetAsync<ApprovalSettings>(cancellationToken).ConfigureAwait(false));
        var variables = await relations.BuiltInsAsync(context, cancellationToken).ConfigureAwait(false);
        foreach (var (key, value) in moduleValues.Value)
        {
            variables[key] = value;
        }

        var previous = await db.Instances.Where(i => i.ProcessKey == process.Key && i.ResourceId == resourceId)
            .OrderByDescending(i => i.Attempt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var submitterName = await NameOfUserAsync(requestContext.User, cancellationToken).ConfigureAwait(false);
        var instance = ApprovalInstance.Create(process, version.Id, submission.Subject, requestContext.UserId, submitterEmployee, submitterName,
            VariableValues.ToJson(variables), previous, now, submission.Comment);
        db.Instances.Add(instance);
        db.History.Add(HistoryEntry.Create(instance.Id, null,
            previous?.Status == ApprovalInstanceStatuses.Returned ? HistoryEvents.Resubmitted : HistoryEvents.Submitted, NextStamp(),
            actorId: requestContext.UserId, actorName: submitterName, comment: submission.Comment,
            data: previous is null ? null : new { previousInstanceId = previous.Id, attempt = instance.Attempt }));

        var advanced = await AdvanceAsync(process, version, instance, 0, variables, context, cancellationToken).ConfigureAwait(false);
        if (advanced.IsFailure)
        {
            return advanced.Error;
        }

        await AppendAuditAsync(process, instance, previous is null ? "workflow.instance.submit" : "workflow.instance.resubmit", null,
            new { instance.Attempt, previousInstanceId = previous?.Id }, submission.Comment, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var hooked = await RunHooksAsync(cancellationToken).ConfigureAwait(false);
        if (hooked.IsFailure)
        {
            return hooked.Error;
        }

        foreach (var guard in standby)
        {
            await guard.OnApprovalSubmittedAsync(new(submission.Subject.ResourceType, submission.Subject.ResourceId), cancellationToken).ConfigureAwait(false);
        }

        return await SummaryAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ApprovalInstanceSummary>> CancelAsync(
        string processKey, Guid resourceId, string reason, object transactionContext, CancellationToken cancellationToken = default)
    {
        var process = registry.Find(processKey);
        if (process is null)
        {
            return Error.Rule("workflow.process.unknown", "workflow.errors.unknownProcess");
        }

        if (!await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("CancelAsync needs the module's DbContext with an open transaction (ADR 0023).");
        }

        var instance = await db.Instances.FirstOrDefaultAsync(
            i => i.ProcessKey == processKey && i.ResourceId == resourceId && i.Status == ApprovalInstanceStatuses.InProgress, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Error.Rule("workflow.instance.notInProgress", "workflow.errors.instanceClosed");
        }

        var now = clock.UtcNow;
        var finished = instance.Finish("cancel", now);
        if (finished.IsFailure)
        {
            return finished.Error;
        }

        foreach (var task in await db.Tasks.Include(t => t.Assignees).Where(t => t.InstanceId == instance.Id && t.Status == ApprovalTaskStatuses.Pending)
                     .ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            task.Cancel(now);
            await RevokeApproverRightsAsync(process, instance, task.ActiveAssignees, cancellationToken).ConfigureAwait(false);
        }

        db.History.Add(HistoryEntry.Create(instance.Id, null, HistoryEvents.Cancelled, NextStamp(), actorId: requestContext.UserId,
            actorName: await NameOfUserAsync(requestContext.User, cancellationToken).ConfigureAwait(false), comment: reason));
        Raise(process, instance, ApprovalEventKinds.Cancelled, null, null, null, reason);
        await AppendAuditAsync(process, instance, "workflow.instance.cancel", null, null, reason, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var hooked = await RunHooksAsync(cancellationToken).ConfigureAwait(false);
        return hooked.IsFailure ? hooked.Error : await SummaryAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApprovalInstanceSummary?> GetLatestAsync(string processKey, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var instance = await db.Instances.AsNoTracking().Where(i => i.ProcessKey == processKey && i.ResourceId == resourceId)
            .OrderByDescending(i => i.Attempt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return instance is null ? null : await SummaryAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApprovalAttempt>> ListAttemptsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var instances = await db.Instances.AsNoTracking().Where(i => i.ResourceType == resourceType && i.ResourceId == resourceId)
            .OrderByDescending(i => i.SubmittedAt).Take(20).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = instances.Select(i => i.Id).ToList();
        var decided = await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.InstanceId) && t.Outcome != null && t.CompletedAt != null)
            .OrderBy(t => t.CompletedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var attempts = new List<ApprovalAttempt>();
        foreach (var instance in instances)
        {
            attempts.Add(new ApprovalAttempt(await SummaryAsync(instance, cancellationToken).ConfigureAwait(false), instance.Comment,
                decided.Where(t => t.InstanceId == instance.Id)
                    .Select(t => new ApprovalDecision(t.StepNameAr, t.StepNameEn, t.Outcome!, t.Comment, t.CompletedAt!.Value)).ToList()));
        }

        return attempts;
    }

    public async Task<ApprovalStatusView?> GetStatusViewAsync(string processKey, Guid resourceId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var instance = await db.Instances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProcessKey == processKey && i.ResourceId == resourceId && i.Status == ApprovalInstanceStatuses.InProgress, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return null;
        }

        var task = await db.Tasks.AsNoTracking().Include(t => t.Assignees)
            .FirstOrDefaultAsync(t => t.InstanceId == instance.Id && t.Status == ApprovalTaskStatuses.Pending, cancellationToken).ConfigureAwait(false);
        var summary = await SummaryAsync(instance, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return new ApprovalStatusView(summary, [], null, null, []);
        }

        var approvers = await PeopleAsync(task.ActiveAssignees.Select(a => (a.UserId, a.EmployeeId, a.Name)), cancellationToken).ConfigureAwait(false);
        var acting = await ActingAsAsync(task, instance, user, null, cancellationToken).ConfigureAwait(false);
        var actions = new List<string>();
        if (acting is not null && await StepOfAsync(instance.VersionId, task.StepSort, cancellationToken).ConfigureAwait(false) is { } step)
        {
            actions.AddRange(step.Actions);
        }

        return new ApprovalStatusView(summary, approvers, task.DueAt, acting is null ? null : task.Id, actions);
    }

    // ------------------------------------------------------------------ decisions

    public async Task<Result<ApprovalDecisionResult>> DecideAsync(
        string processKey, Guid resourceId, string action, string? comment, CancellationToken cancellationToken = default)
    {
        var view = await GetStatusViewAsync(processKey, resourceId, requestContext.User, cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return Error.NotFound();
        }

        if (view.MyTaskId is not { } taskId)
        {
            return Error.Forbidden("workflow.notAnApprover");
        }

        var acted = await ActAsync(taskId, new ActInput(action, comment, null, null), cancellationToken).ConfigureAwait(false);
        return acted.IsSuccess
            ? new ApprovalDecisionResult(acted.Value.InstanceId, acted.Value.TaskId, acted.Value.InstanceStatus, acted.Value.Outcome)
            : acted.Error;
    }

    public async Task<Result<ActResult>> ActAsync(Guid taskId, ActInput input, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.Include(t => t.Assignees).FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken).ConfigureAwait(false);
        var instance = task is null ? null : await db.Instances.FirstOrDefaultAsync(i => i.Id == task.InstanceId, cancellationToken).ConfigureAwait(false);
        var process = instance is null ? null : registry.Find(instance.ProcessKey);
        if (task is null || instance is null || process is null
            || !await CanSeeAsync(instance, requestContext.User, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (task.Status != ApprovalTaskStatuses.Pending || instance.Status != ApprovalInstanceStatuses.InProgress)
        {
            return Error.Rule("workflow.task.closed", "workflow.errors.taskClosed");
        }

        if (await ReadOnlyAsync(instance.ResourceType, instance.ResourceId, cancellationToken).ConfigureAwait(false) is { } readOnly)
        {
            return readOnly;
        }

        var acting = await ActingAsAsync(task, instance, requestContext.User, input.OnBehalfOfUserId, cancellationToken).ConfigureAwait(false);
        if (acting is null)
        {
            return Error.Forbidden("workflow.notAnApprover");
        }

        if (input.Version is { } version && version != task.Version)
        {
            return Error.Conflict();
        }

        var version0 = await db.Versions.Include(v => v.Steps).AsNoTracking().FirstAsync(v => v.Id == instance.VersionId, cancellationToken).ConfigureAwait(false);
        var step = version0.OrderedSteps.First(s => s.Sort == task.StepSort);
        var definition = step.ToDefinition();
        var actionCode = input.Action?.Trim() ?? string.Empty;
        var custom = definition.CustomActions?.FirstOrDefault(c => c.Code == actionCode);
        var outcome = custom?.Outcome ?? actionCode;
        if (!definition.EffectiveActions.Contains(outcome) || (custom is null && !ApprovalActions.All.Contains(actionCode)))
        {
            return Error.Rule("workflow.action.notAllowed", "workflow.errors.actionNotAllowed");
        }

        var commentRequired = outcome switch
        {
            ApprovalActions.Approve => definition.CommentOnApprove,
            ApprovalActions.Reject => definition.CommentOnReject,
            _ => definition.CommentOnReturn,
        };
        if (commentRequired && string.IsNullOrWhiteSpace(input.Comment))
        {
            return Error.Validation([new FieldError("comment", "workflow.errors.commentRequired", "workflow.errors.commentRequired")]);
        }

        if (input.Comment is { Length: > ApprovalTask.CommentMaxLength })
        {
            return Error.Validation([new FieldError("comment", "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = ApprovalTask.CommentMaxLength })]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var actorName = await NameOfUserAsync(requestContext.User, cancellationToken).ConfigureAwait(false);
        string? onBehalfName = null;
        Guid? onBehalfId = null;
        if (acting.Delegated)
        {
            onBehalfId = acting.Assignee.UserId;
            onBehalfName = acting.Assignee.Name;
            requestContext.SetOnBehalfOf(onBehalfId, onBehalfName); // every audit row of this save carries it (CLAUDE.md §8)
        }

        var now = clock.UtcNow;
        db.Entry(task).Property(t => t.Version).OriginalValue = input.Version ?? task.Version;
        var completed = task.Complete(actionCode, requestContext.UserId, actorName, onBehalfId, onBehalfName, input.Comment, now);
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        await RevokeApproverRightsAsync(process, instance, task.ActiveAssignees, cancellationToken).ConfigureAwait(false);
        var historyEvent = outcome switch
        {
            ApprovalActions.Approve => HistoryEvents.Approved,
            ApprovalActions.Reject => HistoryEvents.Rejected,
            _ => HistoryEvents.Returned,
        };
        db.History.Add(HistoryEntry.Create(instance.Id, task.Id, historyEvent, NextStamp(), task.StepNameAr, task.StepNameEn, requestContext.UserId, actorName,
            onBehalfId, onBehalfName, input.Comment, custom is null ? null : new { customAction = custom.Code, custom.LabelAr, custom.LabelEn }));

        var variables = VariableValues.FromJson(instance.Variables, ProcessRegistry.VariablesOf(process));
        var context = new ResolutionContext(process, instance.Subject, instance.SubmitterEmployeeId,
            await settings.GetAsync<ApprovalSettings>(cancellationToken).ConfigureAwait(false));
        if (outcome == ApprovalActions.Approve)
        {
            var advanced = await AdvanceAsync(process, version0, instance, task.StepSort, variables, context, cancellationToken).ConfigureAwait(false);
            if (advanced.IsFailure)
            {
                return advanced.Error;
            }
        }
        else
        {
            var finished = instance.Finish(outcome, now);
            if (finished.IsFailure)
            {
                return finished.Error;
            }

            var kind = outcome == ApprovalActions.Reject ? ApprovalEventKinds.Rejected : ApprovalEventKinds.Returned;
            Raise(process, instance, kind, task.StepCode, actionCode, onBehalfId, input.Comment);
            await NotifySubmitterAsync(process, instance, outcome == ApprovalActions.Reject ? WorkflowNotifications.Rejected : WorkflowNotifications.Returned,
                task, actorName, input.Comment, cancellationToken).ConfigureAwait(false);
        }

        if (acting.Delegated)
        {
            var values = Values(process, instance, task.StepNameAr, task.StepNameEn);
            values["actor"] = actorName ?? string.Empty;
            values["actionAr"] = custom?.LabelAr ?? ActionLabel(outcome, ar: true);
            values["actionEn"] = custom?.LabelEn ?? ActionLabel(outcome, ar: false);
            await notifications.SendAsync(new NotificationRequest(WorkflowNotifications.ActedOnBehalf.Key, [NotificationRecipient.User(acting.Assignee.UserId)],
                values, instance.Link), db, cancellationToken).ConfigureAwait(false);
        }

        await AppendAuditAsync(process, instance, $"workflow.task.{outcome}", task,
            new { taskId = task.Id, step = task.StepCode, action = actionCode, onBehalfOf = onBehalfId }, input.Comment, cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var hooked = await RunHooksAsync(cancellationToken).ConfigureAwait(false);
        if (hooked.IsFailure)
        {
            return hooked.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var next = instance.Status == ApprovalInstanceStatuses.InProgress
            ? version0.OrderedSteps.FirstOrDefault(s => s.Sort == instance.CurrentSort)
            : null;
        return new ActResult(instance.Id, instance.Status, task.Id, actionCode, next?.NameAr, next?.NameEn);
    }

    /// <summary>
    /// The assignee the user may act as: themselves first, else a delegator whose delegation covers the process today. With
    /// <paramref name="onBehalfOfUserId"/> only that assignee is considered.
    /// </summary>
    internal async Task<ActingAs?> ActingAsAsync(ApprovalTask task, ApprovalInstance instance, ClaimsPrincipal user, Guid? onBehalfOfUserId, CancellationToken ct)
    {
        if (task.Status != ApprovalTaskStatuses.Pending || user.UserId() is not { } userId || !user.IsInternal())
        {
            return null;
        }

        var active = task.ActiveAssignees.ToList();
        if (onBehalfOfUserId is null && active.FirstOrDefault(a => a.UserId == userId) is { } self)
        {
            return new ActingAs(self, false);
        }

        // A delegation never lets the submitter decide on their own request (ADR 0025, self-approval).
        if (user.EmployeeIdOf() is not { } employeeId || instance.SubmitterUserId == userId)
        {
            return null;
        }

        var granted = (await delegations.GetDelegationsToAsync(employeeId, clock.Today, ct).ConfigureAwait(false))
            .Where(d => d.Covers(instance.ProcessKey)).Select(d => d.DelegatorEmployeeId).ToHashSet();
        var candidate = active.Where(a => a.UserId != userId && a.EmployeeId is { } e && granted.Contains(e))
            .FirstOrDefault(a => onBehalfOfUserId is null || a.UserId == onBehalfOfUserId);
        return candidate is null ? null : new ActingAs(candidate, true);
    }

    /// <summary>Instance visibility (design note §6): participants, the submitter, delegates of assignees, or readers of the record.</summary>
    internal async Task<bool> CanSeeAsync(ApprovalInstance instance, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsSystem())
        {
            return true;
        }

        if (user.UserId() is not { } userId || !user.IsInternal())
        {
            return false;
        }

        if (instance.SubmitterUserId == userId)
        {
            return true;
        }

        var participants = await db.Assignees.AsNoTracking()
            .Where(a => db.Tasks.Any(t => t.Id == a.TaskId && t.InstanceId == instance.Id))
            .Select(a => new { a.UserId, a.EmployeeId })
            .ToListAsync(ct).ConfigureAwait(false);
        if (participants.Any(p => p.UserId == userId))
        {
            return true;
        }

        if (user.EmployeeIdOf() is { } employeeId)
        {
            var granted = (await delegations.GetDelegationsToAsync(employeeId, clock.Today, ct).ConfigureAwait(false))
                .Where(d => d.Covers(instance.ProcessKey)).Select(d => d.DelegatorEmployeeId).ToHashSet();
            if (participants.Any(p => p.EmployeeId is { } e && granted.Contains(e)))
            {
                return true;
            }
        }

        var process = registry.Find(instance.ProcessKey);
        return process is not null && await access.CanAsync(user, process.ReadPermission,
            new ResourceRef(instance.ResourceType, instance.ResourceId, instance.SubjectOwnerEmployeeId, instance.SubjectOwnerUserId, instance.SubjectUnitId), ct)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ step entry

    /// <summary>
    /// Enters the next included step after <paramref name="afterSort"/>: conditions are evaluated on the submitted variables, approvers are
    /// resolved and snapshotted. When no step is left the instance is approved.
    /// </summary>
    private async Task<Result> AdvanceAsync(
        ApprovalProcessDefinition process, ProcessVersion version, ApprovalInstance instance, int afterSort, Dictionary<string, object?> variables,
        ResolutionContext context, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var processVariables = ProcessRegistry.VariablesOf(process);
        foreach (var step in version.OrderedSteps.Where(s => s.Sort > afterSort))
        {
            if (step.Condition is { } condition)
            {
                var compiled = ExpressionEngine.Compile(condition, processVariables, ExpressionValueType.Boolean);
                var evaluation = compiled.Expression?.Evaluate(variables, clock.Today);
                if (evaluation is null || !evaluation.Success)
                {
                    // A broken condition includes the step: an extra approver, never a missing one (ADR 0025).
                    db.History.Add(HistoryEntry.Create(instance.Id, null, HistoryEvents.ConditionError, NextStamp(), step.NameAr, step.NameEn,
                        data: new { condition, error = evaluation?.Error?.Code ?? (compiled.Errors.Count > 0 ? compiled.Errors[0].Code : null) }));
                }
                else if (!evaluation.IsTrue)
                {
                    db.History.Add(HistoryEntry.Create(instance.Id, null, HistoryEvents.StepSkipped, NextStamp(), step.NameAr, step.NameEn,
                        data: new { reason = "condition", condition, step = step.Code }));
                    continue;
                }
            }

            var resolution = await resolver.ResolveAsync(WorkflowJson.Read<ApproverRule>(step.Rule), context, ct).ConfigureAwait(false);
            var approvers = step.ExcludeSubmitter && instance.SubmitterUserId is { } submitter
                ? resolution.Approvers.Where(a => a.UserId != submitter).ToList()
                : [.. resolution.Approvers];
            if (approvers.Count == 0)
            {
                if (step.OnNoApprover == NoApproverBehaviours.Skip)
                {
                    db.History.Add(HistoryEntry.Create(instance.Id, null, HistoryEvents.StepSkipped, NextStamp(), step.NameAr, step.NameEn,
                        data: new { reason = "noApprover", step = step.Code, notes = resolution.Notes }));
                    continue;
                }

                return Error.Rule("workflow.step.noApprover", "workflow.errors.noApprover",
                    new Dictionary<string, object?> { ["stepAr"] = step.NameAr ?? step.NameEn, ["stepEn"] = step.NameEn ?? step.NameAr });
            }

            var task = ApprovalTask.Open(instance.Id, step, now, await DueAsync(step, instance, now, ct).ConfigureAwait(false));
            foreach (var approver in approvers)
            {
                task.Assignees.Add(TaskAssignee.Create(task.Id, approver.UserId, approver.EmployeeId, approver.DisplayName, AssigneeSources.Rule, now));
            }

            db.Tasks.Add(task);
            instance.EnterStep(step.Sort);
            db.History.Add(HistoryEntry.Create(instance.Id, task.Id, HistoryEvents.StepEntered, NextStamp(), step.NameAr, step.NameEn,
                data: new { approvers = approvers.Select(a => a.DisplayName).ToList(), dueAt = task.DueAt }));
            await GrantApproverRightsAsync(process, instance, approvers.Select(a => a.UserId), ct).ConfigureAwait(false);
            await NotifyAssigneesAsync(process, instance, task, approvers.Select(a => a.UserId), step.NotificationTemplate ?? WorkflowNotifications.TaskAssigned.Key, ct)
                .ConfigureAwait(false);
            Raise(process, instance, ApprovalEventKinds.StepEntered, step.Code, null, null, null);
            return Result.Success();
        }

        var finished = instance.Finish(ApprovalActions.Approve, now);
        if (finished.IsFailure)
        {
            return finished.Error;
        }

        db.History.Add(HistoryEntry.Create(instance.Id, null, HistoryEvents.Completed, NextStamp()));
        Raise(process, instance, ApprovalEventKinds.Approved, null, ApprovalActions.Approve, null, null);
        await NotifySubmitterAsync(process, instance, WorkflowNotifications.Approved, null, null, null, ct).ConfigureAwait(false);
        return Result.Success();
    }

    // ------------------------------------------------------------------ escalation and re-routing (used by the SLA job and the transfer hook)

    /// <summary>Adds users to a pending task (escalation or transfer), granting rights and notifying them.</summary>
    internal async Task<IReadOnlyList<ResolvedApprover>> AddAssigneesAsync(
        ApprovalProcessDefinition process, ApprovalInstance instance, ApprovalTask task, IEnumerable<ResolvedApprover> people, string source, string templateKey,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var added = new List<ResolvedApprover>();
        foreach (var person in people.Where(p => task.ActiveAssignees.All(a => a.UserId != p.UserId)).DistinctBy(p => p.UserId))
        {
            var assignee = TaskAssignee.Create(task.Id, person.UserId, person.EmployeeId, person.DisplayName, source, now);
            task.Assignees.Add(assignee);
            db.Assignees.Add(assignee);
            added.Add(person);
        }

        await GrantApproverRightsAsync(process, instance, added.Select(a => a.UserId), ct).ConfigureAwait(false);
        await NotifyAssigneesAsync(process, instance, task, added.Select(a => a.UserId), templateKey, ct).ConfigureAwait(false);
        return added;
    }

    internal async Task RemoveAssigneesAsync(
        ApprovalProcessDefinition process, ApprovalInstance instance, IReadOnlyCollection<TaskAssignee> removed, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var assignee in removed)
        {
            assignee.Remove(reason, now);
        }

        await RevokeApproverRightsAsync(process, instance, removed, ct).ConfigureAwait(false);
    }

    internal Task NotifyOverdueAsync(ApprovalProcessDefinition process, ApprovalInstance instance, ApprovalTask task, CancellationToken ct) =>
        NotifyAssigneesAsync(process, instance, task, task.ActiveAssignees.Select(a => a.UserId), WorkflowNotifications.TaskOverdue.Key, ct);

    internal async Task<ProcessStep?> StepOfAsync(Guid versionId, int sort, CancellationToken ct) =>
        await db.Steps.AsNoTracking().FirstOrDefaultAsync(s => s.VersionId == versionId && s.Sort == sort, ct).ConfigureAwait(false);

    internal Task AppendEventAsync(ApprovalProcessDefinition process, ApprovalInstance instance, string action, ApprovalTask? task, object? data, string? reason,
        CancellationToken ct) => AppendAuditAsync(process, instance, action, task, data, reason, ct);

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The step's due time (ADR 0027): calendar hours, or working hours of the default / the record's unit / a specific business-hours record
    /// (an inactive or missing one falls back to the default), through the shared calculator.
    /// </summary>
    private async Task<TaskDue?> DueAsync(ProcessStep step, ApprovalInstance instance, DateTimeOffset now, CancellationToken ct)
    {
        if (step.SlaHours is not > 0)
        {
            return null;
        }

        var hours = TimeSpan.FromHours(step.SlaHours.Value);
        if (step.SlaMode == SlaModes.Calendar)
        {
            return new TaskDue(now + hours, SlaModes.Calendar, null, null, null);
        }

        var record = step.BusinessHoursSource switch
        {
            BusinessHoursSources.Unit => await businessHours.ResolveForUnitAsync(instance.SubjectUnitId, ct).ConfigureAwait(false),
            BusinessHoursSources.Specific when step.BusinessHoursId is { } id
                && await businessHours.GetAsync(id, ct).ConfigureAwait(false) is { Active: true } specific => specific,
            _ => await businessHours.GetDefaultAsync(ct).ConfigureAwait(false),
        };
        var due = await businessHours.AddWorkingTimeAsync(record.Id, now, hours, ct).ConfigureAwait(false);
        return new TaskDue(due, SlaModes.Working, record.Id, record.NameAr ?? record.NameEn, record.NameEn ?? record.NameAr);
    }

    /// <summary>
    /// Time of the next history row: now, but strictly after the previous row of this unit of work (one microsecond apart), so the timeline
    /// keeps the order of an action's rows (the database stores microseconds and EF may insert rows of one save in any order).
    /// </summary>
    internal DateTimeOffset NextStamp()
    {
        var now = clock.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        _lastStamp = now > _lastStamp ? now : _lastStamp.AddMicroseconds(1);
        return _lastStamp;
    }

    internal async Task<ProcessVersion?> LatestVersionAsync(string processKey, CancellationToken ct)
    {
        var latest = await db.Versions.Include(v => v.Steps).Where(v => v.ProcessKey == processKey).OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (latest is not null || !registry.IsDynamic(processKey) || registry.Find(processKey) is not { DefaultSteps.Count: > 0 } process)
        {
            return latest;
        }

        // A process created at run time (ADR 0081) gets its version 1 on first use; the Approval Settings centre owns it from then on.
        var seeded = ProcessVersion.Create(process.Key, 1, null, process.DefaultSteps);
        db.Versions.Add(seeded);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return seeded;
    }

    private void Raise(ApprovalProcessDefinition process, ApprovalInstance instance, string kind, string? stepCode, string? action, Guid? onBehalfOf, string? comment) =>
        _pendingEvents.Add(new ApprovalEvent(kind, process.Key, instance.Id, instance.ResourceType, instance.ResourceId, instance.Attempt, stepCode, action,
            requestContext.UserId, onBehalfOf, comment));

    /// <summary>Calls the module hooks for the raised events, inside the open transaction; the first failure stops (and rolls back).</summary>
    private async Task<Result> RunHooksAsync(CancellationToken ct)
    {
        var events = _pendingEvents.ToList();
        _pendingEvents.Clear();
        foreach (var approvalEvent in events)
        {
            foreach (var hook in hooks.Where(h => h.ProcessKeys.Contains(approvalEvent.ProcessKey)))
            {
                var outcome = await hook.OnEventAsync(approvalEvent, db, ct).ConfigureAwait(false);
                if (outcome.IsFailure)
                {
                    return outcome.Error;
                }
            }
        }

        return Result.Success();
    }

    private async Task GrantApproverRightsAsync(ApprovalProcessDefinition process, ApprovalInstance instance, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        if (process.ApproverRecordRole is not { } role)
        {
            return;
        }

        foreach (var userId in userIds.Distinct())
        {
            await recordRoles.GrantInTransactionAsync(db, instance.ResourceType, instance.ResourceId, RecordPrincipalKinds.User, userId, role, ct).ConfigureAwait(false);
        }
    }

    private async Task RevokeApproverRightsAsync(ApprovalProcessDefinition process, ApprovalInstance instance, IEnumerable<TaskAssignee> assignees, CancellationToken ct)
    {
        if (process.ApproverRecordRole is not { } role)
        {
            return;
        }

        foreach (var userId in assignees.Select(a => a.UserId).Distinct().ToList())
        {
            await recordRoles.RevokeInTransactionAsync(db, instance.ResourceType, instance.ResourceId, RecordPrincipalKinds.User, userId, role, ct).ConfigureAwait(false);
        }
    }

    private async Task NotifyAssigneesAsync(
        ApprovalProcessDefinition process, ApprovalInstance instance, ApprovalTask task, IEnumerable<Guid> userIds, string templateKey, CancellationToken ct)
    {
        var recipients = userIds.Distinct().Select(NotificationRecipient.User).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var values = Values(process, instance, task.StepNameAr, task.StepNameEn);
        values["submitter"] = instance.SubmitterName ?? string.Empty;
        await notifications.SendAsync(new NotificationRequest(templateKey, recipients, values, instance.Link), db, ct).ConfigureAwait(false);
    }

    private async Task NotifySubmitterAsync(
        ApprovalProcessDefinition process, ApprovalInstance instance, NotificationTemplateDefinition template, ApprovalTask? task, string? actor, string? comment,
        CancellationToken ct)
    {
        if (instance.SubmitterUserId is not { } submitter)
        {
            return;
        }

        var values = Values(process, instance, task?.StepNameAr, task?.StepNameEn);
        values["actor"] = actor ?? string.Empty;
        values["comment"] = comment ?? "—";
        await notifications.SendAsync(new NotificationRequest(template.Key, [NotificationRecipient.User(submitter)], values, instance.Link), db, ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string> Values(ApprovalProcessDefinition process, ApprovalInstance instance, string? stepAr, string? stepEn) => new(StringComparer.Ordinal)
    {
        ["processAr"] = process.NameAr,
        ["processEn"] = process.NameEn,
        ["number"] = instance.SubjectNumber ?? string.Empty,
        ["titleAr"] = instance.TitleAr ?? instance.TitleEn ?? string.Empty,
        ["titleEn"] = instance.TitleEn ?? instance.TitleAr ?? string.Empty,
        ["stepAr"] = stepAr ?? stepEn ?? string.Empty,
        ["stepEn"] = stepEn ?? stepAr ?? string.Empty,
    };

    private static string ActionLabel(string outcome, bool ar) => outcome switch
    {
        ApprovalActions.Approve => ar ? "موافقة" : "Approve",
        ApprovalActions.Reject => ar ? "رفض" : "Reject",
        _ => ar ? "إعادة للتصحيح" : "Return for correction",
    };

    /// <summary>Explicit audit event in the same transaction (CLAUDE.md §8: approvals and workflow transitions).</summary>
    private async Task AppendAuditAsync(
        ApprovalProcessDefinition process, ApprovalInstance instance, string action, ApprovalTask? task, object? data, string? reason, CancellationToken ct)
    {
        var detail = JsonSerializer.Serialize(new { process = process.Key, resourceType = instance.ResourceType, resourceId = instance.ResourceId, detail = data },
            WorkflowJson.Options);
        var record = new AuditRecord(AuditKinds.Event, action, WorkflowPermissions.SystemCode, ApprovalInstance.EntityType, instance.Id.ToString("D"),
            Success: true, Data: detail, Reason: reason is { Length: > 1000 } ? reason[..1000] : reason)
        {
            ActorId = requestContext.UserId,
        };
        _ = task;
        if (db.Database.CurrentTransaction is { } transaction)
        {
            await auditSink.AppendAsync(db.Database.GetDbConnection(), transaction.GetDbTransaction(), [record], ct).ConfigureAwait(false);
            return;
        }

        await using var own = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await auditSink.AppendAsync(db.Database.GetDbConnection(), own.GetDbTransaction(), [record], ct).ConfigureAwait(false);
        await own.CommitAsync(ct).ConfigureAwait(false);
    }

    internal async Task<string?> NameOfUserAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.EmployeeIdOf() is { } employeeId && await employees.GetAsync(employeeId, ct).ConfigureAwait(false) is { } employee)
        {
            return employee.NameEn ?? employee.NameAr;
        }

        return user.Identity?.Name;
    }

    internal async Task<IReadOnlyList<PersonName>> PeopleAsync(IEnumerable<(Guid UserId, Guid? EmployeeId, string? Name)> people, CancellationToken ct)
    {
        var list = people.DistinctBy(p => p.UserId).ToList();
        var refs = await employees.GetManyAsync(list.Where(p => p.EmployeeId is not null).Select(p => p.EmployeeId!.Value).Distinct().ToList(), ct)
            .ConfigureAwait(false);
        return list.Select(p => p.EmployeeId is { } e && refs.TryGetValue(e, out var r)
            ? new PersonName(p.UserId, r.NameAr ?? r.NameEn, r.NameEn ?? r.NameAr)
            : new PersonName(p.UserId, p.Name, p.Name)).ToList();
    }

    private async Task<ApprovalInstanceSummary> SummaryAsync(ApprovalInstance instance, CancellationToken ct)
    {
        ProcessStep? step = null;
        if (instance.Status == ApprovalInstanceStatuses.InProgress && instance.CurrentSort > 0)
        {
            step = db.Steps.Local.FirstOrDefault(s => s.VersionId == instance.VersionId && s.Sort == instance.CurrentSort)
                ?? await StepOfAsync(instance.VersionId, instance.CurrentSort, ct).ConfigureAwait(false);
        }

        return new ApprovalInstanceSummary(instance.Id, instance.ProcessKey, instance.Status, instance.Attempt, step?.Code, step?.NameAr, step?.NameEn,
            instance.SubmittedAt, instance.CompletedAt);
    }
}
