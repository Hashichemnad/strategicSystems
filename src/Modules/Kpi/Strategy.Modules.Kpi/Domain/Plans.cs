using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Kpi.Domain;

/// <summary>One annual plan per KPI per measuring year (SPEC §2.3 B).</summary>
[Audited(EntityType)]
public sealed class AnnualPlan : AuditableEntity
{
    public const string EntityType = "kpi.plan";

    private AnnualPlan()
    {
    }

    public Guid CardId { get; private set; }

    public int Year { get; private set; }

    public string MeasuringPeriod { get; private set; } = MeasuringPeriods.Quarterly;

    public string UnitCode { get; private set; } = "number";

    public string Direction { get; private set; } = Directions.Increase;

    public bool IsAccumulated { get; private set; }

    public string TargetType { get; private set; } = TargetTypes.SingleValue;

    public decimal? Target { get; private set; }

    public decimal? RangeMin { get; private set; }

    public decimal? RangeMax { get; private set; }

    public Guid? ResponsibleEmployeeId { get; private set; }

    public Guid? ResponsibleUnitId { get; private set; }

    public string Status { get; private set; } = PlanStatuses.Draft;

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    [NotAudited]
    public decimal? AchievedTotal { get; private set; }

    [NotAudited]
    public decimal? ActualPerformance { get; private set; }

    [NotAudited]
    public decimal? CappedPerformance { get; private set; }

    [NotAudited]
    public string Rag { get; private set; } = RagCodes.None;

    [NotAudited]
    public int ReportedCount { get; private set; }

    [NotAudited]
    public int ApprovedCount { get; private set; }

    [NotAudited]
    public int NaCount { get; private set; }

    [NotAudited]
    public decimal? LastAchieved { get; private set; }

    public static AnnualPlan Create(Guid cardId, int year, string measuringPeriod, string unitCode, string direction, bool isAccumulated, Guid? employeeId, Guid? unitId) => new()
    {
        CardId = cardId,
        Year = year,
        MeasuringPeriod = measuringPeriod,
        UnitCode = unitCode,
        Direction = direction,
        IsAccumulated = isAccumulated,
        ResponsibleEmployeeId = employeeId,
        ResponsibleUnitId = unitId,
    };

    public void SetTarget(string targetType, decimal? target, decimal? min, decimal? max)
    {
        TargetType = targetType;
        Target = targetType == TargetTypes.SingleValue ? target : null;
        RangeMin = targetType == TargetTypes.Range ? min : null;
        RangeMax = targetType == TargetTypes.Range ? max : null;
    }

    public void Edit(string unitCode, string direction, bool isAccumulated, Guid? employeeId)
    {
        UnitCode = unitCode;
        Direction = direction;
        IsAccumulated = isAccumulated;
        ResponsibleEmployeeId = employeeId;
    }

    public void SetOwnership(Guid? employeeId, Guid? unitId)
    {
        ResponsibleEmployeeId = employeeId;
        ResponsibleUnitId = unitId;
    }

    public void Confirm(DateTimeOffset at, Guid? by)
    {
        Status = PlanStatuses.Confirmed;
        ConfirmedAt = at;
        ConfirmedBy = by;
    }

    public void SetComputed(AggregateValue aggregate, Performance performance, string rag, int reported, int approved, int na)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        AchievedTotal = aggregate.Achieved;
        LastAchieved = aggregate.Last;
        ActualPerformance = performance.Actual;
        CappedPerformance = performance.Capped;
        Rag = rag;
        ReportedCount = reported;
        ApprovedCount = approved;
        NaCount = na;
    }
}

/// <summary>The submission window of a period of a measuring year (SPEC K2, ADR 0060).</summary>
[Audited(EntityType)]
public sealed class PeriodWindow : AuditableEntity
{
    public const string EntityType = "kpi.period-window";

    private PeriodWindow()
    {
    }

    public int Year { get; private set; }

    public string PeriodCode { get; private set; } = PeriodCodes.Q1;

    public DateOnly OpensOn { get; private set; }

    public DateOnly DueOn { get; private set; }

    public bool Locked { get; private set; }

    public static PeriodWindow Create(int year, string periodCode, DateOnly opensOn, DateOnly dueOn) =>
        new() { Year = year, PeriodCode = periodCode, OpensOn = opensOn, DueOn = dueOn };

    public void SetDates(DateOnly opensOn, DateOnly dueOn)
    {
        OpensOn = opensOn;
        DueOn = dueOn;
    }

    public void Lock() => Locked = true;

    public void Unlock() => Locked = false;
}

