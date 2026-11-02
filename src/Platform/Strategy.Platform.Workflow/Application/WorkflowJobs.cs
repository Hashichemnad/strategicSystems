using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

/// <summary>
/// SLA escalation (design note §6): every pending task past its due time gets one overdue reminder to its approvers and, when the step names
/// an escalation rule, the escalation target is added as an approver and notified. <c>escalated_at</c> makes each task run once.
/// </summary>
internal sealed class SlaEscalation(
    WorkflowDbContext db, ProcessRegistry registry, ApprovalEngine engine, ApproverResolver resolver, ISettingsStore settings, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var dueIds = await db.Tasks.AsNoTracking()
            .Where(t => t.Status == ApprovalTaskStatuses.Pending && t.DueAt != null && t.DueAt <= now && t.EscalatedAt == null)
            .OrderBy(t => t.DueAt).Select(t => t.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var id in dueIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var task = await db.Tasks.Include(t => t.Assignees).FirstAsync(t => t.Id == id, ct).ConfigureAwait(false);
            var instance = await db.Instances.FirstAsync(i => i.Id == task.InstanceId, ct).ConfigureAwait(false);
            if (task.Status != ApprovalTaskStatuses.Pending || task.EscalatedAt is not null || registry.Find(instance.ProcessKey) is not { } process)
            {
                continue;
            }

            var step = await engine.StepOfAsync(instance.VersionId, task.StepSort, ct).ConfigureAwait(false);
            await engine.NotifyOverdueAsync(process, instance, task, ct).ConfigureAwait(false);
            db.History.Add(HistoryEntry.Create(instance.Id, task.Id, HistoryEvents.Reminded, engine.NextStamp(), task.StepNameAr, task.StepNameEn,
                data: new { dueAt = task.DueAt }));
            IReadOnlyList<ResolvedApprover> added = [];
            if (step?.Escalation is { } escalationJson)
            {
                var context = new ResolutionContext(process, instance.Subject, instance.SubmitterEmployeeId,
                    await settings.GetAsync<ApprovalSettings>(ct).ConfigureAwait(false));
                var resolution = await resolver.ResolveAsync(WorkflowJson.Read<ApproverRule>(escalationJson), context, ct).ConfigureAwait(false);
                added = await engine.AddAssigneesAsync(process, instance, task, resolution.Approvers, AssigneeSources.Escalation,
                    WorkflowNotifications.TaskEscalated.Key, ct).ConfigureAwait(false);
                db.History.Add(HistoryEntry.Create(instance.Id, task.Id, HistoryEvents.Escalated, engine.NextStamp(), task.StepNameAr, task.StepNameEn,
                    data: new { added = added.Select(a => a.DisplayName).ToList(), notes = resolution.Notes }));
            }

            task.MarkEscalated(now);
            await engine.AppendEventAsync(process, instance, "workflow.task.escalated", task,
                new { taskId = task.Id, step = task.StepCode, dueAt = task.DueAt, added = added.Select(a => a.UserId).ToList() }, null, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}

internal sealed class SlaEscalationJob(RequestContext requestContext, SlaEscalation escalation) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "workflow.sla", "تصعيد الموافقات المتأخرة", "Escalate overdue approvals", "0 0/15 * * * ?", typeof(SlaEscalationJob),
        "تذكير المعتمدين بالخطوات المتأخرة وتصعيدها حسب المهلة المحددة", "Reminds approvers of overdue steps and escalates them per the step's SLA");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"escalated={await escalation.RunAsync(cancellationToken).ConfigureAwait(false)}";
}

/// <summary>Removes idempotency records older than 24 hours (ADR 0026). They are not business data.</summary>
internal sealed class IdempotencyCleanupJob(RequestContext requestContext, WorkflowDbContext db, IClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "workflow.idempotency-cleanup", "تنظيف مفاتيح منع التكرار", "Clean up idempotency keys", "0 20 3 * * ?", typeof(IdempotencyCleanupJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-24);
        var removed = await db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return $"removed={removed}";
    }
}

