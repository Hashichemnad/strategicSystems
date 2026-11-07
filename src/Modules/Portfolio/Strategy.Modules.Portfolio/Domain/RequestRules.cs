using System.Globalization;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Modules.Portfolio.Domain;

/// <summary>
/// What the rules need besides the values: today (installation time zone), the date windows and the criteria; at submission whether the cost
/// estimation file is on the request and whether Administration confirmed the quotations (project-scope path).
/// </summary>
public sealed record RequestRuleContext(
    DateOnly Today,
    int StartYearsAhead,
    int EndYearsAhead,
    IReadOnlyList<PriorityCriterion> Criteria,
    bool CostEstimationUploaded = false,
    bool QuotationsUploaded = false);

/// <summary>
/// SPEC §1.4 A validation rules (ADR 0047): format rules on every save, completeness and business rules at submission. Field names are the
/// wizard's (camelCase); message keys are translated by the client.
/// </summary>
public static class RequestRules
{
    public const int NameMaxLength = 300;
    public const int LongTextMaxLength = 5000;

    /// <summary>Stored size of the rich-text description / justifications (sanitised HTML, ADR 0161); the 5 000 limit counts the visible text.</summary>
    public const int RichTextStoredMaxLength = RichText.DefaultMaxLength;
    public const int TextMaxLength = 2000;
    public const int OtherMaxLength = 300;

