using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.AuditManagement.Domain;

public static class EngagementStages
{
    public const string Planned = "planned";
    public const string Planning = "planning";
    public const string Fieldwork = "fieldwork";
    public const string Reporting = "reporting";
    public const string FollowUp = "follow_up";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Planned, Planning, Fieldwork, Reporting, FollowUp, Closed, Cancelled];

    /// <summary>Stepper stages (SPEC §10.4): Planning → Fieldwork → Reporting → Follow-up.</summary>
    public static readonly IReadOnlyList<string> Stepper = [Planning, Fieldwork, Reporting, FollowUp];
}

public static class EngagementActions
{
    public const string Start = "start";
    public const string ToFieldwork = "to-fieldwork";
    public const string ToReporting = "to-reporting";
    public const string ToFollowUp = "to-follow-up";
    public const string Close = "close";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [Start, ToFieldwork, ToReporting, ToFollowUp, Close, Cancel];
}

/// <summary>The engagement state machine (docs/design/audit.md §4.6). Explicit table, unit-tested.</summary>
public static class EngagementLifecycle
{
    private static readonly Dictionary<(string From, string Action), string> Table = Build();

    public static string? Next(string from, string action) => Table.TryGetValue((from, action), out var to) ? to : null;

    private static Dictionary<(string, string), string> Build()
    {
        var table = new Dictionary<(string, string), string>
        {
            [(EngagementStages.Planned, EngagementActions.Start)] = EngagementStages.Planning,
            [(EngagementStages.Planning, EngagementActions.ToFieldwork)] = EngagementStages.Fieldwork,
            [(EngagementStages.Fieldwork, EngagementActions.ToReporting)] = EngagementStages.Reporting,
            [(EngagementStages.Reporting, EngagementActions.ToFollowUp)] = EngagementStages.FollowUp,
            [(EngagementStages.FollowUp, EngagementActions.Close)] = EngagementStages.Closed,
        };
        foreach (var stage in new[] { EngagementStages.Planned, EngagementStages.Planning, EngagementStages.Fieldwork, EngagementStages.Reporting, EngagementStages.FollowUp })
        {
            table[(stage, EngagementActions.Cancel)] = EngagementStages.Cancelled;
        }

        return table;
    }
}

public static class PlanStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Returned = "returned";
}

/// <summary>Risk-based ordering of the plan (docs/design/audit.md §4.6, ADR 0136): rating weight × (1 + years since the last audit, capped at 5).</summary>
public static class AuditRiskScore
{
    public static decimal Weight(string? rating) => rating switch
    {
        AuditPriorities.Critical => 4m,
        AuditPriorities.High => 3m,
        AuditPriorities.Medium => 2m,
        AuditPriorities.Low => 1m,
        _ => 1m,
    };

    public static decimal Of(string? rating, DateOnly? lastAudited, DateOnly today)
    {
        var years = lastAudited is { } last ? Math.Clamp((today.DayNumber - last.DayNumber) / 365m, 0m, 5m) : 5m;
        return Math.Round(Weight(rating) * (1m + years), 2);
    }
}

public sealed record AuditableEntityValues(string KindCode, string? NameAr, string? NameEn, Guid? UnitId, Guid? RiskId, string? InherentRating, DateOnly? LastAuditedOn,
    bool Active);

/// <summary>An auditable entity of the audit universe (SPEC §10.4).</summary>
[Audited(EntityType)]
public sealed class AuditableEntityRecord : AuditableEntity
{
    public const string EntityType = "auditmgmt.auditable-entity";

    private AuditableEntityRecord()
    {
    }

    public string KindCode { get; private set; } = "unit";

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? RiskId { get; private set; }

    public string? InherentRating { get; private set; }

    public DateOnly? LastAuditedOn { get; private set; }

    public bool Active { get; private set; } = true;

    public string Origin { get; private set; } = "manual";

    public static AuditableEntityRecord Create(AuditableEntityValues v, string origin)
    {
        var e = new AuditableEntityRecord { Origin = origin };
        e.Edit(v);
        return e;
    }

    public void Edit(AuditableEntityValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        KindCode = v.KindCode;
        NameAr = Json.Clean(v.NameAr);
        NameEn = Json.Clean(v.NameEn);
        UnitId = v.UnitId;
        RiskId = v.RiskId;
        InherentRating = Json.Clean(v.InherentRating);
        LastAuditedOn = v.LastAuditedOn;
        Active = v.Active;
    }

    public AuditableEntityValues Values() => new(KindCode, NameAr, NameEn, UnitId, RiskId, InherentRating, LastAuditedOn, Active);

    public void Audited(DateOnly on) => LastAuditedOn = on;
}

/// <summary>The risk-based annual audit plan (SPEC §10.4), approved through <c>auditmgmt.plan</c>.</summary>
[Audited(EntityType)]
public sealed class AuditPlan : AuditableEntity
{
    public const string EntityType = "auditmgmt.plan";

    private AuditPlan()
    {
    }

