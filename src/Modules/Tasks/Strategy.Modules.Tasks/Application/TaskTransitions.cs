using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

/// <summary>One lifecycle action (<see cref="TaskActions"/>) on a task; the API maps one intent-named route per action.</summary>
public sealed record ActOnTask(
    Guid Id, string Action, uint Version, string? Reason = null, string? Comment = null, int? FeedbackPoint = null, Guid? AssigneeEmployeeId = null)
    : ICommand<TaskDto>;

public sealed class ActOnTaskValidator : AbstractValidator<ActOnTask>
{
    public ActOnTaskValidator()
    {
        RuleFor(x => x.Action).Must(a => TaskActions.All.Contains(a) && a != TaskActions.CreateSubtask).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Reason).MaximumLength(TaskItem.ReasonMaxLength);
        RuleFor(x => x.Comment).MaximumLength(TaskItem.ReasonMaxLength * 2);
    }
}

/// <summary>
/// Runs a transition of <see cref="TaskWorkflow"/>: 404 outside the read scope; 403 when the user is neither the actor the table names nor holds
/// the record-role permission (only the creator approves, ADR 0035); 409 on a stale version; business-rule errors from the guards.
/// </summary>
internal sealed class ActOnTaskHandler(
    TasksDbContext db, TaskVisibility visibility, TaskCreator factory, TaskDeadlines deadlines, TaskAssignees assignees, TaskNotifier notifier, TaskReader reader,
    ISettingsStore settingsStore, IEmployeeDirectory employees, IAuditLog audit, IRequestContext context, IClock clock)
    : ICommandHandler<ActOnTask, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(ActOnTask command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var task = await visibility.FindAsync(command.Id, user, tracked: true, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        var (isCreator, isAssignee) = TaskReader.Roles(task, user);
        isCreator = isCreator && await visibility.HasAsync(task, user, TaskPermissions.Manage, cancellationToken).ConfigureAwait(false);
        isAssignee = isAssignee && await visibility.HasAsync(task, user, TaskPermissions.Work, cancellationToken).ConfigureAwait(false);
        if (!isCreator && !isAssignee)
        {
            return Error.Forbidden();
        }

        var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var guards = new TaskGuards(
            await db.Tasks.CountAsync(s => s.ParentTaskId == task.Id && !TaskStatuses.Closed.Contains(s.Status), cancellationToken).ConfigureAwait(false),
            await (from d in db.Dependencies
                   join p in db.Tasks on d.DependsOnTaskId equals p.Id
                   where d.TaskId == task.Id && d.Active && !TaskStatuses.Closed.Contains(p.Status)
                   select p.Id).CountAsync(cancellationToken).ConfigureAwait(false),
            settings.FeedbackScaleMax);

        EmployeeReference? newAssignee = null;
        if (command.Action == TaskActions.Reassign && command.AssigneeEmployeeId is { } newId)
        {
            newAssignee = await employees.GetAsync(newId, cancellationToken).ConfigureAwait(false);
            var creator = await employees.GetAsync(task.CreatorEmployeeId, cancellationToken).ConfigureAwait(false);
            if (newAssignee is null || newAssignee.Status == "left" || creator is null)
            {
                return Error.Validation([new FieldError("assigneeEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
            }

            if (!await assignees.MayAssignAsync(creator, newAssignee, settings.AssignScope, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("assigneeEmployeeId", "tasks.validation.mayNotAssign", "tasks.validation.mayNotAssign",
                    new Dictionary<string, object?> { ["name"] = newAssignee.NameEn ?? newAssignee.NameAr })]);
            }
        }

        db.Entry(task).Property(x => x.Version).OriginalValue = command.Version;
        var from = task.Status;
        var previousAssignee = task.AssigneeEmployeeId;
        var now = clock.UtcNow;
        var acted = task.Act(command.Action, isCreator, isAssignee, now,
            new TaskActInput(command.Reason, command.Comment, command.FeedbackPoint, newAssignee?.Id, newAssignee?.UnitId), guards);
        if (acted.IsFailure)
        {
            return acted.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await factory.NumberPeriodsAsync(task, cancellationToken).ConfigureAwait(false);
        if (command.Action is TaskActions.Assign or TaskActions.Reassign)
        {
            await deadlines.ApplyAsync(task, settings, cancellationToken).ConfigureAwait(false);
            await deadlines.ApplyAcceptReminderAsync(task, settings, cancellationToken).ConfigureAwait(false);
        }

        var actorEmployee = user.EmployeeIdOf();
        db.Events.Add(TaskEvent.Create(task.Id, now, command.Action, from, task.Status, actorEmployee, user.UserId(), command.Reason ?? command.Comment,
            command.Action switch
            {
                TaskActions.Approve => new { feedbackPoint = command.FeedbackPoint },
                TaskActions.Reassign => new { from = previousAssignee, to = task.AssigneeEmployeeId },
                _ => null,
            }));
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (command.Action == TaskActions.Reassign && previousAssignee != task.AssigneeEmployeeId)
        {
            await factory.RevokeAsync(task, TaskPermissions.AssigneeRole, previousAssignee, cancellationToken).ConfigureAwait(false);
            await factory.GrantAsync(task, TaskPermissions.AssigneeRole, task.AssigneeEmployeeId, cancellationToken).ConfigureAwait(false);
        }

        await NotifyAsync(task, command, actorEmployee, cancellationToken).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent($"tasks.task.{command.Action}", true, TaskPermissions.SystemCode, TaskItem.EntityType, task.Id.ToString(),
            new { from, to = task.Status, feedbackPoint = command.FeedbackPoint }, command.Reason ?? command.Comment), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(task).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, user, cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyAsync(TaskItem task, ActOnTask command, Guid? actorEmployee, CancellationToken ct)
    {
        var actorName = await notifier.NameOfAsync(actorEmployee, ct).ConfigureAwait(false);
        var toCreator = actorEmployee == task.CreatorEmployeeId ? [] : new[] { task.CreatorEmployeeId };
        var toAssignee = actorEmployee == task.AssigneeEmployeeId ? [] : new[] { task.AssigneeEmployeeId };
        var (template, recipients, text) = command.Action switch
        {
            TaskActions.Assign or TaskActions.Reassign => (TaskNotifications.Assigned, toAssignee, (string?)null),
            TaskActions.Reject => (TaskNotifications.Rejected, toCreator, command.Reason),
            TaskActions.Submit => (TaskNotifications.Submitted, toCreator, command.Comment),
            TaskActions.Complete => (TaskNotifications.Completed, toCreator, command.Comment),
            TaskActions.Approve => (TaskNotifications.Approved, toAssignee, $"{command.FeedbackPoint}" + (string.IsNullOrWhiteSpace(command.Comment) ? string.Empty : $" — {command.Comment}")),
            TaskActions.Return => (TaskNotifications.Returned, toAssignee, command.Comment),
            TaskActions.Cancel => (TaskNotifications.Cancelled, toAssignee, command.Reason),
            _ => (null, Array.Empty<Guid>(), null),
        };
        if (template is not null)
        {
            await notifier.SendAsync(template, task, recipients, actorName, text, db, ct).ConfigureAwait(false);
        }
    }
}