    /// <summary>Rules every save must pass (a draft never holds a malformed value).</summary>
    public static List<FieldError> ValidateFormat(RequestValues v, RequestRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<FieldError>();
        MaxLength(errors, "nameAr", v.NameAr, NameMaxLength);
        MaxLength(errors, "nameEn", v.NameEn, NameMaxLength);
        RichLength(errors, "description", v.Description);
        RichLength(errors, "justifications", v.Justifications);
        MaxLength(errors, "scopeOfWork", v.ScopeOfWork, LongTextMaxLength);
        MaxLength(errors, "previousProgramName", v.PreviousProgramName, NameMaxLength);
        MaxLength(errors, "suggestionSourceOther", v.SuggestionSourceOther, OtherMaxLength);
        MaxLength(errors, "mainOptionOther", v.MainOptionOther, OtherMaxLength);
        MaxLength(errors, "altOptionOther", v.AltOptionOther, OtherMaxLength);
        MaxLength(errors, "partnerName", v.PartnerName, NameMaxLength);
        foreach (var (field, text) in new[]
                 {
                     ("mainAdvantages", v.MainAdvantages), ("mainDisadvantages", v.MainDisadvantages), ("altAdvantages", v.AltAdvantages),
                     ("altDisadvantages", v.AltDisadvantages), ("practiceOperation", v.PracticeOperation), ("practiceService", v.PracticeService),
                     ("practiceProject", v.PracticeProject), ("practiceInitiative", v.PracticeInitiative),
                 })
        {
            MaxLength(errors, field, text, TextMaxLength);
        }

        foreach (var (field, amount) in Amounts(v))
        {
            if (amount is not { } a)
            {
                continue;
            }

            if (a < 0)
            {
                errors.Add(new FieldError(field, "validation.greaterThanOrEqual", "validation.greaterThanOrEqual", P("comparisonValue", 0)));
            }
            else if (decimal.Round(a, 2) != a)
            {
                errors.Add(new FieldError(field, "portfolio.errors.twoDecimals", "portfolio.errors.twoDecimals"));
            }
            else if (a > 9_999_999_999_999_999.99m)
            {
                errors.Add(new FieldError(field, "validation.invalid", "validation.invalid"));
            }
        }

        FourDigitYear(errors, "returnYear", v.ReturnYear);
        FourDigitYear(errors, "reductionYear", v.ReductionYear);
        if (v.PartnerShare is { } share && (share < 0 || share > 1))
        {
            errors.Add(new FieldError("partnerShare", "validation.inclusiveBetween", "validation.inclusiveBetween", P("from", 0, "to", 1)));
        }

        if (v.ProgramNature is { } nature && !ProgramNatures.All.Contains(nature))
        {
            errors.Add(new FieldError("programNature", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v.CostPath is { } path && !CostPaths.All.Contains(path))
        {
            errors.Add(new FieldError("costPath", "validation.unknownCode", "validation.unknownCode"));
        }

        if (v.PriorityScores is { } scores)
        {
            foreach (var (code, score) in scores)
            {
                if (!context.Criteria.Any(c => c.Code == code && c.Active))
                {
                    errors.Add(new FieldError($"priorityScores.{code}", "validation.unknownCode", "validation.unknownCode"));
                }
                else if (score is < PriorityCalculator.MinScore or > PriorityCalculator.MaxScore)
                {
                    errors.Add(new FieldError($"priorityScores.{code}", "validation.inclusiveBetween", "validation.inclusiveBetween",
                        P("from", PriorityCalculator.MinScore, "to", PriorityCalculator.MaxScore)));
                }
            }
        }

        OtherText(errors, "suggestionSourceOther", v.SuggestionSourceCode, v.SuggestionSourceOther);
        OtherText(errors, "mainOptionOther", v.MainOptionCode, v.MainOptionOther);
        OtherText(errors, "altOptionOther", v.AltOptionCode, v.AltOptionOther);
        if (v.ExpectedStartDate is { } start && v.ExpectedEndDate is { } end && end < start)
        {
            errors.Add(new FieldError("expectedEndDate", "portfolio.errors.endBeforeStart", "portfolio.errors.endBeforeStart"));
        }

        return errors;
    }

    /// <summary>Format rules plus completeness and the SPEC §1.4 A business rules (submission and resubmission).</summary>
    public static List<FieldError> ValidateForSubmission(RequestValues v, RequestRuleContext context)
    {
        var errors = ValidateFormat(v, context);
        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(Required("nameEn"));
        }

        RequiredRichText(errors, "description", v.Description);
        RequiredRichText(errors, "justifications", v.Justifications);
        RequiredText(errors, "strategicObjectiveCode", v.StrategicObjectiveCode);
        RequiredText(errors, "suggestionSourceCode", v.SuggestionSourceCode);
        RequiredText(errors, "programTypeCode", v.ProgramTypeCode);
        RequiredText(errors, "programNature", v.ProgramNature);
        if (v.RequiresBudget is null)
        {
            errors.Add(Required("requiresBudget"));
        }

        if (v.UnitId is null)
        {
            errors.Add(Required("unitId"));
        }

        // Expected dates: start within the current year or the next N years; end ≥ start and at most N years after the current year.
        var year = context.Today.Year;
        if (v.ExpectedStartDate is not { } start)
        {
            errors.Add(Required("expectedStartDate"));
        }
        else if (start.Year < year || start.Year > year + context.StartYearsAhead)
        {
            errors.Add(new FieldError("expectedStartDate", "portfolio.errors.startOutOfRange", "portfolio.errors.startOutOfRange",
                P("from", Iso(new DateOnly(year, 1, 1)), "to", Iso(new DateOnly(year + context.StartYearsAhead, 12, 31)))));
        }

        if (v.ExpectedEndDate is not { } end)
        {
            errors.Add(Required("expectedEndDate"));
        }
        else if (end.Year > year + context.EndYearsAhead)
        {
            errors.Add(new FieldError("expectedEndDate", "portfolio.errors.endOutOfRange", "portfolio.errors.endOutOfRange",
                P("to", Iso(new DateOnly(year + context.EndYearsAhead, 12, 31)))));
        }

        // Budget: Year 1 + 2 + 3 + 4 = Estimated Budget exactly.
        if (v.RequiresBudget == true)
        {
            if (v.EstimatedBudget is not > 0)
            {
                errors.Add(v.EstimatedBudget is null ? Required("estimatedBudget")
                    : new FieldError("estimatedBudget", "validation.greaterThan", "validation.greaterThan", P("value", 0)));
            }
            else if ((v.YearCostsTotal ?? 0) != v.EstimatedBudget)
            {
                errors.Add(new FieldError("yearCosts", "portfolio.errors.yearCostsMismatch", "portfolio.errors.yearCostsMismatch",
                    P("total", Money(v.YearCostsTotal ?? 0), "budget", Money(v.EstimatedBudget.Value))));
            }

            switch (v.CostPath)
            {
                case null:
                    errors.Add(Required("costPath"));
                    break;
                case CostPaths.CostEstimation when !context.CostEstimationUploaded:
                    errors.Add(new FieldError("costEstimation", "portfolio.errors.costEstimationMissing", "portfolio.errors.costEstimationMissing"));
                    break;
                case CostPaths.ProjectScope when !context.QuotationsUploaded:
                    errors.Add(new FieldError("quotations", "portfolio.errors.quotationsMissing", "portfolio.errors.quotationsMissing"));
                    break;
            }
        }

        if (v.ProgramNature == ProgramNatures.Continuous && string.IsNullOrWhiteSpace(v.PreviousProgramName))
        {
            errors.Add(new FieldError("previousProgramName", "portfolio.errors.previousProgramRequired", "portfolio.errors.previousProgramRequired"));
        }

        if (v.ScreeningAnswers.Any(a => a is null))
        {
            errors.Add(new FieldError("screening", "portfolio.errors.screeningRequired", "portfolio.errors.screeningRequired"));
        }

        if (PriorityCalculator.Score(v.PriorityScores, context.Criteria) is null)
        {
            errors.Add(new FieldError("priorityScores", "portfolio.errors.priorityRequired", "portfolio.errors.priorityRequired"));
        }

        if (string.IsNullOrWhiteSpace(v.PracticeOperation) && string.IsNullOrWhiteSpace(v.PracticeService))
        {
            errors.Add(new FieldError("practiceOperation", "portfolio.errors.operationOrService", "portfolio.errors.operationOrService"));
        }

        if (v.ReturnExpected == true)
        {
            RequiredValue(errors, "returnValue", v.ReturnValue);
            RequiredValue(errors, "returnYear", v.ReturnYear);
        }

        if (v.ReductionExpected == true)
        {
            RequiredValue(errors, "reductionValue", v.ReductionValue);
            RequiredValue(errors, "reductionYear", v.ReductionYear);
        }

        return errors;
    }

    /// <summary>Total expected time frame in whole months, start and end months included (SPEC "computed").</summary>
    public static int? TimeFrameMonths(DateOnly? start, DateOnly? end) =>
        start is { } s && end is { } e && e >= s ? ((e.Year - s.Year) * 12) + e.Month - s.Month + 1 : null;

    private static IEnumerable<(string Field, decimal? Amount)> Amounts(RequestValues v) =>
    [
        ("estimatedBudget", v.EstimatedBudget), ("year1Cost", v.Year1Cost), ("year2Cost", v.Year2Cost), ("year3Cost", v.Year3Cost),
        ("year4Cost", v.Year4Cost), ("sharedCostAmount", v.SharedCostAmount), ("annualOperationCost", v.AnnualOperationCost),
        ("annualLicenceCost", v.AnnualLicenceCost), ("annualMaintenanceCost", v.AnnualMaintenanceCost), ("annualOtherCost", v.AnnualOtherCost),
        ("returnValue", v.ReturnValue), ("reductionValue", v.ReductionValue),
    ];

    private static void FourDigitYear(List<FieldError> errors, string field, int? year)
    {
        if (year is { } y && (y < 1000 || y > 9999))
        {
            errors.Add(new FieldError(field, "portfolio.errors.fourDigitYear", "portfolio.errors.fourDigitYear"));
        }
    }

    private static void OtherText(List<FieldError> errors, string field, string? code, string? text)
    {
        if (OtherCode.Is(code) && string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new FieldError(field, "portfolio.errors.otherTextRequired", "portfolio.errors.otherTextRequired"));
        }
    }

