using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

/// <summary>Edit a task's content (creator, while open). Changing the due date recomputes the deadlines and tells the assignee.</summary>
public sealed record UpdateTask(
    Guid Id, string? TitleAr, string? TitleEn, string? Description, string? SourceCode, string? CategoryCode, string? Priority, bool Confidential,
    DateOnly? PlannedStart, DateOnly? DueDate, decimal? EstimatedHours, int? PercentComplete, uint Version) : ICommand<TaskDto>, ITaskText;

public sealed class UpdateTaskValidator : AbstractValidator<UpdateTask>
{
    public UpdateTaskValidator()
    {
        this.AddTaskTextRules();
        RuleFor(x => x.SourceCode).NotEmpty();
        RuleFor(x => x.Priority).Must(p => p is null || TaskPriorities.All.Contains(p)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.PlannedStart).When(x => x.DueDate is not null && x.PlannedStart is not null)
            .WithErrorCode("validation.dateRange");
        RuleFor(x => x.PercentComplete).InclusiveBetween(0, 100).When(x => x.PercentComplete is not null);
        RuleFor(x => x.EstimatedHours).InclusiveBetween(0, 10_000).When(x => x.EstimatedHours is not null);
    }
}

internal sealed class UpdateTaskHandler(
    TasksDbContext db, TaskVisibility visibility, TaskDeadlines deadlines, TaskNotifier notifier, TaskReader reader, ILookupService lookups,
    ISettingsStore settingsStore, IRequestContext context, IClock clock) : ICommandHandler<UpdateTask, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(UpdateTask command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var task = await visibility.FindAsync(command.Id, user, tracked: true, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        var (isCreator, isAssignee) = TaskReader.Roles(task, user);
        var mayEdit = isCreator && await visibility.HasAsync(task, user, TaskPermissions.Manage, cancellationToken).ConfigureAwait(false);
        var mayProgress = isAssignee && await visibility.HasAsync(task, user, TaskPermissions.Work, cancellationToken).ConfigureAwait(false);
        if (!mayEdit && !mayProgress)
        {
            return Error.Forbidden();
        }

        if (!await lookups.IsActiveCodeAsync(TaskPermissions.SourceLookup, command.SourceCode!, cancellationToken).ConfigureAwait(false)
            || (command.CategoryCode is { Length: > 0 } c && !await lookups.IsActiveCodeAsync(TaskPermissions.CategoryLookup, c, cancellationToken).ConfigureAwait(false)))
        {
            return Error.Validation([new FieldError(command.CategoryCode is null ? "sourceCode" : "categoryCode", "validation.unknownCode", "validation.unknownCode")]);
        }

        db.Entry(task).Property(x => x.Version).OriginalValue = command.Version;
        var dueBefore = task.DueDate;
        if (mayEdit)
        {
            var updated = task.Update(new TaskContent(command.TitleAr, command.TitleEn, command.Description, task.TypeCode, task.Template, command.SourceCode!,
                command.CategoryCode, command.Priority ?? task.Priority, command.Confidential, command.PlannedStart, command.DueDate, command.EstimatedHours,
                task.LinkedType, task.LinkedId));
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }
        else if (!task.IsOpen)
        {
            return Error.Rule("tasks.task.closed", "tasks.errors.closed");
        }

        if (mayEdit || mayProgress)
        {
            task.SetPercentComplete(command.PercentComplete);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (task.DueDate != dueBefore)
        {
            await deadlines.ApplyAsync(task, await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            db.Events.Add(TaskEvent.Create(task.Id, clock.UtcNow, TaskEvents.DueChanged, task.Status, task.Status, user.EmployeeIdOf(), user.UserId(),
                data: new { from = dueBefore, to = task.DueDate }));
            await notifier.SendAsync(TaskNotifications.DueChanged, task, task.AssigneeEmployeeId == user.EmployeeIdOf() ? [] : [task.AssigneeEmployeeId],
                await notifier.NameOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false), null, db, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await db.Entry(task).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, user, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Sub-task (SPEC §11.3): by the parent's assignee, while the parent is In Progress / On Hold; link and confidentiality inherited.</summary>
public sealed record CreateSubtask(
    Guid ParentId, string? TitleAr, string? TitleEn, string? Description, Guid? AssigneeEmployeeId, string? Priority, DateOnly? PlannedStart, DateOnly? DueDate)
    : ICommand<TaskDto>, ITaskText;

public sealed class CreateSubtaskValidator : AbstractValidator<CreateSubtask>
{
    public CreateSubtaskValidator()
    {
        this.AddTaskTextRules();
        RuleFor(x => x.AssigneeEmployeeId).NotNull();
        RuleFor(x => x.Priority).Must(p => p is null || TaskPriorities.All.Contains(p)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.PlannedStart).When(x => x.DueDate is not null && x.PlannedStart is not null)
            .WithErrorCode("validation.dateRange");
    }
}

internal sealed class CreateSubtaskHandler(
    TasksDbContext db, TaskVisibility visibility, TaskCreator factory, TaskAssignees assignees, TaskReader reader, ISettingsStore settingsStore,
    IEmployeeDirectory employees, IRequestContext context, IClock clock) : ICommandHandler<CreateSubtask, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(CreateSubtask command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var parent = await visibility.FindAsync(command.ParentId, user, tracked: false, cancellationToken).ConfigureAwait(false);
        if (parent is null)
        {
            return Error.NotFound();
        }

        var isAssignee = parent.IsAssignee(user.EmployeeIdOf()) && await visibility.HasAsync(parent, user, TaskPermissions.Work, cancellationToken).ConfigureAwait(false);
        if (!isAssignee)
        {
            return Error.Forbidden();
        }

        var allowed = parent.Act(TaskActions.CreateSubtask, false, true, clock.UtcNow, new TaskActInput(), new TaskGuards());
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var creator = await employees.GetAsync(parent.AssigneeEmployeeId, cancellationToken).ConfigureAwait(false);
        var assignee = await employees.GetAsync(command.AssigneeEmployeeId!.Value, cancellationToken).ConfigureAwait(false);
        if (creator is null || assignee is null || assignee.Status == "left")
        {
            return Error.Validation([new FieldError("assigneeEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        if (!await assignees.MayAssignAsync(creator, assignee, settings.AssignScope, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("assigneeEmployeeId", "tasks.validation.mayNotAssign", "tasks.validation.mayNotAssign",
                new Dictionary<string, object?> { ["name"] = assignee.NameEn ?? assignee.NameAr })]);
        }

        var content = new TaskContent(command.TitleAr, command.TitleEn, command.Description, parent.TypeCode, parent.Template, parent.SourceCode, parent.CategoryCode,
            command.Priority ?? parent.Priority, parent.Confidential, command.PlannedStart, command.DueDate ?? parent.DueDate, null, parent.LinkedType, parent.LinkedId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var subtask = await factory.CreateAsync(
            new NewTaskSpec(content, creator.Id, user.UserId(), null, assignee.Id, assignee.UnitId, false, null, parent.Id), settings, creator.Id, user.UserId(),
            cancellationToken).ConfigureAwait(false);
        db.Events.Add(TaskEvent.Create(parent.Id, clock.UtcNow, TaskEvents.SubtaskCreated, parent.Status, parent.Status, creator.Id, user.UserId(),
            data: new { subtaskId = subtask.Id, number = subtask.Number }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(subtask, user, cancellationToken).ConfigureAwait(false);
    }
}
