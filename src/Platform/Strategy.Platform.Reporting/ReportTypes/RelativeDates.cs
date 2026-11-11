using System.Globalization;
using Strategy.Platform.Reporting.Application;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>
/// Relative date filters (ADR 0182): <c>{"operator":"relative","values":["thisQuarter"]}</c> or <c>["lastDays","30"]</c>, resolved at every run
/// to an inclusive date range in the installation's time zone. Weeks start on Monday; quarters and years follow the fiscal start month.
/// </summary>
public static class RelativeDates
{
    public const string Operator = "relative";

    public static readonly IReadOnlyList<string> Codes =
    [
        "today", "yesterday", "tomorrow", "thisWeek", "lastWeek", "nextWeek", "thisMonth", "lastMonth", "nextMonth",
        "thisQuarter", "lastQuarter", "nextQuarter", "thisYear", "lastYear", "nextYear", "lastDays", "nextDays",
    ];

    public static bool IsValid(IReadOnlyList<string> values) =>
        values.Count >= 1 && Codes.Contains(values[0], StringComparer.Ordinal)
        && (values[0] is not ("lastDays" or "nextDays")
            || (values.Count == 2 && int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 3660));

    public static (DateOnly From, DateOnly To) Resolve(IReadOnlyList<string> values, DateOnly today, int fiscalStartMonth)
    {
        ArgumentNullException.ThrowIfNull(values);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        (DateOnly, DateOnly) Period(string kind, int shift)
        {
            var current = ReportPeriod.Current(kind, today, fiscalStartMonth);
            var months = current.Months * shift;
            return (current.From.AddMonths(months), current.To.AddMonths(months + 1).AddDays(-1));
        }

        var days = values.Count > 1 && int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return values[0] switch
        {
            "today" => (today, today),
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "tomorrow" => (today.AddDays(1), today.AddDays(1)),
            "thisWeek" => (monday, monday.AddDays(6)),
            "lastWeek" => (monday.AddDays(-7), monday.AddDays(-1)),
            "nextWeek" => (monday.AddDays(7), monday.AddDays(13)),
            "thisMonth" => Period(PeriodKinds.Month, 0),
            "lastMonth" => Period(PeriodKinds.Month, -1),
            "nextMonth" => Period(PeriodKinds.Month, 1),
            "thisQuarter" => Period(PeriodKinds.Quarter, 0),
            "lastQuarter" => Period(PeriodKinds.Quarter, -1),
            "nextQuarter" => Period(PeriodKinds.Quarter, 1),
            "thisYear" => Period(PeriodKinds.Year, 0),
            "lastYear" => Period(PeriodKinds.Year, -1),
            "nextYear" => Period(PeriodKinds.Year, 1),
            "lastDays" => (today.AddDays(-days), today),
            "nextDays" => (today, today.AddDays(days)),
            _ => throw new ArgumentException($"Unknown relative date '{values[0]}'.", nameof(values)),
        };
    }

    /// <summary>The <c>between</c> filter values for a field of <paramref name="type"/> (date-times as UTC instants of the local days).</summary>
    public static IReadOnlyList<string> BetweenValues(DatasetFieldType type, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (type != DatasetFieldType.DateTime)
        {
            return [from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)];
        }

        static DateTimeOffset Utc(DateOnly date, TimeZoneInfo zone) =>
            new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
        return [Utc(from, zone).ToString("o", CultureInfo.InvariantCulture), Utc(to.AddDays(1), zone).AddTicks(-10).ToString("o", CultureInfo.InvariantCulture)];
    }
}
