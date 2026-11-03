using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Calendar.Infrastructure;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Calendar.Application;

/// <summary>Raised on every calendar change so cached calendars are rebuilt at the next calculation.</summary>
public static class CalendarChangeSignal
{
    private static long _version;

    public static long Version => Interlocked.Read(ref _version);

    public static void Raise() => Interlocked.Increment(ref _version);
}

/// <summary>Builds a <see cref="WorkingCalendar"/> from a record's stored days.</summary>
internal static class Schedules
{
    public static IReadOnlyList<DaySchedule> ToSchedules(IEnumerable<StoredDay> days) =>
        days.Select(d => new DaySchedule(
            d.Day == 7 ? DayOfWeek.Sunday : (DayOfWeek)d.Day,
            d.Mode,
            TimeOnly.TryParse(d.Start, System.Globalization.CultureInfo.InvariantCulture, out var start) ? start : null,
            TimeOnly.TryParse(d.End, System.Globalization.CultureInfo.InvariantCulture, out var end) ? end : null)).ToList();

    public static TimeZoneInfo? FindZone(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
}

/// <summary>The shared working-time calculator (ADR 0027), with one cached calendar per record.</summary>
internal sealed class BusinessHoursCalculator(CalendarDbContext db, IOrgDirectory org, IMemoryCache cache) : IBusinessHoursCalculator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<BusinessHoursReference> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var record = await db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(b => b.IsDefault, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No default business hours (the calendar seeder creates one).");
        return ToReference(record);
    }

    public async Task<BusinessHoursReference?> GetAsync(Guid businessHoursId, CancellationToken cancellationToken = default)
    {
        var record = await db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessHoursId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : ToReference(record);
    }

    public async Task<IReadOnlyList<BusinessHoursReference>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        (await db.BusinessHours.AsNoTracking().Where(b => b.Active).OrderByDescending(b => b.IsDefault).ThenBy(b => b.NameEn)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).Select(ToReference).ToList();

    public async Task<BusinessHoursReference> ResolveForUnitAsync(Guid? unitId, CancellationToken cancellationToken = default) =>
        (await ResolveWithSourceAsync(unitId, cancellationToken).ConfigureAwait(false)).Record;

    /// <summary>The record for a unit and the unit it was found on (null = the default).</summary>
    public async Task<(BusinessHoursReference Record, Guid? FromUnitId)> ResolveWithSourceAsync(Guid? unitId, CancellationToken cancellationToken)
    {
        if (unitId is { } unit)
        {
            var path = await org.GetPathAsync(unit, cancellationToken: cancellationToken).ConfigureAwait(false);
            var ids = path.Select(p => p.Id).ToList();
            if (ids.Count == 0)
            {
                ids.Add(unit);
            }

            var assignments = await db.UnitBusinessHours.AsNoTracking().Where(a => ids.Contains(a.UnitId) && a.BusinessHoursId != null)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var recordIds = assignments.Select(a => a.BusinessHoursId!.Value).ToList();
            var records = await db.BusinessHours.AsNoTracking().Where(b => recordIds.Contains(b.Id) && b.Active)
                .ToDictionaryAsync(b => b.Id, cancellationToken).ConfigureAwait(false);
            foreach (var id in Enumerable.Reverse(ids))
            {
                if (assignments.FirstOrDefault(a => a.UnitId == id) is { } assignment && records.TryGetValue(assignment.BusinessHoursId!.Value, out var record))
                {
                    return (ToReference(record), id);
                }
            }
        }

        return (await GetDefaultAsync(cancellationToken).ConfigureAwait(false), null);
    }

    public async Task<DateTimeOffset> AddWorkingTimeAsync(Guid businessHoursId, DateTimeOffset start, TimeSpan duration, CancellationToken cancellationToken = default) =>
        (await CalendarAsync(businessHoursId, cancellationToken).ConfigureAwait(false)).AddWorkingTime(start, duration);

    public async Task<TimeSpan> WorkingTimeBetweenAsync(Guid businessHoursId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        (await CalendarAsync(businessHoursId, cancellationToken).ConfigureAwait(false)).WorkingTimeBetween(from, to);

    public async Task<bool> IsWorkingTimeAsync(Guid businessHoursId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        (await CalendarAsync(businessHoursId, cancellationToken).ConfigureAwait(false)).IsWorkingTime(at);

    private async Task<WorkingCalendar> CalendarAsync(Guid businessHoursId, CancellationToken cancellationToken)
    {
        var key = (nameof(WorkingCalendar), businessHoursId, CalendarChangeSignal.Version);
        if (cache.TryGetValue(key, out WorkingCalendar? cached) && cached is not null)
        {
            return cached;
        }

        var record = await db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(b => b.Id == businessHoursId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Business hours {businessHoursId} not found.");
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Active).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(h => h.AppliesTo(businessHoursId)).Select(h => h.ToRule()).ToList();
        var zone = Schedules.FindZone(record.TimeZone) ?? TimeZoneInfo.Utc;
        var calendar = new WorkingCalendar(zone, Schedules.ToSchedules(record.ReadDays()), holidays);
        cache.Set(key, calendar, CacheFor);
        return calendar;
    }

    internal static BusinessHoursReference ToReference(BusinessHours record) =>
        new(record.Id, record.NameAr, record.NameEn, record.TimeZone, record.IsDefault, record.Active);
}