    public int Year { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string Status { get; private set; } = PlanStatuses.Draft;

    public string? Note { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public bool Editable => Status is PlanStatuses.Draft or PlanStatuses.Returned;

    public static AuditPlan Create(int year, string? titleAr, string? titleEn) => new() { Year = year, TitleAr = Json.Clean(titleAr), TitleEn = Json.Clean(titleEn) };

    public void Edit(string? titleAr, string? titleEn)
    {
        TitleAr = Json.Clean(titleAr);
        TitleEn = Json.Clean(titleEn);
    }

    public void Submit() => Status = PlanStatuses.Submitted;

    public void Decided(string status, string? note, DateTimeOffset at)
    {
        Status = status;
        Note = Json.Clean(note);
        ApprovedAt = status == PlanStatuses.Approved ? at : null;
    }
}

public sealed record EngagementValues(
    string? TitleAr,
    string? TitleEn,
    Guid? EntityId,
    string? Objectives,
    string? Scope,
    Guid? TeamId,
    Guid? LeadAuditorEmployeeId,
    int? PlannedQuarter,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? BudgetHours);

/// <summary>An audit engagement (SPEC §10.4): Planning → Fieldwork → Reporting → Follow-up.</summary>
[Audited(EntityType)]
public sealed class Engagement : AuditableEntity
{
    public const string EntityType = "auditmgmt.engagement";

    private Engagement()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid? PlanId { get; private set; }

    public Guid? EntityId { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Objectives { get; private set; }

    public string? Scope { get; private set; }

    public Guid? TeamId { get; private set; }

    public Guid? LeadAuditorEmployeeId { get; private set; }

    public int? PlannedQuarter { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    public int? BudgetHours { get; private set; }

    public DateOnly? FieldworkEndOn { get; private set; }

    public string Stage { get; private set; } = EngagementStages.Planned;

    public decimal RiskScore { get; private set; }

    public Guid? ReportId { get; private set; }

    public int? Satisfaction { get; private set; }

    public string? SatisfactionComment { get; private set; }

    public string? CancelReason { get; private set; }

    public static Engagement Create(string number, Guid? planId, EngagementValues values, decimal riskScore)
    {
        var e = new Engagement { Number = number, PlanId = planId, RiskScore = riskScore };
        e.Edit(values);
        return e;
    }

    public void Edit(EngagementValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = Json.Clean(v.TitleAr);
        TitleEn = Json.Clean(v.TitleEn);
        EntityId = v.EntityId;
        Objectives = Json.Clean(v.Objectives);
        Scope = Json.Clean(v.Scope);
        TeamId = v.TeamId;
        LeadAuditorEmployeeId = v.LeadAuditorEmployeeId;
        PlannedQuarter = v.PlannedQuarter;
        PlannedStart = v.PlannedStart;
        PlannedEnd = v.PlannedEnd;
        BudgetHours = v.BudgetHours;
    }

    public EngagementValues Values() => new(TitleAr, TitleEn, EntityId, Objectives, Scope, TeamId, LeadAuditorEmployeeId, PlannedQuarter, PlannedStart, PlannedEnd,
        BudgetHours);

    public void SetRiskScore(decimal score) => RiskScore = score;

    public Result Apply(string action, DateOnly today, string? reason = null)
    {
        if (EngagementLifecycle.Next(Stage, action) is not { } next)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition", new Dictionary<string, object?> { ["status"] = Stage, ["action"] = action });
        }

        if (action == EngagementActions.ToReporting)
        {
            FieldworkEndOn = today;
        }

        if (action == EngagementActions.Cancel)
        {
            CancelReason = Json.Clean(reason);
        }

        Stage = next;
        return Result.Success();
    }

    public void LinkReport(Guid reportId) => ReportId = reportId;

    public void Survey(int score, string? comment)
    {
        Satisfaction = Math.Clamp(score, 1, 5);
        SatisfactionComment = Json.Clean(comment);
    }
}

public static class WorkPaperStatuses
{
    public const string Draft = "draft";
    public const string Prepared = "prepared";
    public const string Reviewed = "reviewed";
}

public sealed record WorkPaperValues(string? Reference, string? Title, string? TestStep, string? Sample, string? Result, string? Conclusion);

/// <summary>Audit programme step / working paper (SPEC §10.4) — Audit Office only, reviewer sign-off.</summary>
[Audited(EntityType)]
public sealed class WorkPaper : AuditableEntity
{
    public const string EntityType = "auditmgmt.work-paper";

    private WorkPaper()
    {
    }

    public Guid EngagementId { get; private set; }

    public string? Reference { get; private set; }

    public string? Title { get; private set; }

    public string? TestStep { get; private set; }

    public string? Sample { get; private set; }

    public string? Result { get; private set; }

    public string? Conclusion { get; private set; }

    public string Status { get; private set; } = WorkPaperStatuses.Draft;

    public Guid? PreparedByEmployeeId { get; private set; }

    public Guid? ReviewedByEmployeeId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public static WorkPaper Create(Guid engagementId, WorkPaperValues v)
    {
        var w = new WorkPaper { EngagementId = engagementId };
        w.Edit(v);
        return w;
    }

    public void Edit(WorkPaperValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Reference = Json.Clean(v.Reference);
        Title = Json.Clean(v.Title);
        TestStep = Json.Clean(v.TestStep);
        Sample = Json.Clean(v.Sample);
        Result = Json.Clean(v.Result);
        Conclusion = Json.Clean(v.Conclusion);
        if (Status == WorkPaperStatuses.Reviewed)
        {
            Status = WorkPaperStatuses.Prepared;
            ReviewedAt = null;
            ReviewedByEmployeeId = null;
        }
    }

    public WorkPaperValues Values() => new(Reference, Title, TestStep, Sample, Result, Conclusion);

    public void Prepare(Guid? by)
    {
        Status = WorkPaperStatuses.Prepared;
        PreparedByEmployeeId = by;
    }

    public Result SignOff(Guid? by, DateTimeOffset at)
    {
        if (Status != WorkPaperStatuses.Prepared)
        {
            return Error.Rule("auditmgmt.notPrepared", "auditmgmt.errors.workPaperNotPrepared");
        }

        if (by is not null && by == PreparedByEmployeeId)
        {
            return Error.Rule("auditmgmt.selfReview", "auditmgmt.errors.selfReview");
        }

        Status = WorkPaperStatuses.Reviewed;
        ReviewedByEmployeeId = by;
        ReviewedAt = at;
        return Platform.Kernel.Results.Result.Success();
    }
}
