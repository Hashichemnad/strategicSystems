using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Modules.Tasks.Application;

public static class TaskViews
{
    public const string Assigned = "assigned";
    public const string Created = "created";
    public const string FollowUp = "follow-up";
    public const string Team = "team";
    public const string All = "all";

    public static readonly IReadOnlyList<string> Codes = [Assigned, Created, FollowUp, Team, All];
}

public sealed record ListTasks(
    PageRequest Paging,
    string? View = null,
    string? Status = null,
    string? Priority = null,
    string? Indicator = null,
    string? Search = null,
    string? LinkedType = null,
    Guid? LinkedId = null,
    Guid? GroupId = null,
    Guid? ParentId = null,
    DateOnly? DueFrom = null,
    DateOnly? DueTo = null,
    bool OpenOnly = false) : IQuery<PagedResult<TaskListItemDto>>;

public sealed class ListTasksValidator : AbstractValidator<ListTasks>
{
    public ListTasksValidator()
    {
        RuleFor(x => x.Paging.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(x => x.Paging.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(x => x.Paging.SortOrder!.Value.Field)
            .Must(field => ListTasksHandler.SortableFields.Contains(field))
            .When(x => x.Paging.SortOrder is not null)
            .WithErrorCode("validation.unknownSortField")
            .OverridePropertyName("sort");
        RuleFor(x => x.View).Must(v => v is null || TaskViews.Codes.Contains(v)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Status).Must(s => s is null || s.Split(',').All(TaskStatuses.All.Contains)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Priority).Must(p => p is null || TaskPriorities.All.Contains(p)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Indicator).Must(i => i is null || TaskIndicators.All.Contains(i)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.LinkedId).NotNull().When(x => x.LinkedType is not null);
        RuleFor(x => x.DueTo).GreaterThanOrEqualTo(x => x.DueFrom).When(x => x.DueFrom is not null && x.DueTo is not null).WithErrorCode("validation.dateRange");
    }
}

/// <summary>My Tasks lists, Kanban and calendar (SPEC §11.7): views by relation to the user, filters, always within <see cref="TaskVisibility"/>.</summary>
internal sealed class ListTasksHandler(TasksDbContext db, TaskVisibility visibility, TaskReader reader, IRequestContext context, IClock clock)
    : IQueryHandler<ListTasks, PagedResult<TaskListItemDto>>
{
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "number", "dueDate", "priority", "status", "createdAt", "updatedAt", "titleAr", "titleEn" };

    public async Task<Result<PagedResult<TaskListItemDto>>> HandleAsync(ListTasks query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var tasks = query.LinkedType is { } type && query.LinkedId is { } linkedId
            ? await visibility.LinkedQueryAsync(user, type, linkedId, cancellationToken).ConfigureAwait(false)
            : await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false);
        tasks = Filter(tasks, query, user.EmployeeIdOf() ?? Guid.Empty, user.UserId() ?? Guid.Empty, clock.UtcNow);

        var total = await tasks.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await Sort(tasks, query.Paging.SortOrder).Skip(query.Paging.Skip).Take(query.Paging.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<TaskListItemDto>(
            await reader.ListItemsAsync(page, user, cancellationToken).ConfigureAwait(false), query.Paging.Page, query.Paging.PageSize, total);
    }

    private IQueryable<TaskItem> Filter(IQueryable<TaskItem> tasks, ListTasks query, Guid employee, Guid userId, DateTimeOffset now)
    {
        var followed = db.FollowUps.AsNoTracking();
        tasks = query.View switch
        {
            TaskViews.Assigned => tasks.Where(t => t.AssigneeEmployeeId == employee && t.Status != TaskStatuses.Draft),
            TaskViews.Created => tasks.Where(t => t.CreatorEmployeeId == employee || t.CreatorUserId == userId || t.EnteredByEmployeeId == employee),
            TaskViews.FollowUp => tasks.Where(t => followed.Any(f => f.TaskId == t.Id && f.Active && f.EmployeeId == employee)),
            TaskViews.Team => tasks.Where(t => t.AssigneeEmployeeId != employee && t.CreatorEmployeeId != employee && t.Status != TaskStatuses.Draft),
            _ => tasks,
        };

        if (query.Status is { } status)
        {
            var codes = status.Split(',');
            tasks = tasks.Where(t => codes.Contains(t.Status));
        }

        if (query.OpenOnly)
        {
            tasks = tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status));
        }

