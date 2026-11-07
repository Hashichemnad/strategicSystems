using System.Globalization;
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
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

public static class ReassignmentModes
{
    /// <summary>Every selected task goes to one new assignee.</summary>
    public const string ToEmployee = "to-employee";

    /// <summary>The creators decide: work not yet submitted comes back to them as Rejected (reassign or cancel).</summary>
    public const string ToCreators = "to-creators";

    public static readonly IReadOnlyList<string> All = [ToEmployee, ToCreators];
}

/// <summary>One open task of the leaving employee. Titles are left out for tasks the administrator may not read (confidential).</summary>
public sealed record ReassignmentTaskDto(
    Guid Id, string? Number, string? TitleAr, string? TitleEn, string Status, string Template, bool Confidential, bool Readable, PersonDto Creator,
    bool CreatedByLeaver, DateOnly? DueDate);

/// <summary><c>Tasks</c>: open tasks assigned to the employee; <c>CreatedTasks</c>: open tasks the employee created for other people (ADR 0169).</summary>
public sealed record ReassignmentPreviewDto(
    PersonDto Employee, string EmployeeStatus, IReadOnlyList<ReassignmentTaskDto> Tasks, IReadOnlyList<ReassignmentTaskDto> CreatedTasks);

/// <summary>Result of moving the creator role: <c>Skipped</c> = selected tasks assigned to the new creator (nobody approves their own work).</summary>
public sealed record CreatorTransferResultDto(int Transferred, int Skipped, IReadOnlyList<Guid> TaskIds);

/// <summary>
/// "Take over as creator" (ADR 0169): the creator role of the leaver's open tasks created for other people moves to <c>ToEmployeeId</c>, and with it
/// the approval of their completion. <c>TaskIds</c> narrows the preview (null = all of them).
/// </summary>
public sealed record TransferCreatorRole(Guid FromEmployeeId, Guid? ToEmployeeId, string? Reason, IReadOnlyList<Guid>? TaskIds) : ICommand<CreatorTransferResultDto>;

public sealed class TransferCreatorRoleValidator : AbstractValidator<TransferCreatorRole>
{
    public TransferCreatorRoleValidator()
    {
        RuleFor(x => x.ToEmployeeId).NotNull();
        RuleFor(x => x.ToEmployeeId).NotEqual(x => x.FromEmployeeId).When(x => x.ToEmployeeId is not null).WithErrorCode("validation.invalid");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(TaskItem.ReasonMaxLength);
        RuleFor(x => x.TaskIds).Must(ids => ids is null || ids.Count <= 1000).WithErrorCode("validation.invalid");
    }
}

public sealed record ReassignmentResultDto(int Reassigned, int ReturnedToCreators, int Skipped, IReadOnlyList<Guid> TaskIds);

public sealed record PreviewReassignment(Guid EmployeeId) : IQuery<ReassignmentPreviewDto>;

/// <summary>
/// "Reassign open tasks of an employee" (ADR 0040). <c>TaskIds</c> narrows the preview (null = all open tasks). In <c>to-creators</c> mode the
/// tasks the leaver created themself need <c>FallbackEmployeeId</c>, who receives them.
/// </summary>
public sealed record ReassignEmployeeTasks(
    Guid FromEmployeeId, string? Mode, Guid? ToEmployeeId, Guid? FallbackEmployeeId, string? Reason, IReadOnlyList<Guid>? TaskIds)
    : ICommand<ReassignmentResultDto>;

public sealed class ReassignEmployeeTasksValidator : AbstractValidator<ReassignEmployeeTasks>
{
    public ReassignEmployeeTasksValidator()
    {
        RuleFor(x => x.Mode).NotEmpty().Must(m => ReassignmentModes.All.Contains(m!)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.ToEmployeeId).NotNull().When(x => x.Mode == ReassignmentModes.ToEmployee);
        RuleFor(x => x.ToEmployeeId).NotEqual(x => x.FromEmployeeId).When(x => x.ToEmployeeId is not null).WithErrorCode("validation.invalid");
        RuleFor(x => x.FallbackEmployeeId).NotEqual(x => x.FromEmployeeId).When(x => x.FallbackEmployeeId is not null).WithErrorCode("validation.invalid");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(TaskItem.ReasonMaxLength);
        RuleFor(x => x.TaskIds).Must(ids => ids is null || ids.Count <= 1000).WithErrorCode("validation.invalid");
    }
}

