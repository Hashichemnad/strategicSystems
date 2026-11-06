using System.Globalization;
using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Risk.Domain;

/// <summary>Scoring methods (SPEC §3.3).</summary>
public static class ScoringMethods
{
    public const string Multiplicative = "multiplicative";
    public const string Additive = "additive";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<string> All = [Multiplicative, Additive, Custom];
}

/// <summary>How a Portfolio project risk (control evaluation, no residual L / I) gets its residual score (ADR 0101).</summary>
public static class ProjectResidualRules
{
    /// <summary>score × (11 − C) ÷ 10 (the Phase 9 rule, ADR 0074).</summary>
    public const string ControlReduction = "control-reduction";

    /// <summary>The custom formula with <c>C</c> = the control evaluation.</summary>
    public const string Formula = "formula";

    public static readonly IReadOnlyList<string> All = [ControlReduction, Formula];
}

/// <summary>Residual statuses (SPEC §3.3 default mapping).</summary>
public static class ResidualStatuses
{
    public const string NoIssue = "no-issue";
    public const string ContinuousReview = "continuous-review";
    public const string ActiveManagement = "active-management";

    public static readonly IReadOnlyList<string> All = [NoIssue, ContinuousReview, ActiveManagement];
}

public sealed record LikelihoodLevel(int Value, string? LabelAr, string? LabelEn, string? DescriptionAr = null, string? DescriptionEn = null);

public sealed record ImpactArea(string Code, string? LabelAr, string? LabelEn);

public sealed record AreaCriterion(string? Ar, string? En);

public sealed record ImpactLevel(int Value, string? LabelAr, string? LabelEn, IReadOnlyDictionary<string, AreaCriterion>? Criteria = null);

public sealed record RatingBand(string Code, string? LabelAr, string? LabelEn, string Color, decimal Min, decimal Max, string ResidualStatus);

/// <summary>A control effectiveness level; <see cref="Factor"/> is the formula variable <c>C</c> (0–10).</summary>
public sealed record EffectivenessLevel(string Code, string? LabelAr, string? LabelEn, decimal Factor);

/// <summary>The whole framework (one immutable version).</summary>
public sealed record FrameworkContent(
    string ScoringMethod,
    string? Formula,
    IReadOnlyList<LikelihoodLevel> Likelihood,
    IReadOnlyList<ImpactArea> ImpactAreas,
    IReadOnlyList<ImpactLevel> Impact,
    IReadOnlyList<RatingBand> Bands,
    IReadOnlyList<EffectivenessLevel> ControlEffectiveness,
    IReadOnlyDictionary<string, int> ReviewFrequencyMonths,
    string ProjectResidualRule = ProjectResidualRules.ControlReduction);

/// <summary>A saved framework version (ADR 0100). Never changed after it is written.</summary>
[Audited(EntityType)]
public sealed class FrameworkVersion : AuditableEntity
{
    public const string EntityType = "risk.framework";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private FrameworkVersion()
    {
    }

    public int VersionNo { get; private set; }

    public string Content { get; private set; } = "{}";

    public string? Reason { get; private set; }

    public string Recalculation { get; private set; } = Contracts.RiskRecalculation.Now;

    public static FrameworkVersion Create(int versionNo, FrameworkContent content, string? reason, string recalculation) => new()
    {
        VersionNo = versionNo,
        Content = JsonSerializer.Serialize(content, Json),
        Reason = reason,
        Recalculation = recalculation,
    };

    public FrameworkContent Read() => JsonSerializer.Deserialize<FrameworkContent>(Content, Json)!;
}

