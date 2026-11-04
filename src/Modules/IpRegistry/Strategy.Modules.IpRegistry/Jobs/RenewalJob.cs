using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Modules.IpRegistry.Jobs;

/// <summary>Renewal reminders (default 6 and 3 months before expiry, once each per expiry) and expiry of registered IP (ADR 0087).</summary>
internal sealed class RenewalRunner(IpDbContext db, IpNotifier notifier, ISettingsStore settings, IClock clock)
{
    public async Task<(int Reminded, int Expired)> RunAsync(CancellationToken ct)
    {
        var configuration = await settings.GetAsync<IpSettings>(ct).ConfigureAwait(false);
        var today = clock.Today;
        var horizon = today.AddMonths(configuration.ReminderMonths.DefaultIfEmpty(0).Max());
        var records = await db.Records.Where(r => (r.Status == IpStatuses.Registered || r.Status == IpStatuses.Renewed) && r.ExpiryDate != null && r.ExpiryDate <= horizon)
            .ToListAsync(ct).ConfigureAwait(false);
        int reminded = 0, expired = 0;
        foreach (var record in records)
        {
            var expiry = record.ExpiryDate!.Value;
            if (today >= expiry)
            {
                record.Expire();
                db.Reminders.Add(ReminderEvent.Create(record.Id, "expired", null, expiry, clock.UtcNow));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await notifier.ExpiredAsync(record, ct).ConfigureAwait(false);
                expired++;
                continue;
            }

            var sent = await db.Reminders.Where(e => e.IpRecordId == record.Id && e.Kind == "reminder" && e.Expiry == expiry && e.Months != null)
                .Select(e => e.Months!.Value).ToListAsync(ct).ConfigureAwait(false);
            var due = IpRules.DueReminders(expiry, today, configuration.ReminderMonths, sent);
            if (due.Count == 0)
            {
                continue;
            }

            foreach (var months in due)
            {
                db.Reminders.Add(ReminderEvent.Create(record.Id, "reminder", months, expiry, clock.UtcNow));
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.RenewalAsync(record, due.Min(), ct).ConfigureAwait(false);
            reminded++;
        }

        return (reminded, expired);
    }
}

[Quartz.DisallowConcurrentExecution]
internal sealed class RenewalJob(RequestContext requestContext, RenewalRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "ip.renewals", "تجديد الملكية الفكرية", "IP renewals", "0 30 2 * * ?", typeof(RenewalJob),
        "يرسل تذكيرات تجديد الملكية الفكرية ويغير حالة السجلات المنتهية.", "Sends IP renewal reminders and marks expired records.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (reminded, expired) = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"reminded={reminded} expired={expired}";
    }
}
