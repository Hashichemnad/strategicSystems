using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Working-time deadlines of results (ADR 0027 / 0060): the due moment and reminder / escalation moments in the unit's business hours.</summary>
internal sealed class KpiDeadlines(IBusinessHoursCalculator calculator)
{
    private const int MaxDaysScanned = 400;

    /// <summary>End of working time on the due date; a non-working due date rolls forward to the next working day.</summary>
    public async Task<DateTimeOffset> DueAtAsync(Guid? unitId, DateOnly due, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = due;
        for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
        {
            day = day.AddDays(1);
        }

        var working = await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false);
        return working > TimeSpan.Zero
            ? await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, day), working, ct).ConfigureAwait(false)
            : Midnight(zone, day.AddDays(1));
    }

    /// <summary>The start of the working day <paramref name="days"/> working days before (negative) or after the moment's day.</summary>
    public async Task<DateTimeOffset> ShiftWorkingDaysAsync(Guid? unitId, DateTimeOffset from, int days, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime);
        var step = days < 0 ? -1 : 1;
        var remaining = Math.Abs(days);
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(step);
            if (await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return days < 0 ? Midnight(zone, day) : Midnight(zone, day.AddDays(1));
    }

    private Task<TimeSpan> WorkingOnAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct) =>
        calculator.WorkingTimeBetweenAsync(hoursId, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct);

    internal static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    internal static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>"Today" in the installation's time zone (Settings → Regional): window dates are local calendar dates.</summary>
internal sealed class KpiCalendar(IClock clock, ISettingsStore settings)
{
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var regional = await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        var zone = KpiDeadlines.Zone(regional.TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    public static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>
/// Opens due windows (SPEC K2.2): results <c>not_open</c> whose window opened and whose plan is confirmed (when Settings require it) become Open for
/// Update; each owner is notified once with the count. Used by the hourly job and when the Strategy Office confirms plans. Idempotent.
/// </summary>
internal sealed class ResultOpener(KpiDbContext db, KpiCalendar calendar, KpiNotifier notifier, ISettingsStore settings, IClock clock)
{
    public async Task<int> OpenDueAsync(IReadOnlyCollection<Guid>? planIds, CancellationToken ct)
    {
        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        var configuration = await settings.GetAsync<KpiSettings>(ct).ConfigureAwait(false);
        var query = db.Results.Where(r => r.Status == ResultStatuses.NotOpen && r.OpensOn != null && r.OpensOn <= today);
        if (planIds is not null)
        {
            query = query.Where(r => planIds.Contains(r.PlanId));
        }

        var candidates = await query.ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var ids = candidates.Select(r => r.PlanId).Distinct().ToList();
        var plans = await db.Plans.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var years = candidates.Select(r => r.Year).Distinct().ToList();
        var locked = await db.Windows.AsNoTracking().Where(w => years.Contains(w.Year) && w.Locked).Select(w => new { w.Year, w.PeriodCode })
            .ToListAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var opened = new List<KpiResult>();
        foreach (var result in candidates)
        {
            if (!plans.TryGetValue(result.PlanId, out var plan) || (configuration.RequirePlanConfirmation && plan.Status != PlanStatuses.Confirmed)
                || locked.Any(l => l.Year == result.Year && l.PeriodCode == result.PeriodCode))
            {
                continue;
            }

            if (result.Apply(ResultActions.OpenWindow, now).IsSuccess)
            {
                result.MarkOpenedNotified(now);
                opened.Add(result);
            }
        }

        if (opened.Count == 0)
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var cards = await db.Cards.AsNoTracking().Where(c => opened.Select(o => o.CardId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        foreach (var group in opened.Where(o => o.OwnerEmployeeId is not null).GroupBy(o => o.OwnerEmployeeId))
        {
            var first = group.OrderBy(r => r.DueOn).First();
            var card = cards[first.CardId];
            var values = KpiNotifier.Values(card, first, KpiCalendar.Date(first.DueOn), count: group.Count());
            await notifier.SendAsync(KpiNotifications.WindowOpened, [group.Key], values, "/kpi/pending", db, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return opened.Count;
    }
}

/// <summary>Recalculates an annual plan from its approved results (SPEC K5 aggregation) and the card's best year.</summary>
internal sealed class PlanCalculator(KpiDbContext db, ISettingsStore settings)
{
    public async Task RecalculateAsync(Guid planId, CancellationToken ct)
    {
        var plan = await db.Plans.FirstAsync(p => p.Id == planId, ct).ConfigureAwait(false);
        var card = await db.Cards.FirstAsync(c => c.Id == plan.CardId, ct).ConfigureAwait(false);
        var configuration = await settings.GetAsync<KpiSettings>(ct).ConfigureAwait(false);
        var results = await db.Results.Where(r => r.PlanId == plan.Id).OrderBy(r => r.Sort).ToListAsync(ct).ConfigureAwait(false);
        var rule = configuration.RuleFor(card.MeasuringMethodCode);
        var periods = results.Select(r => new PeriodValue(r.Target, r.RangeMin, r.RangeMax, r.Achieve, r.NotApplicable, r.Status == ResultStatuses.Approved)).ToList();
        var aggregate = AnnualAggregator.Aggregate(rule, plan.TargetType, periods, plan.Target, plan.RangeMin, plan.RangeMax);
        var performance = aggregate.Counted == 0
            ? Performance.None
            : PerformanceCalculator.Calculate(plan.TargetType, plan.Direction, aggregate.Target, aggregate.RangeMin, aggregate.RangeMax, aggregate.Achieved);
        var rag = RagBands.Band(performance.Capped, card.GreenFrom ?? configuration.GreenFrom, card.AmberFrom ?? configuration.AmberFrom);
        plan.SetComputed(aggregate, performance, rag, results.Count(r => r.SubmittedAt != null), results.Count(r => r.Status == ResultStatuses.Approved),
            results.Count(r => r.Status == ResultStatuses.Approved && r.NotApplicable));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var best = await db.Plans.Where(p => p.CardId == card.Id && p.CappedPerformance != null).MaxAsync(p => p.CappedPerformance, ct).ConfigureAwait(false);
        card.SetMaxAchieved(best);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
