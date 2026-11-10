using System.Text.Json;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.AuditManagement.Domain;

public static class ReportStatuses
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string UnderFollowUp = "under_follow_up";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, Issued, UnderFollowUp, Closed, Cancelled];
}

public static class RecommendationStatuses
{
    public const string NotStarted = "not_started";
    public const string Continuing = "continuing";
    public const string Late = "late";
    public const string Completed = "completed";
    public const string RiskAccepted = "risk_accepted";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [NotStarted, Continuing, Late, Completed, RiskAccepted, Cancelled];

    public static readonly IReadOnlyList<string> Final = [Completed, RiskAccepted, Cancelled];

    public static readonly IReadOnlyList<string> Open = [NotStarted, Continuing, Late];

    public static bool IsFinal(string status) => Final.Contains(status);
}

public static class FindingTypes
{
    public const string Finding = "finding";
    public const string Improvement = "improvement";
}

public static class ReviewStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Returned = "returned";
}

public static class LogKinds
{
    public const string DueRevision = "due-revision";
    public const string RiskAcceptance = "risk-acceptance";
    public const string Cancellation = "cancellation";
    public const string Reopen = "reopen";
}

/// <summary>Recommendation status rule (docs/design/audit.md §4.1, ADR 0131). Pure, table-tested.</summary>
public static class RecommendationStatus
{
    public static string Derive(string current, decimal latestPct, DateOnly? effectiveDue, DateOnly today)
    {
        if (RecommendationStatuses.IsFinal(current))
        {
            return current;
        }

        if (latestPct >= 1m)
        {
            return RecommendationStatuses.Completed;
        }

        if (effectiveDue is { } due && due < today)
        {
            return RecommendationStatuses.Late;
        }

        return latestPct > 0 ? RecommendationStatuses.Continuing : RecommendationStatuses.NotStarted;
    }
}

/// <summary>Finding status and completion from its recommendations (docs/design/audit.md §4.2, ADR 0131). Pure, table-tested.</summary>
public static class FindingStatus
{
    public sealed record RecommendationState(string Status, decimal LatestPct);

    public static string Derive(string current, IReadOnlyCollection<RecommendationState> recommendations, DateOnly? expectedClosing, DateOnly today)
    {
        if (current == AuditFindingStatuses.Cancelled)
        {
            return current;
        }

        if (recommendations.Count == 0)
        {
            return expectedClosing is { } e && e < today ? AuditFindingStatuses.Late : AuditFindingStatuses.Open;
        }

        if (recommendations.All(r => RecommendationStatuses.IsFinal(r.Status)))
        {
            var live = recommendations.Where(r => r.Status != RecommendationStatuses.Cancelled).ToList();
            return live.Count > 0 && live.All(r => r.Status == RecommendationStatuses.RiskAccepted) ? AuditFindingStatuses.RiskAccepted : AuditFindingStatuses.Closed;
        }

        if (recommendations.Any(r => r.Status == RecommendationStatuses.Late) || (expectedClosing is { } expected && expected < today))
        {
            return AuditFindingStatuses.Late;
        }

        return AuditFindingStatuses.Open;
    }

    /// <summary>Mean latest % of the non-cancelled recommendations (completed = 100 %).</summary>
    public static decimal Completion(IReadOnlyCollection<RecommendationState> recommendations)
    {
        var live = recommendations.Where(r => r.Status != RecommendationStatuses.Cancelled).ToList();
        if (live.Count == 0)
        {
            return 0m;
        }

        return Math.Round(live.Average(r => r.Status == RecommendationStatuses.Completed ? 1m : Math.Clamp(r.LatestPct, 0m, 1m)), 4);
    }
}

