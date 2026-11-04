using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Domain;

public static class ItemStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
}

/// <summary>A row under a project (KPI, risk, stakeholder…). Never deleted: deactivated with a reason.</summary>
public abstract class ProjectChild : AuditableEntity
{
    public Guid ProjectId { get; protected set; }

    public string Status { get; protected set; } = ItemStatuses.Active;

    public string? InactiveReason { get; protected set; }

    public void Deactivate(string? reason)
    {
        Status = ItemStatuses.Inactive;
        InactiveReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class KpiNatures
{
    public const string New = "new";
    public const string Existing = "existing";

    public static readonly IReadOnlyList<string> All = [New, Existing];
}

public static class WhenMeasured
{
    public const string During = "during";
    public const string After = "after";

    public static readonly IReadOnlyList<string> All = [During, After];
}

public sealed record ProjectKpiValues(
    string Nature,
    Guid? KpiCardId,
    string? ProjectGoal,
    string? NameAr,
    string? NameEn,
    string? Equation,
    string? Direction,
    string? IndexClassCode,
    decimal? Target,
    int? TargetYear,
    string? WhenMeasured,
    decimal? CurrentResult);

/// <summary>Project KPI (SPEC §1.4 D): new, or an existing KPI card (ADR 0074).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class ProjectKpi : ProjectChild
{
    public const string EntityType = "portfolio.project-kpi";

    private ProjectKpi()
    {
    }

    public string Nature { get; private set; } = KpiNatures.New;

    public Guid? KpiCardId { get; private set; }

    public string? ProjectGoal { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Equation { get; private set; }

    public string? Direction { get; private set; }

    public string? IndexClassCode { get; private set; }

    public decimal? Target { get; private set; }

    public int? TargetYear { get; private set; }

    public string? WhenMeasured { get; private set; }

    public decimal? CurrentResult { get; private set; }

    public string CreatedStage { get; private set; } = ProjectStages.Initiation;

    public static ProjectKpi Create(Guid projectId, string stage, ProjectKpiValues values)
    {
        var kpi = new ProjectKpi { ProjectId = projectId, CreatedStage = stage };
        kpi.Edit(values);
        return kpi;
    }

    public void Edit(ProjectKpiValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Nature = v.Nature;
        KpiCardId = v.Nature == KpiNatures.Existing ? v.KpiCardId : null;
        ProjectGoal = Clean(v.ProjectGoal);
        NameAr = Clean(v.NameAr);
        NameEn = Clean(v.NameEn);
        Equation = Clean(v.Equation);
        Direction = Clean(v.Direction);
        IndexClassCode = Clean(v.IndexClassCode);
        Target = v.Target;
        TargetYear = v.TargetYear;
        WhenMeasured = Clean(v.WhenMeasured);
        CurrentResult = v.CurrentResult;
    }
}

public static class RiskResponses
{
    public static readonly IReadOnlyList<string> All = ["accept", "avoid", "mitigate", "transfer"];
}

public sealed record ProjectRiskValues(
    string? Title,
    string? CategoryCode,
    int Probability,
    int Impact,
    string? RiskFactor,
    int? ControlEvaluation,
    string? Response,
    string? MitigationActions,
    decimal? ImplementationPct,
    DateOnly? ImplementationStart,
    DateOnly? ImplementationEnd,
    Guid? OwnerEmployeeId,
    string? RmsStatus);

/// <summary>Project risk (SPEC §1.4 D) with stored scores so the scoring can be swapped (ADR 0074).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class ProjectRisk : ProjectChild
{
    public const string EntityType = "portfolio.project-risk";

    private ProjectRisk()
    {
    }

    public string? Title { get; private set; }

    public string? CategoryCode { get; private set; }

    public int Probability { get; private set; }

    public int Impact { get; private set; }

    public string? RiskFactor { get; private set; }

    public int? ControlEvaluation { get; private set; }

    public int InherentScore { get; private set; }

    public string InherentBand { get; private set; } = RiskBands.Low;

    public decimal ResidualScore { get; private set; }

    public string ResidualBand { get; private set; } = RiskBands.Low;

    public string? Response { get; private set; }

    public string? MitigationActions { get; private set; }

    public decimal? ImplementationPct { get; private set; }

    public DateOnly? ImplementationStart { get; private set; }

    public DateOnly? ImplementationEnd { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string? RmsStatus { get; private set; }

    public string CreatedStage { get; private set; } = ProjectStages.Initiation;

    public static ProjectRisk Create(Guid projectId, string stage, ProjectRiskValues values, RiskScore score)
    {
        var risk = new ProjectRisk { ProjectId = projectId, CreatedStage = stage };
        risk.Edit(values, score);
        return risk;
    }

    public void Edit(ProjectRiskValues v, RiskScore score)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(score);
        Title = Clean(v.Title);
        CategoryCode = Clean(v.CategoryCode);
        Probability = v.Probability;
        Impact = v.Impact;
        RiskFactor = Clean(v.RiskFactor);
        ControlEvaluation = v.ControlEvaluation;
        Response = Clean(v.Response);
        MitigationActions = Clean(v.MitigationActions);
        ImplementationPct = v.ImplementationPct;
        ImplementationStart = v.ImplementationStart;
        ImplementationEnd = v.ImplementationEnd;
        OwnerEmployeeId = v.OwnerEmployeeId;
        RmsStatus = Clean(v.RmsStatus);
        Score(score);
    }

    public void Score(RiskScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        InherentScore = score.Inherent;
        InherentBand = score.InherentBand;
        ResidualScore = score.Residual;
        ResidualBand = score.ResidualBand;
    }
}

public static class Levels
{
    public static readonly IReadOnlyList<string> All = ["low", "medium", "high"];
}

public sealed record StakeholderValues(
    string? Name, string? ClassificationCode, string? InterestLevel, string? ImpactLevel, string? CommunicationMethod, string? CommunicationFrequency,
    string? Email, string? Phone);

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Stakeholder : ProjectChild
{
    public const string EntityType = "portfolio.stakeholder";

    private Stakeholder()
    {
    }

    public string? Name { get; private set; }

    public string? ClassificationCode { get; private set; }

    public string? InterestLevel { get; private set; }

    public string? ImpactLevel { get; private set; }

    public string? CommunicationMethod { get; private set; }

    public string? CommunicationFrequency { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public static Stakeholder Create(Guid projectId, StakeholderValues values)
    {
        var item = new Stakeholder { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(StakeholderValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Name = Clean(v.Name);
        ClassificationCode = Clean(v.ClassificationCode);
        InterestLevel = Clean(v.InterestLevel);
        ImpactLevel = Clean(v.ImpactLevel);
        CommunicationMethod = Clean(v.CommunicationMethod);
        CommunicationFrequency = Clean(v.CommunicationFrequency);
        Email = Clean(v.Email);
        Phone = Clean(v.Phone);
    }
}

public sealed record TeamMemberValues(Guid EmployeeId, string? RoleAr, string? RoleEn, string? Responsibilities);

/// <summary>A proposed project team member listed on the charter (≥ 3, ADR 0070); the formal team is formed in Teams.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class ProposedMember : ProjectChild
{
    public const string EntityType = "portfolio.team-member";

    private ProposedMember()
    {
    }

    public Guid EmployeeId { get; private set; }

    public string? RoleAr { get; private set; }

    public string? RoleEn { get; private set; }

    public string? Responsibilities { get; private set; }

    public static ProposedMember Create(Guid projectId, TeamMemberValues values)
    {
        var item = new ProposedMember { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(TeamMemberValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        EmployeeId = v.EmployeeId;
        RoleAr = Clean(v.RoleAr);
        RoleEn = Clean(v.RoleEn);
        Responsibilities = Clean(v.Responsibilities);
    }
}

public static class PlanningCategories
{
    public const string InScope = "in-scope";
    public const string OutOfScope = "out-of-scope";
    public const string Assumption = "assumption";
    public const string Constraint = "constraint";
    public const string Deliverable = "deliverable";
    public const string AddedValue = "added-value";

    public static readonly IReadOnlyList<string> All =
    [
        InScope, OutOfScope, Assumption, Constraint, Deliverable, AddedValue, "resource-human", "resource-technical", "resource-financial", "resource-contract",
        "resource-other",
    ];
}

public sealed record PlanningItemValues(string Category, string? Description);

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class PlanningItem : ProjectChild
{
    public const string EntityType = "portfolio.planning-item";

    private PlanningItem()
    {
    }

    public string Category { get; private set; } = PlanningCategories.InScope;

    public string? Description { get; private set; }

    public string CreatedStage { get; private set; } = ProjectStages.Planning;

    public static PlanningItem Create(Guid projectId, string stage, PlanningItemValues values)
    {
        var item = new PlanningItem { ProjectId = projectId, CreatedStage = stage };
        item.Edit(values);
        return item;
    }

    public void Edit(PlanningItemValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Category = v.Category;
        Description = Clean(v.Description);
    }
}

public sealed record ChallengeValues(string? Challenge, string? Impact, decimal? AchievedPct, string? Recommendation, DateOnly? ReportDate, Guid? MilestoneId);

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Challenge : ProjectChild
{
    public const string EntityType = "portfolio.challenge";

    private Challenge()
    {
    }

    public string? Text { get; private set; }

    public string? Impact { get; private set; }

    public decimal? AchievedPct { get; private set; }

    public string? Recommendation { get; private set; }

    public DateOnly? ReportDate { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public static Challenge Create(Guid projectId, ChallengeValues values)
    {
        var item = new Challenge { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(ChallengeValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Text = Clean(v.Challenge);
        Impact = Clean(v.Impact);
        AchievedPct = v.AchievedPct;
        Recommendation = Clean(v.Recommendation);
        ReportDate = v.ReportDate;
        MilestoneId = v.MilestoneId;
    }
}

public sealed record LessonValues(string? Stage, string? Lesson, string? TypeCode, string? Recommendation);

[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Lesson : ProjectChild
{
    public const string EntityType = "portfolio.lesson";

    private Lesson()
    {
    }

    public string? Stage { get; private set; }

    public string? Text { get; private set; }

    public string? TypeCode { get; private set; }

    public string? Recommendation { get; private set; }

    public static Lesson Create(Guid projectId, LessonValues values)
    {
        var item = new Lesson { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(LessonValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Stage = Clean(v.Stage);
        Text = Clean(v.Lesson);
        TypeCode = Clean(v.TypeCode);
        Recommendation = Clean(v.Recommendation);
    }
}

public sealed record MeetingValues(DateTimeOffset MeetingAt, string? Title, string? Notes);

/// <summary>Team meeting / minutes of meeting (MOM). Notes are sanitised rich text.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Meeting : ProjectChild
{
    public const string EntityType = "portfolio.meeting";

    private Meeting()
    {
    }

    public DateTimeOffset MeetingAt { get; private set; }

    public string? Title { get; private set; }

    public string? Notes { get; private set; }

    public static Meeting Create(Guid projectId, MeetingValues values)
    {
        var item = new Meeting { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(MeetingValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        MeetingAt = v.MeetingAt;
        Title = Clean(v.Title);
        Notes = Clean(v.Notes);
    }
}

public sealed record ContractValues(string? Name, string? Number, string? Supplier, DateOnly? IssueDate, decimal? Amount, string? ContractStatus);

/// <summary>Contract / PO (SPEC §1.4 D) — uploaded by Administration with the LPO, or by the PM.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Contract : ProjectChild
{
    public const string EntityType = "portfolio.contract";

    private Contract()
    {
    }

    public string? Name { get; private set; }

    public string? Number { get; private set; }

    public string? Supplier { get; private set; }

    public DateOnly? IssueDate { get; private set; }

    public decimal? Amount { get; private set; }

    public string ContractStatus { get; private set; } = "active";

    public static Contract Create(Guid projectId, ContractValues values)
    {
        var item = new Contract { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(ContractValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Name = Clean(v.Name);
        Number = Clean(v.Number);
        Supplier = Clean(v.Supplier);
        IssueDate = v.IssueDate;
        Amount = v.Amount;
        ContractStatus = Clean(v.ContractStatus) ?? "active";
    }
}

public static class MilestoneStatuses
{
    public const string NotStarted = "not-started";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>Where a milestone's monthly update is (SPEC "Approved?").</summary>
public static class UpdateStates
{
    public const string WaitingUpdate = "waiting-update";
    public const string Draft = "draft";
    public const string PendingApproval = "pending-approval";
    public const string Approved = "approved";
    public const string Returned = "returned";
}

public sealed record MilestoneValues(
    string? TitleAr,
    string? TitleEn,
    Guid? ParentId,
    bool IsHeader,
    bool HighLevel,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    decimal Weight,
    decimal? Cost,
    bool IsDeliverable,
    string? MainDeliverable,
    string? AcceptanceCriteria,
    bool? QualityMet,
    bool IsInnovative,
    string? InnovativeReason,
    int Sort = 0);

/// <summary>Milestone / work plan (SPEC §1.4 C, <c>&lt;Number&gt;-1.1</c>). Weights and achievements are ratios of the whole project.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Milestone : AuditableEntity
{
    public const string EntityType = "portfolio.milestone";

    private Milestone()
    {
    }

    public Guid ProjectId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public bool IsHeader { get; private set; }

    public bool HighLevel { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    public decimal Weight { get; private set; }

    /// <summary>Highest approved achievement (locked: never lowered, ADR 0071).</summary>
    public decimal MaxAchieved { get; private set; }

    public decimal? PendingAchieve { get; private set; }

    public DateOnly? ActualStart { get; private set; }

    public DateOnly? ActualEnd { get; private set; }

    public DateOnly? PendingActualStart { get; private set; }

    public DateOnly? PendingActualEnd { get; private set; }

    public decimal? Cost { get; private set; }

    public string Status { get; private set; } = MilestoneStatuses.NotStarted;

    public string UpdateState { get; private set; } = UpdateStates.WaitingUpdate;

    public string? Causality { get; private set; }

    public string? CorrectiveActions { get; private set; }

    public string? EvidenceNote { get; private set; }

    public bool IsDeliverable { get; private set; }

    public string? MainDeliverable { get; private set; }

    public string? AcceptanceCriteria { get; private set; }

    public bool? QualityMet { get; private set; }

    public bool IsInnovative { get; private set; }

    public string? InnovativeReason { get; private set; }

    public int Sort { get; private set; }

    public string? CancelReason { get; private set; }

    public bool IsActive => Status != MilestoneStatuses.Cancelled;

    public static Milestone Create(Guid projectId, string code, MilestoneValues values)
    {
        var m = new Milestone { ProjectId = projectId, Code = code };
        m.Edit(values);
        return m;
    }

    public void Edit(MilestoneValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = Clean(v.TitleAr);
        TitleEn = Clean(v.TitleEn);
        ParentId = v.IsHeader ? null : v.ParentId;
        IsHeader = v.IsHeader;
        HighLevel = v.HighLevel;
        PlannedStart = v.PlannedStart;
        PlannedEnd = v.PlannedEnd;
        Weight = v.IsHeader ? 0 : v.Weight;
        Cost = v.Cost;
        IsDeliverable = v.IsDeliverable;
        MainDeliverable = v.IsDeliverable ? Clean(v.MainDeliverable) : null;
        AcceptanceCriteria = v.IsDeliverable ? Clean(v.AcceptanceCriteria) : null;
        QualityMet = v.IsDeliverable ? v.QualityMet : null;
        IsInnovative = v.IsInnovative;
        InnovativeReason = v.IsInnovative ? Clean(v.InnovativeReason) : null;
        Sort = v.Sort;
    }

    /// <summary>Stores the PM's pending values for the open window (validated by <see cref="MilestoneRules"/> first).</summary>
    public void RecordProgress(MilestoneProgressValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        PendingAchieve = v.Achieve;
        PendingActualStart = v.ActualStart;
        PendingActualEnd = v.ActualEnd;
        Causality = Clean(v.Causality);
        CorrectiveActions = Clean(v.CorrectiveActions);
        EvidenceNote = Clean(v.EvidenceNote);
        UpdateState = UpdateStates.Draft;
        if (Status == MilestoneStatuses.NotStarted && v.Achieve > 0)
        {
            Status = MilestoneStatuses.Active;
        }
    }

    public bool HasPendingUpdate => UpdateState is UpdateStates.Draft or UpdateStates.Returned or UpdateStates.PendingApproval && PendingAchieve is not null;

    public void SubmitUpdate()
    {
        if (UpdateState is UpdateStates.Draft or UpdateStates.Returned)
        {
            UpdateState = UpdateStates.PendingApproval;
        }
    }

    /// <summary>Approval locks the achievement (Max Achieved) and the actual dates.</summary>
    public void ApproveUpdate()
    {
        if (UpdateState != UpdateStates.PendingApproval || PendingAchieve is not { } achieve)
        {
            return;
        }

        MaxAchieved = Math.Max(MaxAchieved, achieve);
        ActualStart = PendingActualStart ?? ActualStart;
        ActualEnd = PendingActualEnd ?? ActualEnd;
        PendingAchieve = null;
        PendingActualStart = null;
        PendingActualEnd = null;
        UpdateState = UpdateStates.Approved;
        Status = Weight > 0 && MaxAchieved >= Weight ? MilestoneStatuses.Completed : MaxAchieved > 0 ? MilestoneStatuses.Active : Status;
    }

    /// <summary>A new window: an approved, unfinished milestone waits for this month's update.</summary>
    public void ReopenForUpdate()
    {
        if (UpdateState == UpdateStates.Approved)
        {
            UpdateState = UpdateStates.WaitingUpdate;
        }
    }

    public void ReturnUpdate()
    {
        if (UpdateState == UpdateStates.PendingApproval)
        {
            UpdateState = UpdateStates.Returned;
        }
    }

    /// <summary>Header phases: the sums of their children (one level, ADR 0070).</summary>
    public void Aggregate(decimal weight, decimal achieved, DateOnly? start, DateOnly? end)
    {
        Weight = weight;
        MaxAchieved = achieved;
        PlannedStart = start ?? PlannedStart;
        PlannedEnd = end ?? PlannedEnd;
        Status = weight > 0 && achieved >= weight ? MilestoneStatuses.Completed : achieved > 0 ? MilestoneStatuses.Active : MilestoneStatuses.NotStarted;
    }

    public Result Cancel(string? reason)
    {
        if (MaxAchieved > 0)
        {
            return Error.Rule("portfolio.milestone.achieved", "portfolio.errors.milestoneHasAchievement");
        }

        Status = MilestoneStatuses.Cancelled;
        CancelReason = Clean(reason);
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class MonthlyStatuses
{
    public const string Open = "open";
    public const string Submitted = "submitted";
    public const string Returned = "returned";
    public const string Approved = "approved";

    public static bool AcceptsUpdates(string status) => status is Open or Returned;
}

/// <summary>One monthly update window of a project (ADR 0071).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class MonthlyUpdate : AuditableEntity
{
    public const string EntityType = "portfolio.monthly-update";

    private MonthlyUpdate()
    {
    }

    public Guid ProjectId { get; private set; }

    /// <summary><c>YYYY-MM</c>.</summary>
    public string Period { get; private set; } = string.Empty;

    /// <summary>1 for the month's window (job); 2, 3… for additional windows the PM Office opens in the same month (ADR 0071).</summary>
    public int Sequence { get; private set; } = 1;

    public string Status { get; private set; } = MonthlyStatuses.Open;

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    /// <summary><c>job</c> or the user id who opened it now.</summary>
    public string OpenedBy { get; private set; } = "job";

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? RemindedAt { get; private set; }

    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    public string? SubmitComment { get; private set; }

    /// <summary>jsonb snapshot of the submitted milestone updates.</summary>
    public string Items { get; private set; } = "[]";

    public static MonthlyUpdate Open(Guid projectId, string period, int sequence, DateTimeOffset openedAt, DateTimeOffset? dueAt, string openedBy) =>
        new() { ProjectId = projectId, Period = period, Sequence = sequence, OpenedAt = openedAt, DueAt = dueAt, OpenedBy = openedBy };

    public static string PeriodOf(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    public void Submit(DateTimeOffset at, string items, string? comment)
    {
        Status = MonthlyStatuses.Submitted;
        SubmittedAt = at;
        Items = items;
        SubmitComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public void Approve(DateTimeOffset at)
    {
        Status = MonthlyStatuses.Approved;
        ApprovedAt = at;
    }

    public void Return() => Status = MonthlyStatuses.Returned;

    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;

    public void MarkOverdueNotified(DateTimeOffset at) => OverdueNotifiedAt = at;
}

public static class ChangeTypes
{
    public const string Details = "details";
    public const string Cancellation = "cancellation";
    public const string Postponement = "postponement";
    public const string Ownership = "ownership";
    public const string Merge = "merge";

    public static readonly IReadOnlyList<string> All = [Details, Cancellation, Postponement, Ownership, Merge];
}

public static class ChangeStatuses
{
    public const string Draft = "draft";
    public const string UnderReview = "under-review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = [Draft, UnderReview, Approved, Rejected, Cancelled, Completed];
}

public static class ChangeActions
{
    public const string Edit = "edit";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Return = "return";
    public const string Cancel = "cancel";
    public const string Apply = "apply";

    public static readonly IReadOnlyList<string> All = [Edit, Submit, Approve, Reject, Return, Cancel, Apply];
}

/// <summary>Change request lifecycle (W7, ADR 0073), one table, unit-tested.</summary>
public static class ChangeLifecycle
{
    private static readonly Dictionary<(string Status, string Action), string> Transitions = new()
    {
        [(ChangeStatuses.Draft, ChangeActions.Edit)] = ChangeStatuses.Draft,
        [(ChangeStatuses.Draft, ChangeActions.Submit)] = ChangeStatuses.UnderReview,
        [(ChangeStatuses.Draft, ChangeActions.Cancel)] = ChangeStatuses.Cancelled,
        [(ChangeStatuses.UnderReview, ChangeActions.Approve)] = ChangeStatuses.Approved,
        [(ChangeStatuses.UnderReview, ChangeActions.Reject)] = ChangeStatuses.Rejected,
        [(ChangeStatuses.UnderReview, ChangeActions.Return)] = ChangeStatuses.Draft,
        [(ChangeStatuses.UnderReview, ChangeActions.Cancel)] = ChangeStatuses.Cancelled,
        [(ChangeStatuses.Approved, ChangeActions.Apply)] = ChangeStatuses.Completed,
        [(ChangeStatuses.Approved, ChangeActions.Cancel)] = ChangeStatuses.Cancelled,
    };

    public static bool Allows(string status, string action) => Transitions.ContainsKey((status, action));

    public static Result<string> Next(string status, string action) =>
        Transitions.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.change.transition", "portfolio.errors.notAllowedInStatus", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });
}

public sealed record ChangeRequestValues(
    string Type,
    IReadOnlyList<string>? Areas,
    string? Reason,
    string? ImpactOfNonResponse,
    string? ProposedNameAr,
    string? ProposedNameEn,
    string? ProposedDescription,
    string? ProposedScope,
    DateOnly? ProposedStart,
    DateOnly? ProposedEnd,
    decimal? ProposedBudget,
    bool CostAffected,
    Guid? NewPmEmployeeId,
    Guid? NewOwnerEmployeeId,
    Guid? MergeIntoProjectId,
    DateOnly? RequestedDate);

/// <summary>Change request (SPEC §1.4 D, W7). Applying it is a separate PM confirmation (ADR 0073).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class ChangeRequest : AuditableEntity
{
    public const string EntityType = "portfolio.change-request";

    private ChangeRequest()
    {
    }

    public Guid ProjectId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public string Type { get; private set; } = ChangeTypes.Details;

    /// <summary>jsonb array of change areas.</summary>
    public string Areas { get; private set; } = "[]";

    public string? Reason { get; private set; }

    public string? ImpactOfNonResponse { get; private set; }

    public string? ProposedNameAr { get; private set; }

    public string? ProposedNameEn { get; private set; }

    public string? ProposedDescription { get; private set; }

    public string? ProposedScope { get; private set; }

    public DateOnly? ProposedStart { get; private set; }

    public DateOnly? ProposedEnd { get; private set; }

    public decimal? ProposedBudget { get; private set; }

    public bool CostAffected { get; private set; }

    public Guid? NewPmEmployeeId { get; private set; }

    public Guid? NewOwnerEmployeeId { get; private set; }

    public Guid? MergeIntoProjectId { get; private set; }

    public DateOnly? RequestedDate { get; private set; }

    public string Status { get; private set; } = ChangeStatuses.Draft;

    public Guid? RequestedByEmployeeId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public DateTimeOffset? ImplementedAt { get; private set; }

    /// <summary>jsonb log of the applied changes: [{field, old, new}].</summary>
    public string Applied { get; private set; } = "[]";

    public string? CancelReason { get; private set; }

    public static ChangeRequest Create(Guid projectId, string number, Guid? requestedBy, ChangeRequestValues values, string areasJson)
    {
        var cr = new ChangeRequest { ProjectId = projectId, Number = number, RequestedByEmployeeId = requestedBy };
        cr.Edit(values, areasJson);
        return cr;
    }

    public void Edit(ChangeRequestValues v, string areasJson)
    {
        ArgumentNullException.ThrowIfNull(v);
        Type = v.Type;
        Areas = areasJson;
        Reason = Clean(v.Reason);
        ImpactOfNonResponse = Clean(v.ImpactOfNonResponse);
        ProposedNameAr = Clean(v.ProposedNameAr);
        ProposedNameEn = Clean(v.ProposedNameEn);
        ProposedDescription = Clean(v.ProposedDescription);
        ProposedScope = Clean(v.ProposedScope);
        ProposedStart = v.ProposedStart;
        ProposedEnd = v.ProposedEnd;
        ProposedBudget = v.ProposedBudget;
        CostAffected = v.CostAffected;
        NewPmEmployeeId = v.NewPmEmployeeId;
        NewOwnerEmployeeId = v.NewOwnerEmployeeId;
        MergeIntoProjectId = v.MergeIntoProjectId;
        RequestedDate = v.RequestedDate;
    }

    public Result Move(string action, DateTimeOffset at, string? reason = null)
    {
        var next = ChangeLifecycle.Next(Status, action);
        if (next.IsFailure)
        {
            return next.Error;
        }

        Status = next.Value;
        switch (action)
        {
            case ChangeActions.Submit:
                SubmittedAt = at;
                break;
            case ChangeActions.Approve or ChangeActions.Reject:
                DecidedAt = at;
                break;
            case ChangeActions.Cancel:
                CancelReason = Clean(reason);
                break;
        }

        return Result.Success();
    }

    public ChangeRequestValues ToValues() =>
        new(Type, System.Text.Json.JsonSerializer.Deserialize<List<string>>(Areas) ?? [], Reason, ImpactOfNonResponse, ProposedNameAr, ProposedNameEn, ProposedDescription,
            ProposedScope, ProposedStart, ProposedEnd, ProposedBudget, CostAffected, NewPmEmployeeId, NewOwnerEmployeeId, MergeIntoProjectId, RequestedDate);

    public void MarkApplied(string log, DateTimeOffset at)
    {
        Applied = log;
        ImplementedAt = at;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
