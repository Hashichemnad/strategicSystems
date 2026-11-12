using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Infrastructure;

/// <summary>
/// The task record type the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202). Only the listed properties are written; a synced task arrives
/// in its source status without notifications, timeline entries or timesheet periods. The number (when the source gives none), the working-time
/// deadlines and the creator / assignee record roles are derived by <see cref="TaskSyncHook"/> in the same transaction.
/// </summary>
internal static class TasksSyncTargets
{
    public static IServiceCollection AddTasksSyncTargets(this IServiceCollection services)
    {
        services.AddSyncTarget(SyncTarget.Entity<TasksDbContext, TaskItem>(TaskItem.EntityType, TaskPermissions.SystemCode, "مهمة", "Task")
            .Text("number", nameof(TaskItem.Number))
            .Text("titleAr", nameof(TaskItem.TitleAr)).Text("titleEn", nameof(TaskItem.TitleEn))
            .Text("description", nameof(TaskItem.Description))
            .Lookup("type", nameof(TaskItem.TypeCode), TaskPermissions.TypeLookup, required: true)
            .Choice("template", nameof(TaskItem.Template), TaskTemplates.All, required: true)
            .Lookup("source", nameof(TaskItem.SourceCode), TaskPermissions.SourceLookup, required: true)
            .Lookup("category", nameof(TaskItem.CategoryCode), TaskPermissions.CategoryLookup)
            .Choice("priority", nameof(TaskItem.Priority), TaskPriorities.All)
            .Choice("status", nameof(TaskItem.Status), TaskStatuses.All, required: true)
            .Bool("confidential", nameof(TaskItem.Confidential))
            .Person("creatorEmployeeId", nameof(TaskItem.CreatorEmployeeId), required: true)
            .Person("assigneeEmployeeId", nameof(TaskItem.AssigneeEmployeeId), required: true)
            .Unit("unitId", nameof(TaskItem.UnitId))
            .Reference("parentTaskId", nameof(TaskItem.ParentTaskId), TaskItem.EntityType)
            .Date("plannedStart", nameof(TaskItem.PlannedStart)).Date("dueDate", nameof(TaskItem.DueDate))
            .Timestamp("assignedAt", nameof(TaskItem.AssignedAt)).Timestamp("acceptedAt", nameof(TaskItem.AcceptedAt))
            .Timestamp("completedAt", nameof(TaskItem.CompletedAt)).Timestamp("approvedAt", nameof(TaskItem.ApprovedAt))
            .Timestamp("closedAt", nameof(TaskItem.ClosedAt))
            .Text("rejectionReason", nameof(TaskItem.RejectionReason)).Text("cancelReason", nameof(TaskItem.CancelReason))
            .Text("completionComment", nameof(TaskItem.CompletionComment))
            .WholeNumber("feedbackPoint", nameof(TaskItem.FeedbackPoint)).Text("feedbackComment", nameof(TaskItem.FeedbackComment))
            .WholeNumber("percentComplete", nameof(TaskItem.PercentComplete))
            .Number("estimatedHours", nameof(TaskItem.EstimatedHours))
            .Default("template", TaskTemplates.Full)
            .Default("type", "manager-assignment")
            .Default("source", "manager-assignment")
            .Default("priority", TaskPriorities.Medium)
            .Default("status", TaskStatuses.Assigned)
            .After<TaskSyncHook>());
        return services;
    }
}

/// <summary>
/// A synced task: the template's status set is checked, a number is assigned when the source gives none, the deadlines of an open task are
/// computed from its due date in working time (ADR 0037) and the creator / assignee record roles follow the source's people, as
/// <c>TaskCreator</c> and the reassignment grant them for local tasks.
/// </summary>
internal sealed class TaskSyncHook(IRecordRoleService recordRoles, INumberingService numbering, TaskDeadlines deadlines, ISettingsStore settingsStore, IClock clock)
    : ISyncTargetHook
{
    private static readonly string[] Managed = [TaskPermissions.CreatorRole, TaskPermissions.AssigneeRole];

    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (TasksDbContext)transactionContext;
        var task = await db.Tasks.SingleAsync(t => t.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (task.Status != TaskStatuses.Draft && task.Status != TaskStatuses.Cancelled && !TaskStatuses.StagesOf(task.Template).Contains(task.Status)
            && !(task.Template == TaskTemplates.Full && task.Status == TaskStatuses.Rejected))
        {
            throw new DbUpdateException($"Status '{task.Status}' does not belong to the '{task.Template}' task template.");
        }

        if (task.Number is null)
        {
            task.SetNumber(await numbering.NextAsync(db, TaskPermissions.TaskNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false));
        }

        if (task.IsOpen)
        {
            var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
            await deadlines.ApplyAsync(task, settings, cancellationToken).ConfigureAwait(false);
        }
        else if (task.DueAt is not null)
        {
            task.SetDeadlines(null, null, null, null);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var wanted = new HashSet<(Guid Employee, string Role)>
        {
            (task.CreatorEmployeeId, TaskPermissions.CreatorRole),
            (task.AssigneeEmployeeId, TaskPermissions.AssigneeRole),
        };
        var held = (await recordRoles.GetHoldersAsync(TaskPermissions.ResourceType, task.Id, cancellationToken).ConfigureAwait(false))
            .Where(h => h.PrincipalKind == RecordPrincipalKinds.Employee && Managed.Contains(h.RoleCode) && h.ValidTo is null)
            .Select(h => (h.PrincipalId, h.RoleCode)).ToHashSet();
        foreach (var (employee, role) in wanted.Where(w => !held.Contains(w)))
        {
            await recordRoles.GrantInTransactionAsync(db, TaskPermissions.ResourceType, task.Id, RecordPrincipalKinds.Employee, employee, role, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var (employee, role) in held.Where(h => !wanted.Contains(h)))
        {
            await recordRoles.RevokeInTransactionAsync(db, TaskPermissions.ResourceType, task.Id, RecordPrincipalKinds.Employee, employee, role, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