/// <summary>Report status from its findings (docs/design/audit.md §4.3). Pure, table-tested.</summary>
public static class ReportStatus
{
    public static string Derive(string current, IReadOnlyCollection<string> findingStatuses)
    {
        if (current is ReportStatuses.Draft or ReportStatuses.Cancelled)
        {
            return current;
        }

        var live = findingStatuses.Where(s => s != AuditFindingStatuses.Cancelled).ToList();
        if (live.Count > 0 && findingStatuses.All(AuditFindingStatuses.IsFinal))
        {
            return ReportStatuses.Closed;
        }

        return findingStatuses.Count == 0 ? ReportStatuses.Issued : ReportStatuses.UnderFollowUp;
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static List<Guid> Ids(string json) => JsonSerializer.Deserialize<List<Guid>>(json, Options) ?? [];

    public static string Ids(IEnumerable<Guid> ids) => JsonSerializer.Serialize(ids.Distinct().ToList(), Options);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AuditReportValues(
    string? TitleAr,
    string? TitleEn,
    string TypeCode,
    string? AuditorBody,
    Guid? LeadAuditorEmployeeId,
    IReadOnlyList<Guid>? AuditedUnitIds,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo,
    DateOnly? ReportDate,
    IReadOnlyList<Guid>? DistributionEmployeeIds,
    string? DistributionNote,
    string? Summary);

/// <summary>An audit report / engagement source (SPEC §10.3 A).</summary>
[Audited(EntityType)]
public sealed class AuditReport : AuditableEntity
{
    public const string EntityType = AuditLinkTypes.Report;
    public const int TitleMaxLength = 400;
    public const int TextMaxLength = 8000;

    private AuditReport()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string TypeCode { get; private set; } = "internal-audit";

    public string? AuditorBody { get; private set; }

    public Guid? LeadAuditorEmployeeId { get; private set; }

    public string AuditedUnitIdsJson { get; private set; } = "[]";

    public DateOnly? PeriodFrom { get; private set; }

    public DateOnly? PeriodTo { get; private set; }

    public DateOnly? ReportDate { get; private set; }

    public string DistributionJson { get; private set; } = "[]";

    public string? DistributionNote { get; private set; }

    public string? Summary { get; private set; }

    public string Status { get; private set; } = ReportStatuses.Draft;

    public Guid? EngagementId { get; private set; }

    public Guid? CapSourceId { get; private set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<Guid> AuditedUnitIds => Json.Ids(AuditedUnitIdsJson);

    public IReadOnlyList<Guid> DistributionEmployeeIds => Json.Ids(DistributionJson);

    public static AuditReport Create(string number, AuditReportValues values, Guid? engagementId = null)
    {
        var report = new AuditReport { Number = number, EngagementId = engagementId };
        report.Edit(values);
        return report;
    }

    public void Edit(AuditReportValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = Json.Clean(v.TitleAr);
        TitleEn = Json.Clean(v.TitleEn);
        TypeCode = v.TypeCode;
        AuditorBody = Json.Clean(v.AuditorBody);
        LeadAuditorEmployeeId = v.LeadAuditorEmployeeId;
        AuditedUnitIdsJson = Json.Ids(v.AuditedUnitIds ?? []);
        PeriodFrom = v.PeriodFrom;
        PeriodTo = v.PeriodTo;
        ReportDate = v.ReportDate;
        DistributionJson = Json.Ids(v.DistributionEmployeeIds ?? []);
        DistributionNote = Json.Clean(v.DistributionNote);
        Summary = Json.Clean(v.Summary);
    }

    /// <summary>Responsibility reassignment (ADR 0168): only the lead auditor changes (an Audit Office member, checked by the caller).</summary>
    public void AssignLeadAuditor(Guid employeeId) => LeadAuditorEmployeeId = employeeId;

    public AuditReportValues Values() => new(TitleAr, TitleEn, TypeCode, AuditorBody, LeadAuditorEmployeeId, AuditedUnitIds, PeriodFrom, PeriodTo, ReportDate,
        DistributionEmployeeIds, DistributionNote, Summary);

    public bool IsOpen => Status is not (ReportStatuses.Closed or ReportStatuses.Cancelled);

    public Result Issue(DateTimeOffset at)
    {
        if (Status != ReportStatuses.Draft)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition");
        }

        Status = ReportStatuses.Issued;
        IssuedAt = at;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is ReportStatuses.Closed or ReportStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition");
        }

        Status = ReportStatuses.Cancelled;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    /// <summary>Applies the derived status; true when it changed.</summary>
    public bool Refresh(IReadOnlyCollection<string> findingStatuses, DateTimeOffset at)
    {
        var next = ReportStatus.Derive(Status, findingStatuses);
        if (next == Status)
        {
            return false;
        }

        Status = next;
        ClosedAt = next == ReportStatuses.Closed ? at : null;
        return true;
    }

    public void LinkCapSource(Guid sourceId) => CapSourceId = sourceId;

    public void LinkEngagement(Guid engagementId) => EngagementId = engagementId;
}

public sealed record FindingValues(
    string? TitleAr,
    string? TitleEn,
    string TypeCode,
    string? Condition,
    string? Criteria,
    Guid? CriteriaDocumentId,
    string? Cause,
    string? Effect,
    DateOnly FindingDate,
    Guid ResponsibleUnitId,
    string Priority,
    bool Repeat,
    Guid? RepeatOfFindingId,
    DateOnly? ExpectedClosingOn,
    Guid? AuditorEmployeeId);

/// <summary>A finding / observation (SPEC §10.3 A). Status and completion follow its recommendations (ADR 0131).</summary>
[Audited(EntityType)]
public sealed class Finding : AuditableEntity
{
    public const string EntityType = AuditLinkTypes.Finding;

    private Finding()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid ReportId { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string TypeCode { get; private set; } = FindingTypes.Finding;

    public string? Condition { get; private set; }

    public string? Criteria { get; private set; }

    public Guid? CriteriaDocumentId { get; private set; }

    public string? Cause { get; private set; }

    public string? Effect { get; private set; }

    public DateOnly FindingDate { get; private set; }

    public int Year { get; private set; }

    public Guid ResponsibleUnitId { get; private set; }

    /// <summary>Nearest ancestor at the configured department level (or the unit itself) — scope grants and reporting.</summary>
    public Guid DepartmentUnitId { get; private set; }

    public string Priority { get; private set; } = AuditPriorities.Medium;

    public bool Repeat { get; private set; }

    public Guid? RepeatOfFindingId { get; private set; }

    public DateOnly? ExpectedClosingOn { get; private set; }

    public DateOnly? ActualClosedOn { get; private set; }

    public string Status { get; private set; } = AuditFindingStatuses.Open;

    public decimal Completion { get; private set; }

    public Guid? AuditorEmployeeId { get; private set; }

    public Guid? CapRemarkId { get; private set; }

    public string? CapRemarkNumber { get; private set; }

    public string? CancelReason { get; private set; }

    public bool IsOpen => !AuditFindingStatuses.IsFinal(Status);

    public static Finding Create(string number, Guid reportId, FindingValues values, Guid departmentUnitId)
    {
        var finding = new Finding { Number = number, ReportId = reportId };
        finding.Edit(values, departmentUnitId);
        return finding;
    }

    public void Edit(FindingValues v, Guid departmentUnitId)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = Json.Clean(v.TitleAr);
        TitleEn = Json.Clean(v.TitleEn);
        TypeCode = v.TypeCode;
        Condition = Json.Clean(v.Condition);
        Criteria = Json.Clean(v.Criteria);
        CriteriaDocumentId = v.CriteriaDocumentId;
        Cause = Json.Clean(v.Cause);
        Effect = Json.Clean(v.Effect);
        FindingDate = v.FindingDate;
        Year = v.FindingDate.Year;
        ResponsibleUnitId = v.ResponsibleUnitId;
        DepartmentUnitId = departmentUnitId;
        Priority = v.Priority;
        Repeat = v.Repeat || v.RepeatOfFindingId is not null;
        RepeatOfFindingId = v.RepeatOfFindingId;
        ExpectedClosingOn = v.ExpectedClosingOn;
        AuditorEmployeeId = v.AuditorEmployeeId;
    }

    public FindingValues Values() => new(TitleAr, TitleEn, TypeCode, Condition, Criteria, CriteriaDocumentId, Cause, Effect, FindingDate, ResponsibleUnitId, Priority, Repeat,
        RepeatOfFindingId, ExpectedClosingOn, AuditorEmployeeId);

    public void SetDepartment(Guid departmentUnitId) => DepartmentUnitId = departmentUnitId;

    /// <summary>Responsibility reassignment (ADR 0168): only the responsible auditor changes (an Audit Office member, checked by the caller).</summary>
    public void AssignAuditor(Guid employeeId) => AuditorEmployeeId = employeeId;

    /// <summary>Applies the derived status and completion; true when the status changed.</summary>
    public bool Refresh(IReadOnlyCollection<FindingStatus.RecommendationState> recommendations, DateOnly today)
    {
        Completion = FindingStatus.Completion(recommendations);
        var next = FindingStatus.Derive(Status, recommendations, ExpectedClosingOn, today);
        if (next == Status)
        {
            return false;
        }

        Status = next;
        ActualClosedOn = next is AuditFindingStatuses.Closed or AuditFindingStatuses.RiskAccepted ? today : null;
        return true;
    }

    public Result Cancel(string reason)
    {
        if (Status == AuditFindingStatuses.Cancelled)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition");
        }

        Status = AuditFindingStatuses.Cancelled;
        CancelReason = reason.Trim();
        return Result.Success();
    }

    public void LinkCapRemark(Guid remarkId, string number)
    {
        CapRemarkId = remarkId;
        CapRemarkNumber = number;
    }
}

/// <summary>Finding ↔ risk (and optionally one control of it), ADR 0135.</summary>
[Audited(EntityType)]
public sealed class FindingRisk : AuditableEntity
{
    public const string EntityType = "auditmgmt.finding-risk";

