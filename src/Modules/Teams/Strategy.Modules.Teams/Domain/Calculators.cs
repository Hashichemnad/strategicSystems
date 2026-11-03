using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Teams.Domain;

/// <summary>Template validation (ADR 0041). Field paths follow the request body (criteria[0].levels[1].score…).</summary>
public static class TemplateRules
{
    public static List<FieldError> Validate(TemplateContent? content)
    {
        var errors = new List<FieldError>();
        if (content is null)
        {
            errors.Add(E("content", "validation.notEmpty"));
            return errors;
        }

        var criteria = content.Criteria ?? [];
        if (criteria.Count == 0)
        {
            errors.Add(E("criteria", "validation.notEmpty"));
        }
        else if (criteria.Sum(c => c.Weight) != 100m)
        {
            errors.Add(E("criteria", "teams.errors.weightsTotal", new() { ["total"] = criteria.Sum(c => c.Weight) }));
        }

        Duplicates(criteria.Select(c => c.Code), "criteria", errors);
        for (var i = 0; i < criteria.Count; i++)
        {
            var c = criteria[i];
            var at = $"criteria[{i}]";
            if (string.IsNullOrWhiteSpace(c.Code) || string.IsNullOrWhiteSpace(c.NameAr) || string.IsNullOrWhiteSpace(c.NameEn))
            {
                errors.Add(E(at + ".name", "validation.notEmpty"));
            }

            if (c.Weight <= 0)
            {
                errors.Add(E(at + ".weight", "validation.greaterThan", new() { ["value"] = 0 }));
            }

            var levels = c.Levels ?? [];
            if (levels.Count < 2)
            {
                errors.Add(E(at + ".levels", "teams.errors.twoLevels"));
                continue;
            }

            Duplicates(levels.Select(l => l.Code), at + ".levels", errors);
            for (var j = 0; j < levels.Count; j++)
            {
                if (levels[j].Score < 0 || levels[j].Score > c.Weight)
                {
                    errors.Add(E($"{at}.levels[{j}].score", "validation.inclusiveBetween", new() { ["from"] = 0, ["to"] = c.Weight }));
                }
            }

            if (!levels.Any(l => l.Score == c.Weight))
            {
                errors.Add(E(at + ".levels", "teams.errors.levelAtWeight"));
            }

            if (c.DataSource is not null)
            {
                if (!CriterionDataSources.All.Contains(c.DataSource))
                {
                    errors.Add(E(at + ".dataSource", "validation.unknownCode"));
                }
                else if (!RangesContiguous(levels, c.DataSource))
                {
                    errors.Add(E(at + ".levels", "teams.errors.rangesGap"));
                }
            }
        }

        var questions = content.Questions ?? [];
        if (questions.Count is < 1 or > 10)
        {
            errors.Add(E("questions", "validation.inclusiveBetween", new() { ["from"] = 1, ["to"] = 10 }));
        }

        Duplicates(questions.Select(q => q.Code), "questions", errors);
        for (var i = 0; i < questions.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(questions[i].Code) || string.IsNullOrWhiteSpace(questions[i].TextAr) || string.IsNullOrWhiteSpace(questions[i].TextEn))
            {
                errors.Add(E($"questions[{i}].text", "validation.notEmpty"));
            }
        }

        if (questions.Count > 0)
        {
            errors.AddRange(ValidateBands(content.GradeBands ?? [], content.MinPoints, content.MaxPoints));
        }

        if (content.EvidenceThreshold is < 0 or > 100)
        {
            errors.Add(E("evidenceThreshold", "validation.inclusiveBetween", new() { ["from"] = 0, ["to"] = 100 }));
        }

