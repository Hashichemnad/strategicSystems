using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Platform.Notifications.Application;

/// <summary>My Work card "Notifications": unread count and the latest unread items with their deep links (ADR 0149).</summary>
internal sealed class NotificationsMyWorkCard(NotifyDbContext db) : IMyWorkCardSource
{
    public const string Code = "notifications";

    public string SystemCode => Code;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.UserId() is not { } userId)
        {
            return [];
        }

        var unread = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.ReadAt == null);
        var count = await unread.CountAsync(cancellationToken).ConfigureAwait(false);
        var latest = await unread.OrderByDescending(n => n.CreatedAt).Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = latest.Select(n => new MyWorkCardItem("notifications.item", n.Id, null, n.TitleAr, n.TitleEn, "reporting.myWork.items.notification",
            n.Link ?? "/notifications", DateOnly.FromDateTime(n.CreatedAt.UtcDateTime))).ToList();
        return [new MyWorkCard("notifications.latest", Code, "myWork.cards.notifications", [new MyWorkCount("unread", count, count > 0 ? "info" : "neutral")], items, "/notifications", 800)];
    }
}
