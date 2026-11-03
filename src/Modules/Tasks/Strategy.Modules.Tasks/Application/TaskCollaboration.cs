using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Tasks.Application;

// ---- Comments ---------------------------------------------------------------------------------------------------------------------

public sealed record ListTaskComments(Guid TaskId) : IQuery<IReadOnlyList<CommentDto>>;

/// <summary>A comment with @mentions (participants only, notified) and attachments (files already uploaded to the task).</summary>
public sealed record AddTaskComment(Guid TaskId, string? Text, IReadOnlyList<Guid>? Mentions, IReadOnlyList<Guid>? FileIds) : ICommand<CommentDto>;

public sealed class AddTaskCommentValidator : AbstractValidator<AddTaskComment>
{
    public AddTaskCommentValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(TaskComment.TextMaxLength);
        RuleFor(x => x.Mentions).Must(m => m is null || m.Count <= 20).WithErrorCode("validation.invalid");
        RuleFor(x => x.FileIds).Must(m => m is null || m.Count <= 20).WithErrorCode("validation.invalid");
    }
}

internal sealed class TaskComments(
    TasksDbContext db, TaskVisibility visibility, TaskReader reader, TaskNotifier notifier, IFileService files, IRequestContext context)
    : IQueryHandler<ListTaskComments, IReadOnlyList<CommentDto>>, ICommandHandler<AddTaskComment, CommentDto>
{
    public async Task<Result<IReadOnlyList<CommentDto>>> HandleAsync(ListTaskComments query, CancellationToken cancellationToken)
    {
        var task = await visibility.FindAsync(query.TaskId, context.User, tracked: false, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        var comments = await db.Comments.AsNoTracking().Where(c => c.TaskId == task.Id).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(comments.SelectMany(c => c.Mentions.Concat(c.AuthorEmployeeId is { } a ? [a] : [])), cancellationToken).ConfigureAwait(false);
        return comments.Select(c => Map(c, people)).ToList();
    }

    public async Task<Result<CommentDto>> HandleAsync(AddTaskComment command, CancellationToken cancellationToken)
    {
        var user = context.User;
        var task = await visibility.FindAsync(command.TaskId, user, tracked: false, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        if (!await visibility.HasAsync(task, user, TaskPermissions.Comment, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var participants = await ParticipantsAsync(db, task, cancellationToken).ConfigureAwait(false);
        var mentions = (command.Mentions ?? []).Distinct().ToList();
        if (mentions.Any(m => !participants.Contains(m)))
        {
            return Error.Validation([new FieldError("mentions", "tasks.validation.mentionNotParticipant", "tasks.validation.mentionNotParticipant")]);
        }

        var fileIds = (command.FileIds ?? []).Distinct().ToList();
        if (fileIds.Count > 0)
        {
            var taskFiles = (await files.ListAsync(TaskPermissions.ResourceType, task.Id.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false))
                .Select(f => f.Id).ToHashSet();
            if (fileIds.Any(f => !taskFiles.Contains(f)))
            {
                return Error.Validation([new FieldError("fileIds", "validation.unknownReference", "validation.unknownReference")]);
            }
        }

        var comment = TaskComment.Create(task.Id, command.Text!, CommentKinds.Comment, mentions, fileIds, user.EmployeeIdOf(), user.UserId());
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(TaskNotifications.Mentioned, task, mentions.Where(m => m != user.EmployeeIdOf()),
            await notifier.NameOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false), Excerpt(comment.Text), db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(mentions.Concat(comment.AuthorEmployeeId is { } a ? [a] : []), cancellationToken).ConfigureAwait(false);
        return Map(comment, people);
    }

    internal static async Task<HashSet<Guid>> ParticipantsAsync(TasksDbContext db, TaskItem task, CancellationToken ct)
    {
        var set = new HashSet<Guid> { task.CreatorEmployeeId, task.AssigneeEmployeeId };
        if (task.EnteredByEmployeeId is { } e)
        {
            set.Add(e);
        }

        set.UnionWith(await db.FollowUps.AsNoTracking().Where(f => f.TaskId == task.Id && f.Active).Select(f => f.EmployeeId).ToListAsync(ct).ConfigureAwait(false));
        return set;
    }

    internal static string Excerpt(string text) => text.Length > 300 ? text[..300] + "…" : text;

    private static CommentDto Map(TaskComment c, IReadOnlyDictionary<Guid, EmployeeReference> people) =>
        new(c.Id, c.Text, c.Kind, c.AuthorEmployeeId is { } a ? TaskReader.Person(a, people) : null,
            c.Mentions.Select(m => TaskReader.Person(m, people)).ToList(), c.FileIds, c.CreatedAt);
}

// ---- Follow-up employees ---------------------------------------------------------------------------------------------------------

public sealed record AddFollowUp(Guid TaskId, Guid? EmployeeId, bool CanUpload = true) : ICommand<TaskDto>;

public sealed record RemoveFollowUp(Guid TaskId, Guid EmployeeId) : ICommand<TaskDto>;

/// <summary>Follow-up employees (SPEC §11.2): added by the assignee or the department secretary; they comment and upload, nothing more.</summary>
internal sealed class TaskFollowUps(
    TasksDbContext db, TaskVisibility visibility, TaskCreator factory, TaskReader reader, TaskNotifier notifier, IEmployeeDirectory employees, IRequestContext context,
    IClock clock) : ICommandHandler<AddFollowUp, TaskDto>, ICommandHandler<RemoveFollowUp, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(AddFollowUp command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        if (command.EmployeeId is not { } employeeId || await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false) is not { Status: not "left" })
        {
            return Error.Validation([new FieldError("employeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        if (employeeId == task.AssigneeEmployeeId || employeeId == task.CreatorEmployeeId
            || await db.FollowUps.AnyAsync(f => f.TaskId == task.Id && f.Active && f.EmployeeId == employeeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("tasks.followUp.exists", "tasks.errors.followUpExists");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.FollowUps.Add(FollowUp.Create(task.Id, employeeId, context.User.EmployeeIdOf(), command.CanUpload));
        db.Events.Add(TaskEvent.Create(task.Id, clock.UtcNow, TaskEvents.FollowUpAdded, task.Status, task.Status, context.User.EmployeeIdOf(), context.User.UserId(),
            data: new { employeeId, canUpload = command.CanUpload }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await factory.GrantAsync(task, command.CanUpload ? TaskPermissions.FollowUpRole : TaskPermissions.FollowUpCommentRole, employeeId, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(TaskNotifications.FollowUpAdded, task, [employeeId], await notifier.NameOfAsync(context.User.EmployeeIdOf(), cancellationToken).ConfigureAwait(false),
            null, db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TaskDto>> HandleAsync(RemoveFollowUp command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        var followUps = await db.FollowUps.Where(f => f.TaskId == task.Id && f.Active && f.EmployeeId == command.EmployeeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (followUps.Count == 0)
        {
            return Error.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var followUp in followUps)
        {
            followUp.Remove(clock.UtcNow);
        }

        db.Events.Add(TaskEvent.Create(task.Id, clock.UtcNow, TaskEvents.FollowUpRemoved, task.Status, task.Status, context.User.EmployeeIdOf(), context.User.UserId(),
            data: new { employeeId = command.EmployeeId }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await factory.RevokeAsync(task, TaskPermissions.FollowUpRole, command.EmployeeId, cancellationToken).ConfigureAwait(false);
        await factory.RevokeAsync(task, TaskPermissions.FollowUpCommentRole, command.EmployeeId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(TaskItem? Task, Error? Error)> LoadAsync(Guid taskId, CancellationToken ct)
    {
        var task = await visibility.FindAsync(taskId, context.User, tracked: false, ct).ConfigureAwait(false);
        if (task is null)
        {
            return (null, Error.NotFound());
        }

        if (!task.IsOpen)
        {
            return (null, Error.Rule("tasks.task.closed", "tasks.errors.closed"));
        }

        return await visibility.HasAsync(task, context.User, TaskPermissions.FollowUp, ct).ConfigureAwait(false) ? (task, null) : (null, Error.Forbidden());
    }
}

// ---- Checklist -----------------------------------------------------------------------------------------------------------------

public sealed record AddChecklistItem(Guid TaskId, string? Text) : ICommand<TaskDto>;

public sealed record UpdateChecklistItem(Guid TaskId, Guid ItemId, string? Text, bool? Done) : ICommand<TaskDto>;

public sealed record RemoveChecklistItem(Guid TaskId, Guid ItemId) : ICommand<TaskDto>;

public sealed class AddChecklistItemValidator : AbstractValidator<AddChecklistItem>
{
    public AddChecklistItemValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(ChecklistItem.TextMaxLength);
}

public sealed class UpdateChecklistItemValidator : AbstractValidator<UpdateChecklistItem>
{
    public UpdateChecklistItemValidator() => RuleFor(x => x.Text).MaximumLength(ChecklistItem.TextMaxLength);
}

/// <summary>Checklist: the creator and the assignee add, tick and remove items while the task is open.</summary>
internal sealed class TaskChecklist(TasksDbContext db, TaskVisibility visibility, TaskReader reader, IRequestContext context, IClock clock)
    : ICommandHandler<AddChecklistItem, TaskDto>, ICommandHandler<UpdateChecklistItem, TaskDto>, ICommandHandler<RemoveChecklistItem, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(AddChecklistItem command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        var sort = await db.Checklist.Where(c => c.TaskId == task.Id).MaxAsync(c => (int?)c.Sort, cancellationToken).ConfigureAwait(false) ?? 0;
        db.Checklist.Add(ChecklistItem.Create(task.Id, command.Text!, sort + 10));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TaskDto>> HandleAsync(UpdateChecklistItem command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        var item = await db.Checklist.FirstOrDefaultAsync(c => c.Id == command.ItemId && c.TaskId == task.Id && c.Active, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        item.Update(command.Text, command.Done, context.User.EmployeeIdOf(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TaskDto>> HandleAsync(RemoveChecklistItem command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        var item = await db.Checklist.FirstOrDefaultAsync(c => c.Id == command.ItemId && c.TaskId == task.Id && c.Active, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        item.Remove();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(TaskItem? Task, Error? Error)> LoadAsync(Guid taskId, CancellationToken ct)
    {
        var task = await visibility.FindAsync(taskId, context.User, tracked: false, ct).ConfigureAwait(false);
        if (task is null)
        {
            return (null, Error.NotFound());
        }

        var (isCreator, isAssignee) = TaskReader.Roles(task, context.User);
        if (!isCreator && !isAssignee)
        {
            return (null, Error.Forbidden());
        }

        return task.IsOpen ? (task, null) : (null, Error.Rule("tasks.task.closed", "tasks.errors.closed"));
    }
}

// ---- Dependencies --------------------------------------------------------------------------------------------------------------

public sealed record AddDependency(Guid TaskId, Guid? DependsOnTaskId) : ICommand<TaskDto>;

public sealed record RemoveDependency(Guid TaskId, Guid DependsOnTaskId) : ICommand<TaskDto>;

/// <summary>Finish-to-start dependencies (ADR 0038): creator only, predecessors the creator can read, no cycles.</summary>
internal sealed class TaskDependencies(TasksDbContext db, TaskVisibility visibility, TaskReader reader, IRequestContext context)
    : ICommandHandler<AddDependency, TaskDto>, ICommandHandler<RemoveDependency, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(AddDependency command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        if (command.DependsOnTaskId is not { } predecessorId
            || await visibility.FindAsync(predecessorId, context.User, tracked: false, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.Validation([new FieldError("dependsOnTaskId", "validation.unknownReference", "validation.unknownReference")]);
        }

        var edges = (await db.Dependencies.AsNoTracking().Where(d => d.Active).Select(d => new { d.TaskId, d.DependsOnTaskId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(d => d.TaskId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(d => d.DependsOnTaskId).ToList());
        if (TaskDependency.CreatesCycle(task.Id, predecessorId, edges))
        {
            return Error.Rule("tasks.dependency.cycle", "tasks.errors.dependencyCycle");
        }

        if (!edges.TryGetValue(task.Id, out var existing) || !existing.Contains(predecessorId))
        {
            db.Dependencies.Add(TaskDependency.Create(task.Id, predecessorId));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TaskDto>> HandleAsync(RemoveDependency command, CancellationToken cancellationToken)
    {
        var (task, error) = await LoadAsync(command.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return error!;
        }

        var rows = await db.Dependencies.Where(d => d.TaskId == task.Id && d.DependsOnTaskId == command.DependsOnTaskId && d.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return Error.NotFound();
        }

        rows.ForEach(r => r.Remove());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(TaskItem? Task, Error? Error)> LoadAsync(Guid taskId, CancellationToken ct)
    {
        var task = await visibility.FindAsync(taskId, context.User, tracked: false, ct).ConfigureAwait(false);
        if (task is null)
        {
            return (null, Error.NotFound());
        }

        if (!task.IsCreator(context.User.EmployeeIdOf(), context.User.UserId()) || !await visibility.HasAsync(task, context.User, TaskPermissions.Manage, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        return task.IsOpen ? (task, null) : (null, Error.Rule("tasks.task.closed", "tasks.errors.closed"));
    }
}