        return errors;
    }

    /// <summary>Bands must cover exactly [min, max] with integer ranges, no gap and no overlap.</summary>
    public static List<FieldError> ValidateBands(IReadOnlyList<GradeBand> bands, int min, int max)
    {
        var errors = new List<FieldError>();
        if (bands.Count == 0)
        {
            errors.Add(E("gradeBands", "validation.notEmpty"));
            return errors;
        }

        Duplicates(bands.Select(b => b.Code), "gradeBands", errors);
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].Min > bands[i].Max || string.IsNullOrWhiteSpace(bands[i].NameAr) || string.IsNullOrWhiteSpace(bands[i].NameEn))
            {
                errors.Add(E($"gradeBands[{i}]", "teams.errors.bandInvalid"));
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var ordered = bands.OrderBy(b => b.Min).ToList();
        if (ordered[0].Min != min || ordered[^1].Max != max)
        {
            errors.Add(E("gradeBands", "teams.errors.bandsCover", new() { ["min"] = min, ["max"] = max }));
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Min <= ordered[i - 1].Max)
            {
                errors.Add(E("gradeBands", "teams.errors.bandsOverlap", new() { ["at"] = ordered[i].Min }));
            }
            else if (ordered[i].Min != ordered[i - 1].Max + 1)
            {
                errors.Add(E("gradeBands", "teams.errors.bandsGap", new() { ["from"] = ordered[i - 1].Max + 1, ["to"] = ordered[i].Min - 1 }));
            }
        }

        return errors;
    }

    /// <summary>Data ranges cover the whole domain without holes: percent 0–100, hours 0–∞ (open top).</summary>
    public static bool RangesContiguous(IReadOnlyList<CriterionLevel> levels, string dataSource)
    {
        if (levels.Any(l => l.Min is null && l.Max is null))
        {
            return false;
        }

        var ordered = levels.OrderBy(l => l.Min ?? decimal.MinValue).ToList();
        if ((ordered[0].Min ?? 0) > 0)
        {
            return false;
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var previousMax = ordered[i - 1].Max;
            var min = ordered[i].Min;
            if (previousMax is null || min is null || min > previousMax + 0.01m || min < previousMax)
            {
                return false;
            }
        }

        var top = ordered[^1].Max;
        return dataSource == CriterionDataSources.RecommendationsClosure ? top is null || top >= 100 : top is null;
    }

    private static void Duplicates(IEnumerable<string> codes, string field, List<FieldError> errors)
    {
        if (codes.GroupBy(c => c, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            errors.Add(E(field, "validation.duplicate"));
        }
    }

    private static FieldError E(string field, string key, Dictionary<string, object?>? parameters = null) => new(field, key, key, parameters);
}

/// <summary>Data behind the suggested criterion levels (ADR 0045), for one cycle.</summary>
public sealed record CycleActivity(int RecommendationsClosed, int RecommendationsCounted, IReadOnlyList<decimal> MinutesDelays);

/// <summary>Suggested levels from data (pure, unit-tested).</summary>
public static class EvaluationSuggestions
{
    public static (string? Level, SuggestionBasis? Basis) Suggest(TeamCriterion criterion, CycleActivity activity)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        ArgumentNullException.ThrowIfNull(activity);
        SuggestionBasis? basis = criterion.DataSource switch
        {
            CriterionDataSources.RecommendationsClosure when activity.RecommendationsCounted > 0 => new SuggestionBasis(
                criterion.DataSource, Math.Round(100m * activity.RecommendationsClosed / activity.RecommendationsCounted, 2),
                activity.RecommendationsClosed, activity.RecommendationsCounted),
            CriterionDataSources.MinutesDelay when activity.MinutesDelays.Count > 0 => new SuggestionBasis(
                criterion.DataSource, Math.Round(activity.MinutesDelays.Average(), 2), activity.MinutesDelays.Count, activity.MinutesDelays.Count),
            _ => null,
        };
        if (basis is null)
        {
            return (null, null);
        }

        return (LevelFor(criterion, basis.Value)?.Code, basis);
    }

    /// <summary>The level whose range holds the value (values have two decimals; Min and Max inclusive).</summary>
    public static CriterionLevel? LevelFor(TeamCriterion criterion, decimal value)
    {
        ArgumentNullException.ThrowIfNull(criterion);
        var rounded = Math.Round(value, 2);
        return criterion.Levels.OrderBy(l => l.Min ?? decimal.MinValue)
            .FirstOrDefault(l => (l.Min is null || rounded >= l.Min) && (l.Max is null || rounded <= l.Max));
    }
}