    private FindingRisk()
    {
    }

    public Guid FindingId { get; private set; }

    public Guid RiskId { get; private set; }

    public string? RiskNumber { get; private set; }

    public Guid? ControlId { get; private set; }

    public bool FlaggedForReassessment { get; private set; }

    public string? ControlLoweredTo { get; private set; }

    public bool Active { get; private set; } = true;

    public static FindingRisk Create(Guid findingId, Guid riskId, string? riskNumber, Guid? controlId) =>
        new() { FindingId = findingId, RiskId = riskId, RiskNumber = riskNumber, ControlId = controlId };

    public void Flagged() => FlaggedForReassessment = true;

    public void Lowered(string level) => ControlLoweredTo = level;

    public void Remove() => Active = false;

    public void Restore() => Active = true;
}

public sealed record RecommendationValues(
    string? ActionAr,
    string? ActionEn,
    string? ManagementResponseCode,
    string? ManagementComment,
    string? ImplementationPlan,
    Guid? ContactEmployeeId,
    DateOnly DueOn,
    string? Notes);

/// <summary>A recommendation / action (SPEC §10.3 A). Its own due date, no default (SPEC §10.8).</summary>
[Audited(EntityType)]
public sealed class Recommendation : AuditableEntity
{
    public const string EntityType = "auditmgmt.recommendation";