/// <summary>The SPEC §3.3 defaults (version 1).</summary>
public static class DefaultFramework
{
    public static FrameworkContent Content { get; } = new(
        ScoringMethods.Multiplicative,
        null,
        [
            new(1, "نادر", "Rare", "قد يحدث في ظروف استثنائية فقط", "May occur only in exceptional circumstances"),
            new(2, "غير مرجح", "Unlikely", "قد يحدث في وقت ما", "Could occur at some time"),
            new(3, "محتمل", "Possible", "من المحتمل أن يحدث في وقت ما", "Might occur at some time"),
            new(4, "مرجح", "Likely", "سيحدث على الأرجح في معظم الظروف", "Will probably occur in most circumstances"),
            new(5, "شبه مؤكد", "Almost certain", "متوقع أن يحدث في معظم الظروف", "Expected to occur in most circumstances"),
        ],
        [
            new("financial", "مالي", "Financial"), new("reputation", "السمعة", "Reputation"), new("service", "الخدمات", "Service delivery"),
            new("compliance", "الالتزام", "Compliance"), new("safety", "السلامة", "Health & safety"),
        ],
        [
            Level(1, "ضئيل", "Insignificant", ("أقل من 50 ألف درهم", "Below AED 50k"), ("تأثير داخلي محدود", "Limited internal impact"),
                ("تأخير أقل من يوم", "Delay under one day"), ("مخالفة إجرائية بسيطة", "Minor procedural breach"), ("لا إصابات", "No injuries")),
            Level(2, "طفيف", "Minor", ("50 – 250 ألف درهم", "AED 50k – 250k"), ("تغطية إعلامية محلية محدودة", "Limited local media coverage"),
                ("تأخير يوم إلى ثلاثة أيام", "Delay of 1–3 days"), ("ملاحظة من جهة رقابية", "Observation by a regulator"), ("إسعافات أولية", "First aid")),
            Level(3, "متوسط", "Moderate", ("250 ألف – مليون درهم", "AED 250k – 1m"), ("تغطية إعلامية محلية", "Local media coverage"),
                ("توقف خدمة رئيسية حتى أسبوع", "Key service down up to a week"), ("مخالفة تتطلب إجراءً تصحيحياً", "Breach requiring corrective action"),
                ("إصابة تتطلب علاجاً طبياً", "Injury requiring medical treatment")),
            Level(4, "كبير", "Major", ("1 – 5 ملايين درهم", "AED 1m – 5m"), ("تغطية إعلامية وطنية", "National media coverage"),
                ("توقف خدمة رئيسية حتى شهر", "Key service down up to a month"), ("غرامة أو عقوبة", "Fine or sanction"), ("إصابة خطيرة", "Serious injury")),
            Level(5, "كارثي", "Catastrophic", ("أكثر من 5 ملايين درهم", "Above AED 5m"), ("تغطية إعلامية دولية وفقدان الثقة", "International coverage, loss of trust"),
                ("توقف الخدمات لأكثر من شهر", "Services down over a month"), ("إجراء قانوني جسيم", "Major legal action"), ("وفاة", "Fatality")),
        ],
        [
            new("low", "منخفض", "Low", "#16a34a", 1, 4, ResidualStatuses.NoIssue),
            new("medium", "متوسط", "Medium", "#eab308", 5, 9, ResidualStatuses.ContinuousReview),
            new("high", "مرتفع", "High", "#f97316", 10, 16, ResidualStatuses.ActiveManagement),
            new("critical", "حرج", "Critical", "#dc2626", 20, 25, ResidualStatuses.ActiveManagement),
        ],
        [
            new("effective", "فعالة", "Effective", 8),
            new("partially-effective", "فعالة جزئياً", "Partially effective", 5),
            new("ineffective", "غير فعالة", "Ineffective", 1),
        ],
        new Dictionary<string, int> { ["critical"] = 1, ["high"] = 1, ["medium"] = 3, ["low"] = 6 });

    private static ImpactLevel Level(int value, string ar, string en, params (string Ar, string En)[] criteria)
    {
        string[] areas = ["financial", "reputation", "service", "compliance", "safety"];
        return new ImpactLevel(value, ar, en, areas.Zip(criteria).ToDictionary(x => x.First, x => new AreaCriterion(x.Second.Ar, x.Second.En)));
    }
}