/// <summary>Team score, member points and grades (pure, unit-tested).</summary>
public static class EvaluationCalculator
{
    /// <summary>Sum of the selected levels' scores; null when any criterion has no level yet.</summary>
    public static decimal? TeamScore(TemplateContent template, IReadOnlyList<CriterionResult> results)
    {
        decimal total = 0;
        foreach (var criterion in template.Criteria)
        {
            var result = results.FirstOrDefault(r => r.Code == criterion.Code);
            var level = criterion.Levels.FirstOrDefault(l => l.Code == result?.Level);
            if (level is null)
            {
                return null;
            }

            total += level.Score;
        }

        return total;
    }

    public static bool EvidenceRequired(TemplateContent template, decimal? score) => score is { } s && s >= template.EvidenceThreshold;

    /// <summary>Sum of the answers; null unless every question has an answer in 1–5.</summary>
    public static int? Points(TemplateContent template, IReadOnlyDictionary<string, int> answers)
    {
        var total = 0;
        foreach (var question in template.Questions)
        {
            if (!answers.TryGetValue(question.Code, out var answer) || answer < TemplateContent.AnswerMin || answer > TemplateContent.AnswerMax)
            {
                return null;
            }

            total += answer;
        }

        return total;
    }

    public static GradeBand? Grade(TemplateContent template, int? points) =>
        points is { } p ? template.GradeBands.FirstOrDefault(b => p >= b.Min && p <= b.Max) : null;

    /// <summary>Validation of a submission: every criterion levelled, overrides justified, evidence above the threshold, every member answered.</summary>
    public static List<FieldError> ValidateSubmission(
        TemplateContent template, IReadOnlyList<CriterionResult> results, int evidenceCount, IReadOnlyList<(Guid Id, int? Points)> members)
    {
        var errors = new List<FieldError>();
        for (var i = 0; i < template.Criteria.Count; i++)
        {
            var criterion = template.Criteria[i];
            var result = results.FirstOrDefault(r => r.Code == criterion.Code);
            if (result?.Level is null || criterion.Levels.All(l => l.Code != result.Level))
            {
                errors.Add(new FieldError($"criteria[{i}].level", "validation.notEmpty", "validation.notEmpty"));
            }
            else if (result.SuggestedLevel is not null && result.SuggestedLevel != result.Level && string.IsNullOrWhiteSpace(result.OverrideJustification))
            {
                errors.Add(new FieldError($"criteria[{i}].overrideJustification", "teams.errors.overrideJustification", "teams.errors.overrideJustification"));
            }
        }

        if (EvidenceRequired(template, TeamScore(template, results)) && evidenceCount == 0)
        {
            errors.Add(new FieldError("evidence", "teams.errors.evidenceRequired", "teams.errors.evidenceRequired",
                new Dictionary<string, object?> { ["threshold"] = template.EvidenceThreshold }));
        }

        foreach (var (id, points) in members)
        {
            if (points is null)
            {
                errors.Add(new FieldError($"members[{id:D}]", "teams.errors.memberIncomplete", "teams.errors.memberIncomplete"));
            }
        }

        return errors;
    }
}

/// <summary>Inconsistency flags (docs/design/teams.md §7), deterministic.</summary>
public static class InconsistencyRules
{
    public const string AllTopScores = "all-top-scores";
    public const string AllEqual = "all-equal";
    public const string TopGradeLowAttendance = "top-grade-low-attendance";
    public const string LowGradeHighAttendance = "low-grade-high-attendance";
    public const string FarFromSuggestion = "far-from-suggestion";