public static class ResultStatuses
{
    public const string NotOpen = "not_open";
    public const string Open = "open";
    public const string PendingApproval = "pending_approval";
    public const string Returned = "returned";
    public const string Approved = "approved";
    public const string Locked = "locked";

    public static readonly IReadOnlyList<string> All = [NotOpen, Open, PendingApproval, Returned, Approved, Locked];

    /// <summary>"Open for Update" (SPEC §2.3 C): the owner can enter and submit.</summary>
    public static bool IsOpenForUpdate(string status) => status is Open or Returned;
}

public static class ResultActions
{
    public const string OpenWindow = "open-window";
    public const string Submit = "submit";
    public const string Recall = "recall";
    public const string Approved = "approved";
    public const string Returned = "returned";
    public const string Reopen = "reopen";
    public const string Lock = "lock";

    public static readonly IReadOnlyList<string> All = [OpenWindow, Submit, Recall, Approved, Returned, Reopen, Lock];
}

/// <summary>The result lifecycle (docs/design/kpi.md §3, ADR 0061) as an explicit transition table.</summary>
public static class ResultLifecycle
{
    public static readonly IReadOnlyList<(string From, string Action, string To)> Transitions =
    [
        (ResultStatuses.NotOpen, ResultActions.OpenWindow, ResultStatuses.Open),
        (ResultStatuses.Open, ResultActions.Submit, ResultStatuses.PendingApproval),
        (ResultStatuses.Returned, ResultActions.Submit, ResultStatuses.PendingApproval),
        (ResultStatuses.PendingApproval, ResultActions.Recall, ResultStatuses.Open),
        (ResultStatuses.PendingApproval, ResultActions.Approved, ResultStatuses.Approved),
        (ResultStatuses.PendingApproval, ResultActions.Returned, ResultStatuses.Returned),
        (ResultStatuses.Approved, ResultActions.Reopen, ResultStatuses.Open),
        (ResultStatuses.NotOpen, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Open, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Returned, ResultActions.Lock, ResultStatuses.Locked),
        (ResultStatuses.Locked, ResultActions.Reopen, ResultStatuses.Open),
    ];

    public static string? Next(string from, string action) =>
        Transitions.FirstOrDefault(t => t.From == from && t.Action == action) is { To: { } to } ? to : null;

    public static IReadOnlyList<string> ActionsFrom(string from) => Transitions.Where(t => t.From == from).Select(t => t.Action).ToList();
}

/// <summary>A submitted value (or Not Applicable) with its narrative.</summary>
public sealed record ResultEntry(
    decimal? Achieve, IReadOnlyDictionary<string, decimal?>? ElementValues, decimal? ScaleValue, string? Causality, string? CorrectiveActions, string? Notes,
    bool NotApplicable, string? NaReason);

/// <summary>One period result of an annual plan (SPEC §2.3 C).</summary>
[Audited(EntityType)]
public sealed class KpiResult : AuditableEntity
{
    public const string EntityType = "kpi.result";
    public const int TextMaxLength = 4000;

    private KpiResult()
    {
    }

    public Guid PlanId { get; private set; }

    public Guid CardId { get; private set; }

    public int Year { get; private set; }

    public string PeriodCode { get; private set; } = PeriodCodes.Q1;

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public int Sort { get; private set; }

    public string TargetType { get; private set; } = TargetTypes.SingleValue;

    public decimal? Target { get; private set; }

    public decimal? RangeMin { get; private set; }

    public decimal? RangeMax { get; private set; }

    public decimal? Achieve { get; private set; }

    public string ElementValues { get; private set; } = "{}";

    public decimal? ScaleValue { get; private set; }

    public decimal? ActualPerformance { get; private set; }

    public decimal? CappedPerformance { get; private set; }

    public bool? Achieved { get; private set; }

    public string? Causality { get; private set; }

    public string? CorrectiveActions { get; private set; }

    public string? Notes { get; private set; }

    public bool NotApplicable { get; private set; }

    public string? NaReason { get; private set; }

    public string Status { get; private set; } = ResultStatuses.NotOpen;

