using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Portfolio.Domain;

public static class GroupKinds
{
    public const string Portfolio = "portfolio";
    public const string Programme = "programme";

    public static readonly IReadOnlyList<string> All = [Portfolio, Programme];
}

public sealed record PortfolioGroupValues(string? Kind, string? Code, string? NameAr, string? NameEn, string? Description, Guid? ParentId, Guid? OwnerEmployeeId);

/// <summary>Portfolio or programme (SPEC §1.12: Portfolio → Programme → Project / Initiative, ADR 0095).</summary>
[Audited(EntityType)]
public sealed class PortfolioGroup : AuditableEntity
{
    public const string EntityType = "portfolio.group";

    private PortfolioGroup()
    {
    }

    public string Kind { get; private set; } = GroupKinds.Portfolio;

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    /// <summary>A programme's portfolio.</summary>
    public Guid? ParentId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string Status { get; private set; } = ItemStatuses.Active;

    public static PortfolioGroup Create(PortfolioGroupValues values)
    {
        var item = new PortfolioGroup();
        item.Edit(values);
        return item;
    }

    public void Edit(PortfolioGroupValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Kind = v.Kind == GroupKinds.Programme ? GroupKinds.Programme : GroupKinds.Portfolio;
        Code = v.Code?.Trim() ?? string.Empty;
        NameAr = AgileText.Clean(v.NameAr);
        NameEn = AgileText.Clean(v.NameEn);
        Description = AgileText.Clean(v.Description);
        ParentId = Kind == GroupKinds.Programme ? v.ParentId : null;
        OwnerEmployeeId = v.OwnerEmployeeId;
    }

    public void SetActive(bool active) => Status = active ? ItemStatuses.Active : ItemStatuses.Inactive;
}

public static class RaidTypes
{
    public const string Issue = "issue";
    public const string Assumption = "assumption";
    public const string Dependency = "dependency";
    public const string Decision = "decision";

    public static readonly IReadOnlyList<string> All = [Issue, Assumption, Dependency, Decision];

    public static string Prefix(string type) => type switch
    {
        Issue => "I",
        Assumption => "A",
        Dependency => "D",
        _ => "C",
    };
}

public static class RaidStatuses
{
    public const string Proposed = "proposed";
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Proposed, Open, Resolved, Closed];
}

public static class RaidSources
{
    public const string Manual = "manual";
    public const string Sprint = "sprint";
    public const string Task = "task";
}

public sealed record RaidValues(
    string? Type,
    string? Title,
    string? Description,
    Guid? OwnerEmployeeId,
    string? Priority,
    DateOnly? DueDate,
    string? Status,
    string? Resolution,
    Guid? MilestoneId,
    Guid? DependsOnProjectId,
    Guid? DependsOnMilestoneId,
    DateOnly? DecidedOn,
    string? DecidedBy);

