using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

public sealed record GetAssignmentGroup(Guid Id) : IQuery<GroupDto>;

public sealed record MessageGroup(Guid Id, string? Text) : ICommand<GroupDto>;

public sealed record ExtendGroupDue(Guid Id, DateOnly? DueDate) : ICommand<GroupDto>;

public sealed record CancelGroup(Guid Id, string? Reason) : ICommand<GroupDto>;

public sealed class MessageGroupValidator : AbstractValidator<MessageGroup>
{
    public MessageGroupValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(TaskComment.TextMaxLength);
}

public sealed class ExtendGroupDueValidator : AbstractValidator<ExtendGroupDue>
{
    public ExtendGroupDueValidator() => RuleFor(x => x.DueDate).NotNull();
}

public sealed class CancelGroupValidator : AbstractValidator<CancelGroup>
{
    public CancelGroupValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(TaskItem.ReasonMaxLength);
}

/// <summary>
/// The assignment group page and its creator actions (SPEC §11.4): one view of every assignee's status and progress ("x of n approved"); message
/// all, extend the due date of all open tasks, cancel all open tasks — each in one transaction. Members are filtered by <see cref="TaskVisibility"/>.
/// </summary>
internal sealed class TaskGroups(
    TasksDbContext db, TaskVisibility visibility, TaskReader reader, TaskDeadlines deadlines, TaskNotifier notifier, ISettingsStore settingsStore, IRequestContext context,
    IClock clock)
    : IQueryHandler<GetAssignmentGroup, GroupDto>, ICommandHandler<MessageGroup, GroupDto>, ICommandHandler<ExtendGroupDue, GroupDto>, ICommandHandler<CancelGroup, GroupDto>
{
    public Task<Result<GroupDto>> HandleAsync(GetAssignmentGroup query, CancellationToken cancellationToken) => ViewAsync(query.Id, cancellationToken);

    public async Task<Result<GroupDto>> HandleAsync(MessageGroup command, CancellationToken cancellationToken)
    {
        var (group, members, error) = await ManageAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return error!;
        }

        var user = context.User;
        var actorName = await notifier.NameOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in members.Where(t => t.Status != TaskStatuses.Cancelled))
        {
            db.Comments.Add(TaskComment.Create(task.Id, command.Text!, CommentKinds.GroupMessage, [], [], user.EmployeeIdOf(), user.UserId()));
            await notifier.SendAsync(TaskNotifications.GroupMessage, task, [task.AssigneeEmployeeId], actorName, TaskComments.Excerpt(command.Text!), db, cancellationToken)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(group.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<GroupDto>> HandleAsync(ExtendGroupDue command, CancellationToken cancellationToken)
    {
        var (group, members, error) = await ManageAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return error!;
        }

        var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var user = context.User;
        var actorName = await notifier.NameOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in members.Where(t => t.IsOpen))
        {
            if (task.PlannedStart is { } start && command.DueDate < start)
            {
                return Error.Validation([new FieldError("dueDate", "validation.dateRange", "validation.dateRange")]);
            }

            var before = task.DueDate;
            task.SetDueDate(command.DueDate);
            await deadlines.ApplyAsync(task, settings, cancellationToken).ConfigureAwait(false);
            db.Events.Add(TaskEvent.Create(task.Id, clock.UtcNow, TaskEvents.DueChanged, task.Status, task.Status, user.EmployeeIdOf(), user.UserId(),
                data: new { from = before, to = command.DueDate, groupId = group.Id }));
            await notifier.SendAsync(TaskNotifications.DueChanged, task, [task.AssigneeEmployeeId], actorName, null, db, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(group.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<GroupDto>> HandleAsync(CancelGroup command, CancellationToken cancellationToken)
    {
        var (group, members, error) = await ManageAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return error!;
        }

        var user = context.User;
        var actorName = await notifier.NameOfAsync(user.EmployeeIdOf(), cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in members.Where(t => t.IsOpen))
        {
            var from = task.Status;
            var cancelled = task.Act(TaskActions.Cancel, true, false, now, new TaskActInput(command.Reason), new TaskGuards());
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }

            db.Events.Add(TaskEvent.Create(task.Id, now, TaskActions.Cancel, from, task.Status, user.EmployeeIdOf(), user.UserId(), command.Reason, new { groupId = group.Id }));
            await notifier.SendAsync(TaskNotifications.Cancelled, task, [task.AssigneeEmployeeId], actorName, command.Reason, db, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(group.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(AssignmentGroup? Group, List<TaskItem> Members, Error? Error)> ManageAsync(Guid id, CancellationToken ct)
    {
        var user = context.User;
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
        var members = group is null ? [] : await db.Tasks.Include(t => t.Periods).Where(t => t.GroupId == id).ToListAsync(ct).ConfigureAwait(false);
        var readable = new List<TaskItem>();
        foreach (var member in members)
        {
            if (await visibility.CanReadAsync(member, user, ct).ConfigureAwait(false))
            {
                readable.Add(member);
            }
        }

        if (group is null || readable.Count == 0)
        {
            return (null, [], Error.NotFound());
        }

        // Only the creator manages the group, and needs the creator record role on every member it acts on.
        if (!(group.CreatorEmployeeId == user.EmployeeIdOf() || (group.CreatorUserId is { } u && u == user.UserId())))
        {
            return (null, [], Error.Forbidden());
        }

        foreach (var member in members)
        {
            if (!await visibility.HasAsync(member, user, TaskPermissions.Manage, ct).ConfigureAwait(false))
            {
                return (null, [], Error.Forbidden());
            }
        }

        return (group, members, null);
    }

    private async Task<Result<GroupDto>> ViewAsync(Guid id, CancellationToken ct)
    {
        var user = context.User;
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
        if (group is null)
        {
            return Error.NotFound();
        }

        var all = await db.Tasks.AsNoTracking().Where(t => t.GroupId == id).OrderBy(t => t.Number).ToListAsync(ct).ConfigureAwait(false);
        var visible = new List<TaskItem>();
        foreach (var task in all)
        {
            if (await visibility.CanReadAsync(task, user, ct).ConfigureAwait(false))
            {
                visible.Add(task);
            }
        }

        if (visible.Count == 0)
        {
            return Error.NotFound();
        }

        var statuses = all.Select(t => t.Status).ToList();
        var items = await reader.ListItemsAsync(visible, user, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([group.CreatorEmployeeId], ct).ConfigureAwait(false);
        var isCreator = group.CreatorEmployeeId == user.EmployeeIdOf() || (group.CreatorUserId is { } u && u == user.UserId());
        return new GroupDto(
            group.Id, group.TitleAr, group.TitleEn, TaskReader.Person(group.CreatorEmployeeId, people), group.CompletionRule, group.CreatedAt,
            statuses.Count(s => s != TaskStatuses.Cancelled), statuses.Count(AssignmentGroup.IsFinished), statuses.Count(s => s == TaskStatuses.Cancelled),
            AssignmentGroup.IsComplete(group.CompletionRule, statuses),
            items.Select(i => new GroupMemberDto(i, AssignmentGroup.IsFinished(i.Status))).ToList(),
            isCreator && all.Any(t => t.IsOpen), all.Count - visible.Count);
    }
}