/// <summary>
/// Scores and bands for one framework version (ADR 0100) — the single scoring implementation for enterprise and project risks
/// (CLAUDE.md §15). Pure and deterministic.
/// </summary>
public sealed class RiskScorer
{
    public static readonly IReadOnlyList<ExpressionVariable> Variables =
    [
        new("L", ExpressionValueType.Number, "الاحتمالية", "Likelihood"),
        new("I", ExpressionValueType.Number, "الأثر", "Impact"),
        new("C", ExpressionValueType.Number, "فعالية الضوابط", "Control effectiveness (0–10)"),
    ];

    private readonly CompiledExpression? _formula;

    public RiskScorer(FrameworkContent content)
    {
        Content = content;
        if (content.ScoringMethod == ScoringMethods.Custom)
        {
            var compiled = ExpressionEngine.Compile(content.Formula, Variables.ToList(), ExpressionValueType.Number);
            _formula = compiled.Success ? compiled.Expression : throw new InvalidOperationException("Invalid risk formula.");
        }
    }

    public FrameworkContent Content { get; }

    public int MaxLikelihood => Content.Likelihood.Count == 0 ? 5 : Content.Likelihood.Max(l => l.Value);

    public int MaxImpact => Content.Impact.Count == 0 ? 5 : Content.Impact.Max(l => l.Value);

    /// <summary>The score of likelihood <paramref name="l"/> and impact <paramref name="i"/> with control factor <paramref name="c"/> (0 = none).</summary>
    public decimal Score(int l, int i, decimal? c = null)
    {
        var value = Content.ScoringMethod switch
        {
            ScoringMethods.Additive => l + i,
            ScoringMethods.Custom => Evaluate(l, i, c ?? 0m),
            _ => (decimal)l * i,
        };
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private decimal Evaluate(int l, int i, decimal c)
    {
        var result = _formula!.Evaluate(new Dictionary<string, object?> { ["L"] = (decimal)l, ["I"] = (decimal)i, ["C"] = c });
        return result is { Success: true, Value: decimal d } ? d : throw new InvalidOperationException("Risk formula evaluation failed.");
    }

    /// <summary>
    /// The band whose range contains the score; a score between two bands takes the higher one (ADR 0100); below the first → the first,
    /// above the last → the last.
    /// </summary>
    public RatingBand Band(decimal score) => BandMapper.Map(Content.Bands, score);

    public string ResidualStatusOf(string bandCode) => Content.Bands.FirstOrDefault(b => b.Code == bandCode)?.ResidualStatus ?? ResidualStatuses.ContinuousReview;

    public decimal? Factor(string? effectivenessCode) => effectivenessCode is null ? null : Content.ControlEffectiveness.FirstOrDefault(e => e.Code == effectivenessCode)?.Factor;

    public bool IsLikelihood(int value) => Content.Likelihood.Any(l => l.Value == value);

    public bool IsImpact(int value) => Content.Impact.Any(l => l.Value == value);

    public int ReviewMonths(string bandCode) => Content.ReviewFrequencyMonths.TryGetValue(bandCode, out var months) && months > 0 ? months : 3;

    /// <summary>Lowest effectiveness level (the one CAP hears about, ADR 0106).</summary>
    public string? LowestEffectiveness => Content.ControlEffectiveness.OrderBy(e => e.Factor).FirstOrDefault()?.Code;

    /// <summary>
    /// Overall control effectiveness of a risk (design §5.4): the lower median of the rated controls' factors, i.e. one of the scale's levels;
    /// none when no control is rated.
    /// </summary>
    public string? OverallEffectiveness(IEnumerable<string?> controlRatings)
    {
        var levels = controlRatings.Select(c => Content.ControlEffectiveness.FirstOrDefault(e => e.Code == c)).Where(e => e is not null).Select(e => e!)
            .OrderBy(e => e.Factor).ToList();
        return levels.Count == 0 ? null : levels[(levels.Count - 1) / 2].Code;
    }

    /// <summary>A Portfolio project risk (ADR 0101): inherent with C = 0; residual by the project residual rule.</summary>
    public (decimal Inherent, RatingBand InherentBand, decimal Residual, RatingBand ResidualBand) ScoreProject(int probability, int impact, int? controlEvaluation)
    {
        var l = Math.Clamp(probability, 1, MaxLikelihood);
        var i = Math.Clamp(impact, 1, MaxImpact);
        var inherent = Score(l, i, 0);
        decimal residual;
        if (controlEvaluation is not { } control)
        {
            residual = inherent;
        }
        else if (Content.ProjectResidualRule == ProjectResidualRules.Formula && Content.ScoringMethod == ScoringMethods.Custom)
        {
            residual = Score(l, i, Math.Clamp(control, 1, 10));
        }
        else
        {
            residual = decimal.Round(inherent * (11 - Math.Clamp(control, 1, 10)) / 10m, 2, MidpointRounding.AwayFromZero);
        }

        return (inherent, Band(inherent), residual, Band(residual));
    }

    /// <summary>The matrix (likelihood rows × impact columns) for the preview and the mechanism page.</summary>
    public IReadOnlyList<MatrixCell> Matrix()
    {
        var cells = new List<MatrixCell>();
        foreach (var l in Content.Likelihood.OrderBy(x => x.Value))
        {
            foreach (var i in Content.Impact.OrderBy(x => x.Value))
            {
                var score = Score(l.Value, i.Value, 0);
                cells.Add(new MatrixCell(l.Value, i.Value, score, Band(score).Code));
            }
        }

        return cells;
    }
}

public sealed record MatrixCell(int Likelihood, int Impact, decimal Score, string Band);

public static class BandMapper
{
    public static RatingBand Map(IReadOnlyList<RatingBand> bands, decimal score)
    {
        var ordered = bands.OrderBy(b => b.Min).ToList();
        foreach (var band in ordered)
        {
            if (score <= band.Max)
            {
                return band; // inside the band, or in the gap below it (→ the higher band)
            }
        }

        return ordered[^1];
    }
}

/// <summary>Framework validation (ADR 0100) — keys AR / EN, field paths for the editor. Pure.</summary>
public static class FrameworkRules
{
    public static readonly IReadOnlyList<int> ReviewMonthsAllowed = [1, 2, 3, 4, 6, 12];