    private Recommendation()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid FindingId { get; private set; }

    public string? ActionAr { get; private set; }

    public string? ActionEn { get; private set; }

    public string? ManagementResponseCode { get; private set; }

    public string? ManagementComment { get; private set; }

    public string? ImplementationPlan { get; private set; }

    public Guid? ContactEmployeeId { get; private set; }

    public DateOnly DueOn { get; private set; }

    public DateOnly? RevisedDueOn { get; private set; }

    public string? RevisedReason { get; private set; }

    public string Status { get; private set; } = RecommendationStatuses.NotStarted;

    public decimal LatestPct { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public DateOnly? LastFollowUpOn { get; private set; }

    public DateOnly? NextFollowUpOn { get; private set; }

    public string? LastComment { get; private set; }

    public string? RiskAcceptanceJustification { get; private set; }

    public string? RiskAcceptanceReference { get; private set; }

    public DateOnly? RiskAcceptedOn { get; private set; }

    public string? Notes { get; private set; }

    public string? CancelReason { get; private set; }

    public DateOnly? FollowUpRemindedFor { get; private set; }

    public DateOnly? DueRemindedFor { get; private set; }

    public DateTimeOffset? LateNotifiedAt { get; private set; }

    public DateOnly EffectiveDue => RevisedDueOn ?? DueOn;

    public bool IsOpen => !RecommendationStatuses.IsFinal(Status);

    public static Recommendation Create(string number, Guid findingId, RecommendationValues values)
    {
        var r = new Recommendation { Number = number, FindingId = findingId };
        r.Edit(values);
        return r;
    }

    public void Edit(RecommendationValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        ActionAr = Json.Clean(v.ActionAr);
        ActionEn = Json.Clean(v.ActionEn);
        ManagementResponseCode = Json.Clean(v.ManagementResponseCode);
        ManagementComment = Json.Clean(v.ManagementComment);
        ImplementationPlan = Json.Clean(v.ImplementationPlan);
        ContactEmployeeId = v.ContactEmployeeId;
        if (v.DueOn != DueOn)
        {
            DueRemindedFor = null;
            LateNotifiedAt = null;
        }

        DueOn = v.DueOn;
        Notes = Json.Clean(v.Notes);
    }

    public RecommendationValues Values() => new(ActionAr, ActionEn, ManagementResponseCode, ManagementComment, ImplementationPlan, ContactEmployeeId, DueOn, Notes);

    /// <summary>Applies a recorded (and, with review on, approved) follow-up.</summary>
    public void ApplyFollowUp(decimal pct, DateOnly followUpOn, DateOnly? achievedOn, DateOnly? nextFollowUpOn, string? comment)
    {
        LatestPct = Math.Clamp(pct, 0m, 1m);
        LastFollowUpOn = followUpOn;
        NextFollowUpOn = LatestPct >= 1m ? null : nextFollowUpOn;
        LastComment = Json.Clean(comment) ?? LastComment;
        FollowUpRemindedFor = null;
        if (LatestPct >= 1m && Status != RecommendationStatuses.Cancelled && Status != RecommendationStatuses.RiskAccepted)
        {
            Status = RecommendationStatuses.Completed;
            CompletedOn = achievedOn ?? followUpOn;
            NextFollowUpOn = null;
        }
    }

    /// <summary>Re-derives the status with today's date; true when it changed.</summary>
    public bool Refresh(DateOnly today)
    {
        var next = RecommendationStatus.Derive(Status, LatestPct, EffectiveDue, today);
        if (next == Status)
        {
            return false;
        }

        Status = next;
        if (next == RecommendationStatuses.Completed)
        {
            CompletedOn ??= today;
        }

        if (next != RecommendationStatuses.Late)
        {
            LateNotifiedAt = null;
        }

        return true;
    }

    public Result Revise(DateOnly newDue, string reason)
    {
        if (!IsOpen)
        {
            return Error.Rule("auditmgmt.notOpen", "auditmgmt.errors.recommendationClosed");
        }

        RevisedDueOn = newDue;
        RevisedReason = reason.Trim();
        DueRemindedFor = null;
        LateNotifiedAt = null;
        return Result.Success();
    }

    public Result AcceptRisk(string justification, string? reference, DateOnly on)
    {
        if (!IsOpen)
        {
            return Error.Rule("auditmgmt.notOpen", "auditmgmt.errors.recommendationClosed");
        }

        Status = RecommendationStatuses.RiskAccepted;
        RiskAcceptanceJustification = justification.Trim();
        RiskAcceptanceReference = Json.Clean(reference);
        RiskAcceptedOn = on;
        NextFollowUpOn = null;
        return Result.Success();
    }

    public Result Reopen()
    {
        if (Status != RecommendationStatuses.RiskAccepted)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition");
        }

        Status = RecommendationStatuses.NotStarted;
        RiskAcceptedOn = null;
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status == RecommendationStatuses.Cancelled || Status == RecommendationStatuses.Completed)
        {
            return Error.Rule("auditmgmt.invalidTransition", "auditmgmt.errors.invalidTransition");
        }

