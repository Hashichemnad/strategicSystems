namespace Strategy.Platform.Calendar.Application;

/// <summary>Day modes of a business-hours record.</summary>
public static class DayModes
{
    public const string Closed = "closed";
    public const string AllDay = "allDay";
    public const string Hours = "hours";

    public static readonly IReadOnlyList<string> All = [Closed, AllDay, Hours];
}

/// <summary>One weekday of a record: closed, 24 hours, or one interval on that day.</summary>
public sealed record DaySchedule(DayOfWeek Day, string Mode, TimeOnly? Start = null, TimeOnly? End = null);

/// <summary>A holiday as the calculator needs it: dates (inclusive), optional hours (null = the whole day), yearly recurrence.</summary>
public sealed record HolidayRule(DateOnly StartDate, DateOnly EndDate, bool Recurring, TimeOnly? StartTime = null, TimeOnly? EndTime = null)
{
    public bool AllDay => StartTime is null || EndTime is null;

    /// <summary>Does the holiday fall on <paramref name="date"/> (recurring: same month/day every year; 29 February only in leap years)?</summary>
    public bool Covers(DateOnly date)
    {
        if (!Recurring)
        {
            return StartDate <= date && date <= EndDate;
        }

        if (!TryOn(date.Year, StartDate, out var start) || !TryOn(date.Year, EndDate, out var end))
        {
            return false;
        }

        return start <= date && date <= end;
    }

    private static bool TryOn(int year, DateOnly template, out DateOnly result)
    {
        result = default;
        if (template.Month == 2 && template.Day == 29 && !DateTime.IsLeapYear(year))
        {
            return false;
        }

        result = new DateOnly(year, template.Month, template.Day);
        return true;
    }
}

/// <summary>
/// Pure working-time arithmetic for one business-hours record (ADR 0027): per local date the day's interval minus holidays, converted to instants
/// with the record's time zone. Deterministic and side-effect free (unit-tested).
/// </summary>
public sealed class WorkingCalendar
{
    /// <summary>How far the calculator looks for working time before giving up (a record whose every day is a holiday).</summary>
    public const int HorizonDays = 3 * 366;

    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<DayOfWeek, DaySchedule> _days;
    private readonly IReadOnlyList<HolidayRule> _holidays;

    public WorkingCalendar(TimeZoneInfo zone, IEnumerable<DaySchedule> days, IEnumerable<HolidayRule> holidays)
    {
        _zone = zone;
        _days = days.ToDictionary(d => d.Day);
        _holidays = [.. holidays];
    }

    /// <summary>The working intervals of a local date, as instants, in order.</summary>
    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> IntervalsOn(DateOnly date)
    {
        if (!_days.TryGetValue(date.DayOfWeek, out var day) || day.Mode == DayModes.Closed)
        {
            return [];
        }

        var intervals = new List<(TimeSpan From, TimeSpan To)>();
        if (day.Mode == DayModes.AllDay)
        {
            intervals.Add((TimeSpan.Zero, TimeSpan.FromDays(1)));
        }
        else if (day.Start is { } start && day.End is { } end && end > start)
        {
            intervals.Add((start.ToTimeSpan(), end.ToTimeSpan()));
        }

        foreach (var holiday in _holidays.Where(h => h.Covers(date)))
        {
            if (holiday.AllDay)
            {
                return [];
            }

            var cutFrom = holiday.StartTime!.Value.ToTimeSpan();
            var cutTo = holiday.EndTime!.Value.ToTimeSpan();
            intervals = intervals.SelectMany(i => Subtract(i, cutFrom, cutTo)).ToList();
        }

        return intervals.Where(i => i.To > i.From)
            .Select(i => (ToInstant(date, i.From), ToInstant(date, i.To)))
            .Where(i => i.Item2 > i.Item1)
            .ToList();
    }

    public DateTimeOffset AddWorkingTime(DateTimeOffset start, TimeSpan duration)
    {
        start = start.ToUniversalTime();
        if (duration <= TimeSpan.Zero)
        {
            return start;
        }

        var remaining = duration;
        var date = LocalDate(start).AddDays(-1); // an interval of the previous local date may still be running
        for (var i = 0; i <= HorizonDays; i++, date = date.AddDays(1))
        {
            foreach (var (from, to) in IntervalsOn(date))
            {
                if (to <= start)
                {
                    continue;
                }

                var begin = from > start ? from : start;
                var available = to - begin;
                if (remaining <= available)
                {
                    return begin + remaining;
                }

                remaining -= available;
            }
        }

        throw new InvalidOperationException("No working time within the calculation horizon.");
    }

    public TimeSpan WorkingTimeBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        var last = LocalDate(to);
        for (var date = LocalDate(from).AddDays(-1); date <= last; date = date.AddDays(1))
        {
            foreach (var (start, end) in IntervalsOn(date))
            {
                var begin = start > from ? start : from;
                var finish = end < to ? end : to;
                if (finish > begin)
                {
                    total += finish - begin;
                }
            }
        }

        return total;
    }

    public bool IsWorkingTime(DateTimeOffset at) =>
        IntervalsOn(LocalDate(at)).Concat(IntervalsOn(LocalDate(at).AddDays(-1))).Any(i => i.Start <= at && at < i.End);

    private DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

    private DateTimeOffset ToInstant(DateOnly date, TimeSpan time)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).Add(time);
        // A local time skipped by a daylight-saving change moves to the first valid moment after the gap.
        for (var guard = 0; _zone.IsInvalidTime(local) && guard < 240; guard++)
        {
            local = local.AddMinutes(1);
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static IEnumerable<(TimeSpan From, TimeSpan To)> Subtract((TimeSpan From, TimeSpan To) interval, TimeSpan cutFrom, TimeSpan cutTo)
    {
        if (cutTo <= interval.From || cutFrom >= interval.To)
        {
            yield return interval;
            yield break;
        }

        if (cutFrom > interval.From)
        {
            yield return (interval.From, cutFrom);
        }

        if (cutTo < interval.To)
        {
            yield return (cutTo, interval.To);
        }
    }
}