/// <summary>
/// Administrative bulk reassignment when an employee leaves (or moves). Permission <c>tasks.reassignment.manage</c> within its data scope over the
/// leaving employee (owner employee + unit). Everything happens in one transaction: tasks, timeline entries, record roles, deadlines and the
/// notifications to the new assignee, the creators and the leaver; one audit event lists the tasks (confidential ones included — they must not
/// be orphaned, but their titles are never shown to the administrator).
/// </summary>
internal sealed class TaskReassignment(
    TasksDbContext db, TaskVisibility visibility, TaskReader reader, TaskCreator creator, TaskDeadlines deadlines, IAccessService access, IEmployeeDirectory employees,
    IUserDirectory users, INotificationService notifications, ISettingsStore settingsStore, IAuditLog audit, IRequestContext context, IClock clock)
    : IQueryHandler<PreviewReassignment, ReassignmentPreviewDto>, ICommandHandler<ReassignEmployeeTasks, ReassignmentResultDto>,
        ICommandHandler<TransferCreatorRole, CreatorTransferResultDto>
{
    public async Task<Result<ReassignmentPreviewDto>> HandleAsync(PreviewReassignment query, CancellationToken cancellationToken)
    {
        var employee = await AuthorisedEmployeeAsync(query.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee.IsFailure)
        {
            return employee.Error;
        }

        var tasks = await OpenTasksOf(query.EmployeeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var created = await OpenTasksCreatedBy(query.EmployeeId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ReassignmentPreviewDto(
            TaskReader.Person(employee.Value.Id, await reader.PeopleAsync([employee.Value.Id], cancellationToken).ConfigureAwait(false)),
            employee.Value.Status, await ItemsAsync(tasks, query.EmployeeId, cancellationToken).ConfigureAwait(false),
            await ItemsAsync(created, query.EmployeeId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Moves the creator role of the leaver's open tasks (assigned to other people) to another employee in one transaction: creator, timeline,
    /// record roles, notifications to both; one audit event afterwards (ADR 0169).
    /// </summary>
    public async Task<Result<CreatorTransferResultDto>> HandleAsync(TransferCreatorRole command, CancellationToken cancellationToken)
    {
        var leaver = await AuthorisedEmployeeAsync(command.FromEmployeeId, cancellationToken).ConfigureAwait(false);
        if (leaver.IsFailure)
        {
            return leaver.Error;
        }

        if (await ActiveEmployeeAsync(command.ToEmployeeId!.Value, cancellationToken).ConfigureAwait(false) is not { } target)
        {
            return Error.Validation([new FieldError("toEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        var query = OpenTasksCreatedBy(command.FromEmployeeId);
        if (command.TaskIds is { } ids)
        {
            query = query.Where(t => ids.Contains(t.Id));
        }

        var taskIds = await query.Select(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tasks = await db.Tasks.Where(t => taskIds.Contains(t.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var moving = tasks.Where(t => t.AssigneeEmployeeId != target.Id).ToList(); // the new creator never approves their own work
        if (moving.Count == 0)
        {
            return Error.Rule("tasks.reassignment.nothingToReassign", "tasks.errors.nothingToReassign");
        }

        var now = clock.UtcNow;
        var reason = command.Reason!.Trim();
        var actor = context.User.EmployeeIdOf();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in moving)
        {
            task.TakeOverAsCreator(target.Id);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.CreatorChanged, task.Status, task.Status, actor, context.User.UserId(), reason,
                new { from = command.FromEmployeeId, to = target.Id }));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in moving)
        {
            await creator.RevokeAsync(task, TaskPermissions.CreatorRole, command.FromEmployeeId, cancellationToken).ConfigureAwait(false);
            await creator.GrantAsync(task, TaskPermissions.CreatorRole, target.Id, cancellationToken).ConfigureAwait(false);
        }

        var names = await reader.PeopleAsync([leaver.Value.Id, target.Id, actor ?? Guid.Empty], cancellationToken).ConfigureAwait(false);
        string Name(Guid id, bool ar) => (ar ? names.GetValueOrDefault(id)?.NameAr ?? names.GetValueOrDefault(id)?.NameEn : names.GetValueOrDefault(id)?.NameEn
            ?? names.GetValueOrDefault(id)?.NameAr) ?? string.Empty;
        var values = new Dictionary<string, string>
        {
            ["count"] = moving.Count.ToString(CultureInfo.InvariantCulture),
            ["fromAr"] = Name(leaver.Value.Id, true),
            ["fromEn"] = Name(leaver.Value.Id, false),
            ["toAr"] = Name(target.Id, true),
            ["toEn"] = Name(target.Id, false),
            ["actor"] = Name(actor ?? Guid.Empty, false),
            ["text"] = reason,
        };
        await SendAsync(TaskNotifications.CreatorRoleToYou, [target.Id], values, "/tasks?tab=created", cancellationToken).ConfigureAwait(false);
        await SendAsync(TaskNotifications.YourCreatedTasksMoved, [leaver.Value.Id], values, "/tasks", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var skipped = tasks.Count - moving.Count;
        await audit.WriteAsync(new AuditEvent("tasks.task.creator-transfer", true, TaskPermissions.SystemCode, "org.employee", command.FromEmployeeId.ToString(),
            new { to = target.Id, tasks = moving.Select(t => t.Id), skipped }, reason), cancellationToken).ConfigureAwait(false);
        return new CreatorTransferResultDto(moving.Count, skipped, moving.Select(t => t.Id).ToList());
    }

    public async Task<Result<ReassignmentResultDto>> HandleAsync(ReassignEmployeeTasks command, CancellationToken cancellationToken)
    {
        var leaver = await AuthorisedEmployeeAsync(command.FromEmployeeId, cancellationToken).ConfigureAwait(false);
        if (leaver.IsFailure)
        {
            return leaver.Error;
        }

        EmployeeReference? target = null;
        if (command.ToEmployeeId is { } toId && (target = await ActiveEmployeeAsync(toId, cancellationToken).ConfigureAwait(false)) is null)
        {
            return Error.Validation([new FieldError("toEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        EmployeeReference? fallback = null;
        if (command.FallbackEmployeeId is { } fallbackId && (fallback = await ActiveEmployeeAsync(fallbackId, cancellationToken).ConfigureAwait(false)) is null)
        {
            return Error.Validation([new FieldError("fallbackEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        var query = OpenTasksOf(command.FromEmployeeId);
        if (command.TaskIds is { } ids)
        {
            query = query.Where(t => ids.Contains(t.Id));
        }

        // Ids first: the preview query is read-only (no tracking), the tasks to change are loaded tracked.
        var taskIds = await query.Select(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var tasks = await db.Tasks.Include(t => t.Periods).Where(t => taskIds.Contains(t.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (tasks.Count == 0)
        {
            return Error.Rule("tasks.reassignment.nothingToReassign", "tasks.errors.nothingToReassign");
        }

        var toCreators = command.Mode == ReassignmentModes.ToCreators;
        var leaverCreated = tasks.Where(t => t.CreatorEmployeeId == command.FromEmployeeId).ToList();
        if (toCreators && leaverCreated.Count > 0 && fallback is null)
        {
            return Error.Validation([new FieldError("fallbackEmployeeId", "tasks.validation.fallbackNeeded", "tasks.validation.fallbackNeeded",
                new Dictionary<string, object?> { ["count"] = leaverCreated.Count })]);
        }

        var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var reason = command.Reason!.Trim();
        var actor = context.User.EmployeeIdOf();
        var reassigned = new List<(TaskItem Task, Guid To)>();
        var returned = new List<TaskItem>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var task in tasks)
        {
            var from = task.Status;
            var newAssignee = toCreators ? (task.CreatorEmployeeId == command.FromEmployeeId ? fallback : null) : target;
            if (toCreators && newAssignee is null)
            {
                // The creator decides: the task comes back as Rejected (or stays Submitted / Draft for the creator to act on).
                task.ReturnToCreator(reason, now).ThrowIfFailure();
                db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.ReturnedToCreator, from, task.Status, actor, context.User.UserId(), reason,
                    new { fromEmployeeId = command.FromEmployeeId }));
                returned.Add(task);
                continue;
            }

            var to = newAssignee!;
            task.TransferTo(to.Id, to.UnitId, now).ThrowIfFailure();
            if (task.CreatorEmployeeId == command.FromEmployeeId)
            {
                // The leaver's own task (personal or self-assigned): the new person also takes over as creator, the approver.
                task.TakeOverAsCreator(to.Id);
            }

            await deadlines.ApplyAsync(task, settings, cancellationToken).ConfigureAwait(false);
            await deadlines.ApplyAcceptReminderAsync(task, settings, cancellationToken).ConfigureAwait(false);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.Transferred, from, task.Status, actor, context.User.UserId(), reason,
                new { from = command.FromEmployeeId, to = to.Id }));
            reassigned.Add((task, to.Id));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (task, to) in reassigned)
        {
            await creator.RevokeAsync(task, TaskPermissions.AssigneeRole, command.FromEmployeeId, cancellationToken).ConfigureAwait(false);
            await creator.GrantAsync(task, TaskPermissions.AssigneeRole, to, cancellationToken).ConfigureAwait(false);
            if (task.CreatorEmployeeId == to)
            {
                await creator.RevokeAsync(task, TaskPermissions.CreatorRole, command.FromEmployeeId, cancellationToken).ConfigureAwait(false);
                await creator.GrantAsync(task, TaskPermissions.CreatorRole, to, cancellationToken).ConfigureAwait(false);
            }
        }

        await NotifyAsync(leaver.Value, reassigned, returned, reason, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        await audit.WriteAsync(new AuditEvent("tasks.task.bulk-reassign", true, TaskPermissions.SystemCode, "org.employee", command.FromEmployeeId.ToString(),
            new
            {
                mode = command.Mode,
                to = target?.Id,
                fallback = fallback?.Id,
                reassigned = reassigned.Select(r => new { task = r.Task.Id, to = r.To }),
                returnedToCreators = returned.Select(t => t.Id),
            }, reason), cancellationToken).ConfigureAwait(false);
        return new ReassignmentResultDto(reassigned.Count, returned.Count, 0, tasks.Select(t => t.Id).ToList());
    }

    /// <summary>Open tasks assigned to the employee (all of them, confidential included: the action must not leave tasks behind).</summary>
    private IQueryable<TaskItem> OpenTasksOf(Guid employeeId) =>
        db.Tasks.AsNoTracking().Where(t => t.AssigneeEmployeeId == employeeId && !TaskStatuses.Closed.Contains(t.Status))
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Number);

    /// <summary>Open tasks the employee created for other people (ADR 0169); their own tasks are handled by the assignee reassignment.</summary>
    private IQueryable<TaskItem> OpenTasksCreatedBy(Guid employeeId) =>
        db.Tasks.AsNoTracking().Where(t => t.CreatorEmployeeId == employeeId && t.AssigneeEmployeeId != employeeId && !TaskStatuses.Closed.Contains(t.Status))
            .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Number);

    private async Task<Result<EmployeeReference>> AuthorisedEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await employees.GetAsync(employeeId, ct).ConfigureAwait(false);
        if (employee is null
            || !await access.CanAsync(context.User, TaskPermissions.ReassignmentManage,
                new ResourceRef("org.employee", employee.Id, OwnerEmployeeId: employee.Id, UnitId: employee.UnitId), ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return employee;
    }

    private async Task<EmployeeReference?> ActiveEmployeeAsync(Guid id, CancellationToken ct) =>
        await employees.GetAsync(id, ct).ConfigureAwait(false) is { Status: not "left" } employee ? employee : null;

    private async Task<IReadOnlyList<ReassignmentTaskDto>> ItemsAsync(List<TaskItem> tasks, Guid leaver, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(tasks.Select(t => t.CreatorEmployeeId), ct).ConfigureAwait(false);
        var items = new List<ReassignmentTaskDto>();
        foreach (var t in tasks)
        {
            var readable = await visibility.CanReadAsync(t, context.User, ct).ConfigureAwait(false);
            items.Add(new ReassignmentTaskDto(t.Id, readable ? t.Number : null, readable ? t.TitleAr : null, readable ? t.TitleEn : null, t.Status, t.Template,
                t.Confidential, readable, TaskReader.Person(t.CreatorEmployeeId, people), t.CreatorEmployeeId == leaver, t.DueDate));
        }

        return items;
    }

    /// <summary>One summary per person: the new assignee(s), each creator, the leaver (skipped when the account is inactive).</summary>
    private async Task NotifyAsync(
        EmployeeReference leaver, List<(TaskItem Task, Guid To)> reassigned, List<TaskItem> returned, string reason, CancellationToken ct)
    {
        var names = await reader.PeopleAsync(reassigned.Select(r => r.To).Append(leaver.Id).Append(context.User.EmployeeIdOf() ?? Guid.Empty), ct).ConfigureAwait(false);
        string Name(Guid id, bool ar) => (ar ? names.GetValueOrDefault(id)?.NameAr ?? names.GetValueOrDefault(id)?.NameEn : names.GetValueOrDefault(id)?.NameEn
            ?? names.GetValueOrDefault(id)?.NameAr) ?? string.Empty;
        var actorName = Name(context.User.EmployeeIdOf() ?? Guid.Empty, false);

        Dictionary<string, string> Values(int count, Guid? to) => new()
        {
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
            ["fromAr"] = Name(leaver.Id, true),
            ["fromEn"] = Name(leaver.Id, false),
            ["toAr"] = to is { } t ? Name(t, true) : string.Empty,
            ["toEn"] = to is { } e ? Name(e, false) : string.Empty,
            ["actor"] = actorName,
            ["text"] = reason,
        };

        foreach (var group in reassigned.GroupBy(r => r.To))
        {
            await SendAsync(TaskNotifications.ReassignedToYou, [group.Key], Values(group.Count(), group.Key), "/tasks?tab=assigned", ct).ConfigureAwait(false);
        }

        foreach (var group in reassigned.Where(r => r.Task.CreatorEmployeeId != r.To).GroupBy(r => (r.Task.CreatorEmployeeId, r.To)))
        {
            await SendAsync(TaskNotifications.AssigneeChanged, [group.Key.CreatorEmployeeId], Values(group.Count(), group.Key.To), "/tasks?tab=created", ct)
                .ConfigureAwait(false);
        }

        foreach (var group in returned.GroupBy(t => t.CreatorEmployeeId))
        {
            await SendAsync(TaskNotifications.DecisionNeeded, [group.Key], Values(group.Count(), null), "/tasks?tab=created", ct).ConfigureAwait(false);
        }

        await SendAsync(TaskNotifications.YourTasksReassigned, [leaver.Id], Values(reassigned.Count + returned.Count, null), "/tasks", ct).ConfigureAwait(false);
    }

    private async Task SendAsync(NotificationTemplateDefinition template, IReadOnlyCollection<Guid> employeeIds, Dictionary<string, string> values, string link, CancellationToken ct)
    {
        var recipients = (await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => NotificationRecipient.User(u.Id)).ToList();
        if (recipients.Count > 0)
        {
            await notifications.SendAsync(new NotificationRequest(template.Key, recipients, values, link), db, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// When an employee leaves, the people who may reassign their tasks hear about it (with a link to the action) if open tasks remain
/// (ADR 0040). Nothing is moved automatically: who takes the work over is a person's decision.
/// </summary>
internal sealed class OfferReassignmentWhenEmployeeLeaves(TasksDbContext db, IEmployeeDirectory employees, IUserRoleQuery roles, IUserDirectory users,
    INotificationService notifications) : IEmployeeLifecycleHook
{
    public async Task EmployeeLeftAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var open = await db.Tasks.AsNoTracking()
            .CountAsync(t => (t.AssigneeEmployeeId == employeeId || t.CreatorEmployeeId == employeeId) && !TaskStatuses.Closed.Contains(t.Status), cancellationToken)
            .ConfigureAwait(false);
        if (open == 0 || await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false) is not { } employee)
        {
            return;
        }

        var recipients = new HashSet<Guid>(await roles.UsersWithPermissionAsync(TaskPermissions.ReassignmentManage, cancellationToken).ConfigureAwait(false));
        if (employee.ManagerEmployeeId is { } manager)
        {
            recipients.UnionWith((await users.GetByEmployeesAsync([manager], cancellationToken).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id));
        }

        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(new NotificationRequest(
            TaskNotifications.EmployeeLeftWithTasks.Key,
            recipients.Select(NotificationRecipient.User).ToList(),
            new Dictionary<string, string>
            {
                ["count"] = open.ToString(CultureInfo.InvariantCulture),
                ["fromAr"] = employee.NameAr ?? employee.NameEn ?? string.Empty,
                ["fromEn"] = employee.NameEn ?? employee.NameAr ?? string.Empty,
            },
            $"/settings/tasks/reassign?employee={employeeId:D}"), cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

internal static class ResultExtensions
{
    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error!.Code);
        }
    }
}
