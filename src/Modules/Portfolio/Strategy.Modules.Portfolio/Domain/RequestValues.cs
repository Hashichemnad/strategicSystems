namespace Strategy.Modules.Portfolio.Domain;

/// <summary>
/// Every editable value of a Program Request (SPEC §1.4 A, docs/design/portfolio.md §3.1). Used by the wizard (create / update), the rules
/// and the calculators. Amounts are in the installation currency, two decimals; ratios are 0–1.
/// </summary>
public sealed record RequestValues
{
    public string? NameAr { get; init; }

    public string? NameEn { get; init; }

    public string? Description { get; init; }

    public string? Justifications { get; init; }

    public string? StrategicObjectiveCode { get; init; }

    public string? SuggestionSourceCode { get; init; }

    public string? SuggestionSourceOther { get; init; }

    public string? ProgramTypeCode { get; init; }

    public bool? RequiresBudget { get; init; }

    public decimal? EstimatedBudget { get; init; }

    public decimal? Year1Cost { get; init; }

    public decimal? Year2Cost { get; init; }

    public decimal? Year3Cost { get; init; }

    public decimal? Year4Cost { get; init; }

    public DateOnly? ExpectedStartDate { get; init; }

    public DateOnly? ExpectedEndDate { get; init; }

    /// <summary>Requesting unit (department or section); defaults to the requester's unit.</summary>
    public Guid? UnitId { get; init; }

    /// <summary>Section to implement.</summary>
    public Guid? SectionUnitId { get; init; }

    public string? ProgramNature { get; init; }

    public string? PreviousProgramName { get; init; }

    public string? ScopeOfWork { get; init; }

    public bool? ScreenCoreWork { get; init; }

    public bool? ScreenOneTime { get; init; }

    public bool? ScreenTimeBound { get; init; }

    public bool? ScreenUniqueOutput { get; init; }

    public bool? ScreenMinCost { get; init; }

    public bool? ScreenOperationalCosts { get; init; }

    /// <summary>Criterion code → score 1–5 (Settings → Portfolio criteria).</summary>
    public IReadOnlyDictionary<string, int>? PriorityScores { get; init; }

    public string? MainOptionCode { get; init; }

    public string? MainOptionOther { get; init; }

    public string? MainAdvantages { get; init; }

    public string? MainDisadvantages { get; init; }

    public string? AltOptionCode { get; init; }

    public string? AltOptionOther { get; init; }

    public string? AltAdvantages { get; init; }

    public string? AltDisadvantages { get; init; }

    public bool? OthersBearCosts { get; init; }

    public bool? OthersCoverImplementation { get; init; }

    public decimal? SharedCostAmount { get; init; }

    public string? PartnerName { get; init; }

    /// <summary>Partner cost-sharing ratio (0–1).</summary>
    public decimal? PartnerShare { get; init; }

    public decimal? AnnualOperationCost { get; init; }

    public decimal? AnnualLicenceCost { get; init; }

    public decimal? AnnualMaintenanceCost { get; init; }

    public decimal? AnnualOtherCost { get; init; }

    public DateOnly? OperatingCostsFrom { get; init; }

    public bool? ReturnExpected { get; init; }

    public decimal? ReturnValue { get; init; }

    public int? ReturnYear { get; init; }

    public bool? ReductionExpected { get; init; }

    public decimal? ReductionValue { get; init; }

    public int? ReductionYear { get; init; }

    public string? PracticeOperation { get; init; }

    public string? PracticeService { get; init; }

    public string? PracticeProject { get; init; }

    public string? PracticeInitiative { get; init; }

    /// <summary><see cref="CostPaths"/>: whether a cost estimation file is available or the project scope goes to Administration.</summary>
    public string? CostPath { get; init; }

    public bool? CostProposalsRequested { get; init; }

    /// <summary>The six screening answers in SPEC order.</summary>
    public IReadOnlyList<bool?> ScreeningAnswers =>
        [ScreenCoreWork, ScreenOneTime, ScreenTimeBound, ScreenUniqueOutput, ScreenMinCost, ScreenOperationalCosts];

    public decimal? YearCostsTotal =>
        Year1Cost is null && Year2Cost is null && Year3Cost is null && Year4Cost is null
            ? null
            : (Year1Cost ?? 0) + (Year2Cost ?? 0) + (Year3Cost ?? 0) + (Year4Cost ?? 0);
}

public static class ProgramNatures
{
    public const string New = "new";
    public const string Continuous = "continuous";

    public static readonly IReadOnlyList<string> All = [New, Continuous];
}

public static class CostPaths
{
    public const string CostEstimation = "cost-estimation";
    public const string ProjectScope = "project-scope";

    public static readonly IReadOnlyList<string> All = [CostEstimation, ProjectScope];
}

public static class ProcurementStatuses
{
    public const string WaitingQuotations = "waiting-quotations";
    public const string QuotationsUploaded = "quotations-uploaded";
}

public static class Classifications
{
    public const string Project = "project";
    public const string Initiative = "initiative";

    public static readonly IReadOnlyList<string> All = [Project, Initiative];
}

/// <summary>The lookup code whose free-text companion is required (ADR 0047).</summary>
public static class OtherCode
{
    public const string Value = "other";

    public static bool Is(string? code) => string.Equals(code, Value, StringComparison.Ordinal);
}