    private static void MaxLength(List<FieldError> errors, string field, string? text, int max)
    {
        if (text is { } t && t.Length > max)
        {
            errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", P("maxLength", max)));
        }
    }

    /// <summary>Rich text (ADR 0161): measured after sanitising, on the visible text.</summary>
    public static void RichLength(List<FieldError> errors, string field, string? text, int max = LongTextMaxLength)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (RichText.LengthError(field, RichText.Sanitize(text), max, RichTextStoredMaxLength) is { } error)
        {
            errors.Add(error);
        }
    }

    private static void RequiredRichText(List<FieldError> errors, string field, string? text)
    {
        if (RichText.PlainLength(RichText.Sanitize(text)) == 0)
        {
            errors.Add(Required(field));
        }
    }

    private static void RequiredText(List<FieldError> errors, string field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(Required(field));
        }
    }

    private static void RequiredValue<T>(List<FieldError> errors, string field, T? value)
        where T : struct
    {
        if (value is null)
        {
            errors.Add(Required(field));
        }
    }

    private static FieldError Required(string field) => new(field, "validation.notEmpty", "validation.notEmpty");

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> P(string key, object? value) => new() { [key] = value };

    private static Dictionary<string, object?> P(string key1, object? value1, string key2, object? value2) => new() { [key1] = value1, [key2] = value2 };
}