    public DateOnly? OpensOn { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedBy { get; private set; }

    public bool Late { get; private set; }

    public int DaysLate { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public int ReturnCount { get; private set; }

    public int ReopenedCount { get; private set; }

    public string? StatusReason { get; private set; }

    public string Checklist { get; private set; } = "{}";

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? OwnerUnitId { get; private set; }

    /// <summary>The draft was written by an MCP / AI client and not yet reviewed by the owner in the UI.</summary>
    public bool DraftFromMcp { get; private set; }

    [NotAudited]
    public DateTimeOffset? OpenedNotifiedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? RemindedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    [NotAudited]
    public DateTimeOffset? EscalatedAt { get; private set; }

    public static KpiResult Create(AnnualPlan plan, PeriodSpec period, PeriodTarget target, Guid? ownerEmployeeId, Guid? ownerUnitId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(target);
        var result = new KpiResult
        {
            PlanId = plan.Id,
            CardId = plan.CardId,
            Year = plan.Year,
            PeriodCode = period.Code,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            Sort = period.Sort,
            OwnerEmployeeId = ownerEmployeeId,
            OwnerUnitId = ownerUnitId,
        };
        result.SetTarget(plan.TargetType, target.Target, target.RangeMin, target.RangeMax);
        return result;
    }

    public IReadOnlyDictionary<string, decimal?> Elements => KpiCard.Read<Dictionary<string, decimal?>>(ElementValues) ?? [];

    public IReadOnlyDictionary<string, bool> ChecklistAnswers => KpiCard.Read<Dictionary<string, bool>>(Checklist) ?? [];

    public bool IsOpenForUpdate => ResultStatuses.IsOpenForUpdate(Status);

    public void SetTarget(string targetType, decimal? target, decimal? min, decimal? max)
    {
        TargetType = targetType;
        Target = targetType == TargetTypes.SingleValue ? target : null;
        RangeMin = targetType == TargetTypes.Range ? min : null;
        RangeMax = targetType == TargetTypes.Range ? max : null;
    }

    public void SetWindow(DateOnly opensOn, DateOnly dueOn, DateTimeOffset? dueAt)
    {
        if (OpensOn != opensOn || DueOn != dueOn)
        {
            RemindedAt = null;
            OverdueNotifiedAt = null;
            EscalatedAt = null;
        }

        OpensOn = opensOn;
        DueOn = dueOn;
        DueAt = dueAt;
    }

    public void SetOwnership(Guid? employeeId, Guid? unitId)
    {
        OwnerEmployeeId = employeeId;
        OwnerUnitId = unitId;
    }

    /// <summary>Saves the owner's entry without submitting (draft). Achieve is the computed value for composite / qualitative KPIs.</summary>
    public void SaveEntry(ResultEntry entry, decimal? achieve, Performance performance, bool fromMcp = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        NotApplicable = entry.NotApplicable;
        NaReason = entry.NotApplicable ? StrategyElement.Clean(entry.NaReason) : null;
        Achieve = entry.NotApplicable ? null : achieve;
        ElementValues = KpiCard.Write(entry.NotApplicable ? new Dictionary<string, decimal?>() : entry.ElementValues ?? new Dictionary<string, decimal?>());
        ScaleValue = entry.NotApplicable ? null : entry.ScaleValue;
        Causality = StrategyElement.Clean(entry.Causality);
        CorrectiveActions = StrategyElement.Clean(entry.CorrectiveActions);
        Notes = StrategyElement.Clean(entry.Notes);
        Recalculate(performance);
        DraftFromMcp = fromMcp;
    }

    public void Recalculate(Performance performance)
    {
        ActualPerformance = NotApplicable ? null : performance.Actual;
        CappedPerformance = NotApplicable ? null : performance.Capped;
        Achieved = NotApplicable || performance.Capped is null ? null : performance.Capped >= 1m;
    }

    public void SetChecklist(IReadOnlyDictionary<string, bool> answers) => Checklist = KpiCard.Write(answers);

    public Result Apply(string action, DateTimeOffset at, Guid? by = null, string? reason = null)
    {
        var next = ResultLifecycle.Next(Status, action);
        if (next is null)
        {
            return Error.Rule("kpi.invalidTransition", "kpi.errors.invalidTransition",
                new Dictionary<string, object?> { ["status"] = Status, ["action"] = action });
        }

        switch (action)
        {
            case ResultActions.Submit:
                SubmittedAt = at;
                SubmittedBy = by;
                DraftFromMcp = false;
                break;
            case ResultActions.Approved:
                ApprovedAt = at;
                ApprovedBy = by;
                break;
            case ResultActions.Returned:
                ReturnCount++;
                break;
            case ResultActions.Reopen:
                ReopenedCount++;
                ApprovedAt = null;
                ApprovedBy = null;
                break;
        }

        if (reason is not null)
        {
            StatusReason = reason.Trim();
        }

        Status = next;
        return Result.Success();
    }

    public void MarkLate(bool late, int daysLate)
    {
        Late = late;
        DaysLate = daysLate;
    }

    public void MarkOpenedNotified(DateTimeOffset at) => OpenedNotifiedAt = at;

    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;

    public void MarkOverdueNotified(DateTimeOffset at) => OverdueNotifiedAt = at;

    public void MarkEscalated(DateTimeOffset at) => EscalatedAt = at;
}