        if (query.Priority is { } priority)
        {
            tasks = tasks.Where(t => t.Priority == priority);
        }

        tasks = query.Indicator switch
        {
            TaskIndicators.Overdue => tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status) && t.Status != TaskStatuses.OnHold && t.DueAt != null && t.DueAt < now),
            TaskIndicators.DueSoon => tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status) && t.Status != TaskStatuses.OnHold && t.DueAt != null && t.DueAt >= now
                && t.DueSoonAt != null && t.DueSoonAt <= now),
            TaskIndicators.OnHold => tasks.Where(t => t.Status == TaskStatuses.OnHold),
            TaskIndicators.OnTime => tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status) && t.Status != TaskStatuses.OnHold
                && (t.DueAt == null || t.DueSoonAt == null || t.DueSoonAt > now)),
            _ => tasks,
        };

        if (query.GroupId is { } group)
        {
            tasks = tasks.Where(t => t.GroupId == group);
        }

        if (query.ParentId is { } parent)
        {
            tasks = tasks.Where(t => t.ParentTaskId == parent);
        }

        if (query.DueFrom is { } dueFrom)
        {
            tasks = tasks.Where(t => t.DueDate >= dueFrom);
        }

        if (query.DueTo is { } dueTo)
        {
            tasks = tasks.Where(t => t.DueDate <= dueTo);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            tasks = tasks.Where(t => EF.Functions.ILike(t.TitleAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.TitleEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(t.Number ?? string.Empty, pattern, "\\"));
        }

        return tasks;
    }

    private static IQueryable<TaskItem> Sort(IQueryable<TaskItem> tasks, (string Field, bool Descending)? order) =>
        order switch
        {
            ("number", var d) => d ? tasks.OrderByDescending(x => x.Number) : tasks.OrderBy(x => x.Number),
            ("dueDate", var d) => d ? tasks.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.Id) : tasks.OrderBy(x => x.DueDate == null).ThenBy(x => x.DueDate).ThenBy(x => x.Id),
            ("priority", var d) => d
                ? tasks.OrderByDescending(x => x.Priority == TaskPriorities.High ? 3 : x.Priority == TaskPriorities.Medium ? 2 : 1).ThenBy(x => x.DueDate)
                : tasks.OrderBy(x => x.Priority == TaskPriorities.High ? 3 : x.Priority == TaskPriorities.Medium ? 2 : 1).ThenBy(x => x.DueDate),
            ("status", var d) => d ? tasks.OrderByDescending(x => x.Status) : tasks.OrderBy(x => x.Status),
            ("titleAr", var d) => d ? tasks.OrderByDescending(x => x.TitleAr) : tasks.OrderBy(x => x.TitleAr),
            ("titleEn", var d) => d ? tasks.OrderByDescending(x => x.TitleEn) : tasks.OrderBy(x => x.TitleEn),
            ("updatedAt", var d) => d ? tasks.OrderByDescending(x => x.UpdatedAt) : tasks.OrderBy(x => x.UpdatedAt),
            ("createdAt", false) => tasks.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => tasks.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        };

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

public sealed record GetTask(Guid Id) : IQuery<TaskDto>;

