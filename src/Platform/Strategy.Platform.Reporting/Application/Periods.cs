using System.Globalization;

namespace Strategy.Platform.Reporting.Application;

public static class PeriodKinds
{
    public const string Month = "month";
    public const string Quarter = "quarter";
    public const string Year = "year";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Month, Quarter, Year, Custom];
}

/// <summary>
/// A reporting period of whole months (ADR 0142): <see cref="From"/> and <see cref="To"/> are first days of months, inclusive.
/// Quarters and years follow the fiscal start month (Settings → Reporting, default January).
/// </summary>
public readonly record struct ReportPeriod(string Kind, DateOnly From, DateOnly To)
{
    public int Months => ((To.Year - From.Year) * 12) + To.Month - From.Month + 1;

    /// <summary>The preceding period of the same length.</summary>
    public ReportPeriod Previous() => new(Kind, From.AddMonths(-Months), From.AddMonths(-1));

    public string Key => Kind + ":" + From.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ":" + To.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public DateOnly LastDay => To.AddMonths(1).AddDays(-1);

    public IEnumerable<DateOnly> EachMonth()
    {
        for (var m = From; m <= To; m = m.AddMonths(1))
        {
            yield return m;
        }
    }

    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The period of <paramref name="kind"/> that contains <paramref name="today"/>.</summary>
    public static ReportPeriod Current(string kind, DateOnly today, int fiscalStartMonth = 1)
    {
        var month = MonthOf(today);
        return kind switch
        {
            PeriodKinds.Quarter => Containing(month, 3, fiscalStartMonth, PeriodKinds.Quarter),
            PeriodKinds.Year => Containing(month, 12, fiscalStartMonth, PeriodKinds.Year),
            _ => new ReportPeriod(PeriodKinds.Month, month, month),
        };
    }

    private static ReportPeriod Containing(DateOnly month, int length, int fiscalStartMonth, string kind)
    {
        var start = Math.Clamp(fiscalStartMonth, 1, 12);
        var offset = ((month.Month - start) % 12 + 12) % 12;
        var from = month.AddMonths(-(offset % length));
        return new ReportPeriod(kind, from, from.AddMonths(length - 1));
    }

    /// <summary>
    /// Parses <c>from</c> / <c>to</c> as <c>yyyy-MM</c>. Missing values give the current period of <paramref name="kind"/>; a range longer than
    /// 60 months or reversed is refused (null).
    /// </summary>
    public static ReportPeriod? Parse(string? kind, string? from, string? to, DateOnly today, int fiscalStartMonth = 1)
    {
        var k = PeriodKinds.All.Contains(kind ?? string.Empty, StringComparer.Ordinal) ? kind! : PeriodKinds.Month;
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            return Current(k == PeriodKinds.Custom ? PeriodKinds.Month : k, today, fiscalStartMonth) with { Kind = k };
        }

        if (!TryMonth(from, out var f) || !TryMonth(string.IsNullOrWhiteSpace(to) ? from : to, out var t) || t < f)
        {
            return null;
        }

        var period = new ReportPeriod(k, f, t);
        return period.Months > 60 ? null : period;
    }

    private static bool TryMonth(string? value, out DateOnly month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Length >= 7 ? value[..7] : value;
        if (DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            month = parsed;
            return true;
        }

        return false;
    }
}