    public static List<EvaluationFlag> Flags(
        TemplateContent template, IReadOnlyList<CriterionResult> criteria, IReadOnlyList<(Guid Id, int? Points, decimal? Attendance)> members)
    {
        var flags = new List<EvaluationFlag>();
        var rated = members.Where(m => m.Points is not null).ToList();
        if (rated.Count >= 2 && rated.All(m => m.Points == template.MaxPoints))
        {
            flags.Add(new(AllTopScores));
        }
        else if (rated.Count >= 3 && rated.Select(m => m.Points).Distinct().Count() == 1)
        {
            flags.Add(new(AllEqual));
        }

        var bands = template.GradeBands.OrderBy(b => b.Min).ToList();
        if (bands.Count > 0)
        {
            foreach (var member in rated)
            {
                var band = EvaluationCalculator.Grade(template, member.Points);
                if (band == bands[^1] && member.Attendance is < 0.5m)
                {
                    flags.Add(new(TopGradeLowAttendance, member.Id));
                }
                else if (band == bands[0] && member.Attendance is >= 0.9m)
                {
                    flags.Add(new(LowGradeHighAttendance, member.Id));
                }
            }
        }

        foreach (var criterion in template.Criteria)
        {
            var result = criteria.FirstOrDefault(r => r.Code == criterion.Code);
            if (result?.Level is null || result.SuggestedLevel is null)
            {
                continue;
            }

            var ordered = criterion.Levels.OrderBy(l => l.Score).Select(l => l.Code).ToList();
            if (Math.Abs(ordered.IndexOf(result.Level) - ordered.IndexOf(result.SuggestedLevel)) > 1)
            {
                flags.Add(new(FarFromSuggestion));
                break;
            }
        }

        return flags;
    }
}

/// <summary>Reward amounts from the matrix (pure, unit-tested).</summary>
public static class RewardCalculator
{
    public static (decimal Amount, bool Certificate) Calculate(RewardMatrixContent matrix, string? grade, string workload, string role)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (grade is null)
        {
            return (0m, false);
        }

        var cell = matrix.Cells.FirstOrDefault(c => c.Grade == grade && c.Workload == workload && c.Role == role);
        return (cell?.Amount ?? 0m, matrix.CertificateGrades.Contains(grade, StringComparer.Ordinal));
    }

    public static List<FieldError> Validate(RewardMatrixContent? matrix, IReadOnlyCollection<string> grades, IReadOnlyCollection<string> roles)
    {
        var errors = new List<FieldError>();
        if (matrix is null)
        {
            errors.Add(new("content", "validation.notEmpty", "validation.notEmpty"));
            return errors;
        }

        if (matrix.AnnualBudget < 0)
        {
            errors.Add(new("annualBudget", "validation.greaterThanOrEqual", "validation.greaterThanOrEqual", new Dictionary<string, object?> { ["comparisonValue"] = 0 }));
        }

        if (string.IsNullOrWhiteSpace(matrix.Currency) || matrix.Currency.Length != 3)
        {
            errors.Add(new("currency", "validation.invalid", "validation.invalid"));
        }

        for (var i = 0; i < matrix.Cells.Count; i++)
        {
            var c = matrix.Cells[i];
            if (c.Amount < 0 || !grades.Contains(c.Grade) || !Workloads.All.Contains(c.Workload) || !roles.Contains(c.Role))
            {
                errors.Add(new($"cells[{i}]", "validation.invalid", "validation.invalid"));
            }
        }

        if (matrix.Cells.GroupBy(c => (c.Grade, c.Workload, c.Role)).Any(g => g.Count() > 1))
        {
            errors.Add(new("cells", "validation.duplicate", "validation.duplicate"));
        }

        if (matrix.CertificateGrades.Any(g => !grades.Contains(g)))
        {
            errors.Add(new("certificateGrades", "validation.unknownCode", "validation.unknownCode"));
        }

        return errors;
    }
}