    public static List<FieldError> Validate(FrameworkContent c)
    {
        if (c is null)
        {
            return [new FieldError("content", "validation.required", "validation.required")];
        }

        var errors = new List<FieldError>();
        void Add(string field, string key, Dictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        if (!ScoringMethods.All.Contains(c.ScoringMethod))
        {
            Add("scoringMethod", "validation.invalid");
        }

        if (!ProjectResidualRules.All.Contains(c.ProjectResidualRule))
        {
            Add("projectResidualRule", "validation.invalid");
        }

        Scale(errors, "likelihood", c.Likelihood.Select(l => l.Value).ToList(), c.Likelihood.Select(l => (l.LabelAr, l.LabelEn)).ToList());
        Scale(errors, "impact", c.Impact.Select(l => l.Value).ToList(), c.Impact.Select(l => (l.LabelAr, l.LabelEn)).ToList());
        if (c.ImpactAreas.Select(a => a.Code).Distinct().Count() != c.ImpactAreas.Count || c.ImpactAreas.Any(a => string.IsNullOrWhiteSpace(a.Code)))
        {
            Add("impactAreas", "risk.errors.duplicateCodes");
        }

        if (c.Bands.Count < 2)
        {
            Add("bands", "risk.errors.bandsMinimum");
        }

        if (c.Bands.Select(b => b.Code).Distinct().Count() != c.Bands.Count || c.Bands.Any(b => string.IsNullOrWhiteSpace(b.Code)))
        {
            Add("bands", "risk.errors.duplicateCodes");
        }

        for (var n = 0; n < c.Bands.Count; n++)
        {
            var band = c.Bands[n];
            if (band.Min > band.Max || band.Min < 0)
            {
                Add($"bands[{n}].min", "risk.errors.bandRange");
            }

            if (n > 0 && band.Min <= c.Bands[n - 1].Max)
            {
                Add($"bands[{n}].min", "risk.errors.bandsOverlap");
            }

            if (!ResidualStatuses.All.Contains(band.ResidualStatus))
            {
                Add($"bands[{n}].residualStatus", "validation.invalid");
            }

            if (string.IsNullOrWhiteSpace(band.LabelAr) && string.IsNullOrWhiteSpace(band.LabelEn))
            {
                Add($"bands[{n}].labelEn", "validation.atLeastOneLanguage");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(band.Color ?? string.Empty, "^#[0-9a-fA-F]{6}$"))
            {
                Add($"bands[{n}].color", "validation.invalid");
            }
        }

        if (c.ControlEffectiveness.Count < 2 || c.ControlEffectiveness.Select(e => e.Code).Distinct().Count() != c.ControlEffectiveness.Count)
        {
            Add("controlEffectiveness", "risk.errors.effectivenessScale");
        }

        if (c.ControlEffectiveness.Any(e => e.Factor is < 0 or > 10))
        {
            Add("controlEffectiveness", "risk.errors.effectivenessFactor");
        }

        foreach (var band in c.Bands)
        {
            if (!c.ReviewFrequencyMonths.TryGetValue(band.Code, out var months) || !ReviewMonthsAllowed.Contains(months))
            {
                Add($"reviewFrequencyMonths.{band.Code}", "risk.errors.reviewFrequency");
            }
        }

        if (c.ScoringMethod == ScoringMethods.Custom && errors.Count == 0)
        {
            Formula(errors, c);
        }

        return errors;
    }

    private static void Scale(List<FieldError> errors, string field, List<int> values, List<(string? Ar, string? En)> labels)
    {
        var ordered = values.OrderBy(v => v).ToList();
        if (values.Count is < 3 or > 10 || ordered[0] != 1 || ordered.Where((v, n) => v != n + 1).Any())
        {
            errors.Add(new FieldError(field, "risk.errors.scale", "risk.errors.scale"));
        }

        if (labels.Any(l => string.IsNullOrWhiteSpace(l.Ar) && string.IsNullOrWhiteSpace(l.En)))
        {
            errors.Add(new FieldError(field, "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }
    }

    /// <summary>Compiles the formula and evaluates it on every cell with C = 0 and C = 10: it must give a finite, non-negative number.</summary>
    private static void Formula(List<FieldError> errors, FrameworkContent c)
    {
        var compiled = ExpressionEngine.Compile(c.Formula, RiskScorer.Variables.ToList(), ExpressionValueType.Number);
        if (!compiled.Success)
        {
            var e = compiled.Errors.Count > 0 ? compiled.Errors[0] : null;
            errors.Add(new FieldError("formula", e?.Code ?? "expressions.invalid", e?.MessageKey ?? "expressions.errors.invalid",
                new Dictionary<string, object?> { ["position"] = e?.Position }));
            return;
        }

        foreach (var l in c.Likelihood)
        {
            foreach (var i in c.Impact)
            {
                foreach (var factor in new[] { 0m, 10m })
                {
                    var r = compiled.Expression!.Evaluate(new Dictionary<string, object?> { ["L"] = (decimal)l.Value, ["I"] = (decimal)i.Value, ["C"] = factor });
                    if (!r.Success || r.Value is not decimal d || d < 0)
                    {
                        errors.Add(new FieldError("formula", "risk.errors.formulaCell", "risk.errors.formulaCell",
                            new Dictionary<string, object?>
                            {
                                ["likelihood"] = l.Value,
                                ["impact"] = i.Value,
                                ["c"] = factor.ToString(CultureInfo.InvariantCulture),
                            }));
                        return;
                    }
                }
            }
        }
    }
}
