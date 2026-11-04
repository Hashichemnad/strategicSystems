using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Jobs;

/// <summary>
/// Period windows (SPEC K2, ADR 0060): opens due windows, reminds owners N working days before due, sends the overdue notice and escalates to the
/// head of the responsible unit M working days after due. Each step once per result (stamps); one transaction per result.
/// </summary>
internal sealed class WindowRunner(KpiDbContext db, IServiceScopeFactory scopes, ISettingsStore settings, IClock clock)
{
    public async Task<(int Opened, int Reminded, int Overdue, int Escalated)> RunAsync(CancellationToken ct)
    {
        int opened;
        await using (var scope = JobScope.Create(scopes))
        {
            opened = await scope.ServiceProvider.GetRequiredService<ResultOpener>().OpenDueAsync(null, ct).ConfigureAwait(false);
        }

        var configuration = await settings.GetAsync<KpiSettings>(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var candidates = await db.Results.AsNoTracking()
            .Where(r => (r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned) && r.DueAt != null
                && (r.RemindedAt == null || r.OverdueNotifiedAt == null || r.EscalatedAt == null))
            .Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        int reminded = 0, overdue = 0, escalated = 0;
        foreach (var id in candidates)
        {
            await using var scope = JobScope.Create(scopes);
            var scoped = scope.ServiceProvider.GetRequiredService<KpiDbContext>();
            var deadlines = scope.ServiceProvider.GetRequiredService<KpiDeadlines>();
            var notifier = scope.ServiceProvider.GetRequiredService<KpiNotifier>();
            var reader = scope.ServiceProvider.GetRequiredService<KpiReader>();
            var result = await scoped.Results.FirstAsync(r => r.Id == id, ct).ConfigureAwait(false);
            var card = await scoped.Cards.AsNoTracking().FirstAsync(c => c.Id == result.CardId, ct).ConfigureAwait(false);
            var due = result.DueAt!.Value;
            var link = $"/kpi/results/{result.Id:D}";
            var values = KpiNotifier.Values(card, result, KpiCalendar.Date(result.DueOn));
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var changed = false;
            if (now <= due && result.RemindedAt is null
                && now >= await deadlines.ShiftWorkingDaysAsync(result.OwnerUnitId, due, -configuration.ReminderWorkingDaysBefore, ct).ConfigureAwait(false))
            {
                result.MarkReminded(now);
                await notifier.SendAsync(KpiNotifications.Reminder, [result.OwnerEmployeeId], values, link, scoped, ct).ConfigureAwait(false);
                reminded++;
                changed = true;
            }

            if (now > due && result.OverdueNotifiedAt is null)
            {
                result.MarkOverdueNotified(now);
                result.MarkReminded(result.RemindedAt ?? now);
                await notifier.SendAsync(KpiNotifications.Overdue, [result.OwnerEmployeeId], values, link, scoped, ct).ConfigureAwait(false);
                overdue++;
                changed = true;
            }

            if (now > due && result.EscalatedAt is null
                && now >= await deadlines.ShiftWorkingDaysAsync(result.OwnerUnitId, due, configuration.EscalationWorkingDaysAfter, ct).ConfigureAwait(false))
            {
                result.MarkEscalated(now);
                var head = result.OwnerUnitId is { } unit ? await reader.HeadOfUnitAsync(unit, ct).ConfigureAwait(false) : null;
                await notifier.SendAsync(KpiNotifications.Escalation, [head], values, link, scoped, ct).ConfigureAwait(false);
                escalated++;
                changed = true;
            }

            if (changed)
            {
                await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        return (opened, reminded, overdue, escalated);
    }
}

[DisallowConcurrentExecution]
internal sealed class PeriodWindowsJob(RequestContext requestContext, WindowRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "kpi.period-windows", "فترات إدخال نتائج المؤشرات", "KPI period windows", "0 5 * * * ?", typeof(PeriodWindowsJob),
        "يفتح فترات إدخال النتائج ويرسل التذكيرات وإشعارات التأخر والتصعيد لمدير الوحدة.",
        "Opens result windows and sends reminders, overdue notices and escalations to the unit manager.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (opened, reminded, overdue, escalated) = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"opened={opened} reminded={reminded} overdue={overdue} escalated={escalated}";
    }
}

/// <summary>A child scope that runs as the job (system principal, channel job): each unit of work has its own DbContexts and transaction.</summary>
internal static class JobScope
{
    public static AsyncServiceScope Create(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
