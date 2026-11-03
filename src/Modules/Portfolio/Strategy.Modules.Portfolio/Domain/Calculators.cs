namespace Strategy.Modules.Portfolio.Domain;

/// <summary>A prioritisation criterion (SPEC §1.12 configurable weighted scoring; defaults are the four ADoF criteria).</summary>
public sealed record PriorityCriterion(string Code, string LabelAr, string LabelEn, decimal Weight, bool Active = true);

/// <summary>A priority band: the highest band whose minimum (ratio, 1.0 = 100 %) is at or below the score applies.</summary>
public sealed record PriorityBand(string Code, string LabelAr, string LabelEn, decimal Minimum);

/// <summary>Q-P5 (Appendix A): Project when every screening answer is Yes and the estimated budget exceeds the threshold.</summary>
public static class ClassificationCalculator
{
    public static string Classify(RequestValues values, decimal threshold)
    {
        ArgumentNullException.ThrowIfNull(values);
        var allYes = values.ScreeningAnswers.All(a => a == true);
        var overThreshold = values.RequiresBudget == true && values.EstimatedBudget is { } budget && budget > threshold;
        return allYes && overThreshold ? Classifications.Project : Classifications.Initiative;
    }

    /// <summary>The PM Office override when present, otherwise the computed classification.</summary>
    public static string Effective(string computed, string? overridden) => overridden ?? computed;
}

/// <summary>Q-P6: Evaluation Score = Σ(score × weight) ÷ Σweight ÷ 5 (ratio); band from the configured minimums.</summary>
public static class PriorityCalculator
{
    public const int MinScore = 1;
    public const int MaxScore = 5;

    /// <summary>Null when any active criterion has no score (submission requires all), or there is no active criterion.</summary>
    public static decimal? Score(IReadOnlyDictionary<string, int>? scores, IReadOnlyList<PriorityCriterion> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var active = criteria.Where(c => c.Active && c.Weight > 0).ToList();
        if (active.Count == 0 || scores is null)
        {
            return null;
        }

        decimal weighted = 0;
        decimal weights = 0;
        foreach (var criterion in active)
        {
            if (!scores.TryGetValue(criterion.Code, out var score) || score is < MinScore or > MaxScore)
            {
                return null;
            }

            weighted += score * criterion.Weight;
            weights += criterion.Weight;
        }

        return Math.Round(weighted / weights / MaxScore, 4, MidpointRounding.AwayFromZero);
    }

    public static PriorityBand? Band(decimal? score, IReadOnlyList<PriorityBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        return score is { } s ? bands.Where(b => b.Minimum <= s).OrderByDescending(b => b.Minimum).FirstOrDefault() : null;
    }
}