/// <summary>RAID log entry: issue, assumption, dependency or decision (SPEC §1.12, ADR 0095). Risks are listed from the risks table.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class RaidItem : AuditableEntity
{
    public const string EntityType = "portfolio.raid";

    private RaidItem()
    {
    }

    public Guid ProjectId { get; private set; }

    public string Type { get; private set; } = RaidTypes.Issue;

    public string Code { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string? Description { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string Priority { get; private set; } = "medium";

    public DateOnly? DueDate { get; private set; }

    public string Status { get; private set; } = RaidStatuses.Open;

    public string? Resolution { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public Guid? DependsOnProjectId { get; private set; }

    public Guid? DependsOnMilestoneId { get; private set; }

    public DateOnly? DecidedOn { get; private set; }

    public string? DecidedBy { get; private set; }

    public string Source { get; private set; } = RaidSources.Manual;

    public Guid? SourceId { get; private set; }

    public static RaidItem Create(Guid projectId, string code, RaidValues values, bool proposed, string source = RaidSources.Manual, Guid? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var item = new RaidItem { ProjectId = projectId, Code = code, Type = values.Type ?? RaidTypes.Issue, Source = source, SourceId = sourceId };
        item.Edit(values);
        item.Status = proposed ? RaidStatuses.Proposed : RaidStatuses.Open;
        return item;
    }

    public void Edit(RaidValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Title = AgileText.Clean(v.Title);
        Description = AgileText.Clean(v.Description);
        OwnerEmployeeId = v.OwnerEmployeeId;
        Priority = v.Priority is "high" or "medium" or "low" ? v.Priority : "medium";
        DueDate = v.DueDate;
        Resolution = AgileText.Clean(v.Resolution);
        MilestoneId = v.MilestoneId;
        DependsOnProjectId = Type == RaidTypes.Dependency ? v.DependsOnProjectId : null;
        DependsOnMilestoneId = Type == RaidTypes.Dependency ? v.DependsOnMilestoneId : null;
        DecidedOn = Type == RaidTypes.Decision ? v.DecidedOn : null;
        DecidedBy = Type == RaidTypes.Decision ? AgileText.Clean(v.DecidedBy) : null;
        if (v.Status is RaidStatuses.Open or RaidStatuses.Resolved or RaidStatuses.Closed && Status != RaidStatuses.Proposed)
        {
            Status = v.Status;
        }
    }

    public void Accept() => Status = Status == RaidStatuses.Proposed ? RaidStatuses.Open : Status;
}

public static class RaciRoles
{
    public const string Responsible = "R";
    public const string Accountable = "A";
    public const string Consulted = "C";
    public const string Informed = "I";

    public static readonly IReadOnlyList<string> All = [Responsible, Accountable, Consulted, Informed];
}

public sealed record RaciValues(string? Area, Guid? MilestoneId, Guid EmployeeId, string? Role);

/// <summary>RACI on the project team: one row per area × person (SPEC §1.12).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class RaciEntry : ProjectChild
{
    public const string EntityType = "portfolio.raci";

    private RaciEntry()
    {
    }

    public string Area { get; private set; } = string.Empty;

    public Guid? MilestoneId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public string Role { get; private set; } = RaciRoles.Responsible;

    public static RaciEntry Create(Guid projectId, RaciValues values)
    {
        var item = new RaciEntry { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(RaciValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Area = Clean(v.Area) ?? string.Empty;
        MilestoneId = v.MilestoneId;
        EmployeeId = v.EmployeeId;
        Role = v.Role ?? RaciRoles.Responsible;
    }
}

public static class CommunicationFrequencies
{
    public const string Weekly = "weekly";
    public const string Biweekly = "biweekly";
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Once = "once";

    public static readonly IReadOnlyList<string> All = [Weekly, Biweekly, Monthly, Quarterly, Once];

    /// <summary>The next due date after a communication was done (null for once).</summary>
    public static DateOnly? Next(string frequency, DateOnly from) => frequency switch
    {
        Weekly => from.AddDays(7),
        Biweekly => from.AddDays(14),
        Monthly => from.AddMonths(1),
        Quarterly => from.AddMonths(3),
        _ => null,
    };

    /// <summary>Maps a stakeholder's free-text frequency to a code (default monthly).</summary>
    public static string FromText(string? text)
    {
        var t = text?.Trim().ToUpperInvariant() ?? string.Empty;
        return t.Contains("BI", StringComparison.Ordinal) || t.Contains("كل أسبوعين", StringComparison.Ordinal) ? Biweekly
            : t.Contains("WEEK", StringComparison.Ordinal) || t.Contains("أسبوع", StringComparison.Ordinal) ? Weekly
            : t.Contains("QUARTER", StringComparison.Ordinal) || t.Contains("ربع", StringComparison.Ordinal) ? Quarterly
            : t.Contains("ONCE", StringComparison.Ordinal) || t.Contains("مرة", StringComparison.Ordinal) ? Once
            : Monthly;
    }
}

public sealed record CommunicationValues(
    Guid? StakeholderId, string? Audience, string? Message, string? Method, string? Frequency, Guid? OwnerEmployeeId, DateOnly? NextDue);

/// <summary>Communication plan item derived from stakeholders (method, frequency, owner) with reminders (SPEC §1.12).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class CommunicationItem : ProjectChild
{
    public const string EntityType = "portfolio.communication";

    private CommunicationItem()
    {
    }

    public Guid? StakeholderId { get; private set; }

    public string? Audience { get; private set; }

    public string? Message { get; private set; }

    public string? Method { get; private set; }

    public string Frequency { get; private set; } = CommunicationFrequencies.Monthly;

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly? NextDue { get; private set; }

    public DateOnly? LastDone { get; private set; }

    /// <summary>The due date a reminder was already sent for (once per due date).</summary>
    public DateOnly? RemindedFor { get; private set; }

    public static CommunicationItem Create(Guid projectId, CommunicationValues values)
    {
        var item = new CommunicationItem { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(CommunicationValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        StakeholderId = v.StakeholderId;
        Audience = Clean(v.Audience);
        Message = Clean(v.Message);
        Method = Clean(v.Method);
        Frequency = v.Frequency is { } f && CommunicationFrequencies.All.Contains(f) ? f : CommunicationFrequencies.Monthly;
        OwnerEmployeeId = v.OwnerEmployeeId;
        NextDue = v.NextDue;
    }

    public void MarkDone(DateOnly on)
    {
        LastDone = on;
        NextDue = CommunicationFrequencies.Next(Frequency, on);
    }

    public void MarkReminded(DateOnly due) => RemindedFor = due;
}

public static class BenefitStatuses
{
    public const string Planned = "planned";
    public const string Realised = "realised";
    public const string Partly = "partly";
    public const string NotRealised = "not-realised";

    public static readonly IReadOnlyList<string> Measured = [Realised, Partly, NotRealised];
}

public sealed record BenefitValues(
    string? TitleAr, string? TitleEn, string? Measure, decimal? BaselineValue, decimal? TargetValue, string? Unit, Guid? OwnerEmployeeId, DateOnly? MeasurementDate);

public sealed record BenefitMeasurement(decimal? ActualValue, string? Status, string? Note);

/// <summary>Expected benefit defined at request, measured at Impact (SPEC §1.12 benefits realisation).</summary>
[Audited(EntityType)]
public sealed class Benefit : AuditableEntity
{
    public const string EntityType = "portfolio.benefit";

    private Benefit()
    {
    }

    public Guid? RequestId { get; private set; }

    public Guid? ProjectId { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Measure { get; private set; }

    public decimal? BaselineValue { get; private set; }

    public decimal? TargetValue { get; private set; }

    public string? Unit { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly? MeasurementDate { get; private set; }

    public decimal? ActualValue { get; private set; }

    public DateTimeOffset? MeasuredAt { get; private set; }

    public Guid? MeasuredByUserId { get; private set; }

    public string Status { get; private set; } = BenefitStatuses.Planned;

    public string? Note { get; private set; }

    public string ItemStatus { get; private set; } = ItemStatuses.Active;

    public bool IsActive => ItemStatus == ItemStatuses.Active;

    public bool IsMeasured => BenefitStatuses.Measured.Contains(Status);

    public static Benefit ForRequest(Guid requestId, BenefitValues values)
    {
        var item = new Benefit { RequestId = requestId };
        item.Edit(values);
        return item;
    }

    public static Benefit ForProject(Guid projectId, BenefitValues values)
    {
        var item = new Benefit { ProjectId = projectId };
        item.Edit(values);
        return item;
    }

    public void Edit(BenefitValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = AgileText.Clean(v.TitleAr);
        TitleEn = AgileText.Clean(v.TitleEn);
        Measure = AgileText.Clean(v.Measure);
        BaselineValue = v.BaselineValue;
        TargetValue = v.TargetValue;
        Unit = AgileText.Clean(v.Unit);
        OwnerEmployeeId = v.OwnerEmployeeId;
        MeasurementDate = v.MeasurementDate;
    }

    public void AttachToProject(Guid projectId) => ProjectId = projectId;

    public void RecordMeasurement(BenefitMeasurement m, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(m);
        ActualValue = m.ActualValue;
        Status = m.Status is { } s && BenefitStatuses.Measured.Contains(s) ? s : Status;
        Note = AgileText.Clean(m.Note);
        MeasuredAt = at;
        MeasuredByUserId = by;
    }

    public void Deactivate() => ItemStatus = ItemStatuses.Inactive;
}

public static class PaymentStatuses
{
    public const string Planned = "planned";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Paid, Cancelled];
}

public sealed record PaymentValues(string? Title, DateOnly? DueDate, decimal Amount, Guid? MilestoneId, string? Status, DateOnly? PaidOn);

/// <summary>Payment milestone of a contract / PO (SPEC §1.12 procurement).</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class ContractPayment : AuditableEntity
{
    public const string EntityType = "portfolio.contract-payment";

    private ContractPayment()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid ContractId { get; private set; }

    public string? Title { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public decimal Amount { get; private set; }

    public Guid? MilestoneId { get; private set; }

    public string Status { get; private set; } = PaymentStatuses.Planned;

    public DateOnly? PaidOn { get; private set; }

    public static ContractPayment Create(Guid projectId, Guid contractId, PaymentValues values)
    {
        var item = new ContractPayment { ProjectId = projectId, ContractId = contractId };
        item.Edit(values);
        return item;
    }

    public void Edit(PaymentValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Title = AgileText.Clean(v.Title);
        DueDate = v.DueDate;
        Amount = v.Amount;
        MilestoneId = v.MilestoneId;
        Status = v.Status is { } s && PaymentStatuses.All.Contains(s) ? s : PaymentStatuses.Planned;
        PaidOn = Status == PaymentStatuses.Paid ? v.PaidOn : null;
    }
}

/// <summary>One item of a baseline snapshot (milestone or epic).</summary>
public sealed record BaselineItem(Guid Id, string Kind, string Code, string? TitleAr, string? TitleEn, DateOnly? Start, DateOnly? End, decimal Weight, decimal? Cost);

/// <summary>Plan baseline (SPEC §1.12, ADR 0094): v1 at planning approval, later versions only from an applied change request.</summary>
[Audited(EntityType)]
[AuditParent(Project.EntityType, nameof(ProjectId))]
public sealed class Baseline : AuditableEntity
{
    public const string EntityType = "portfolio.baseline";

    private Baseline()
    {
    }

    public Guid ProjectId { get; private set; }

    public int VersionNo { get; private set; }

    public DateTimeOffset TakenAt { get; private set; }

    public Guid? TakenByUserId { get; private set; }

    public string? Reason { get; private set; }

    public Guid? ChangeRequestId { get; private set; }

    public decimal? Budget { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    /// <summary>jsonb array of <see cref="BaselineItem"/>.</summary>
    public string Items { get; private set; } = "[]";

    public static Baseline Take(Guid projectId, int versionNo, DateTimeOffset at, Guid? by, string? reason, Guid? changeRequestId, decimal? budget, DateOnly? start,
        DateOnly? end, string items) =>
        new()
        {
            ProjectId = projectId,
            VersionNo = versionNo,
            TakenAt = at,
            TakenByUserId = by,
            Reason = reason,
            ChangeRequestId = changeRequestId,
            Budget = budget,
            PlannedStart = start,
            PlannedEnd = end,
            Items = items,
        };
}

public sealed record TemplateMilestone(string? TitleAr, string? TitleEn, decimal Weight, int OffsetDays, int DurationDays, bool IsHeader = false, int? ParentIndex = null);

public sealed record TemplatePlanningItem(string Category, string Description);

public sealed record TemplateRisk(string Title, string? CategoryCode, int Probability, int Impact);

public sealed record TemplateEpic(string? TitleAr, string? TitleEn, decimal Weight);

public sealed record ProjectTemplateContent(
    IReadOnlyList<TemplateMilestone>? Milestones = null,
    IReadOnlyList<TemplatePlanningItem>? PlanningItems = null,
    IReadOnlyList<TemplateRisk>? Risks = null,
    IReadOnlyList<TemplateEpic>? Epics = null);

/// <summary>Project template (SPEC §1.12): milestones, planning items, risks, epics to start a project quickly.</summary>
[Audited(EntityType)]
public sealed class ProjectTemplate : AuditableEntity
{
    public const string EntityType = "portfolio.template";

    private ProjectTemplate()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string Methodology { get; private set; } = Methodologies.Waterfall;

    public string Content { get; private set; } = "{}";

    public string Status { get; private set; } = ItemStatuses.Active;

    public static ProjectTemplate Create(string code, string? nameAr, string? nameEn, string methodology, string content)
    {
        var item = new ProjectTemplate { Code = code };
        item.Edit(nameAr, nameEn, methodology, content);
        return item;
    }

    public void Edit(string? nameAr, string? nameEn, string methodology, string content)
    {
        NameAr = AgileText.Clean(nameAr);
        NameEn = AgileText.Clean(nameEn);
        Methodology = methodology;
        Content = content;
    }

    public void SetActive(bool active) => Status = active ? ItemStatuses.Active : ItemStatuses.Inactive;
}

public static class Rag
{
    public const string Green = "green";
    public const string Amber = "amber";
    public const string Red = "red";
    public const string Grey = "grey";

    public static readonly IReadOnlyList<string> Colours = [Green, Amber, Red];

    public static int Severity(string rag) => rag switch
    {
        Red => 3,
        Amber => 2,
        Green => 1,
        _ => 0,
    };
}

public static class HealthDimensions
{
    public const string Schedule = "schedule";
    public const string Cost = "cost";
    public const string Scope = "scope";
    public const string Risk = "risk";
    public const string Overall = "overall";

    public static readonly IReadOnlyList<string> All = [Schedule, Cost, Scope, Risk, Overall];
}

/// <summary>Configurable RAG thresholds (ADR 0093).</summary>
public sealed record HealthThresholds(
    decimal ScheduleAmberGap = 0.05m,
    decimal ScheduleRedGap = 0.15m,
    int PastDueRedDays = 30,
    decimal CostAmberVariance = 0.05m,
    decimal CostRedVariance = 0.15m,
    int ScopeWindowDays = 180,
    int ScopeRedChanges = 2)
{
    public static readonly HealthThresholds Default = new();
}

/// <summary>Facts the health of one project is calculated from (loaded in bulk).</summary>
public sealed record HealthFacts(
    bool HasPlan,
    decimal Progress,
    decimal Planned,
    int MaxDaysPastDue,
    int OverdueTasks,
    decimal? Budget,
    decimal? ActualCost,
    int RecentScopeChanges,
    bool ScopeChangeUnderReview,
    string? HighestRiskBand,
    bool HasRisks);

public sealed record HealthOverride(string Value, string Justification, Guid? ByUserId, DateTimeOffset At);

public sealed record HealthValue(string Auto, string Effective, HealthOverride? Override);

public sealed record ProjectHealth(HealthValue Schedule, HealthValue Cost, HealthValue Scope, HealthValue Risk, HealthValue Overall);

/// <summary>Project health RAG (SPEC §1.12, ADR 0093). Pure.</summary>
public static class HealthCalculator
{
    public static string Schedule(HealthFacts f, HealthThresholds t)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(t);
        if (!f.HasPlan)
        {
            return Rag.Grey;
        }

        var gap = f.Planned - f.Progress;
        if (gap > t.ScheduleRedGap || f.MaxDaysPastDue > t.PastDueRedDays)
        {
            return Rag.Red;
        }

        return gap > t.ScheduleAmberGap || f.MaxDaysPastDue > 0 || f.OverdueTasks > 0 ? Rag.Amber : Rag.Green;
    }

    /// <summary>(actual − budget × progress) ÷ budget; null without budget or actual cost.</summary>
    public static decimal? CostVariance(HealthFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return f.Budget is not > 0 || f.ActualCost is not > 0 ? null : decimal.Round((f.ActualCost.Value - (f.Budget.Value * f.Progress)) / f.Budget.Value, 4);
    }

    public static string Cost(HealthFacts f, HealthThresholds t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return CostVariance(f) switch
        {
            null => Rag.Grey,
            var v when v > t.CostRedVariance => Rag.Red,
            var v when v > t.CostAmberVariance => Rag.Amber,
            _ => Rag.Green,
        };
    }

    public static string Scope(HealthFacts f, HealthThresholds t)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(t);
        return f.RecentScopeChanges >= t.ScopeRedChanges ? Rag.Red : f.RecentScopeChanges > 0 || f.ScopeChangeUnderReview ? Rag.Amber : Rag.Green;
    }

    public static string Risk(HealthFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return !f.HasRisks ? Rag.Grey : f.HighestRiskBand switch
        {
            RiskBands.Critical => Rag.Red,
            RiskBands.High => Rag.Amber,
            _ => Rag.Green,
        };
    }

    /// <summary>The worst of the (non-grey) colours; grey when none has data.</summary>
    public static string Worst(IEnumerable<string> colours) =>
        colours.Where(c => c != Rag.Grey).OrderByDescending(Rag.Severity).FirstOrDefault() ?? Rag.Grey;

    public static ProjectHealth Calculate(HealthFacts f, HealthThresholds t, IReadOnlyDictionary<string, HealthOverride>? overrides = null)
    {
        overrides ??= new Dictionary<string, HealthOverride>();
        HealthValue V(string dimension, string auto) =>
            new(auto, overrides.TryGetValue(dimension, out var o) ? o.Value : auto, overrides.GetValueOrDefault(dimension));
        var schedule = V(HealthDimensions.Schedule, Schedule(f, t));
        var cost = V(HealthDimensions.Cost, Cost(f, t));
        var scope = V(HealthDimensions.Scope, Scope(f, t));
        var risk = V(HealthDimensions.Risk, Risk(f));
        var overall = V(HealthDimensions.Overall, Worst([schedule.Effective, cost.Effective, scope.Effective, risk.Effective]));
        return new ProjectHealth(schedule, cost, scope, risk, overall);
    }
}

public sealed record EarnedValue(
    decimal Bac, decimal PlannedRatio, decimal ProgressRatio, decimal Pv, decimal Ev, decimal Ac, decimal Sv, decimal Cv, decimal? Spi, decimal? Cpi, decimal? Eac,
    decimal? Etc, decimal? Vac, decimal? Tcpi);

/// <summary>Earned value (SPEC §1.12, ADR 0094). Money rounded to 2 decimals, indices to 4.</summary>
public static class EarnedValueCalculator
{
    public static EarnedValue Calculate(decimal bac, decimal plannedRatio, decimal progressRatio, decimal actualCost)
    {
        static decimal M(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
        static decimal? Div(decimal a, decimal b) => b == 0 ? null : a / b;
        var pv = bac * plannedRatio;
        var ev = bac * progressRatio;
        var ac = actualCost;
        var spi = Div(ev, pv);
        var cpi = Div(ev, ac);
        decimal? eac = cpi is { } c && c != 0 ? bac / c : null;
        return new EarnedValue(
            M(bac), plannedRatio, progressRatio, M(pv), M(ev), M(ac), M(ev - pv), M(ev - ac), spi is { } s ? decimal.Round(s, 4) : null,
            cpi is { } ci ? decimal.Round(ci, 4) : null, eac is { } e ? M(e) : null, eac is { } e2 ? M(e2 - ac) : null, eac is { } e3 ? M(bac - e3) : null,
            Div(bac - ev, bac - ac) is { } t ? decimal.Round(t, 4) : null);
    }
}

public sealed record VarianceRow(
    Guid Id, string Kind, string Code, string? TitleAr, string? TitleEn, string Change, DateOnly? BaselineStart, DateOnly? BaselineEnd, DateOnly? CurrentStart,
    DateOnly? CurrentEnd, DateOnly? ActualEnd, int? StartVarianceDays, int? EndVarianceDays, int? ActualEndVarianceDays, decimal BaselineWeight, decimal CurrentWeight,
    decimal? BaselineCost, decimal? CurrentCost);

public sealed record CurrentPlanItem(BaselineItem Item, DateOnly? ActualEnd);

/// <summary>Baseline vs current vs actual (ADR 0094). Pure.</summary>
public static class BaselineVariance
{
    public const string Unchanged = "unchanged";
    public const string Changed = "changed";
    public const string Added = "added";
    public const string Removed = "removed";

    private static int? Days(DateOnly? a, DateOnly? b) => a is { } x && b is { } y ? x.DayNumber - y.DayNumber : null;

    public static IReadOnlyList<VarianceRow> Compare(IReadOnlyCollection<BaselineItem> baseline, IReadOnlyCollection<CurrentPlanItem> current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var rows = new List<VarianceRow>();
        foreach (var c in current)
        {
            var b = baseline.FirstOrDefault(x => x.Id == c.Item.Id);
            var i = c.Item;
            if (b is null)
            {
                rows.Add(new VarianceRow(i.Id, i.Kind, i.Code, i.TitleAr, i.TitleEn, Added, null, null, i.Start, i.End, c.ActualEnd, null, null, null, 0, i.Weight, null,
                    i.Cost));
                continue;
            }

            var changed = b.Start != i.Start || b.End != i.End || b.Weight != i.Weight || b.Cost != i.Cost;
            rows.Add(new VarianceRow(i.Id, i.Kind, i.Code, i.TitleAr, i.TitleEn, changed ? Changed : Unchanged, b.Start, b.End, i.Start, i.End, c.ActualEnd,
                Days(i.Start, b.Start), Days(i.End, b.End), Days(c.ActualEnd, b.End), b.Weight, i.Weight, b.Cost, i.Cost));
        }

        foreach (var b in baseline.Where(b => current.All(c => c.Item.Id != b.Id)))
        {
            rows.Add(new VarianceRow(b.Id, b.Kind, b.Code, b.TitleAr, b.TitleEn, Removed, b.Start, b.End, null, null, null, null, null, null, b.Weight, 0, b.Cost, null));
        }

        return rows;
    }

    /// <summary>Linear planned-to-date of a baseline (same rule as <see cref="ProgressCalculator.PlannedToDate"/>).</summary>
    public static decimal PlannedToDate(IEnumerable<BaselineItem> items, DateOnly date) =>
        ProgressCalculator.PlannedToDate(items.Where(i => i.Kind == "milestone").Select(i => new MilestoneProgressInput(i.Weight, 0, i.Start, i.End)), date);
}

/// <summary>A gate checklist item ticked on a project.</summary>
public sealed record ChecklistTick(bool Checked, Guid? ByUserId, DateTimeOffset At, string? Note);

public sealed record ChecklistItemDefinition(string Code, string? LabelAr, string? LabelEn, bool Required = true);

public static class Gates
{
    public const string Charter = "charter";
    public const string Planning = "planning";
    public const string Closure = "closure";

    public static readonly IReadOnlyList<string> All = [Charter, Planning, Closure];
}

/// <summary>Configurable stage-gate checklists (SPEC §1.12), in addition to the hard minimums. Seeded items are optional; an organisation marks the ones it requires.</summary>
public sealed record GateChecklists(
    IReadOnlyList<ChecklistItemDefinition> Charter, IReadOnlyList<ChecklistItemDefinition> Planning, IReadOnlyList<ChecklistItemDefinition> Closure)
{
    public static readonly GateChecklists Default = new(
        [
            new("business-case-reviewed", "تمت مراجعة دراسة الجدوى", "Business case reviewed", Required: false),
            new("sponsor-agreed", "وافق راعي المشروع على الميثاق", "Sponsor agreed the charter", Required: false),
        ],
        [
            new("schedule-reviewed", "تمت مراجعة الجدول الزمني مع الفريق", "Schedule reviewed with the team", Required: false),
            new("resources-confirmed", "تم تأكيد توفر الموارد", "Resources confirmed", Required: false),
        ],
        [
            new("deliverables-handed-over", "تم تسليم المخرجات للجهة المستفيدة", "Deliverables handed over", Required: false),
            new("documents-archived", "تمت أرشفة وثائق المشروع", "Project documents archived", Required: false),
        ]);

    public IReadOnlyList<ChecklistItemDefinition> For(string gate) => gate switch
    {
        Gates.Charter => Charter ?? [],
        Gates.Planning => Planning ?? [],
        Gates.Closure => Closure ?? [],
        _ => [],
    };
}
