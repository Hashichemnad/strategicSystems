using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>
/// My Work card "my tasks" (docs/design/reporting.md §6): open tasks assigned to me, created by me, followed by me and my overdue ones — the
/// My Tasks tabs — within <see cref="TaskVisibility"/>; items are the existing My Work items (<see cref="TaskMyWork"/>).
/// </summary>
internal sealed class TaskMyWorkCards(TasksDbContext db, TaskVisibility visibility, IClock clock) : IMyWorkCardSource
{
    public const string Code = "tasks.my-tasks";

    public string SystemCode => TaskPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var userId = user.UserId() ?? Guid.Empty;
        var now = clock.UtcNow;
        var open = (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => t.Status != TaskStatuses.Draft && t.Status != TaskStatuses.Approved && t.Status != TaskStatuses.Done && t.Status != TaskStatuses.Cancelled);
        var followUps = db.FollowUps.AsNoTracking();
        var assigned = await open.CountAsync(t => t.AssigneeEmployeeId == me, cancellationToken).ConfigureAwait(false);
        var overdue = await open.CountAsync(t => t.AssigneeEmployeeId == me && t.Status != TaskStatuses.OnHold && t.DueAt != null && t.DueAt < now, cancellationToken)
            .ConfigureAwait(false);
        var created = await open.CountAsync(t => t.AssigneeEmployeeId != me && (t.CreatorEmployeeId == me || t.CreatorUserId == userId || t.EnteredByEmployeeId == me),
            cancellationToken).ConfigureAwait(false);
        var followed = await open.CountAsync(t => followUps.Any(f => f.TaskId == t.Id && f.Active && f.EmployeeId == me), cancellationToken).ConfigureAwait(false);
        if (assigned + created + followed == 0)
        {
            return [];
        }

        var items = await new TaskMyWork(visibility, clock).GetItemsAsync(user, cancellationToken).ConfigureAwait(false);
        var top = items.Take(5).Select(i => new MyWorkCardItem(i.ResourceType, i.ResourceId, i.Number, i.TitleAr, i.TitleEn, i.MessageKey, i.Link, i.DueDate,
            i.MessageKey == "tasks.myWork.overdue")).ToList();
        return
        [
            new MyWorkCard(Code, SystemCode, "myWork.cards.myTasks",
            [
                new MyWorkCount("myWork.counts.assigned", assigned, "info", "/tasks?tab=assigned"),
                new MyWorkCount("myWork.counts.created", created, "neutral", "/tasks?tab=created"),
                new MyWorkCount("myWork.counts.followUp", followed, "neutral", "/tasks?tab=follow-up"),
                new MyWorkCount("myWork.counts.overdue", overdue, overdue > 0 ? "danger" : "neutral", "/tasks?tab=assigned&indicator=overdue"),
            ],
            top, "/tasks", 20),
        ];
    }
}
