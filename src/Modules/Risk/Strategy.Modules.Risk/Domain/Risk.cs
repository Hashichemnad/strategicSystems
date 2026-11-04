using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Risk.Domain;

public static class RiskStatuses
{
    public const string Draft = "draft";
    public const string UnderReview = "under_review";
    public const string Active = "active";
    public const string Closed = "closed";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = [Draft, UnderReview, Active, Closed, Rejected];

    /// <summary>Open = rated and re-rated by framework changes.</summary>
    public static readonly IReadOnlyList<string> Open = [Draft, UnderReview, Active];
}

public static class RiskTypes
{
    public const string Internal = "internal";
    public const string External = "external";

    public static readonly IReadOnlyList<string> All = [Internal, External];
}

/// <summary>Why a risk needs an unscheduled review (SPEC R3.4).</summary>
public static class ReassessmentReasons
{
    public const string KriBreach = "kri-breach";
    public const string Event = "event";
    public const string Appetite = "appetite";
    public const string AuditFinding = "audit-finding";
    public const string Framework = "framework";
    public const string ControlAdded = "control-added";

    public static readonly IReadOnlyList<string> All = [KriBreach, Event, Appetite, AuditFinding, Framework, ControlAdded];
}

public static class EscalationReasons
{
    public const string Critical = "critical";
    public const string OutsideAppetite = "outside-appetite";
    public const string Manual = "manual";
}

/// <summary>Lifecycle actions (ADR 0102).</summary>
public static class RiskActions
{
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Return = "return";
    public const string Reject = "reject";
    public const string Recall = "recall";
    public const string RequestClosure = "request-closure";
    public const string ClosureApproved = "closure-approved";
    public const string ClosureRefused = "closure-refused";
    public const string Reopen = "reopen";
    public const string Discard = "discard";

    public static readonly IReadOnlyList<string> All =
        [Submit, Approve, Return, Reject, Recall, RequestClosure, ClosureApproved, ClosureRefused, Reopen, Discard];
}

/// <summary>The risk state machine (explicit transition table, unit-tested — ADR 0102).</summary>
public static class RiskLifecycle
{
    private static readonly Dictionary<(string From, string Action), string> Table = new()
    {
        [(RiskStatuses.Draft, RiskActions.Submit)] = RiskStatuses.UnderReview,
        [(RiskStatuses.Draft, RiskActions.Discard)] = RiskStatuses.Draft,
        [(RiskStatuses.UnderReview, RiskActions.Approve)] = RiskStatuses.Active,
        [(RiskStatuses.UnderReview, RiskActions.Return)] = RiskStatuses.Draft,
        [(RiskStatuses.UnderReview, RiskActions.Recall)] = RiskStatuses.Draft,
        [(RiskStatuses.UnderReview, RiskActions.Reject)] = RiskStatuses.Rejected,
        [(RiskStatuses.Active, RiskActions.RequestClosure)] = RiskStatuses.Active,
        [(RiskStatuses.Active, RiskActions.ClosureApproved)] = RiskStatuses.Closed,
        [(RiskStatuses.Active, RiskActions.ClosureRefused)] = RiskStatuses.Active,
        [(RiskStatuses.Closed, RiskActions.Reopen)] = RiskStatuses.Active,
        [(RiskStatuses.Rejected, RiskActions.Reopen)] = RiskStatuses.Draft,
    };

    public static string? Next(string from, string action) => Table.TryGetValue((from, action), out var to) ? to : null;

    public static bool Allows(string from, string action) => Table.ContainsKey((from, action));
}

