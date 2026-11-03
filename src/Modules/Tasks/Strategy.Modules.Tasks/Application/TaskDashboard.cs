using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

public sealed record GetTaskDashboard : IQuery<TaskDashboardDto>;

/// <summary>
/// Task dashboard (SPEC §11.7, §0.4 level 2–3): open / overdue by unit, source, status and priority; average time to accept and to complete; on-time
/// rate; average feedback point; workload per employee — over the tasks the viewer may read (<see cref="TaskVisibility"/>).
/// </summary>
internal sealed class GetTaskDashboardHandler(TaskVisibility visibility, TaskReader reader, IOrgDirectory org, IRequestContext context, IClock clock)
    : IQueryHandler<GetTaskDashboard, TaskDashboardDto>
{
    public async Task<Result<TaskDashboardDto>> HandleAsync(GetTaskDashboard query, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var tasks = (await visibility.QueryAsync(context.User, cancellationToken).ConfigureAwait(false)).Where(t => t.Status != TaskStatuses.Draft);
        var open = await tasks.Where(t => !TaskStatuses.Closed.Contains(t.Status))
            .Select(t => new { t.UnitId, t.SourceCode, t.Status, t.Priority, t.AssigneeEmployeeId, t.DueAt, t.DueSoonAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var since = now.AddDays(-90);
        var closed = await tasks.Where(t => (t.Status == TaskStatuses.Approved || t.Status == TaskStatuses.Done) && t.ClosedAt >= since)
            .Select(t => new { t.TimeToAcceptSeconds, t.TimeToCompleteSeconds, t.CompletedOnTime, t.FeedbackPoint })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        bool Overdue(DateTimeOffset? dueAt, string status) => status != TaskStatuses.OnHold && dueAt is { } due && due < now;
        var byUnit = open.GroupBy(t => t.UnitId?.ToString() ?? "none").Select(g => new CountByDto(g.Key, g.Count(), g.Count(t => Overdue(t.DueAt, t.Status))))
            .OrderByDescending(c => c.Open).ToList();
        var bySource = open.GroupBy(t => t.SourceCode).Select(g => new CountByDto(g.Key, g.Count(), g.Count(t => Overdue(t.DueAt, t.Status)))).OrderByDescending(c => c.Open).ToList();
        var byStatus = open.GroupBy(t => t.Status).Select(g => new CountByDto(g.Key, g.Count(), g.Count(t => Overdue(t.DueAt, t.Status)))).OrderByDescending(c => c.Open).ToList();
        var byPriority = open.GroupBy(t => t.Priority).Select(g => new CountByDto(g.Key, g.Count(), g.Count(t => Overdue(t.DueAt, t.Status))))
            .OrderBy(c => TaskPriorities.All.ToList().IndexOf(c.Key)).ToList();
        var workloadRows = open.GroupBy(t => t.AssigneeEmployeeId)
            .Select(g => (Id: g.Key, Open: g.Count(), Overdue: g.Count(t => Overdue(t.DueAt, t.Status)), OnHold: g.Count(t => t.Status == TaskStatuses.OnHold)))
            .OrderByDescending(r => r.Open).ThenByDescending(r => r.Overdue).Take(20).ToList();
        var people = await reader.PeopleAsync(workloadRows.Select(r => r.Id), cancellationToken).ConfigureAwait(false);

        var unitNames = new Dictionary<string, BilingualName>(StringComparer.Ordinal);
        foreach (var unitId in open.Where(t => t.UnitId is not null).Select(t => t.UnitId!.Value).Distinct())
        {
            if (await org.GetUnitAsync(unitId, cancellationToken).ConfigureAwait(false) is { } unit)
            {
                unitNames[unitId.ToString()] = new BilingualName(unit.NameAr, unit.NameEn);
            }
        }

        static double? Hours(IEnumerable<long?> seconds)
        {
            var values = seconds.Where(s => s is not null).Select(s => s!.Value / 3600d).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }

        var completedWithFlag = closed.Where(c => c.CompletedOnTime is not null).ToList();
        var feedback = closed.Where(c => c.FeedbackPoint is not null).Select(c => (double)c.FeedbackPoint!.Value).ToList();
        return new TaskDashboardDto(
            open.Count,
            open.Count(t => Overdue(t.DueAt, t.Status)),
            open.Count(t => !Overdue(t.DueAt, t.Status) && t.Status != TaskStatuses.OnHold && t.DueSoonAt is { } soon && soon <= now),
            closed.Count,
            Hours(closed.Select(c => c.TimeToAcceptSeconds)),
            Hours(closed.Select(c => c.TimeToCompleteSeconds)),
            completedWithFlag.Count == 0 ? null : Math.Round((double)completedWithFlag.Count(c => c.CompletedOnTime == true) / completedWithFlag.Count, 4),
            feedback.Count == 0 ? null : Math.Round(feedback.Average(), 2),
            byUnit, bySource, byStatus, byPriority,
            workloadRows.Select(r => new WorkloadDto(TaskReader.Person(r.Id, people), r.Open, r.Overdue, r.OnHold)).ToList(),
            unitNames);
    }
}

public sealed record GetEmployeePerformance(Guid EmployeeId) : IQuery<PerformanceDto>;

/// <summary>
/// Employee performance input (SPEC §11.11): time to accept, on-time completion rate and feedback points — for the employee themself, their
/// manager (Team scope) and holders of <c>tasks.performance.read</c> in scope. Confidential tasks count for the employee only.
/// </summary>
internal sealed class GetEmployeePerformanceHandler(
    Infrastructure.TasksDbContext db, IAccessService access, IEmployeeDirectory employees, TaskReader reader, IRequestContext context, IClock clock)
    : IQueryHandler<GetEmployeePerformance, PerformanceDto>
{
    public async Task<Result<PerformanceDto>> HandleAsync(GetEmployeePerformance query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var employee = await employees.GetAsync(query.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound();
        }

        var self = user.EmployeeIdOf() == employee.Id;
        if (!self && !await access.CanAsync(user, TaskPermissions.PerformanceRead,
                new ResourceRef("org.employee", employee.Id, OwnerEmployeeId: employee.Id, UnitId: employee.UnitId), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var since = clock.UtcNow.AddDays(-365);
        var rows = await db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeEmployeeId == employee.Id && (t.Status == TaskStatuses.Approved || t.Status == TaskStatuses.Done) && t.ClosedAt >= since
                && (self || !t.Confidential))
            .Select(t => new { t.CompletedOnTime, t.TimeToAcceptSeconds, t.FeedbackPoint })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([employee.Id], cancellationToken).ConfigureAwait(false);
        var onTime = rows.Count(r => r.CompletedOnTime == true);
        var accept = rows.Where(r => r.TimeToAcceptSeconds is not null).Select(r => r.TimeToAcceptSeconds!.Value / 3600d).ToList();
        var feedback = rows.Where(r => r.FeedbackPoint is not null).Select(r => (double)r.FeedbackPoint!.Value).ToList();
        return new PerformanceDto(
            TaskReader.Person(employee.Id, people), rows.Count, onTime, rows.Count == 0 ? null : Math.Round((double)onTime / rows.Count, 4),
            accept.Count == 0 ? null : Math.Round(accept.Average(), 1), feedback.Count == 0 ? null : Math.Round(feedback.Average(), 2), feedback.Count);
    }
}

public sealed record TaskSettingsDto(TaskSettings Value, uint Version);

public sealed record GetTaskSettings : IQuery<TaskSettingsDto>;

public sealed record SaveTaskSettings(TaskSettings Value, uint Version) : ICommand<TaskSettingsDto>;

/// <summary>Settings → Tasks (<c>tasks.settings.manage</c>; the endpoint checks it, the save is audited as a settings change).</summary>
internal sealed class TaskSettingsHandlers(ISettingsEditor editor, ILookupService lookups)
    : IQueryHandler<GetTaskSettings, TaskSettingsDto>, ICommandHandler<SaveTaskSettings, TaskSettingsDto>
{
    public async Task<Result<TaskSettingsDto>> HandleAsync(GetTaskSettings query, CancellationToken cancellationToken)
    {
        var document = await editor.GetDocumentAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var value = document.Value.TypeTemplates is null ? document.Value with { TypeTemplates = TaskSettings.DefaultTypeTemplates } : document.Value;
        return new TaskSettingsDto(value, document.Version);
    }

    public async Task<Result<TaskSettingsDto>> HandleAsync(SaveTaskSettings command, CancellationToken cancellationToken)
    {
        var types = (await lookups.GetValuesAsync(TaskPermissions.TypeLookup, includeInactive: true, cancellationToken).ConfigureAwait(false)).Select(v => v.Code).ToHashSet();
        var categories = (await lookups.GetValuesAsync(TaskPermissions.CategoryLookup, includeInactive: true, cancellationToken).ConfigureAwait(false)).Select(v => v.Code).ToHashSet();
        var saved = await editor.SaveAsync(command.Value, command.Version, value =>
        {
            var errors = TaskSettings.Validate(value);
            if ((value.TypeTemplates ?? new Dictionary<string, string>()).Keys.Any(k => !types.Contains(k)))
            {
                errors.Add(new FieldError("typeTemplates", "validation.unknownCode", "validation.unknownCode"));
            }

            if ((value.ConfidentialCategories ?? []).Any(c => !categories.Contains(c)))
            {
                errors.Add(new FieldError("confidentialCategories", "validation.unknownCode", "validation.unknownCode"));
            }

            return errors;
        }, cancellationToken).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : new TaskSettingsDto(saved.Value.Value, saved.Value.Version);
    }
}