        Status = RecommendationStatuses.Cancelled;
        CancelReason = reason.Trim();
        NextFollowUpOn = null;
        return Result.Success();
    }

    public void MarkFollowUpReminded(DateOnly date) => FollowUpRemindedFor = date;

    public void MarkDueReminded(DateOnly date) => DueRemindedFor = date;

    public void MarkLateNotified(DateTimeOffset at) => LateNotifiedAt = at;
}

public sealed record FollowUpValues(
    DateOnly FollowUpOn,
    DateOnly? AchievedOn,
    decimal Pct,
    string? ActionsImplemented,
    string? EvidenceText,
    DateOnly? NextFollowUpOn,
    string? Comment);

/// <summary>A follow-up recorded by the Audit Office (SPEC §10.3 A "Progress Update").</summary>
[Audited(EntityType)]
public sealed class FollowUp : AuditableEntity
{
    public const string EntityType = "auditmgmt.follow-up";

    private FollowUp()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid RecommendationId { get; private set; }

    public DateOnly FollowUpOn { get; private set; }

    public DateOnly? AchievedOn { get; private set; }

    public decimal Pct { get; private set; }

    public string? ActionsImplemented { get; private set; }

    public string? EvidenceText { get; private set; }

    public DateOnly? NextFollowUpOn { get; private set; }

