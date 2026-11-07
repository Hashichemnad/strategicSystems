using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Application;

/// <summary>
/// Responsibility reassignment (SPEC-ADDENDUM §D2, ADR 0168): the pending steps of the records' approvals follow the new people. Only rules that
/// depend on the record are resolved again; the same removal / addition as the bulk role transfer hook (ADR 0023: one transaction).
/// </summary>
internal sealed class ApprovalRerouting(WorkflowDbContext db, ProcessRegistry registry, ApprovalEngine engine, ApproverResolver resolver, ISettingsStore settings)
    : IApprovalRerouting
{
    private static readonly string[] RecordKinds =
        [ApproverRuleKinds.RecordRole, ApproverRuleKinds.TeamRole, ApproverRuleKinds.SectionManager, ApproverRuleKinds.DepartmentDirector, ApproverRuleKinds.HeadOfUnit];

    public async Task<int> RerouteAsync(ApprovalReroute reroute, object transactionContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reroute);
        var ids = reroute.ResourceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var instances = await db.Instances
            .Where(i => i.Status == ApprovalInstanceStatuses.InProgress && i.ResourceType == reroute.ResourceType && ids.Contains(i.ResourceId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return 0;
        }

        foreach (var instance in instances)
        {
            instance.UpdateSubject(reroute.OwnerEmployeeId, reroute.UnitId);
        }

        var instanceIds = instances.Select(i => i.Id).ToList();
        var pending = await db.Tasks.Include(t => t.Assignees).Where(t => t.Status == ApprovalTaskStatuses.Pending && instanceIds.Contains(t.InstanceId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var approvalSettings = await settings.GetAsync<ApprovalSettings>(cancellationToken).ConfigureAwait(false);
        var comment = reroute.Reason;
        var reason = "responsibility" + (reroute.Reason is { Length: > 0 } r ? $" — {r}" : string.Empty);
        var rerouted = 0;
        foreach (var task in pending)
        {
            var instance = instances.First(i => i.Id == task.InstanceId);
            if (registry.Find(instance.ProcessKey) is not { } process || await engine.StepOfAsync(instance.VersionId, task.StepSort, cancellationToken).ConfigureAwait(false) is not { } step)
            {
                continue;
            }

            var rule = WorkflowJson.Read<ApproverRule>(step.Rule);
            var dependsOnRecord = rule.Kind is ApproverRuleKinds.RecordRole or ApproverRuleKinds.TeamRole
                || (RecordKinds.Contains(rule.Kind) && rule.Of is null or ApproverRuleSubjects.Record && rule.UnitId is null);
            if (!dependsOnRecord)
            {
                continue;
            }

            var context = new ResolutionContext(process, instance.Subject, instance.SubmitterEmployeeId, approvalSettings);
            var holders = (await resolver.ResolveAsync(rule, context, cancellationToken).ConfigureAwait(false)).Approvers
                .Where(a => !step.ExcludeSubmitter || a.UserId != instance.SubmitterUserId).ToList();
            var current = task.ActiveAssignees.Where(a => a.Source is AssigneeSources.Rule or AssigneeSources.Transfer).ToList();
            var removed = current.Where(a => holders.All(h => h.UserId != a.UserId)).ToList();
            var toAdd = holders.Where(h => current.All(a => a.UserId != h.UserId)).ToList();
            if ((removed.Count == 0 && toAdd.Count == 0) || holders.Count == 0)
            {
                // Unchanged, or nobody would hold the step: keep the current approvers rather than orphan the task (ADR 0025).
                continue;
            }

            await engine.RemoveAssigneesAsync(process, instance, removed, reason, cancellationToken).ConfigureAwait(false);
            var added = await engine.AddAssigneesAsync(process, instance, task, toAdd, AssigneeSources.Transfer, WorkflowNotifications.TaskAssigned.Key, cancellationToken)
                .ConfigureAwait(false);
            db.History.Add(HistoryEntry.Create(instance.Id, task.Id, HistoryEvents.Rerouted, engine.NextStamp(), task.StepNameAr, task.StepNameEn, comment: comment,
                data: new { responsibility = true, removed = removed.Select(a => a.Name).ToList(), added = added.Select(a => a.DisplayName).ToList() }));
            await engine.AppendEventAsync(process, instance, "workflow.task.rerouted", task,
                new { taskId = task.Id, removed = removed.Select(a => a.UserId).ToList(), added = added.Select(a => a.UserId).ToList() }, comment, cancellationToken)
                .ConfigureAwait(false);
            rerouted++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return rerouted;
    }
}
