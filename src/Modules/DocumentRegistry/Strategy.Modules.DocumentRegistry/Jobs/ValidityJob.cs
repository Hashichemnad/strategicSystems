using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Jobs;

/// <summary>
/// Validity (ADR 0084): reminders before expiry (each configured day count once per expiry date) and expiry of published documents without action.
/// Idempotent through <see cref="ValidityEvent"/>.
/// </summary>
internal sealed class ValidityRunner(DocRegDbContext db, DocRegNotifier notifier, DocRegCalendar calendar, ISettingsStore settings, IClock clock)
{
    public async Task<(int Reminded, int Expired)> RunAsync(CancellationToken ct)
    {
        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        var horizon = today.AddDays(configuration.ReminderDays.DefaultIfEmpty(0).Max());
        var documents = await db.Documents.Where(d => d.Status == DocumentStatuses.Published && d.ExpiryDate != null && d.ExpiryDate <= horizon)
            .ToListAsync(ct).ConfigureAwait(false);
        int reminded = 0, expired = 0;
        foreach (var document in documents)
        {
            var expiry = document.ExpiryDate!.Value;
            if (ValidityCalculator.IsExpired(expiry, today))
            {
                document.Expire();
                db.ValidityEvents.Add(ValidityEvent.Create(document.Id, ValidityEventKinds.Expired, null, expiry, expiry, null, null, clock.UtcNow));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await notifier.ExpiredAsync(document, ct).ConfigureAwait(false);
                expired++;
                continue;
            }

            var sent = await db.ValidityEvents.Where(e => e.DocumentId == document.Id && e.Kind == ValidityEventKinds.Reminder && e.NewExpiry == expiry && e.Days != null)
                .Select(e => e.Days!.Value).ToListAsync(ct).ConfigureAwait(false);
            var due = ValidityCalculator.DueReminders(expiry, today, configuration.ReminderDays, sent);
            if (due.Count == 0)
            {
                continue;
            }

            // One notice (the nearest count), every due count recorded.
            foreach (var days in due)
            {
                db.ValidityEvents.Add(ValidityEvent.Create(document.Id, ValidityEventKinds.Reminder, days, expiry, expiry, null, null, clock.UtcNow));
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.ReminderAsync(document, expiry.DayNumber - today.DayNumber, ct).ConfigureAwait(false);
            reminded++;
        }

        return (reminded, expired);
    }
}

[Quartz.DisallowConcurrentExecution]
internal sealed class ValidityJob(RequestContext requestContext, ValidityRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "docreg.validity", "صلاحية الوثائق", "Document validity", "0 15 2 * * ?", typeof(ValidityJob),
        "يرسل تذكيرات انتهاء صلاحية الوثائق ويغير حالة الوثائق المنتهية.", "Sends document expiry reminders and marks expired documents.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (reminded, expired) = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"reminded={reminded} expired={expired}";
    }
}