    public string? Comment { get; private set; }

    public Guid? RecordedByEmployeeId { get; private set; }

    public string ReviewStatus { get; private set; } = ReviewStatuses.None;

    public string? ReviewNote { get; private set; }

    public bool Applied { get; private set; }

    public static FollowUp Create(string number, Guid recommendationId, FollowUpValues v, Guid? recordedBy, bool review) => new()
    {
        Number = number,
        RecommendationId = recommendationId,
        FollowUpOn = v.FollowUpOn,
        AchievedOn = v.AchievedOn,
        Pct = Math.Clamp(v.Pct, 0m, 1m),
        ActionsImplemented = Json.Clean(v.ActionsImplemented),
        EvidenceText = Json.Clean(v.EvidenceText),
        NextFollowUpOn = v.Pct >= 1m ? null : v.NextFollowUpOn,
        Comment = Json.Clean(v.Comment),
        RecordedByEmployeeId = recordedBy,
        ReviewStatus = review ? ReviewStatuses.Pending : ReviewStatuses.None,
    };

    public void MarkApplied() => Applied = true;

    public void Reviewed(string status, string? note)
    {
        ReviewStatus = status;
        ReviewNote = Json.Clean(note);
    }

    /// <summary>Validation of follow-up values (docs/design/audit.md §4.4). Field paths for the dialog.</summary>
    public static List<FieldError> Validate(FollowUpValues v, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(v);
        var errors = new List<FieldError>();
        if (v.Pct is < 0m or > 1m)
        {
            errors.Add(new FieldError("pct", "validation.inclusiveBetween", "validation.inclusiveBetween", new Dictionary<string, object?> { ["from"] = 0, ["to"] = 100 }));
        }

        if (v.FollowUpOn > today)
        {
            errors.Add(new FieldError("followUpOn", "auditmgmt.errors.followUpInFuture", "auditmgmt.errors.followUpInFuture"));
        }

        if (v.AchievedOn is { } achieved && achieved > today)
        {
            errors.Add(new FieldError("achievedOn", "auditmgmt.errors.followUpInFuture", "auditmgmt.errors.followUpInFuture"));
        }

        if (v.Pct < 1m)
        {
            if (v.NextFollowUpOn is null)
            {
                errors.Add(new FieldError("nextFollowUpOn", "auditmgmt.errors.nextFollowUpRequired", "auditmgmt.errors.nextFollowUpRequired"));
            }
            else if (v.NextFollowUpOn <= v.FollowUpOn)
            {
                errors.Add(new FieldError("nextFollowUpOn", "auditmgmt.errors.nextFollowUpAfter", "auditmgmt.errors.nextFollowUpAfter"));
            }
        }

        foreach (var (field, value) in new[] { ("actionsImplemented", v.ActionsImplemented), ("evidenceText", v.EvidenceText), ("comment", v.Comment) })
        {
            if (value is { Length: > AuditReport.TextMaxLength })
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength",
                    new Dictionary<string, object?> { ["maxLength"] = AuditReport.TextMaxLength }));
            }
        }

        return errors;
    }
}

/// <summary>Due-date revision / risk acceptance / cancellation log (SPEC §10.3 A).</summary>
[Audited(EntityType)]
public sealed class RecommendationLog : AuditableEntity
{
    public const string EntityType = "auditmgmt.recommendation-log";

    private RecommendationLog()
    {
    }

    public Guid RecommendationId { get; private set; }

    public string Kind { get; private set; } = LogKinds.DueRevision;

    public DateOnly? PreviousDueOn { get; private set; }

    public DateOnly? NewDueOn { get; private set; }

    public string Justification { get; private set; } = string.Empty;

    public string? Reference { get; private set; }

    public Guid? RecordedByEmployeeId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static RecommendationLog Create(Guid recommendationId, string kind, DateOnly? previous, DateOnly? next, string justification, string? reference, Guid? by,
        DateTimeOffset at) => new()
        {
            RecommendationId = recommendationId,
            Kind = kind,
            PreviousDueOn = previous,
            NewDueOn = next,
            Justification = justification.Trim(),
            Reference = Json.Clean(reference),
            RecordedByEmployeeId = by,
            At = at,
        };
}
