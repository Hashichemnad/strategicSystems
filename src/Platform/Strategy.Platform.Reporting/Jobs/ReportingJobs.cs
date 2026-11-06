using Microsoft.EntityFrameworkCore;
using Quartz;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Infrastructure;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Jobs;

/// <summary>
/// Keeps the current month's aggregates fresh (ADR 0142): every minute it refreshes the systems marked dirty by saved changes (after a short
/// settle time), and every <see cref="ReportingSettings.RefreshMinutes"/> it refreshes every system.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class ReportingRefreshJob(
    RequestContext requestContext, FactRefresher refresher, ReportingChangeTracker tracker, ReportingDbContext db, ReportingClock clock, ISettingsStore settings)
    : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "reporting.refresh", "تحديث مجاميع لوحات المعلومات", "Dashboard aggregates refresh", "0 * * * * ?", typeof(ReportingRefreshJob),
        "يحدّث مجاميع الشهر الحالي للأنظمة التي تغيّرت بياناتها، ولجميع الأنظمة دورياً.", "Refreshes the current month's aggregates of systems whose data changed, and of every system periodically.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var minutes = Math.Clamp((await settings.GetAsync<ReportingSettings>(cancellationToken).ConfigureAwait(false)).RefreshMinutes, 1, 1440);
        var since = clock.UtcNow.AddMinutes(-minutes);
        var month = ReportPeriod.MonthOf(await clock.TodayAsync(cancellationToken).ConfigureAwait(false));
        var fullDue = !await db.RefreshLogs.AnyAsync(l => l.Kind == "full" && l.At >= since, cancellationToken).ConfigureAwait(false);
        if (fullDue)
        {
            tracker.Take(TimeSpan.Zero);
            var facts = await refresher.RefreshAsync(null, month, month, "full", cancellationToken).ConfigureAwait(false);
            return $"full facts={facts}";
        }

        var dirty = tracker.Take(TimeSpan.FromSeconds(20));
        if (dirty.Count == 0)
        {
            return "nothing to refresh";
        }

        var count = await refresher.RefreshAsync(dirty, month, month, "dirty", cancellationToken).ConfigureAwait(false);
        return $"systems={string.Join(',', dirty)} facts={count}";
    }
}

/// <summary>On the first of the month: recomputes the previous month (flows final, stock frozen as of its last refresh) and the new month.</summary>
[DisallowConcurrentExecution]
internal sealed class ReportingSnapshotJob(RequestContext requestContext, FactRefresher refresher, ReportingClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "reporting.snapshot", "إقفال مجاميع الشهر", "Monthly aggregates close", "0 30 1 1 * ?", typeof(ReportingSnapshotJob),
        "يحسب التدفقات النهائية للشهر السابق ويبدأ الشهر الجديد.", "Computes the previous month's final flows and starts the new month.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var month = ReportPeriod.MonthOf(await clock.TodayAsync(cancellationToken).ConfigureAwait(false));
        var facts = await refresher.RefreshAsync(null, month.AddMonths(-1), month, "snapshot", cancellationToken).ConfigureAwait(false);
        return $"facts={facts}";
    }
}

/// <summary>Recomputes 24 months of flows and the current month (run on demand from the job dashboard; yearly as a safety net).</summary>
[DisallowConcurrentExecution]
internal sealed class ReportingRebuildJob(RequestContext requestContext, FactRefresher refresher, ReportingClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "reporting.rebuild", "إعادة بناء مجاميع لوحات المعلومات", "Dashboard aggregates rebuild", "0 0 3 1 1 ?", typeof(ReportingRebuildJob),
        "يعيد حساب تدفقات ٢٤ شهراً والشهر الحالي (تشغيل يدوي عند الحاجة).", "Recomputes 24 months of flows and the current month (run on demand when needed).");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var month = ReportPeriod.MonthOf(await clock.TodayAsync(cancellationToken).ConfigureAwait(false));
        var facts = await refresher.RefreshAsync(null, month.AddMonths(-23), month, "rebuild", cancellationToken).ConfigureAwait(false);
        return $"facts={facts}";
    }
}

/// <summary>Sends due scheduled dashboards and reports (ADR 0145) and removes delivered files after the retention period.</summary>
[DisallowConcurrentExecution]
internal sealed class ReportingDeliveryJob(
    RequestContext requestContext, DeliveryRunner runner, ReportingDbContext db, IFileService files, ISettingsStore settings, ReportingClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "reporting.deliver", "إرسال اللوحات والتقارير المجدولة", "Scheduled dashboards and reports", "0 0/5 * * * ?", typeof(ReportingDeliveryJob),
        "يرسل اللوحات والتقارير المجدولة لكل مستلم بلغته وصلاحياته وقت الإرسال.", "Sends scheduled dashboards and reports to each recipient in their language, with their access at send time.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var sent = await runner.RunDueAsync(cancellationToken).ConfigureAwait(false);
        var retention = (await settings.GetAsync<ReportingSettings>(cancellationToken).ConfigureAwait(false)).DeliveryRetentionDays;
        var before = clock.UtcNow.AddDays(-Math.Max(1, retention));
        var old = await db.Deliveries.Where(d => d.FileId != null && d.CreatedAt < before).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var delivery in old)
        {
            await files.DeleteAsync(delivery.FileId!.Value, "reporting.retention", cancellationToken).ConfigureAwait(false);
            delivery.FileRemoved();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return $"sent={sent} expiredFiles={old.Count}";
    }
}