internal sealed class GetTaskHandler(TaskVisibility visibility, TaskReader reader, IRequestContext context) : IQueryHandler<GetTask, TaskDto>
{
    public async Task<Result<TaskDto>> HandleAsync(GetTask query, CancellationToken cancellationToken)
    {
        var task = await visibility.FindAsync(query.Id, context.User, tracked: false, cancellationToken).ConfigureAwait(false);
        return task is null ? Error.NotFound() : await reader.DetailAsync(task, context.User, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record GetTaskCounts : IQuery<TaskCountsDto>;

/// <summary>Counts for the My Tasks tabs and the My Work card.</summary>
internal sealed class GetTaskCountsHandler(TasksDbContext db, TaskVisibility visibility, IRequestContext context, IClock clock) : IQueryHandler<GetTaskCounts, TaskCountsDto>
{
    public async Task<Result<TaskCountsDto>> HandleAsync(GetTaskCounts query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var employee = user.EmployeeIdOf() ?? Guid.Empty;
        var userId = user.UserId() ?? Guid.Empty;
        var now = clock.UtcNow;
        var open = (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => !TaskStatuses.Closed.Contains(t.Status));
        var followed = db.FollowUps.AsNoTracking();
        var assigned = open.Where(t => t.AssigneeEmployeeId == employee && t.Status != TaskStatuses.Draft);
        var team = open.Where(t => t.AssigneeEmployeeId != employee && t.CreatorEmployeeId != employee && t.Status != TaskStatuses.Draft);
        return new TaskCountsDto(
            await assigned.CountAsync(cancellationToken).ConfigureAwait(false),
            await assigned.CountAsync(t => t.DueAt != null && t.DueAt < now && t.Status != TaskStatuses.OnHold, cancellationToken).ConfigureAwait(false),
            await open.CountAsync(t => t.Status == TaskStatuses.Submitted && (t.CreatorEmployeeId == employee || t.CreatorUserId == userId), cancellationToken).ConfigureAwait(false),
            await open.CountAsync(t => t.CreatorEmployeeId == employee || t.CreatorUserId == userId || t.EnteredByEmployeeId == employee, cancellationToken).ConfigureAwait(false),
            await open.CountAsync(t => followed.Any(f => f.TaskId == t.Id && f.Active && f.EmployeeId == employee), cancellationToken).ConfigureAwait(false),
            await team.CountAsync(cancellationToken).ConfigureAwait(false),
            await team.CountAsync(t => t.DueAt != null && t.DueAt < now && t.Status != TaskStatuses.OnHold, cancellationToken).ConfigureAwait(false));
    }
}

public sealed record GetTaskTimeline(Guid Id) : IQuery<TimelineDto>;

/// <summary>Timeline (assigned → accepted → paused / restarted → submitted → approved, with durations) and the timesheet periods.</summary>
internal sealed class GetTaskTimelineHandler(TasksDbContext db, TaskVisibility visibility, TaskReader reader, IRequestContext context, IClock clock)
    : IQueryHandler<GetTaskTimeline, TimelineDto>
{
    public async Task<Result<TimelineDto>> HandleAsync(GetTaskTimeline query, CancellationToken cancellationToken)
    {
        var task = await visibility.FindAsync(query.Id, context.User, tracked: false, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return Error.NotFound();
        }

        var events = await db.Events.AsNoTracking().Where(e => e.TaskId == task.Id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(events.Where(e => e.ActorEmployeeId != null).Select(e => e.ActorEmployeeId!.Value), cancellationToken).ConfigureAwait(false);
        DateTimeOffset? previous = null;
        var items = new List<TaskEventDto>();
        foreach (var e in events)
        {
            items.Add(new TaskEventDto(e.Id, e.OccurredAt, e.Event, e.FromStatus, e.ToStatus,
                e.ActorEmployeeId is { } a ? TaskReader.Person(a, people) : null, e.Comment, e.Data,
                previous is { } p ? (long)(e.OccurredAt - p).TotalSeconds : null));
            previous = e.OccurredAt;
        }

        return new TimelineDto(items, Timesheet(task, clock.UtcNow));
    }

    /// <summary>Periods with durations; the running one counts up to now.</summary>
    internal static TimesheetDto Timesheet(TaskItem task, DateTimeOffset now)
    {
        var periods = task.Periods.OrderBy(p => p.StartedAt)
            .Select(p => new TimesheetPeriodDto(p.Id, p.Number, p.StartedAt, p.EndedAt, p.DurationSeconds ?? (long)(now - p.StartedAt).TotalSeconds, p.HoldReason, p.EndEvent))
            .ToList();
        return new TimesheetDto(periods, periods.Sum(p => p.DurationSeconds), task.OpenPeriod is not null);
    }
}
