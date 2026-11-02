using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Daily: one bilingual summary e-mail per user who chose the digest, listing the items waiting for it (idempotent: items are marked).</summary>
internal sealed class DigestJob(RequestContext requestContext, NotifyDbContext db, INotificationService notifications, IClock clock)
    : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "notifications.digest", "الملخص اليومي للإشعارات", "Daily notification digest", "0 20 7 * * ?", typeof(DigestJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var waiting = await db.Notifications.Where(n => n.AwaitingDigest && n.DigestedAt == null).OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        foreach (var group in waiting.GroupBy(n => n.UserId))
        {
            var items = string.Join(" · ", group.Take(20).Select(n => $"{n.TitleEn} / {n.TitleAr}"));
            await notifications.SendAsync(new NotificationRequest(
                NotificationTemplates.Digest.Key,
                [NotificationRecipient.User(group.Key)],
                new Dictionary<string, string>
                {
                    ["count"] = group.Count().ToString(CultureInfo.InvariantCulture),
                    ["items"] = items,
                },
                "/notifications",
                NotificationChannels.Email), db, cancellationToken).ConfigureAwait(false);
            foreach (var item in group)
            {
                item.DigestedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return $"users={waiting.Select(n => n.UserId).Distinct().Count()} items={waiting.Count}";
    }
}
