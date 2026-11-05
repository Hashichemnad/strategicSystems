namespace Strategy.Modules.Risk.Reporting;

/// <summary>
/// Risk movement (SPEC §3.4 E): a residual score compared with an earlier one. One rule for the dashboard ("since the start of the quarter")
/// and the monthly <c>risk.movement</c> facts (each month a risk was re-assessed: its last score that month vs its last score before it). Pure.
/// </summary>
public static class RiskMovement
{
    public const string Improved = "improved";
    public const string Worsened = "worsened";
    public const string Unchanged = "unchanged";

    public static readonly IReadOnlyList<string> All = [Improved, Worsened, Unchanged];

    /// <summary>No baseline or the same score → unchanged; a lower residual score → improved; higher → worsened.</summary>
    public static string Direction(decimal? baseline, decimal current) =>
        baseline is not { } before || before == current ? Unchanged : current < before ? Improved : Worsened;

    /// <summary>
    /// The direction of one risk in each month of <paramref name="fromMonth"/>…<paramref name="toMonth"/> in which it has an assessment and an
    /// earlier one to compare with (a risk first registered that month has no movement). <paramref name="history"/>: its residual scores.
    /// </summary>
    public static IEnumerable<(DateOnly Month, string Direction)> Monthly(IEnumerable<(DateTimeOffset At, decimal Score)> history, DateOnly fromMonth, DateOnly toMonth)
    {
        ArgumentNullException.ThrowIfNull(history);
        var ordered = history.OrderBy(h => h.At).ToList();
        for (var month = new DateOnly(fromMonth.Year, fromMonth.Month, 1); month <= toMonth; month = month.AddMonths(1))
        {
            var start = new DateTimeOffset(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = start.AddMonths(1);
            var inMonth = ordered.Where(h => h.At >= start && h.At < end).ToList();
            var before = ordered.Where(h => h.At < start).ToList();
            if (inMonth.Count == 0 || before.Count == 0)
            {
                continue;
            }

            yield return (month, Direction(before[^1].Score, inMonth[^1].Score));
        }
    }
}