/// <summary>Everything an author sets on a risk (wizard / API / AI draft).</summary>
public sealed record RiskValues(
    string? TitleAr,
    string? TitleEn,
    string? Cause,
    string? EventText,
    string? Consequence,
    string? DeterminedByCode,
    DateOnly IdentifiedOn,
    DateOnly? ExposureStart,
    string RiskType,
    IReadOnlyList<string> CategoryCodes,
    bool Emerging,
    bool Opportunity,
    Guid UnitId,
    Guid? OwnerEmployeeId,
    Guid? CoordinatorEmployeeId,
    Guid? FollowUpEmployeeId,
    IReadOnlyList<string>? ObjectiveCodes,
    string? Processes,
    int InherentLikelihood,
    int InherentImpact,
    string? InherentImpactArea,
    int? ResidualLikelihood,
    int? ResidualImpact,
    int? TargetLikelihood,
    int? TargetImpact,
    DateOnly? TargetDate,
    string? ResponseCode,
    bool NeedsFollowUp);

/// <summary>Computed ratings of a risk (§5.4), written together.</summary>
public sealed record RiskRatings(
    decimal InherentScore, string InherentBand, string? ControlEffectiveness, decimal ResidualScore, string ResidualBand, string ResidualStatus,
    decimal? TargetScore, string? TargetBand, bool? WithinAppetite, Guid FrameworkVersionId);

