using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>Overdue tasks (most overdue first) among the tasks the viewer may read (<see cref="TaskVisibility"/>, confidential rule included).</summary>
internal sealed class TaskListSource(TaskVisibility visibility, IClock clock) : IReportingListSource
{
    public const string OverdueList = "tasks.overdue";

    public string SystemCode => TaskPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(OverdueList, TaskPermissions.SystemCode, "المهام المتأخرة", "Overdue tasks", TaskPermissions.Read),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (code != OverdueList)
        {
            return [];
        }

        var now = clock.UtcNow;
        var tasks = (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => t.Status != TaskStatuses.Draft && t.Status != TaskStatuses.Approved && t.Status != TaskStatuses.Done && t.Status != TaskStatuses.Cancelled
                && t.Status != TaskStatuses.OnHold && t.DueAt != null && t.DueAt < now);
        if (query.UnitIds is { Count: > 0 } units)
        {
            var ids = units.ToList();
            tasks = tasks.Where(t => t.UnitId != null && ids.Contains(t.UnitId.Value));
        }

        var rows = await tasks.OrderBy(t => t.DueAt).ThenBy(t => t.Number).Take(Math.Clamp(query.Take, 1, 100)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(t => new ReportingListItem(TaskPermissions.ResourceType, t.Id, t.Number, t.TitleAr, t.TitleEn, t.Status, "danger",
            Math.Floor((decimal)(now - t.DueAt!.Value).TotalDays), t.DueDate, t.UnitId, $"/tasks/{t.Id:D}")).ToList();
    }
}
