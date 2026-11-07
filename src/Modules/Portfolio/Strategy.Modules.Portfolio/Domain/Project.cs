using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Modules.Portfolio.Domain;

public static class ProjectStages
{
    public const string Initiation = "initiation";
    public const string Planning = "planning";
    public const string Execution = "execution";
    public const string Closure = "closure";
    public const string Impact = "impact";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> ProjectPath = [Initiation, Planning, Execution, Closure, Impact, Completed];

    /// <summary>Q-P2 default: an initiative skips charter, closure approval and Impact.</summary>
    public static readonly IReadOnlyList<string> InitiativePath = [Planning, Execution, Completed];
}

public static class ProjectStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string OnHold = "on-hold";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
}

/// <summary>
/// A Project or Initiative (SPEC §1.4 B, <c>PROJ-…</c>), created by the request's final approval with the request's data (ADR 0049). Its
/// lifecycle (Take Ownership → … → Completed) is <see cref="ProjectLifecycle"/> driven by the snapshotted <see cref="StageTemplate"/> (ADR 0070).
/// </summary>
[Audited(EntityType)]
public sealed class Project : AuditableEntity
{
    public const string EntityType = "portfolio.project";

    private Project()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string Classification { get; private set; } = Classifications.Project;

    public Guid RequestId { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public string? ScopeOfWork { get; private set; }

    public string Stage { get; private set; } = ProjectStages.Initiation;

    public string Status { get; private set; } = ProjectStatuses.Active;

    /// <summary>Position inside the stage (<see cref="SubStates"/>).</summary>
    public string SubState { get; private set; } = SubStates.Ownership;

    /// <summary>jsonb snapshot of the <see cref="StageTemplate"/> (null on projects created before Phase 9: the current settings apply on first use).</summary>
    public string? Template { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? SectionUnitId { get; private set; }

    public string? StrategicObjectiveCode { get; private set; }

    public string? ProgramTypeCode { get; private set; }

    /// <summary>jsonb array of operational theme codes (lookup <c>portfolio.operational-theme</c>).</summary>
    public string OperationalThemes { get; private set; } = "[]";

    public decimal? Budget { get; private set; }

    public decimal? Year1PlannedCost { get; private set; }

    public decimal? Year2PlannedCost { get; private set; }

    public decimal? Year3PlannedCost { get; private set; }

    public decimal? Year4PlannedCost { get; private set; }

    public decimal? Year1ContractedCost { get; private set; }

    public decimal? Year2ContractedCost { get; private set; }

    public decimal? Year3ContractedCost { get; private set; }

    public decimal? Year4ContractedCost { get; private set; }

    public decimal? Year1ActualCost { get; private set; }

    public decimal? Year2ActualCost { get; private set; }

    public decimal? Year3ActualCost { get; private set; }

    public decimal? Year4ActualCost { get; private set; }

    public decimal? ContractedInitialCost { get; private set; }

    public decimal? AnnualOperationCost { get; private set; }

    public decimal? AnnualMaintenanceCost { get; private set; }

    public decimal? AnnualLicenceCost { get; private set; }

    public decimal? AnnualOtherCost { get; private set; }

    public DateOnly? OperatingCostsFrom { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    /// <summary>Project Owner: the department director of the unit at approval (SPEC §1.9).</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public Guid? RequesterUserId { get; private set; }

    /// <summary>Project Manager: the employee who took ownership (changed only by an Ownership change request).</summary>
    public Guid? PmEmployeeId { get; private set; }

    public DateTimeOffset? OwnershipTakenAt { get; private set; }

    public string? StructuralChanges { get; private set; }

    public string? ResourcesHuman { get; private set; }

    public string? ResourcesFinancial { get; private set; }

    public string? ResourcesTechnical { get; private set; }

    public string? ResourcesContract { get; private set; }

    public string? ResourcesOther { get; private set; }

    public bool LpoUploaded { get; private set; }

    public Guid? TeamId { get; private set; }

    public DateTimeOffset? TeamFormedAt { get; private set; }

    /// <summary>Process in progress or last decided (e.g. <c>portfolio.charter</c>).</summary>
    public string? ApprovalProcess { get; private set; }

    public string? ApprovalStepCode { get; private set; }

    public string? ApprovalStepNameAr { get; private set; }

    public string? ApprovalStepNameEn { get; private set; }

    /// <summary>Last result: null (in progress) · approved · returned · rejected · cancelled.</summary>
    public string? ApprovalResult { get; private set; }

    public decimal Performance { get; private set; }

    public decimal PlannedProgress { get; private set; }

    public string ProgressStatus { get; private set; } = ProgressCalculator.OnTrack;

    public string? SummaryOfAchievement { get; private set; }

    public string? MainDeliverables { get; private set; }

    public decimal? CostPerformance { get; private set; }

    public int? CommunitySatisfaction { get; private set; }

    public int? EmployeeSatisfaction { get; private set; }

    public int? VendorSatisfaction { get; private set; }

    public decimal? PreviousOperationalCost { get; private set; }

    public string? ImpactNotes { get; private set; }

    public DateTimeOffset? UpdateOpenedAt { get; private set; }

    public DateTimeOffset? MonthlyApprovedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string? HoldReason { get; private set; }

    public Guid? MergedIntoProjectId { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Waterfall · agile · hybrid (SPEC §1.13, ADR 0090).</summary>
    public string Methodology { get; private set; } = Methodologies.Waterfall;

    /// <summary>Wasel milestone update mode (SPEC §1.18): <c>pm-only</c> (default) or <c>vendor-then-pm</c>.</summary>
    public string MilestoneUpdateMode { get; private set; } = UpdateModes.PmOnly;

    public void SetUpdateMode(string mode) => MilestoneUpdateMode = mode;

    /// <summary>Project override of the agile approval mode (null = the installation default, ADR 0091).</summary>
    public string? AgileApprovalMode { get; private set; }

    public int? SprintLengthDays { get; private set; }

    public string? DefinitionOfDone { get; private set; }

    /// <summary>jsonb <see cref="BoardConfig"/> (null = default board).</summary>
    public string? Board { get; private set; }

    /// <summary>jsonb: dimension → <see cref="HealthOverride"/> (ADR 0093).</summary>
    public string HealthOverrides { get; private set; } = "{}";

    /// <summary>jsonb: gate → item code → <see cref="ChecklistTick"/> (ADR 0095).</summary>
    public string Checklist { get; private set; } = "{}";

    public Guid? PortfolioGroupId { get; private set; }

    public Guid? ProgrammeId { get; private set; }

    /// <summary>An approved, not yet applied change request with the area <c>plan</c> re-opens planning (ADR 0094).</summary>
    public Guid? ReplanChangeRequestId { get; private set; }

    public static Project FromRequest(string number, ProgramRequest request, Guid? ownerEmployeeId, StageTemplate? template = null, string? methodology = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classification = request.EffectiveClassification;
        var project = new Project
        {
            Number = number,
            Classification = classification,
            RequestId = request.Id,
            NameAr = request.NameAr,
            NameEn = request.NameEn,
            Description = request.Description,
            ScopeOfWork = request.ScopeOfWork,
            Stage = classification == Classifications.Project ? ProjectStages.Initiation : ProjectStages.Planning,
            Status = ProjectStatuses.Active,
            SubState = SubStates.Ownership,
            UnitId = request.UnitId,
            SectionUnitId = request.SectionUnitId,
            StrategicObjectiveCode = request.StrategicObjectiveCode,
            ProgramTypeCode = request.ProgramTypeCode,
            Budget = request.RequiresBudget == true ? request.ApprovedBudget ?? request.EstimatedBudget : null,
            Year1PlannedCost = request.Year1Cost,
            Year2PlannedCost = request.Year2Cost,
            Year3PlannedCost = request.Year3Cost,
            Year4PlannedCost = request.Year4Cost,
            AnnualOperationCost = request.AnnualOperationCost,
            AnnualMaintenanceCost = request.AnnualMaintenanceCost,
            AnnualLicenceCost = request.AnnualLicenceCost,
            AnnualOtherCost = request.AnnualOtherCost,
            OperatingCostsFrom = request.OperatingCostsFrom,
            PlannedStart = request.ExpectedStartDate,
            PlannedEnd = request.ExpectedEndDate,
            OwnerEmployeeId = ownerEmployeeId,
            RequesterEmployeeId = request.RequesterEmployeeId,
            RequesterUserId = request.RequesterUserId,
            Methodology = methodology is { } m && Methodologies.All.Contains(m) ? m : Methodologies.Waterfall,
        };
        if (template is not null)
        {
            project.UseTemplate(template);
        }

        return project;
    }

    /// <summary>The template in force (snapshot; <paramref name="fallback"/> when the project predates Phase 9).</summary>
    public StageTemplate TemplateOr(StageTemplate fallback) =>
        Template is { Length: > 2 } json ? System.Text.Json.JsonSerializer.Deserialize<StageTemplate>(json, TemplateJson) ?? fallback : fallback;

    public void UseTemplate(StageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        Template = System.Text.Json.JsonSerializer.Serialize(template, TemplateJson);
        Stage = ProjectLifecycle.StageOf(SubState, template);
    }

    private static readonly System.Text.Json.JsonSerializerOptions TemplateJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Moves the lifecycle (transition table) and the stage.</summary>
    public Strategy.Platform.Kernel.Results.Result Apply(string action, StageTemplate template, DateTimeOffset at)
    {
        var next = ProjectLifecycle.Next(Status, SubState, action, template);
        if (next.IsFailure)
        {
            return next.Error;
        }

        SubState = next.Value;
        Stage = ProjectLifecycle.StageOf(SubState, template);
        if (SubState == SubStates.Done)
        {
            Status = ProjectStatuses.Completed;
            CompletedAt = at;
        }

        return Strategy.Platform.Kernel.Results.Result.Success();
    }

    public Strategy.Platform.Kernel.Results.Result ApplyStatus(string action, string? reason, DateTimeOffset at)
    {
        var next = ProjectLifecycle.NextStatus(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        switch (action)
        {
            case ProjectStatusActions.Hold:
                HoldReason = reason;
                break;
            case ProjectStatusActions.Resume:
                HoldReason = null;
                break;
            case ProjectStatusActions.Cancel:
                CancelReason = reason;
                CompletedAt = at;
                break;
        }

        return Strategy.Platform.Kernel.Results.Result.Success();
    }

    public void SetPm(Guid employeeId, DateTimeOffset at)
    {
        PmEmployeeId = employeeId;
        OwnershipTakenAt ??= at;
    }

    public void SetOwner(Guid? employeeId) => OwnerEmployeeId = employeeId;

    public void EditCharter(ProjectCharterValues v, string themesJson)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = Clean(v.NameAr) ?? NameAr;
        NameEn = Clean(v.NameEn) ?? NameEn;
        Description = RichText.Sanitize(v.Description);
        ScopeOfWork = Clean(v.ScopeOfWork);
        StrategicObjectiveCode = Clean(v.StrategicObjectiveCode);
        OperationalThemes = themesJson;
        StructuralChanges = Clean(v.StructuralChanges);
        ResourcesHuman = Clean(v.ResourcesHuman);
        ResourcesFinancial = Clean(v.ResourcesFinancial);
        ResourcesTechnical = Clean(v.ResourcesTechnical);
        ResourcesContract = Clean(v.ResourcesContract);
        ResourcesOther = Clean(v.ResourcesOther);
        PlannedStart = v.PlannedStart ?? PlannedStart;
        PlannedEnd = v.PlannedEnd ?? PlannedEnd;
        Year1ContractedCost = v.Year1ContractedCost;
        Year2ContractedCost = v.Year2ContractedCost;
        Year3ContractedCost = v.Year3ContractedCost;
        Year4ContractedCost = v.Year4ContractedCost;
        Year1ActualCost = v.Year1ActualCost;
        Year2ActualCost = v.Year2ActualCost;
        Year3ActualCost = v.Year3ActualCost;
        Year4ActualCost = v.Year4ActualCost;
        ContractedInitialCost = v.ContractedInitialCost;
        AnnualOperationCost = v.AnnualOperationCost;
        AnnualMaintenanceCost = v.AnnualMaintenanceCost;
        AnnualLicenceCost = v.AnnualLicenceCost;
        AnnualOtherCost = v.AnnualOtherCost;
        OperatingCostsFrom = v.OperatingCostsFrom;
    }

    public void EditClosure(string? summary, string? mainDeliverables)
    {
        SummaryOfAchievement = Clean(summary);
        MainDeliverables = Clean(mainDeliverables);
    }

    public void EditImpact(decimal? costPerformance, int? community, int? employee, int? vendor, decimal? previousOperationalCost, string? notes)
    {
        CostPerformance = costPerformance;
        CommunitySatisfaction = community;
        EmployeeSatisfaction = employee;
        VendorSatisfaction = vendor;
        PreviousOperationalCost = previousOperationalCost;
        ImpactNotes = Clean(notes);
    }

    public void MarkLpoUploaded() => LpoUploaded = true;

    public void LinkTeam(Guid teamId, DateTimeOffset at)
    {
        TeamId = teamId;
        TeamFormedAt ??= at;
    }

    public void EnterApproval(string process, string? stepCode, string? stepAr, string? stepEn)
    {
        ApprovalProcess = process;
        ApprovalStepCode = stepCode;
        ApprovalStepNameAr = stepAr;
        ApprovalStepNameEn = stepEn;
        ApprovalResult = null;
    }

    public void DecideApproval(string process, string result)
    {
        ApprovalProcess = process;
        ApprovalResult = result;
        ApprovalStepCode = null;
        ApprovalStepNameAr = null;
        ApprovalStepNameEn = null;
    }

    public void SetProgress(decimal performance, decimal planned, string status)
    {
        Performance = performance;
        PlannedProgress = planned;
        ProgressStatus = status;
    }

    public void MarkUpdateOpened(DateTimeOffset at) => UpdateOpenedAt = at;

    public void MarkMonthlyApproved(DateTimeOffset at) => MonthlyApprovedAt = at;

    /// <summary>Change request "details" / "postponement" (ADR 0073).</summary>
    public void ApplyDetails(string? nameAr, string? nameEn, string? description, string? scope, DateOnly? start, DateOnly? end, decimal? budget)
    {
        NameAr = Clean(nameAr) ?? NameAr;
        NameEn = Clean(nameEn) ?? NameEn;
        Description = RichText.Sanitize(description) ?? Description;
        ScopeOfWork = Clean(scope) ?? ScopeOfWork;
        PlannedStart = start ?? PlannedStart;
        PlannedEnd = end ?? PlannedEnd;
        Budget = budget ?? Budget;
    }

    public void MergeInto(Guid target) => MergedIntoProjectId = target;

    public void SetMethodology(string methodology, int? sprintLengthDays, string? definitionOfDone, string? approvalMode)
    {
        Methodology = methodology;
        SprintLengthDays = Methodologies.UsesSprints(methodology) ? sprintLengthDays : null;
        DefinitionOfDone = Methodologies.UsesSprints(methodology) ? Clean(definitionOfDone) : null;
        AgileApprovalMode = methodology == Methodologies.Agile && approvalMode is { } a && AgileApprovalModes.All.Contains(a) ? a : null;
    }

    public void ChangeMethodology(string methodology) => Methodology = methodology;

    public void SetBoard(BoardConfig board)
    {
        ArgumentNullException.ThrowIfNull(board);
        Board = board.ToJson();
    }

    public BoardConfig BoardOrDefault() => BoardConfig.FromJson(Board);

    public void SetHealthOverrides(string json) => HealthOverrides = json;

    public void SetChecklist(string json) => Checklist = json;

    public void SetGrouping(Guid? portfolioGroupId, Guid? programmeId)
    {
        PortfolioGroupId = portfolioGroupId;
        ProgrammeId = programmeId;
    }

    public void OpenReplan(Guid? changeRequestId) => ReplanChangeRequestId = changeRequestId;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Charter details the PM edits (SPEC §1.4 B identity, budget &amp; cost, charter resources).</summary>
public sealed record ProjectCharterValues(
    string? NameAr = null,
    string? NameEn = null,
    string? Description = null,
    string? ScopeOfWork = null,
    string? StrategicObjectiveCode = null,
    IReadOnlyList<string>? OperationalThemes = null,
    string? StructuralChanges = null,
    string? ResourcesHuman = null,
    string? ResourcesFinancial = null,
    string? ResourcesTechnical = null,
    string? ResourcesContract = null,
    string? ResourcesOther = null,
    DateOnly? PlannedStart = null,
    DateOnly? PlannedEnd = null,
    decimal? Year1ContractedCost = null,
    decimal? Year2ContractedCost = null,
    decimal? Year3ContractedCost = null,
    decimal? Year4ContractedCost = null,
    decimal? Year1ActualCost = null,
    decimal? Year2ActualCost = null,
    decimal? Year3ActualCost = null,
    decimal? Year4ActualCost = null,
    decimal? ContractedInitialCost = null,
    decimal? AnnualOperationCost = null,
    decimal? AnnualMaintenanceCost = null,
    decimal? AnnualLicenceCost = null,
    decimal? AnnualOtherCost = null,
    DateOnly? OperatingCostsFrom = null);

/// <summary>One immutable version of the AI Proposal Assessment of a request (SPEC §1.15 A5–A6, ADR 0050).</summary>
[Audited(EntityType)]
[AuditParent(ProgramRequest.EntityType, nameof(RequestId))]
public sealed class ProposalAssessment : AuditableEntity
{
    public const string EntityType = "portfolio.assessment";

    private ProposalAssessment()
    {
    }

    public Guid RequestId { get; private set; }

    public int VersionNo { get; private set; }

    /// <summary>jsonb: the validated assessment (docs/design/portfolio.md §8.2) plus the server-computed similar records.</summary>
    public string Content { get; private set; } = "{}";

    public string? Language { get; private set; }

    /// <summary>jsonb array of the document names read.</summary>
    public string Documents { get; private set; } = "[]";

    public Guid? UsageId { get; private set; }

    public Guid? GeneratedByUserId { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public static ProposalAssessment Create(Guid requestId, int versionNo, string content, string? language, string documents, Guid? usageId, Guid? by, DateTimeOffset at) =>
        new()
        {
            RequestId = requestId,
            VersionNo = versionNo,
            Content = content,
            Language = language,
            Documents = documents,
            UsageId = usageId,
            GeneratedByUserId = by,
            GeneratedAt = at,
        };
}