/// <summary>An enterprise risk (SPEC §3.4 A).</summary>
[Audited(EntityType)]
public sealed class RiskRecord : AuditableEntity
{
    public const string EntityType = "risk.risk";
    public const int TitleMaxLength = 400;
    public const int TextMaxLength = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private RiskRecord()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public int RegisterYear { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string? Cause { get; private set; }

    public string? EventText { get; private set; }

    public string? Consequence { get; private set; }

    public string? DeterminedByCode { get; private set; }

    public DateOnly IdentifiedOn { get; private set; }

    public DateOnly? ExposureStart { get; private set; }

    public string RiskType { get; private set; } = RiskTypes.Internal;

    public string CategoryCodesJson { get; private set; } = "[]";

    public string Status { get; private set; } = RiskStatuses.Draft;

    public bool Emerging { get; private set; }

    public bool Opportunity { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? CoordinatorEmployeeId { get; private set; }

    public Guid? FollowUpEmployeeId { get; private set; }

    public string ObjectiveCodesJson { get; private set; } = "[]";

    public string? Processes { get; private set; }

    public int InherentLikelihood { get; private set; }

    public int InherentImpact { get; private set; }

    public string? InherentImpactArea { get; private set; }

    public decimal InherentScore { get; private set; }

    public string InherentBand { get; private set; } = string.Empty;

    public string? ControlEffectivenessCode { get; private set; }

    public string? ControlEffectivenessOverride { get; private set; }

    public int? ResidualLikelihood { get; private set; }

    public int? ResidualImpact { get; private set; }

    public decimal ResidualScore { get; private set; }

    public string ResidualBand { get; private set; } = string.Empty;

    public string ResidualStatus { get; private set; } = ResidualStatuses.ContinuousReview;

    public string? ResidualStatusOverride { get; private set; }

    public string? ResidualStatusJustification { get; private set; }

    public int? TargetLikelihood { get; private set; }

    public int? TargetImpact { get; private set; }

    public decimal? TargetScore { get; private set; }

    public string? TargetBand { get; private set; }

    public DateOnly? TargetDate { get; private set; }

    public Guid FrameworkVersionId { get; private set; }

    public string? ResponseCode { get; private set; }

    public bool? WithinAppetite { get; private set; }

    public bool NeedsFollowUp { get; private set; }

    public bool Top10 { get; private set; }

    public int? Top10Rank { get; private set; }

    public bool Escalated { get; private set; }

    public string? EscalationReason { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public DateOnly? LastReviewOn { get; private set; }

    public DateOnly? NextReviewOn { get; private set; }

    public DateTimeOffset? NextReviewDueAt { get; private set; }

    public DateTimeOffset? ReviewRemindedAt { get; private set; }

    public DateTimeOffset? ReviewOverdueNotifiedAt { get; private set; }

    public bool ReassessmentRequired { get; private set; }

    public string? ReassessmentReason { get; private set; }

    public string? PendingProcess { get; private set; }

    public bool Returned { get; private set; }

    public string? ReturnComment { get; private set; }

    public string? ClosureReasonCode { get; private set; }

    public string? ClosureNote { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public IReadOnlyList<string> CategoryCodes => JsonSerializer.Deserialize<List<string>>(CategoryCodesJson, Json) ?? [];

    public IReadOnlyList<string> ObjectiveCodes => JsonSerializer.Deserialize<List<string>>(ObjectiveCodesJson, Json) ?? [];

    /// <summary>The control effectiveness used for the residual score: the Risk Office override, else the computed one.</summary>
    public string? EffectiveControl => ControlEffectivenessOverride ?? ControlEffectivenessCode;

    public bool IsOpen => RiskStatuses.Open.Contains(Status);

    public string Title => TitleEn ?? TitleAr ?? Number;

    public static RiskRecord Create(string number, int registerYear, RiskValues values)
    {
        var risk = new RiskRecord { Number = number, RegisterYear = registerYear };
        risk.Edit(values);
        return risk;
    }

    public void Edit(RiskValues v)
    {
        TitleAr = Clean(v.TitleAr);
        TitleEn = Clean(v.TitleEn);
        Cause = Clean(v.Cause);
        EventText = Clean(v.EventText);
        Consequence = Clean(v.Consequence);
        DeterminedByCode = Clean(v.DeterminedByCode);
        IdentifiedOn = v.IdentifiedOn;
        ExposureStart = v.ExposureStart;
        RiskType = v.RiskType;
        CategoryCodesJson = JsonSerializer.Serialize(v.CategoryCodes.Distinct().ToList(), Json);
        Emerging = v.Emerging;
        Opportunity = v.Opportunity;
        UnitId = v.UnitId;
        OwnerEmployeeId = v.OwnerEmployeeId;
        CoordinatorEmployeeId = v.CoordinatorEmployeeId;
        FollowUpEmployeeId = v.FollowUpEmployeeId;
        ObjectiveCodesJson = JsonSerializer.Serialize((v.ObjectiveCodes ?? []).Distinct().ToList(), Json);
        Processes = Clean(v.Processes);
        InherentLikelihood = v.InherentLikelihood;
        InherentImpact = v.InherentImpact;
        InherentImpactArea = Clean(v.InherentImpactArea);
        ResidualLikelihood = v.ResidualLikelihood;
        ResidualImpact = v.ResidualImpact;
        TargetLikelihood = v.TargetLikelihood;
        TargetImpact = v.TargetImpact;
        TargetDate = v.TargetDate;
        ResponseCode = Clean(v.ResponseCode);
        NeedsFollowUp = v.NeedsFollowUp;
    }

    public RiskValues Values() => new(TitleAr, TitleEn, Cause, EventText, Consequence, DeterminedByCode, IdentifiedOn, ExposureStart, RiskType, CategoryCodes, Emerging,
        Opportunity, UnitId, OwnerEmployeeId, CoordinatorEmployeeId, FollowUpEmployeeId, ObjectiveCodes, Processes, InherentLikelihood, InherentImpact, InherentImpactArea,
        ResidualLikelihood, ResidualImpact, TargetLikelihood, TargetImpact, TargetDate, ResponseCode, NeedsFollowUp);

    /// <summary>Residual inputs from a periodic review (R3).</summary>
    public void SetResidual(int likelihood, int impact)
    {
        ResidualLikelihood = likelihood;
        ResidualImpact = impact;
    }

    /// <summary>Writes computed ratings; true when a band changed.</summary>
    public bool ApplyRatings(RiskRatings r)
    {
        var changed = InherentBand != r.InherentBand || ResidualBand != r.ResidualBand || TargetBand != r.TargetBand;
        InherentScore = r.InherentScore;
        InherentBand = r.InherentBand;
        ControlEffectivenessCode = r.ControlEffectiveness;
        ResidualScore = r.ResidualScore;
        ResidualBand = r.ResidualBand;
        ResidualStatus = ResidualStatusOverride ?? r.ResidualStatus;
        TargetScore = r.TargetScore;
        TargetBand = r.TargetBand;
        WithinAppetite = r.WithinAppetite;
        FrameworkVersionId = r.FrameworkVersionId;
        return changed;
    }

    /// <summary>Risk Office adjustments (R1.3 / R4): override of control effectiveness and residual status (justification), Top-10.</summary>
    public void OfficeUpdate(string? controlOverride, string? statusOverride, string? justification, bool top10, int? top10Rank)
    {
        ControlEffectivenessOverride = Clean(controlOverride);
        ResidualStatusOverride = Clean(statusOverride);
        ResidualStatusJustification = ResidualStatusOverride is null ? null : Clean(justification);
        Top10 = top10;
        Top10Rank = top10 ? top10Rank : null;
    }

    public void Escalate(string reason, DateTimeOffset at)
    {
        if (!Escalated)
        {
            EscalatedAt = at;
        }

        Escalated = true;
        EscalationReason = reason;
    }

    public void ClearEscalation()
    {
        Escalated = false;
        EscalationReason = null;
        EscalatedAt = null;
    }

    public void FlagReassessment(string reason)
    {
        ReassessmentRequired = true;
        ReassessmentReason = reason;
    }

    public void SetReview(DateOnly lastReview, DateOnly next, DateTimeOffset dueAt)
    {
        LastReviewOn = lastReview;
        NextReviewOn = next;
        NextReviewDueAt = dueAt;
        ReviewRemindedAt = null;
        ReviewOverdueNotifiedAt = null;
        ReassessmentRequired = false;
        ReassessmentReason = null;
    }

    public void MarkReviewReminded(DateTimeOffset at) => ReviewRemindedAt = at;

    public void MarkReviewOverdue(DateTimeOffset at) => ReviewOverdueNotifiedAt = at;

    public void SetPending(string? process) => PendingProcess = process;

    /// <summary>Applies a lifecycle action (ADR 0102).</summary>
    public Result Apply(string action, DateTimeOffset at, Guid? by, string? comment = null, string? reasonCode = null)
    {
        if (RiskLifecycle.Next(Status, action) is not { } next)
        {
            return Error.Rule("risk.invalidTransition", "risk.errors.invalidTransition",
                new Dictionary<string, object?> { ["status"] = Status, ["action"] = action });
        }

        switch (action)
        {
            case RiskActions.Submit:
                Returned = false;
                ReturnComment = null;
                break;
            case RiskActions.Return:
                Returned = true;
                ReturnComment = Clean(comment);
                break;
            case RiskActions.Reject:
                RejectionReason = Clean(comment);
                break;
            case RiskActions.RequestClosure:
                ClosureReasonCode = reasonCode;
                ClosureNote = Clean(comment);
                break;
            case RiskActions.ClosureApproved:
                ClosedAt = at;
                ClosedBy = by;
                break;
            case RiskActions.ClosureRefused:
                ClosureReasonCode = null;
                ClosureNote = null;
                break;
            case RiskActions.Reopen:
                ClosedAt = null;
                ClosedBy = null;
                ClosureReasonCode = null;
                ClosureNote = null;
                RejectionReason = null;
                break;
        }

        Status = next;
        return Result.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A risk category (multi-level) with its appetite — max acceptable residual score; empty by default (Q-R3).</summary>
[Audited(EntityType)]
public sealed class Category : AuditableEntity
{
    public const string EntityType = "risk.category";

    private Category()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public Guid? ParentId { get; private set; }

    public decimal? Appetite { get; private set; }

    public int Sort { get; private set; }

    public bool Active { get; private set; } = true;

    public static Category Create(string code) => new() { Code = code };

    public void Update(string? nameAr, string? nameEn, Guid? parentId, decimal? appetite, int sort, bool active)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        ParentId = parentId;
        Appetite = appetite;
        Sort = sort;
        Active = active;
    }
}

public static class ControlStatuses
{
    public const string Active = "active";
    public const string Retired = "retired";
}

public sealed record ControlValues(
    string? NameAr, string? NameEn, string? Description, string? TypeCode, string? NatureCode, Guid? OwnerEmployeeId, string? FrequencyCode, string? EffectivenessCode,
    DateOnly? LastAssessedOn, string? Evidence);

/// <summary>An existing control (SPEC §3.4 B).</summary>
[Audited(EntityType)]
public sealed class Control : AuditableEntity
{
    public const string EntityType = "risk.control";

    private Control()
    {
    }

    public Guid RiskId { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? Description { get; private set; }

    public string? TypeCode { get; private set; }

    public string? NatureCode { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string? FrequencyCode { get; private set; }

    public string? EffectivenessCode { get; private set; }

    public DateOnly? LastAssessedOn { get; private set; }

    public string? Evidence { get; private set; }

    public Guid? SourceActionId { get; private set; }

    public string Status { get; private set; } = ControlStatuses.Active;

    public static Control Create(Guid riskId, ControlValues values, Guid? sourceActionId = null)
    {
        var control = new Control { RiskId = riskId, SourceActionId = sourceActionId };
        control.Update(values);
        return control;
    }

    public void Update(ControlValues v)
    {
        NameAr = v.NameAr?.Trim();
        NameEn = v.NameEn?.Trim();
        Description = v.Description;
        TypeCode = v.TypeCode;
        NatureCode = v.NatureCode;
        OwnerEmployeeId = v.OwnerEmployeeId;
        FrequencyCode = v.FrequencyCode;
        EffectivenessCode = v.EffectivenessCode;
        LastAssessedOn = v.LastAssessedOn;
        Evidence = v.Evidence;
    }

    public void Rate(string? effectiveness, DateOnly on)
    {
        EffectivenessCode = effectiveness;
        LastAssessedOn = on;
    }

    public void Retire() => Status = ControlStatuses.Retired;
}

public static class ActionStatuses
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [NotStarted, InProgress, Completed, Cancelled];
}

public sealed record ActionValues(
    string? TitleAr, string? TitleEn, Guid? OwnerEmployeeId, IReadOnlyList<Guid>? ResponsibleUnitIds, DateOnly? PlannedStart, DateOnly? PlannedEnd, decimal? Cost);

/// <summary>A treatment action (SPEC §3.4 C).</summary>
[Audited(EntityType)]
public sealed class TreatmentAction : AuditableEntity
{
    public const string EntityType = "risk.action";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TreatmentAction()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid RiskId { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string ResponsibleUnitIdsJson { get; private set; } = "[]";

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    public DateOnly? ActualStart { get; private set; }

    public DateOnly? ActualEnd { get; private set; }

    public decimal CompletionPct { get; private set; }

    public string Status { get; private set; } = ActionStatuses.NotStarted;

    public decimal? Cost { get; private set; }

    public Guid? ConvertedControlId { get; private set; }

    public DateTimeOffset? RemindedAt { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public IReadOnlyList<Guid> ResponsibleUnitIds => JsonSerializer.Deserialize<List<Guid>>(ResponsibleUnitIdsJson, Json) ?? [];

    public static TreatmentAction Create(string number, Guid riskId, ActionValues values)
    {
        var action = new TreatmentAction { Number = number, RiskId = riskId };
        action.Update(values);
        return action;
    }

    public void Update(ActionValues v)
    {
        TitleAr = v.TitleAr?.Trim();
        TitleEn = v.TitleEn?.Trim();
        OwnerEmployeeId = v.OwnerEmployeeId;
        ResponsibleUnitIdsJson = JsonSerializer.Serialize((v.ResponsibleUnitIds ?? []).Distinct().ToList(), Json);
        if (v.PlannedEnd != PlannedEnd)
        {
            RemindedAt = null;
            EscalatedAt = null;
        }

        PlannedStart = v.PlannedStart;
        PlannedEnd = v.PlannedEnd;
        Cost = v.Cost;
    }

    /// <summary>Progress update (R2.1): 100 % completes the action.</summary>
    public void Progress(decimal pct, DateOnly? actualStart, DateOnly? actualEnd, DateOnly today)
    {
        CompletionPct = Math.Clamp(pct, 0m, 1m);
        ActualStart = actualStart ?? ActualStart ?? (CompletionPct > 0 ? today : null);
        if (CompletionPct >= 1m)
        {
            Status = ActionStatuses.Completed;
            ActualEnd = actualEnd ?? today;
        }
        else
        {
            Status = CompletionPct > 0 ? ActionStatuses.InProgress : ActionStatuses.NotStarted;
            ActualEnd = null;
        }
    }

    public void Cancel() => Status = ActionStatuses.Cancelled;

    public void MarkConverted(Guid controlId) => ConvertedControlId = controlId;

    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;

    public void MarkEscalated(DateTimeOffset at) => EscalatedAt = at;

    public bool IsOverdue(DateOnly today) => Status is ActionStatuses.NotStarted or ActionStatuses.InProgress && PlannedEnd is { } end && end < today;
}

/// <summary>A progress update of a treatment action, kept as history (R2.1).</summary>
[Audited(EntityType)]
public sealed class ActionUpdate : AuditableEntity
{
    public const string EntityType = "risk.action-update";

    private ActionUpdate()
    {
    }

    public Guid ActionId { get; private set; }

    public string? Note { get; private set; }

    public decimal CompletionPct { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid? By { get; private set; }

    /// <summary>A draft note written through MCP (ADR 0106); the owner confirms it in the UI.</summary>
    public bool Draft { get; private set; }

    public static ActionUpdate Create(Guid actionId, string? note, decimal pct, DateTimeOffset at, Guid? by, bool draft = false) =>
        new() { ActionId = actionId, Note = note, CompletionPct = pct, At = at, By = by, Draft = draft };

    public void Confirm() => Draft = false;
}

public static class KriBases
{
    public const string Value = "value";
    public const string Performance = "performance";

    public static readonly IReadOnlyList<string> All = [Value, Performance];
}

public static class KriDirections
{
    public const string Above = "above";
    public const string Below = "below";

    public static readonly IReadOnlyList<string> All = [Above, Below];
}

public static class KriLevels
{
    public const string Normal = "normal";
    public const string Warning = "warning";
    public const string Breach = "breach";
    public const string None = "none";
}

/// <summary>Pure KRI evaluation (ADR 0104): inclusive thresholds in the KRI's direction.</summary>
public static class KriEvaluator
{
    public static string Level(decimal value, string direction, decimal? warning, decimal breach)
    {
        bool Reached(decimal threshold) => direction == KriDirections.Below ? value <= threshold : value >= threshold;
        if (Reached(breach))
        {
            return KriLevels.Breach;
        }

        return warning is { } w && Reached(w) ? KriLevels.Warning : KriLevels.Normal;
    }
}

public sealed record KriValues(Guid KpiCardId, string Basis, string Direction, decimal? WarningThreshold, decimal BreachThreshold);

/// <summary>Key risk indicator: risk ↔ KPI card with thresholds (SPEC §3.4 D).</summary>
[Audited(EntityType)]
public sealed class Kri : AuditableEntity
{
    public const string EntityType = "risk.kri";

    private Kri()
    {
    }

    public Guid RiskId { get; private set; }

    public Guid KpiCardId { get; private set; }

    public string CardCode { get; private set; } = string.Empty;

    public string? CardNameAr { get; private set; }

    public string? CardNameEn { get; private set; }

    public string Basis { get; private set; } = KriBases.Value;

    public string Direction { get; private set; } = KriDirections.Above;

    public decimal? WarningThreshold { get; private set; }

    public decimal BreachThreshold { get; private set; }

    public string Status { get; private set; } = ControlStatuses.Active;

    public Guid? LastResultId { get; private set; }

    public decimal? LastValue { get; private set; }

    public string LastLevel { get; private set; } = KriLevels.None;

    public string? LastPeriod { get; private set; }

    public DateTimeOffset? LastAt { get; private set; }

    public static Kri Create(Guid riskId, KriValues v, string cardCode, string? nameAr, string? nameEn)
    {
        var kri = new Kri { RiskId = riskId, KpiCardId = v.KpiCardId, CardCode = cardCode, CardNameAr = nameAr, CardNameEn = nameEn };
        kri.Update(v);
        return kri;
    }

    public void Update(KriValues v)
    {
        Basis = v.Basis;
        Direction = v.Direction;
        WarningThreshold = v.WarningThreshold;
        BreachThreshold = v.BreachThreshold;
    }

    public void Record(Guid resultId, decimal value, string level, string period, DateTimeOffset at)
    {
        LastResultId = resultId;
        LastValue = value;
        LastLevel = level;
        LastPeriod = period;
        LastAt = at;
    }

    public void Remove() => Status = ControlStatuses.Retired;
}

public static class AssessmentKinds
{
    public const string Registration = "registration";
    public const string Approval = "approval";
    public const string Review = "review";
    public const string Reassessment = "reassessment";
    public const string Recalculation = "recalculation";
    public const string Closure = "closure";
    public const string Reopen = "reopen";
}

/// <summary>Snapshot on every (re)assessment (SPEC §3.4 E). Append-only.</summary>
[Audited(EntityType)]
public sealed class Assessment : AuditableEntity
{
    public const string EntityType = "risk.assessment";

    private Assessment()
    {
    }

    public Guid RiskId { get; private set; }

    public string Kind { get; private set; } = AssessmentKinds.Registration;

    public int InherentLikelihood { get; private set; }

    public int InherentImpact { get; private set; }

    public decimal InherentScore { get; private set; }

    public string InherentBand { get; private set; } = string.Empty;

    public int? ResidualLikelihood { get; private set; }

    public int? ResidualImpact { get; private set; }

    public decimal ResidualScore { get; private set; }

    public string ResidualBand { get; private set; } = string.Empty;

    public string ResidualStatus { get; private set; } = string.Empty;

    public decimal? TargetScore { get; private set; }

    public string? TargetBand { get; private set; }

    public string? ControlEffectiveness { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public int FrameworkVersionNo { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid? By { get; private set; }

    public static Assessment Of(RiskRecord r, string kind, int frameworkVersionNo, string? comment, DateTimeOffset at, Guid? by) => new()
    {
        RiskId = r.Id,
        Kind = kind,
        InherentLikelihood = r.InherentLikelihood,
        InherentImpact = r.InherentImpact,
        InherentScore = r.InherentScore,
        InherentBand = r.InherentBand,
        ResidualLikelihood = r.ResidualLikelihood ?? r.InherentLikelihood,
        ResidualImpact = r.ResidualImpact ?? r.InherentImpact,
        ResidualScore = r.ResidualScore,
        ResidualBand = r.ResidualBand,
        ResidualStatus = r.ResidualStatus,
        TargetScore = r.TargetScore,
        TargetBand = r.TargetBand,
        ControlEffectiveness = r.EffectiveControl,
        Status = r.Status,
        FrameworkVersionNo = frameworkVersionNo,
        Comment = comment,
        At = at,
        By = by,
    };
}

public sealed record EventValues(
    DateOnly OccurredOn, string? Description, decimal? FinancialImpact, string? Currency, string? OtherImpact, bool NearMiss, string? RootCause, string? Lessons,
    Guid? UnitId, IReadOnlyList<Guid> RiskIds);

/// <summary>A risk event / incident (loss event log, SPEC §3.4 F).</summary>
[Audited(EntityType)]
public sealed class RiskEvent : AuditableEntity
{
    public const string EntityType = "risk.event";

    private RiskEvent()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public DateOnly OccurredOn { get; private set; }

    public string? Description { get; private set; }

    public decimal? FinancialImpact { get; private set; }

    public string Currency { get; private set; } = "AED";

    public string? OtherImpact { get; private set; }

    public bool NearMiss { get; private set; }

    public string? RootCause { get; private set; }

    public string? Lessons { get; private set; }

    public Guid? UnitId { get; private set; }

    public Guid? CapRemarkId { get; private set; }

    public string Status { get; private set; } = "open";

    public static RiskEvent Create(string number, EventValues v)
    {
        var e = new RiskEvent { Number = number };
        e.Update(v);
        return e;
    }

    public void Update(EventValues v)
    {
        OccurredOn = v.OccurredOn;
        Description = v.Description;
        FinancialImpact = v.FinancialImpact;
        Currency = string.IsNullOrWhiteSpace(v.Currency) ? "AED" : v.Currency.Trim().ToUpperInvariant();
        OtherImpact = v.OtherImpact;
        NearMiss = v.NearMiss;
        RootCause = v.RootCause;
        Lessons = v.Lessons;
        UnitId = v.UnitId;
    }

    public void LinkCapRemark(Guid remarkId) => CapRemarkId = remarkId;
}

/// <summary>Event ↔ risk link (many-to-many).</summary>
public sealed class EventRisk
{
    public Guid EventId { get; set; }

    public Guid RiskId { get; set; }
}

public static class ReviewStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
}

public sealed record ReviewValues(int ResidualLikelihood, int ResidualImpact, IReadOnlyDictionary<Guid, string?>? ControlRatings, string? ActionNotes, string? Commentary);

/// <summary>A periodic review (R3).</summary>
[Audited(EntityType)]
public sealed class Review : AuditableEntity
{
    public const string EntityType = "risk.review";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Review()
    {
    }

    public Guid RiskId { get; private set; }

    public string Status { get; private set; } = ReviewStatuses.Draft;

    public int ResidualLikelihood { get; private set; }

    public int ResidualImpact { get; private set; }

    public string ControlRatingsJson { get; private set; } = "{}";

    public string? ActionNotes { get; private set; }

    public string? Commentary { get; private set; }

    public string? DecisionComment { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public IReadOnlyDictionary<Guid, string?> ControlRatings => JsonSerializer.Deserialize<Dictionary<Guid, string?>>(ControlRatingsJson, Json) ?? [];

    public static Review Create(Guid riskId, ReviewValues v)
    {
        var review = new Review { RiskId = riskId };
        review.Update(v);
        return review;
    }

    public void Update(ReviewValues v)
    {
        ResidualLikelihood = v.ResidualLikelihood;
        ResidualImpact = v.ResidualImpact;
        ControlRatingsJson = JsonSerializer.Serialize(v.ControlRatings ?? new Dictionary<Guid, string?>(), Json);
        ActionNotes = v.ActionNotes;
        Commentary = v.Commentary;
    }

    public ReviewValues Values() => new(ResidualLikelihood, ResidualImpact, ControlRatings, ActionNotes, Commentary);

    public void Submit(DateTimeOffset at)
    {
        Status = ReviewStatuses.Submitted;
        SubmittedAt = at;
    }

    public void Decide(string status, string? comment, DateTimeOffset at)
    {
        Status = status;
        DecisionComment = comment;
        DecidedAt = at;
    }
}

/// <summary>A Risk Committee decision recorded on a risk (R4).</summary>
[Audited(EntityType)]
public sealed class CommitteeDecision : AuditableEntity
{
    public const string EntityType = "risk.committee-decision";

    private CommitteeDecision()
    {
    }

    public Guid RiskId { get; private set; }

    public string DecisionCode { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public DateOnly DecidedOn { get; private set; }

    public static CommitteeDecision Create(Guid riskId, string code, string? note, DateOnly on) => new() { RiskId = riskId, DecisionCode = code, Note = note, DecidedOn = on };
}