/// <summary>Dates of reports and evaluation triggers (pure, unit-tested).</summary>
public static class TeamCalendar
{
    /// <summary>The reporting period containing <paramref name="day"/>, aligned to calendar months from January.</summary>
    public static (DateOnly Start, DateOnly End)? PeriodOf(string? cadence, DateOnly day)
    {
        if (Cadences.MonthsOf(cadence) is not { } months)
        {
            return null;
        }

        var startMonth = ((day.Month - 1) / months * months) + 1;
        var start = new DateOnly(day.Year, startMonth, 1);
        return (start, start.AddMonths(months).AddDays(-1));
    }

    /// <summary>The annual trigger date of <paramref name="year"/> (Feb 29 → Feb 28 in other years).</summary>
    public static DateOnly AnnualDate(int year, int month, int day) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>Annual cycle due on <paramref name="today"/>: the previous year, when today is on or after this year's trigger date.</summary>
    public static int? AnnualCycleDue(DateOnly today, int month, int day) =>
        today >= AnnualDate(today.Year, month, day) ? today.Year - 1 : null;

    /// <summary>Annual cycle period = the year ∩ team life.</summary>
    public static (DateOnly Start, DateOnly End)? AnnualPeriod(int year, DateOnly? teamStart, DateOnly? teamEnd)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        if (teamStart is { } s && s > start)
        {
            start = s;
        }

        if (teamEnd is { } e && e < end)
        {
            end = e;
        }

        return start <= end ? (start, end) : null;
    }
}

/// <summary>The SPEC §4.3 default template (ADoF policy 2025), seeded as version 1.</summary>
public static class DefaultTemplate
{
    public static TemplateContent Content { get; } = new(
    [
        new("recommendations", "إغلاق التوصيات", "Recommendations closure", 25,
        [
            new("high", "95% فأكثر", "95% or more", 25, 95, null),
            new("medium", "من 80% إلى 94%", "80% to 94%", 12.5m, 80, 94.99m),
            new("low", "أقل من 80%", "Below 80%", 0, 0, 79.99m),
        ], CriterionDataSources.RecommendationsClosure),
        new("effectiveness", "فاعلية التنفيذ والأثر", "Implementation effectiveness & impact", 30,
        [
            new("radical", "حلول جذرية", "Radical solution", 30),
            new("steering", "توجيهية", "Steering", 15),
            new("unenforceable", "قرارات غير قابلة للتنفيذ", "Unenforceable decisions", 0),
        ]),
        new("quality", "جودة المخرجات", "Quality of output", 20,
        [
            new("no-feedback", "اعتُمدت دون ملاحظات", "Approved without feedback", 20),
            new("feedback", "اعتُمدت مع ملاحظات", "Approved with feedback", 10),
            new("not-delivered", "لم تُسلَّم", "Not delivered", 0),
        ]),
        new("governance", "الحوكمة والانضباط والتوثيق", "Governance, discipline & documentation", 10,
        [
            new("timely", "المحاضر خلال 24 ساعة", "Minutes within 24 h", 10, 0, 24),
            new("late", "المحاضر خلال 48–72 ساعة", "Minutes within 48–72 h", 5, 24.01m, 72),
            new("poor", "ضعيف", "Poor", 0, 72.01m, null),
        ], CriterionDataSources.MinutesDelay),
        new("cooperation", "التعاون والانسجام", "Cooperation & harmony", 15,
        [
            new("shared", "عمل مشترك احترافي", "Shared professionally", 15),
            new("few", "يعتمد على قلة", "Depends on a few", 7.5m),
            new("disputes", "خلافات", "Disputes", 0),
        ]),
    ],
    [
        new("contribution", "المساهمة في تحقيق الأهداف", "Contribution to achieving goals"),
        new("commitment", "الالتزام بحضور الاجتماعات والتسليم في الوقت", "Commitment to attend meetings and submit on time"),
        new("communication", "التواصل مع الفريق", "Communication with the team"),
    ],
    [
        new("very-active", "فعّال جداً", "Very active", 12, 15),
        new("active", "فعّال", "Active", 9, 11),
        new("not-active", "غير فعّال", "Not active", 3, 8),
    ],
    90);
}
