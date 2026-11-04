using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Domain;

public static class RequestStatuses
{
    public const string Draft = "draft";
    public const string InApproval = "in-approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, InApproval, Approved, Rejected, Cancelled];

    public static bool IsFinal(string status) => status is Approved or Rejected or Cancelled;
}

/// <summary>Result of the last approval decision (SPEC "Approval Status").</summary>
public static class RequestApprovalResults
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
}

public static class RequestActions
{
    public const string Edit = "edit";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Return = "return";
    public const string Cancel = "cancel";
    public const string Classify = "classify";
    public const string Procure = "procure";

    public static readonly IReadOnlyList<string> All = [Edit, Submit, Approve, Reject, Return, Cancel, Classify, Procure];
}

/// <summary>The request state machine (docs/design/portfolio.md §6), one table, unit-tested for every pair.</summary>
public static class RequestLifecycle
{
    private static readonly Dictionary<(string Status, string Action), string> Transitions = new()
    {
        [(RequestStatuses.Draft, RequestActions.Edit)] = RequestStatuses.Draft,
        [(RequestStatuses.Draft, RequestActions.Submit)] = RequestStatuses.InApproval,
        [(RequestStatuses.Draft, RequestActions.Cancel)] = RequestStatuses.Cancelled,
        [(RequestStatuses.Draft, RequestActions.Classify)] = RequestStatuses.Draft,
        [(RequestStatuses.Draft, RequestActions.Procure)] = RequestStatuses.Draft,
        [(RequestStatuses.InApproval, RequestActions.Approve)] = RequestStatuses.Approved,
        [(RequestStatuses.InApproval, RequestActions.Reject)] = RequestStatuses.Rejected,
        [(RequestStatuses.InApproval, RequestActions.Return)] = RequestStatuses.Draft,
        [(RequestStatuses.InApproval, RequestActions.Cancel)] = RequestStatuses.Cancelled,
        [(RequestStatuses.InApproval, RequestActions.Classify)] = RequestStatuses.InApproval,
    };

    public static bool Allows(string status, string action) => Transitions.ContainsKey((status, action));

    public static Result<string> Next(string status, string action) =>
        Transitions.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.request.transition", "portfolio.errors.notAllowedInStatus", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });
}

/// <summary>A Program Request (SPEC §1.4 A, <c>PR-XXXX</c>). Values are edited only in Draft; the approval engine's hook moves the status.</summary>
[Audited(EntityType)]
public sealed class ProgramRequest : AuditableEntity
{
    public const string EntityType = "portfolio.request";

    private ProgramRequest()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string Status { get; private set; } = RequestStatuses.Draft;

    public string? ApprovalStatus { get; private set; }

    public string? ApprovalStepCode { get; private set; }

    public string? ApprovalStepNameAr { get; private set; }

