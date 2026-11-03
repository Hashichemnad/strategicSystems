using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Calendar.Domain;

namespace Strategy.UnitTests.Calendar;

/// <summary>Working-time arithmetic (ADR 0027). Dates in November 2026: the 2nd is a Monday, the 6th a Friday.</summary>
public sealed class WorkingCalendarTests
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"); // UTC+4, no daylight saving

    private static IEnumerable<DaySchedule> Standard() =>
    [
        .. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(d => new DaySchedule(d, DayModes.Hours, new TimeOnly(7, 30), new TimeOnly(15, 30))),
        new(DayOfWeek.Saturday, DayModes.Closed),
        new(DayOfWeek.Sunday, DayModes.Closed),
    ];

    /// <summary>A Dubai local time as an instant.</summary>
    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(4));

    [Fact]
    public void Adds_working_hours_within_a_day_and_into_the_next()
    {
        var calendar = new WorkingCalendar(Dubai, Standard(), []);
        calendar.AddWorkingTime(Local(2, 7, 30), TimeSpan.FromHours(8)).ShouldBe(Local(2, 15, 30)); // ends exactly at closing time
        calendar.AddWorkingTime(Local(2, 14, 0), TimeSpan.FromHours(2)).ShouldBe(Local(3, 8, 0));
        calendar.AddWorkingTime(Local(2, 5, 0), TimeSpan.FromHours(1)).ShouldBe(Local(2, 8, 30)); // starts before opening
        calendar.AddWorkingTime(Local(2, 18, 0), TimeSpan.FromHours(1)).ShouldBe(Local(3, 8, 30)); // starts after closing
        calendar.AddWorkingTime(Local(2, 9, 0), TimeSpan.Zero).ShouldBe(Local(2, 9, 0));
    }

    [Fact]
    public void Skips_the_weekend()
    {
        var calendar = new WorkingCalendar(Dubai, Standard(), []);
        calendar.AddWorkingTime(Local(6, 13, 0), TimeSpan.FromHours(4)).ShouldBe(Local(9, 9, 0)); // Friday 13:00 + 4 h → Monday 09:00
        calendar.AddWorkingTime(Local(7, 10, 0), TimeSpan.FromHours(16)).ShouldBe(Local(10, 15, 30)); // Saturday → Monday–Tuesday
        calendar.WorkingTimeBetween(Local(6, 13, 0), Local(9, 9, 0)).ShouldBe(TimeSpan.FromHours(4));
        calendar.IsWorkingTime(Local(7, 10, 0)).ShouldBeFalse();
        calendar.IsWorkingTime(Local(6, 10, 0)).ShouldBeTrue();
    }

    [Fact]
    public void Skips_an_all_day_holiday_and_the_hours_of_a_partial_one()
    {
        var holidays = new[]
        {
            new HolidayRule(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4), Recurring: false),                          // Tue–Wed off
            new HolidayRule(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5), false, new TimeOnly(12, 0), new TimeOnly(15, 30)), // Thu afternoon
        };
        var calendar = new WorkingCalendar(Dubai, Standard(), holidays);
        calendar.AddWorkingTime(Local(2, 14, 0), TimeSpan.FromHours(4)).ShouldBe(Local(5, 10, 0)); // Mon 1.5 h, Thu from 07:30 2.5 h
        calendar.AddWorkingTime(Local(5, 11, 0), TimeSpan.FromHours(2)).ShouldBe(Local(6, 8, 30)); // Thu 11–12, then Friday
        calendar.WorkingTimeBetween(Local(2, 7, 30), Local(6, 15, 30)).ShouldBe(TimeSpan.FromHours(8 + 4.5 + 8));
    }

    [Fact]
    public void Recurring_holidays_repeat_every_year_and_29_february_only_in_leap_years()
    {
        var newYear = new HolidayRule(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), Recurring: true);
        newYear.Covers(new DateOnly(2027, 1, 1)).ShouldBeTrue();
        newYear.Covers(new DateOnly(2027, 1, 2)).ShouldBeFalse();
        var leapDay = new HolidayRule(new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29), Recurring: true);
        leapDay.Covers(new DateOnly(2028, 2, 29)).ShouldBeTrue();
        leapDay.Covers(new DateOnly(2027, 2, 28)).ShouldBeFalse();
        leapDay.Covers(new DateOnly(2027, 3, 1)).ShouldBeFalse();

        var calendar = new WorkingCalendar(Dubai, Standard(), [newYear]);
        // Thursday 31 Dec 2026 14:00 + 3 h: 1.5 h on Thursday, Friday 1 Jan is the holiday → Monday 4 Jan 09:00.
        calendar.AddWorkingTime(new DateTimeOffset(2026, 12, 31, 14, 0, 0, TimeSpan.FromHours(4)), TimeSpan.FromHours(3))
            .ShouldBe(new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void Works_in_the_records_time_zone_whatever_the_offset_of_the_input()
    {
        var calendar = new WorkingCalendar(Dubai, Standard(), []);
        // 03:30 UTC on Monday = 07:30 in Dubai: eight working hours end at 11:30 UTC (15:30 Dubai).
        var start = new DateTimeOffset(2026, 11, 2, 3, 30, 0, TimeSpan.Zero);
        calendar.AddWorkingTime(start, TimeSpan.FromHours(8)).ShouldBe(new DateTimeOffset(2026, 11, 2, 11, 30, 0, TimeSpan.Zero));
        // 20:00 UTC on Sunday is already Monday 00:00 in Dubai — still before opening.
        calendar.AddWorkingTime(new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1))
            .ShouldBe(new DateTimeOffset(2026, 11, 2, 4, 30, 0, TimeSpan.Zero));

        // The same schedule in UTC gives a different instant.
        new WorkingCalendar(TimeZoneInfo.Utc, Standard(), []).AddWorkingTime(start, TimeSpan.FromHours(8))
            .ShouldBe(new DateTimeOffset(2026, 11, 2, 15, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Daylight_saving_gaps_do_not_break_the_arithmetic()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); // clocks went back on Sunday 25 Oct 2026
        var allWeek = Enum.GetValues<DayOfWeek>().Select(d => new DaySchedule(d, DayModes.AllDay));
        var calendar = new WorkingCalendar(london, allWeek, []);
        var saturdayNoon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(1));
        // 48 elapsed hours across the change: a 24-hour calendar counts real time.
        calendar.AddWorkingTime(saturdayNoon, TimeSpan.FromHours(48)).ShouldBe(saturdayNoon.AddHours(48));
        calendar.WorkingTimeBetween(saturdayNoon, saturdayNoon.AddHours(48)).ShouldBe(TimeSpan.FromHours(48));
    }

    [Fact]
    public void A_24_hour_record_counts_every_hour_and_holidays_still_apply()
    {
        var allWeek = Enum.GetValues<DayOfWeek>().Select(d => new DaySchedule(d, DayModes.AllDay)).ToList();
        var calendar = new WorkingCalendar(Dubai, allWeek, []);
        calendar.AddWorkingTime(Local(6, 22, 0), TimeSpan.FromHours(30)).ShouldBe(Local(8, 4, 0));
        calendar.IsWorkingTime(Local(8, 3, 0)).ShouldBeTrue();

        var withHoliday = new WorkingCalendar(Dubai, allWeek, [new HolidayRule(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 7), false)]);
        withHoliday.AddWorkingTime(Local(6, 22, 0), TimeSpan.FromHours(30)).ShouldBe(Local(9, 4, 0)); // Saturday removed
    }

    [Fact]
    public void Gives_up_when_there_is_never_any_working_time()
    {
        var closed = Enum.GetValues<DayOfWeek>().Select(d => new DaySchedule(d, DayModes.Closed));
        Should.Throw<InvalidOperationException>(() => new WorkingCalendar(Dubai, closed, []).AddWorkingTime(Local(2, 8), TimeSpan.FromHours(1)));
    }

    public static TheoryData<string, string, string, string, bool> Overlaps => new()
    {
        // a (start..end, hours), b, overlapping?
        { "2026-12-01..2026-12-03", "", "2026-12-03..2026-12-05", "", true },
        { "2026-12-01..2026-12-02", "", "2026-12-03..2026-12-05", "", false },
        { "2026-12-01..2026-12-01", "08:00-12:00", "2026-12-01..2026-12-01", "12:00-15:00", false }, // back to back hours
        { "2026-12-01..2026-12-01", "08:00-12:00", "2026-12-01..2026-12-01", "11:00-15:00", true },
        { "2026-12-01..2026-12-01", "08:00-12:00", "2026-12-01..2026-12-01", "", true },             // all day covers any hours
        { "R2020-12-02..2020-12-03", "", "2027-12-03..2027-12-03", "", true },                       // recurring vs one-time
        { "R2020-12-02..2020-12-03", "", "R2024-12-04..2024-12-04", "", false },
    };

    [Theory]
    [MemberData(nameof(Overlaps))]
    public void Holiday_overlap_rule(string a, string aHours, string b, string bHours, bool overlap) =>
        (CalendarAdministration.FirstOverlap(Values(a, aHours), Values(b, bHours)) is not null).ShouldBe(overlap);

    private static HolidayValues Values(string range, string hours)
    {
        var recurring = range.StartsWith('R');
        var parts = range.TrimStart('R').Split("..");
        var times = hours.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return new HolidayValues("ع", "x", DateOnly.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), times.Length == 0,
            times.Length == 0 ? null : TimeOnly.Parse(times[0], System.Globalization.CultureInfo.InvariantCulture),
            times.Length == 0 ? null : TimeOnly.Parse(times[1], System.Globalization.CultureInfo.InvariantCulture), recurring, true, []);
    }
}