/// <summary>
/// Bulk role transfer (SPEC §0.2): re-routes pending tasks whose step rule is a transferred role to the role's holders after the transfer,
/// inside the transfer's transaction (ADR 0023). Old approvers are removed with the reason, new ones added, notified and given approver rights.
/// </summary>
internal sealed class ApprovalRoleTransferHook(
    WorkflowDbContext db, ProcessRegistry registry, ApprovalEngine engine, ApproverResolver resolver, ISettingsStore settings, IClock clock) : IRoleTransferHook
{
    public async Task OnRoleTransferredAsync(RoleTransfer transfer, object? transactionContext, CancellationToken cancellationToken)
    {
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);

        // Only steps whose rule is one of the transferred roles matter; their versions narrow the pending tasks.
        var steps = (await db.Steps.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(s => WorkflowJson.Read<ApproverRule>(s.Rule) is { Kind: ApproverRuleKinds.Role, RoleKey: { } key } && transfer.RoleKeys.Contains(key, StringComparer.Ordinal))
            .ToList();
        if (steps.Count == 0)
        {
            return;
        }

        var versionIds = steps.Select(s => s.VersionId).Distinct().ToList();
        var instances = await db.Instances.Where(i => i.Status == ApprovalInstanceStatuses.InProgress && versionIds.Contains(i.VersionId))
            .ToDictionaryAsync(i => i.Id, cancellationToken).ConfigureAwait(false);
        var instanceIds = instances.Keys.ToList();
        var pending = await db.Tasks.Include(t => t.Assignees).Where(t => t.Status == ApprovalTaskStatuses.Pending && instanceIds.Contains(t.InstanceId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var approvalSettings = await settings.GetAsync<ApprovalSettings>(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var reason = $"transfer:{transfer.TransferId:D}" + (transfer.Reason is { Length: > 0 } r ? $" — {r}" : string.Empty);
        var changed = false;
        foreach (var task in pending)
        {
            var instance = instances[task.InstanceId];
            var step = steps.FirstOrDefault(s => s.VersionId == instance.VersionId && s.Sort == task.StepSort);
            if (step is null || registry.Find(instance.ProcessKey) is not { } process)
            {
                continue;
            }

            var rule = WorkflowJson.Read<ApproverRule>(step.Rule);
            if (rule.Kind != ApproverRuleKinds.Role || rule.RoleKey is null || !transfer.RoleKeys.Contains(rule.RoleKey, StringComparer.Ordinal))
            {
                continue;
            }

            var context = new ResolutionContext(process, instance.Subject, instance.SubmitterEmployeeId, approvalSettings);
            var holders = (await resolver.ResolveAsync(rule, context, cancellationToken).ConfigureAwait(false)).Approvers
                .Where(a => !step.ExcludeSubmitter || a.UserId != instance.SubmitterUserId).ToList();
            var current = task.ActiveAssignees.Where(a => a.Source is AssigneeSources.Rule or AssigneeSources.Transfer).ToList();
            var removed = current.Where(a => holders.All(h => h.UserId != a.UserId)).ToList();
            var toAdd = holders.Where(h => current.All(a => a.UserId != h.UserId)).ToList();
            if (removed.Count == 0 && toAdd.Count == 0)
            {
                continue;
            }

            if (holders.Count == 0)
            {
                // Nobody would hold the step: keep the current approvers rather than orphan the task (ADR 0025: never approved by nobody).
                continue;
            }

            await engine.RemoveAssigneesAsync(process, instance, removed, reason, cancellationToken).ConfigureAwait(false);
            var added = await engine.AddAssigneesAsync(process, instance, task, toAdd, AssigneeSources.Transfer, WorkflowNotifications.TaskAssigned.Key, cancellationToken)
                .ConfigureAwait(false);
            db.History.Add(HistoryEntry.Create(instance.Id, task.Id, HistoryEvents.Rerouted, engine.NextStamp(), task.StepNameAr, task.StepNameEn, comment: transfer.Reason,
                data: new { transferId = transfer.TransferId, removed = removed.Select(a => a.Name).ToList(), added = added.Select(a => a.DisplayName).ToList() }));
            await engine.AppendEventAsync(process, instance, "workflow.task.rerouted", task,
                new
                {
                    taskId = task.Id,
                    transferId = transfer.TransferId,
                    removed = removed.Select(a => a.UserId).ToList(),
                    added = added.Select(a => a.UserId).ToList(),
                }, transfer.Reason, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