    public string? ApprovalStepNameEn { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? LastActionAt { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public Guid? RequesterUserId { get; private set; }

    /// <summary><c>manual</c> or <c>document</c> (started from a document with AI).</summary>
    public string Source { get; private set; } = "manual";

    public Guid? ProjectId { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public string? Justifications { get; private set; }

    public string? StrategicObjectiveCode { get; private set; }

    public string? SuggestionSourceCode { get; private set; }

    public string? SuggestionSourceOther { get; private set; }

    public string? ProgramTypeCode { get; private set; }

    public bool? RequiresBudget { get; private set; }

    public decimal? EstimatedBudget { get; private set; }

    public decimal? Year1Cost { get; private set; }

    public decimal? Year2Cost { get; private set; }

    public decimal? Year3Cost { get; private set; }

    public decimal? Year4Cost { get; private set; }

    public DateOnly? ExpectedStartDate { get; private set; }

    public DateOnly? ExpectedEndDate { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? SectionUnitId { get; private set; }

    public string? ProgramNature { get; private set; }

    public string? PreviousProgramName { get; private set; }

    public string? ScopeOfWork { get; private set; }

    public bool? ScreenCoreWork { get; private set; }

    public bool? ScreenOneTime { get; private set; }

    public bool? ScreenTimeBound { get; private set; }

    public bool? ScreenUniqueOutput { get; private set; }

    public bool? ScreenMinCost { get; private set; }

    public bool? ScreenOperationalCosts { get; private set; }

    /// <summary>Computed classification at the last save (Q-P5).</summary>
    public string Classification { get; private set; } = Classifications.Initiative;

    public string? ClassificationOverride { get; private set; }

    public string? OverrideReason { get; private set; }

    public Guid? OverrideByUserId { get; private set; }

    public DateTimeOffset? OverrideAt { get; private set; }

    /// <summary>jsonb: criterion code → score 1–5.</summary>
    public string PriorityScores { get; private set; } = "{}";

    /// <summary>Ratio (1.0 = 100 %) computed at the last save (Q-P6).</summary>
    public decimal? EvaluationScore { get; private set; }

    public string? PriorityBand { get; private set; }

    public string? MainOptionCode { get; private set; }

    public string? MainOptionOther { get; private set; }

    public string? MainAdvantages { get; private set; }

    public string? MainDisadvantages { get; private set; }

    public string? AltOptionCode { get; private set; }

    public string? AltOptionOther { get; private set; }

    public string? AltAdvantages { get; private set; }

    public string? AltDisadvantages { get; private set; }

    public bool? OthersBearCosts { get; private set; }

    public bool? OthersCoverImplementation { get; private set; }

    public decimal? SharedCostAmount { get; private set; }

    public string? PartnerName { get; private set; }

    public decimal? PartnerShare { get; private set; }

    public decimal? AnnualOperationCost { get; private set; }

    public decimal? AnnualLicenceCost { get; private set; }

    public decimal? AnnualMaintenanceCost { get; private set; }

    public decimal? AnnualOtherCost { get; private set; }

    public DateOnly? OperatingCostsFrom { get; private set; }

    public bool? ReturnExpected { get; private set; }

    public decimal? ReturnValue { get; private set; }

    public int? ReturnYear { get; private set; }

    public bool? ReductionExpected { get; private set; }

    public decimal? ReductionValue { get; private set; }

    public int? ReductionYear { get; private set; }

    public string? PracticeOperation { get; private set; }

    public string? PracticeService { get; private set; }

    public string? PracticeProject { get; private set; }

    public string? PracticeInitiative { get; private set; }

    public string? CostPath { get; private set; }

    public bool? CostProposalsRequested { get; private set; }

    public string? ProcurementStatus { get; private set; }

    public bool CostEstimationUploaded { get; private set; }

    public bool ProjectScopeUploaded { get; private set; }

    public DateTimeOffset? QuotationsHandedOffAt { get; private set; }

    public DateTimeOffset? QuotationsUploadedAt { get; private set; }

    public decimal? ApprovedBudget { get; private set; }

    public string EffectiveClassification => ClassificationCalculator.Effective(Classification, ClassificationOverride);

    public static ProgramRequest Create(string number, Guid? requesterEmployeeId, Guid? requesterUserId, string source) => new()
    {
        Number = number,
        RequesterEmployeeId = requesterEmployeeId,
        RequesterUserId = requesterUserId,
        Source = source == "document" ? "document" : "manual",
    };

    /// <summary>Replaces the editable values (Draft only) with the computed classification and priority.</summary>
    public Result Edit(RequestValues v, string classification, decimal? score, string? band)
    {
        ArgumentNullException.ThrowIfNull(v);
        var allowed = RequestLifecycle.Next(Status, RequestActions.Edit);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        (NameAr, NameEn, Description, Justifications) = (Trim(v.NameAr), Trim(v.NameEn), Trim(v.Description), Trim(v.Justifications));
        (StrategicObjectiveCode, SuggestionSourceCode, ProgramTypeCode) = (Trim(v.StrategicObjectiveCode), Trim(v.SuggestionSourceCode), Trim(v.ProgramTypeCode));
        SuggestionSourceOther = OtherCode.Is(SuggestionSourceCode) ? Trim(v.SuggestionSourceOther) : null;
        RequiresBudget = v.RequiresBudget;
        var budget = v.RequiresBudget != false;
        EstimatedBudget = budget ? v.EstimatedBudget : null;
        (Year1Cost, Year2Cost, Year3Cost, Year4Cost) = budget ? (v.Year1Cost, v.Year2Cost, v.Year3Cost, v.Year4Cost) : (null, null, null, null);
        (ExpectedStartDate, ExpectedEndDate, UnitId, SectionUnitId) = (v.ExpectedStartDate, v.ExpectedEndDate, v.UnitId, v.SectionUnitId);
        ProgramNature = v.ProgramNature;
        PreviousProgramName = v.ProgramNature == ProgramNatures.Continuous ? Trim(v.PreviousProgramName) : null;
        ScopeOfWork = Trim(v.ScopeOfWork);
        (ScreenCoreWork, ScreenOneTime, ScreenTimeBound) = (v.ScreenCoreWork, v.ScreenOneTime, v.ScreenTimeBound);
        (ScreenUniqueOutput, ScreenMinCost, ScreenOperationalCosts) = (v.ScreenUniqueOutput, v.ScreenMinCost, v.ScreenOperationalCosts);
        PriorityScores = JsonSerializer.Serialize(v.PriorityScores?.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary() ?? []);
        (MainOptionCode, MainAdvantages, MainDisadvantages) = (Trim(v.MainOptionCode), Trim(v.MainAdvantages), Trim(v.MainDisadvantages));
        MainOptionOther = OtherCode.Is(MainOptionCode) ? Trim(v.MainOptionOther) : null;
        (AltOptionCode, AltAdvantages, AltDisadvantages) = (Trim(v.AltOptionCode), Trim(v.AltAdvantages), Trim(v.AltDisadvantages));
        AltOptionOther = OtherCode.Is(AltOptionCode) ? Trim(v.AltOptionOther) : null;
        (OthersBearCosts, OthersCoverImplementation, SharedCostAmount) = (v.OthersBearCosts, v.OthersCoverImplementation, v.SharedCostAmount);
        (PartnerName, PartnerShare) = (Trim(v.PartnerName), v.PartnerShare);
        (AnnualOperationCost, AnnualLicenceCost, AnnualMaintenanceCost, AnnualOtherCost) =
            (v.AnnualOperationCost, v.AnnualLicenceCost, v.AnnualMaintenanceCost, v.AnnualOtherCost);
        OperatingCostsFrom = v.OperatingCostsFrom;
        ReturnExpected = v.ReturnExpected;
        (ReturnValue, ReturnYear) = v.ReturnExpected == false ? (null, null) : (v.ReturnValue, v.ReturnYear);
        ReductionExpected = v.ReductionExpected;
        (ReductionValue, ReductionYear) = v.ReductionExpected == false ? (null, null) : (v.ReductionValue, v.ReductionYear);
        (PracticeOperation, PracticeService, PracticeProject, PracticeInitiative) =
            (Trim(v.PracticeOperation), Trim(v.PracticeService), Trim(v.PracticeProject), Trim(v.PracticeInitiative));
        CostProposalsRequested = v.CostProposalsRequested;
        if (ProcurementStatus is null)
        {
            CostPath = budget ? v.CostPath : null;
        }

        Classification = classification;
        (EvaluationScore, PriorityBand) = (score, band);
        return Result.Success();
    }

    public RequestValues ToValues() => new()
    {
        NameAr = NameAr,
        NameEn = NameEn,
        Description = Description,
        Justifications = Justifications,
        StrategicObjectiveCode = StrategicObjectiveCode,
        SuggestionSourceCode = SuggestionSourceCode,
        SuggestionSourceOther = SuggestionSourceOther,
        ProgramTypeCode = ProgramTypeCode,
        RequiresBudget = RequiresBudget,
        EstimatedBudget = EstimatedBudget,
        Year1Cost = Year1Cost,
        Year2Cost = Year2Cost,
        Year3Cost = Year3Cost,
        Year4Cost = Year4Cost,
        ExpectedStartDate = ExpectedStartDate,
        ExpectedEndDate = ExpectedEndDate,
        UnitId = UnitId,
        SectionUnitId = SectionUnitId,
        ProgramNature = ProgramNature,
        PreviousProgramName = PreviousProgramName,
        ScopeOfWork = ScopeOfWork,
        ScreenCoreWork = ScreenCoreWork,
        ScreenOneTime = ScreenOneTime,
        ScreenTimeBound = ScreenTimeBound,
        ScreenUniqueOutput = ScreenUniqueOutput,
        ScreenMinCost = ScreenMinCost,
        ScreenOperationalCosts = ScreenOperationalCosts,
        PriorityScores = ReadScores(),
        MainOptionCode = MainOptionCode,
        MainOptionOther = MainOptionOther,
        MainAdvantages = MainAdvantages,
        MainDisadvantages = MainDisadvantages,
        AltOptionCode = AltOptionCode,
        AltOptionOther = AltOptionOther,
        AltAdvantages = AltAdvantages,
        AltDisadvantages = AltDisadvantages,
        OthersBearCosts = OthersBearCosts,
        OthersCoverImplementation = OthersCoverImplementation,
        SharedCostAmount = SharedCostAmount,
        PartnerName = PartnerName,
        PartnerShare = PartnerShare,
        AnnualOperationCost = AnnualOperationCost,
        AnnualLicenceCost = AnnualLicenceCost,
        AnnualMaintenanceCost = AnnualMaintenanceCost,
        AnnualOtherCost = AnnualOtherCost,
        OperatingCostsFrom = OperatingCostsFrom,
        ReturnExpected = ReturnExpected,
        ReturnValue = ReturnValue,
        ReturnYear = ReturnYear,
        ReductionExpected = ReductionExpected,
        ReductionValue = ReductionValue,
        ReductionYear = ReductionYear,
        PracticeOperation = PracticeOperation,
        PracticeService = PracticeService,
        PracticeProject = PracticeProject,
        PracticeInitiative = PracticeInitiative,
        CostPath = CostPath,
        CostProposalsRequested = CostProposalsRequested,
    };

    public IReadOnlyDictionary<string, int> ReadScores() => JsonSerializer.Deserialize<Dictionary<string, int>>(PriorityScores) ?? [];

    public Result Submit(DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, RequestActions.Submit);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        (SubmittedAt, LastActionAt) = (at, at);
        return Result.Success();
    }

    /// <summary>The approval step in progress (StepEntered hook, ADR 0048).</summary>
    public void EnterStep(string? code, string? nameAr, string? nameEn, DateTimeOffset at)
    {
        (ApprovalStepCode, ApprovalStepNameAr, ApprovalStepNameEn) = (code, nameAr, nameEn);
        LastActionAt = at;
    }

    public Result Decide(string action, DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        ApprovalStatus = action switch
        {
            RequestActions.Approve => RequestApprovalResults.Approved,
            RequestActions.Reject => RequestApprovalResults.Rejected,
            RequestActions.Return => RequestApprovalResults.Returned,
            _ => ApprovalStatus,
        };
        (ApprovalStepCode, ApprovalStepNameAr, ApprovalStepNameEn) = (null, null, null);
        LastActionAt = at;
        return Result.Success();
    }

    /// <summary>Final approval: the created project and the approved budget (defaults to the estimated budget, ADR 0048).</summary>
    public void LinkProject(Guid projectId)
    {
        ProjectId = projectId;
        if (RequiresBudget == true)
        {
            ApprovedBudget ??= EstimatedBudget;
        }
    }

    public Result SetApprovedBudget(decimal amount)
    {
        if (Status != RequestStatuses.InApproval)
        {
            return RequestLifecycle.Next(Status, RequestActions.Approve).Error!;
        }

        ApprovedBudget = amount;
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, RequestActions.Cancel);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        (CancelReason, CancelledAt, LastActionAt) = (reason.Trim(), at, at);
        (ApprovalStepCode, ApprovalStepNameAr, ApprovalStepNameEn) = (null, null, null);
        return Result.Success();
    }

    public Result OverrideClassification(string? value, string? reason, Guid? by, DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, RequestActions.Classify);
        if (next.IsFailure)
        {
            return next.Error;
        }

        (ClassificationOverride, OverrideReason) = (value, value is null ? null : reason?.Trim());
        (OverrideByUserId, OverrideAt) = value is null ? ((Guid?)null, (DateTimeOffset?)null) : (by, at);
        return Result.Success();
    }

    /// <summary>Records which cost files are on the request (Draft only).</summary>
    public void SetFileFlags(bool costEstimation, bool projectScope)
    {
        CostEstimationUploaded = costEstimation;
        ProjectScopeUploaded = projectScope;
    }

    /// <summary>W1.3: the project scope goes to Administration for quotations (ADR 0051).</summary>
    public Result HandOff(DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, RequestActions.Procure);
        if (next.IsFailure)
        {
            return next.Error;
        }

        if (CostPath != CostPaths.ProjectScope || !ProjectScopeUploaded)
        {
            return Error.Rule("portfolio.request.scopeMissing", "portfolio.errors.projectScopeMissing");
        }

        if (ProcurementStatus is not null)
        {
            return Error.Rule("portfolio.request.alreadyHandedOff", "portfolio.errors.alreadyHandedOff");
        }

        ProcurementStatus = ProcurementStatuses.WaitingQuotations;
        (QuotationsHandedOffAt, LastActionAt) = (at, at);
        return Result.Success();
    }

    public Result ConfirmQuotations(DateTimeOffset at)
    {
        var next = RequestLifecycle.Next(Status, RequestActions.Procure);
        if (next.IsFailure)
        {
            return next.Error;
        }

        if (ProcurementStatus != ProcurementStatuses.WaitingQuotations)
        {
            return Error.Rule("portfolio.request.notWaitingQuotations", "portfolio.errors.notWaitingQuotations");
        }

        ProcurementStatus = ProcurementStatuses.QuotationsUploaded;
        (QuotationsUploadedAt, LastActionAt) = (at, at);
        return Result.Success();
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
