using System.Text.Json;
using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Cap.Domain;

/// <summary>Stage of a remark on the stepper (SPEC §9.6): Analysis › Corrective Action › Implementation › Verification › Closed.</summary>
public static class RemarkStages
{
    public const string Analysis = "analysis";
    public const string CorrectiveAction = "corrective-action";
    public const string Implementation = "implementation";
    public const string Verification = "verification";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Analysis, CorrectiveAction, Implementation, Verification, Closed];

    public static string Of(string status) => status switch
    {
        CapRemarkStatuses.Assigned or CapRemarkStatuses.RejectionPending => Analysis,
        CapRemarkStatuses.Planning or CapRemarkStatuses.Validation or CapRemarkStatuses.Correction => CorrectiveAction,
        CapRemarkStatuses.Implementation => Implementation,
        CapRemarkStatuses.Verification => Verification,
        _ => Closed,
    };
}

/// <summary>Remark lifecycle actions (ADR 0121).</summary>
public static class RemarkActions
{
    public const string Accept = "accept";
    public const string Reject = "reject";
    public const string RejectForConfirmation = "reject-for-confirmation";
    public const string ConfirmRejection = "confirm-rejection";
    public const string SendBack = "send-back";
    public const string Submit = "submit";
    public const string ToImplementation = "to-implementation";
    public const string ToCorrection = "to-correction";
    public const string ToPlanning = "to-planning";
    public const string ToValidation = "to-validation";
    public const string Complete = "complete";
    public const string ToVerification = "to-verification";
    public const string Effective = "effective";
    public const string NotEffective = "not-effective";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All =
    [
        Accept, Reject, RejectForConfirmation, ConfirmRejection, SendBack, Submit, ToImplementation, ToCorrection, ToPlanning, ToValidation, Complete, ToVerification,
        Effective, NotEffective, Cancel,
    ];
}

/// <summary>The remark state machine (explicit transition table, unit-tested — docs/design/cap.md §4.1).</summary>
public static class RemarkLifecycle
{
    private static readonly string[] Open =
    [
        CapRemarkStatuses.Assigned, CapRemarkStatuses.RejectionPending, CapRemarkStatuses.Planning, CapRemarkStatuses.Validation, CapRemarkStatuses.Correction,
        CapRemarkStatuses.Implementation, CapRemarkStatuses.Verification,
    ];

    private static readonly Dictionary<(string From, string Action), string> Table = BuildTable();

    public static string? Next(string from, string action) => Table.TryGetValue((from, action), out var to) ? to : null;

    public static bool Allows(string from, string action) => Table.ContainsKey((from, action));

    private static Dictionary<(string, string), string> BuildTable()
    {
        var table = new Dictionary<(string, string), string>
        {
            [(CapRemarkStatuses.Assigned, RemarkActions.Accept)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Assigned, RemarkActions.Reject)] = CapRemarkStatuses.Rejected,
            [(CapRemarkStatuses.Assigned, RemarkActions.RejectForConfirmation)] = CapRemarkStatuses.RejectionPending,
            [(CapRemarkStatuses.RejectionPending, RemarkActions.ConfirmRejection)] = CapRemarkStatuses.Rejected,
            [(CapRemarkStatuses.RejectionPending, RemarkActions.SendBack)] = CapRemarkStatuses.Assigned,
            [(CapRemarkStatuses.Planning, RemarkActions.Submit)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Correction, RemarkActions.Submit)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Validation, RemarkActions.ToImplementation)] = CapRemarkStatuses.Implementation,
            [(CapRemarkStatuses.Validation, RemarkActions.ToCorrection)] = CapRemarkStatuses.Correction,
            [(CapRemarkStatuses.Validation, RemarkActions.ToPlanning)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Validation, RemarkActions.ToValidation)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Implementation, RemarkActions.Complete)] = CapRemarkStatuses.Completed,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToVerification)] = CapRemarkStatuses.Verification,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToPlanning)] = CapRemarkStatuses.Planning,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToCorrection)] = CapRemarkStatuses.Correction,
            [(CapRemarkStatuses.Implementation, RemarkActions.ToValidation)] = CapRemarkStatuses.Validation,
            [(CapRemarkStatuses.Verification, RemarkActions.Effective)] = CapRemarkStatuses.Completed,
            [(CapRemarkStatuses.Verification, RemarkActions.NotEffective)] = CapRemarkStatuses.Planning,
        };
        foreach (var status in Open)
        {
            table[(status, RemarkActions.Cancel)] = CapRemarkStatuses.Cancelled;
        }

        return table;
    }
}

public static class ValidationStatuses
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";

    public static readonly IReadOnlyList<string> All = [Draft, Pending, Approved, Rejected, Returned];
}

public static class ActionStatuses
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Late = "late";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [NotStarted, InProgress, Late, Completed, Cancelled];

    public static readonly IReadOnlyList<string> Open = [NotStarted, InProgress, Late];
}

/// <summary>Root-cause methods (SPEC §9.1).</summary>
public static class RootCauseMethods
{
    public const string FiveWhys = "five-whys";
    public const string Fishbone = "fishbone";
    public const string Text = "text";

    public static readonly IReadOnlyList<string> All = [FiveWhys, Fishbone, Text];

    /// <summary>Fishbone (Ishikawa) categories — fixed codes, labels in the SPA.</summary>
    public static readonly IReadOnlyList<string> FishboneCategories = ["people", "process", "equipment", "materials", "environment", "management"];
}

/// <summary>The derived remark status after its actions changed (docs/design/cap.md §4.1). Pure, table-tested.</summary>
public static class RemarkProgress
{
    public sealed record ActionState(string ValidationStatus, string Status);

    /// <summary>The status the remark should have now, given its actions; null when it should stay as it is.</summary>
    public static string? Derive(string current, IReadOnlyCollection<ActionState> actions, bool verificationEnabled)
    {
        if (current is not (CapRemarkStatuses.Validation or CapRemarkStatuses.Implementation))
        {
            return null;
        }

        var live = actions.Where(a => a.Status != ActionStatuses.Cancelled).ToList();
        if (live.Any(a => a.ValidationStatus == ValidationStatuses.Pending))
        {
            return CapRemarkStatuses.Validation;
        }

        if (live.Any(a => a.ValidationStatus is ValidationStatuses.Draft or ValidationStatuses.Returned))
        {
            return CapRemarkStatuses.Correction;
        }

        var approved = live.Where(a => a.ValidationStatus == ValidationStatuses.Approved).ToList();
        if (approved.Count == 0)
        {
            return CapRemarkStatuses.Planning;
        }

        if (approved.All(a => a.Status == ActionStatuses.Completed))
        {
            return verificationEnabled ? CapRemarkStatuses.Verification : CapRemarkStatuses.Completed;
        }

        return CapRemarkStatuses.Implementation;
    }

    /// <summary>The lifecycle action that moves <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static string? ActionFor(string from, string to) => to switch
    {
        CapRemarkStatuses.Validation => RemarkActions.ToValidation,
        CapRemarkStatuses.Correction => RemarkActions.ToCorrection,
        CapRemarkStatuses.Planning => RemarkActions.ToPlanning,
        CapRemarkStatuses.Implementation => RemarkActions.ToImplementation,
        CapRemarkStatuses.Verification => RemarkActions.ToVerification,
        CapRemarkStatuses.Completed when from == CapRemarkStatuses.Implementation => RemarkActions.Complete,
        _ => null,
    };
}

/// <summary>Deterministic quality flags of an action for the PM Office (SPEC §9.8 "action quality check"). Pure.</summary>
public static class ActionQuality
{
    private static readonly string[] VagueTerms =
    [
        "improve", "enhance", "review", "study", "consider", "look into", "work on", "follow up", "as needed", "if possible",
        "تحسين", "تطوير", "دراسة", "مراجعة", "النظر في", "متابعة", "حسب الحاجة", "إن أمكن",
    ];

    public const string Vague = "vague";
    public const string NoAssignee = "no-assignee";
    public const string NoDates = "no-dates";
    public const string EndBeforeStart = "end-before-start";
    public const string EndInPast = "end-in-past";
    public const string AfterRemarkDue = "after-remark-due";
    public const string NoCost = "budget-without-cost";
    public const string TooShort = "too-short";

    public static IReadOnlyList<string> Flags(ActionValues v, DateOnly today, DateOnly? remarkDue)
    {
        ArgumentNullException.ThrowIfNull(v);
        var flags = new List<string>();
        var text = $"{v.DetailsAr} {v.DetailsEn}".Trim();
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < 5)
        {
            flags.Add(TooShort);
        }

        if (words < 12 && VagueTerms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add(Vague);
        }

        if (v.AssignedEmployeeId is null)
        {
            flags.Add(NoAssignee);
        }

        if (v.PlannedEnd is null)
        {
            flags.Add(NoDates);
        }
        else
        {
            if (v.PlannedStart is { } start && v.PlannedEnd < start)
            {
                flags.Add(EndBeforeStart);
            }

            if (v.PlannedEnd < today)
            {
                flags.Add(EndInPast);
            }

            if (remarkDue is { } due && v.PlannedEnd > due)
            {
                flags.Add(AfterRemarkDue);
            }
        }

        if (v.BudgetRequired && v.EstimatedCost is null or <= 0)
        {
            flags.Add(NoCost);
        }

        return flags;
    }
}

/// <summary>A remarks source (SPEC §9.3 A).</summary>
[Audited(EntityType)]
public sealed class CapSource : AuditableEntity
{
    public const string EntityType = CapLinkTypes.Source;
    public const int TitleMaxLength = 400;
    public const int TextMaxLength = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private CapSource()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string SourceTypeCode { get; private set; } = CapSourceTypes.Other;

    public string? Description { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? UnitId { get; private set; }

    /// <summary><c>manual</c> (PM Office) or <c>system</c> (created through the source-system contract).</summary>
    public string Origin { get; private set; } = "manual";

    public string? LinkedType { get; private set; }

    public Guid? LinkedId { get; private set; }

    public Guid? TeamId { get; private set; }

    public string ViewerEmployeeIdsJson { get; private set; } = "[]";

    public string Status { get; private set; } = "active";

    public IReadOnlyList<Guid> ViewerEmployeeIds => JsonSerializer.Deserialize<List<Guid>>(ViewerEmployeeIdsJson, Json) ?? [];

    public static CapSource Create(string number, SourceValues values, string origin, string? linkedType, Guid? linkedId)
    {
        var source = new CapSource { Number = number, Origin = origin, LinkedType = linkedType, LinkedId = linkedId };
        source.Edit(values);
        return source;
    }

    public void Edit(SourceValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TitleAr = Clean(v.TitleAr);
        TitleEn = Clean(v.TitleEn);
        SourceTypeCode = v.SourceTypeCode;
        Description = Clean(v.Description);
        ReceivedOn = v.ReceivedOn;
        OwnerEmployeeId = v.OwnerEmployeeId;
        UnitId = v.UnitId;
        TeamId = v.TeamId;
    }

    public void SetViewers(IEnumerable<Guid> viewers) => ViewerEmployeeIdsJson = JsonSerializer.Serialize(viewers.Distinct().ToList(), Json);

    public void Close() => Status = "closed";

    public void Reopen() => Status = "active";

    public SourceValues Values() => new(TitleAr, TitleEn, SourceTypeCode, Description, ReceivedOn, OwnerEmployeeId, UnitId, TeamId);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SourceValues(
    string? TitleAr, string? TitleEn, string SourceTypeCode, string? Description, DateOnly ReceivedOn, Guid? OwnerEmployeeId, Guid? UnitId, Guid? TeamId);

public sealed record RemarkValues(
    string? TextAr, string? TextEn, string? CriterionCode, string TypeCode, string Priority, Guid DepartmentUnitId, Guid? SectionUnitId, Guid? OwnerEmployeeId);

public sealed record RootCauseValues(string Method, IReadOnlyList<string>? Whys, IReadOnlyDictionary<string, IReadOnlyList<string>>? Fishbone, string? Text);

/// <summary>A remark — finding, observation or improvement opportunity (SPEC §9.3 B).</summary>
[Audited(EntityType)]
public sealed class Remark : AuditableEntity
{
    public const string EntityType = CapLinkTypes.Remark;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Remark()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid SourceId { get; private set; }

    public string? TextAr { get; private set; }

    public string? TextEn { get; private set; }

    public string? CriterionCode { get; private set; }

    public string TypeCode { get; private set; } = CapRemarkTypes.Observation;

    public string Priority { get; private set; } = CapPriorities.Medium;

    public Guid DepartmentUnitId { get; private set; }

    public Guid? SectionUnitId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly AssignedOn { get; private set; }

    public DateOnly DueOn { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public string Status { get; private set; } = CapRemarkStatuses.Assigned;

    public string? RejectionReason { get; private set; }

    public string? PmoDecision { get; private set; }

    public string? PmoDecisionNote { get; private set; }

    public string? RootCauseMethod { get; private set; }

    public string RootCauseJson { get; private set; } = "{}";

    public string? RootCauseText { get; private set; }

    public string? LinkedType { get; private set; }

    public Guid? LinkedId { get; private set; }

    public string? ExternalKey { get; private set; }

    public int ActionSeq { get; private set; }

    public bool AllRejected { get; private set; }

    public int ReopenedCount { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateOnly? VerificationDueOn { get; private set; }

    public DateTimeOffset? VerificationNotifiedAt { get; private set; }

    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string Stage => RemarkStages.Of(Status);

    public bool IsOpen => !CapRemarkStatuses.IsFinal(Status);

    public bool HasRootCause => RootCauseMethod is not null
        && (!string.IsNullOrWhiteSpace(RootCauseText) || RootCause().Whys.Count > 0 || RootCause().Fishbone.Values.Any(v => v.Count > 0));

    public static Remark Create(string number, Guid sourceId, RemarkValues values, DateOnly assignedOn, DateOnly dueOn, DateTimeOffset dueAt, string? externalKey,
        string? linkedType, Guid? linkedId)
    {
        var remark = new Remark
        {
            Number = number, SourceId = sourceId, AssignedOn = assignedOn, ExternalKey = externalKey, LinkedType = linkedType, LinkedId = linkedId,
        };
        remark.Edit(values);
        remark.SetDue(dueOn, dueAt);
        return remark;
    }

    public void Edit(RemarkValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        TextAr = CapSource.Clean(v.TextAr);
        TextEn = CapSource.Clean(v.TextEn);
        CriterionCode = CapSource.Clean(v.CriterionCode);
        TypeCode = v.TypeCode;
        Priority = v.Priority;
        DepartmentUnitId = v.DepartmentUnitId;
        SectionUnitId = v.SectionUnitId;
        OwnerEmployeeId = v.OwnerEmployeeId;
    }

    public RemarkValues Values() => new(TextAr, TextEn, CriterionCode, TypeCode, Priority, DepartmentUnitId, SectionUnitId, OwnerEmployeeId);

    public void SetDue(DateOnly dueOn, DateTimeOffset dueAt)
    {
        if (dueOn != DueOn)
        {
            OverdueNotifiedAt = null;
        }

        DueOn = dueOn;
        DueAt = dueAt;
    }

    public void Reassign(Guid ownerEmployeeId) => OwnerEmployeeId = ownerEmployeeId;

    public int NextActionNo() => ++ActionSeq;

    public (List<string> Whys, Dictionary<string, List<string>> Fishbone) RootCause()
    {
        var doc = JsonSerializer.Deserialize<RootCauseDocument>(RootCauseJson, Json) ?? new RootCauseDocument();
        return (doc.Whys ?? [], doc.Fishbone ?? []);
    }

    public void SetRootCause(RootCauseValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        RootCauseMethod = v.Method;
        RootCauseText = CapSource.Clean(v.Text);
        var whys = (v.Whys ?? []).Select(w => w.Trim()).Where(w => w.Length > 0).ToList();
        var fishbone = (v.Fishbone ?? new Dictionary<string, IReadOnlyList<string>>())
            .Where(kv => RootCauseMethods.FishboneCategories.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Trim()).Where(c => c.Length > 0).ToList());
        RootCauseJson = JsonSerializer.Serialize(new RootCauseDocument { Whys = whys, Fishbone = fishbone }, Json);
    }

    public void MarkOverdueNotified(DateTimeOffset at) => OverdueNotifiedAt = at;

    public void MarkVerificationNotified(DateTimeOffset at) => VerificationNotifiedAt = at;

    /// <summary>Applies a lifecycle action (ADR 0121).</summary>
    public Result Apply(string action, DateTimeOffset at, string? reason = null, DateOnly? verificationDue = null)
    {
        if (RemarkLifecycle.Next(Status, action) is not { } next)
        {
            return Error.Rule("cap.invalidTransition", "cap.errors.invalidTransition", new Dictionary<string, object?> { ["status"] = Status, ["action"] = action });
        }

        switch (action)
        {
            case RemarkActions.Accept:
                RespondedAt = at;
                break;
            case RemarkActions.Reject:
            case RemarkActions.RejectForConfirmation:
                RespondedAt = at;
                RejectionReason = CapSource.Clean(reason);
                break;
            case RemarkActions.ConfirmRejection:
                PmoDecision = "confirmed";
                PmoDecisionNote = CapSource.Clean(reason);
                break;
            case RemarkActions.SendBack:
                PmoDecision = "sent-back";
                PmoDecisionNote = CapSource.Clean(reason);
                RespondedAt = null;
                break;
            case RemarkActions.Complete:
            case RemarkActions.Effective:
                CompletedAt = at;
                break;
            case RemarkActions.ToVerification:
                VerificationDueOn = verificationDue;
                VerificationNotifiedAt = null;
                break;
            case RemarkActions.NotEffective:
                ReopenedCount++;
                CompletedAt = null;
                VerificationDueOn = null;
                break;
            case RemarkActions.Cancel:
                CancelReason = CapSource.Clean(reason);
                break;
        }

        AllRejected = next == CapRemarkStatuses.Planning && action == RemarkActions.ToPlanning;
        Status = next;
        return Result.Success();
    }

    private sealed class RootCauseDocument
    {
        public List<string>? Whys { get; set; }

        public Dictionary<string, List<string>>? Fishbone { get; set; }
    }
}

public sealed record ActionValues(
    string? DetailsAr,
    string? DetailsEn,
    string? TypeCode,
    bool BudgetRequired,
    bool? BudgetAvailable,
    decimal? EstimatedCost,
    string? RelatedProcess,
    Guid? AssignedEmployeeId,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd);

/// <summary>A corrective action (SPEC §9.3 C). Serves one or more remarks (<see cref="RemarkAction"/>).</summary>
[Audited(EntityType)]
public sealed class CapAction : AuditableEntity
{
    public const string EntityType = CapLinkTypes.Action;
    public const string Currency = "AED";

    private CapAction()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid HomeRemarkId { get; private set; }

    public string? DetailsAr { get; private set; }

    public string? DetailsEn { get; private set; }

    public string? TypeCode { get; private set; }

    public bool BudgetRequired { get; private set; }

    public bool? BudgetAvailable { get; private set; }

    public decimal? EstimatedCost { get; private set; }

    public string CurrencyCode { get; private set; } = Currency;

    public string? RelatedProcess { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public DateOnly? PlannedStart { get; private set; }

    public DateOnly? PlannedEnd { get; private set; }

    public DateOnly? RevisedEnd { get; private set; }

    public string ValidationStatus { get; private set; } = ValidationStatuses.Draft;

    public string? ValidationNote { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? ProgramRequestId { get; private set; }

    public string? ProgramRequestNumber { get; private set; }

    public DateOnly? ActualStart { get; private set; }

    public DateOnly? ActualEnd { get; private set; }

    public decimal AchievedPct { get; private set; }

    public string Status { get; private set; } = ActionStatuses.NotStarted;

    public string? PendingProcess { get; private set; }

    public string? CompletionNote { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string? MonthlyRemindedFor { get; private set; }

    public DateTimeOffset? LateNotifiedAt { get; private set; }

    public DateTimeOffset? PmoEscalatedAt { get; private set; }

    public string? ExternalKey { get; private set; }

    public DateOnly? EffectiveEnd => RevisedEnd ?? PlannedEnd;

    public bool IsOpen => Status != ActionStatuses.Completed && Status != ActionStatuses.Cancelled;

    /// <summary>Editable by the owner while not submitted or returned.</summary>
    public bool IsEditable => ValidationStatus is ValidationStatuses.Draft or ValidationStatuses.Returned && Status != ActionStatuses.Cancelled;

    public static CapAction Create(string number, Guid homeRemarkId, ActionValues values, string? externalKey = null)
    {
        var action = new CapAction { Number = number, HomeRemarkId = homeRemarkId, ExternalKey = externalKey };
        action.Edit(values);
        return action;
    }

    public void Edit(ActionValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        DetailsAr = CapSource.Clean(v.DetailsAr);
        DetailsEn = CapSource.Clean(v.DetailsEn);
        TypeCode = CapSource.Clean(v.TypeCode);
        BudgetRequired = v.BudgetRequired;
        BudgetAvailable = v.BudgetRequired ? v.BudgetAvailable : null;
        EstimatedCost = v.EstimatedCost;
        RelatedProcess = CapSource.Clean(v.RelatedProcess);
        AssignedEmployeeId = v.AssignedEmployeeId;
        PlannedStart = v.PlannedStart;
        if (v.PlannedEnd != PlannedEnd)
        {
            LateNotifiedAt = null;
            PmoEscalatedAt = null;
        }

        PlannedEnd = v.PlannedEnd;
    }

    public ActionValues Values() => new(DetailsAr, DetailsEn, TypeCode, BudgetRequired, BudgetAvailable, EstimatedCost, RelatedProcess, AssignedEmployeeId, PlannedStart,
        PlannedEnd);

    public void SubmitForValidation()
    {
        ValidationStatus = ValidationStatuses.Pending;
        PendingProcess = CapProcesses.Validation;
    }

    /// <summary>PMO decision through the approval engine (ADR 0121).</summary>
    public void Validated(string status, string? note)
    {
        ValidationStatus = status;
        ValidationNote = CapSource.Clean(note);
        PendingProcess = null;
        if (status == ValidationStatuses.Approved)
        {
            Status = AchievedPct > 0 ? ActionStatuses.InProgress : ActionStatuses.NotStarted;
        }
    }

    public void ValidationCancelled()
    {
        ValidationStatus = ValidationStatuses.Draft;
        PendingProcess = null;
    }

    public void LinkProject(Guid? projectId) => ProjectId = projectId;

    public void LinkProgramRequest(Guid requestId, string number)
    {
        ProgramRequestId = requestId;
        ProgramRequestNumber = number;
    }

    /// <summary>Achievement update (C3.2): status follows the percentage; late is re-evaluated by the monitor.</summary>
    public void Progress(decimal pct, DateOnly? actualStart, DateOnly? actualEnd, DateOnly today)
    {
        AchievedPct = Math.Clamp(pct, 0m, 1m);
        ActualStart = actualStart ?? ActualStart ?? (AchievedPct > 0 ? today : null);
        ActualEnd = AchievedPct >= 1m ? actualEnd ?? ActualEnd ?? today : null;
        if (Status != ActionStatuses.Completed)
        {
            Status = IsLate(today) ? ActionStatuses.Late : AchievedPct > 0 ? ActionStatuses.InProgress : ActionStatuses.NotStarted;
        }
    }

    public bool IsLate(DateOnly today) => ValidationStatus == ValidationStatuses.Approved && Status != ActionStatuses.Completed && Status != ActionStatuses.Cancelled
        && AchievedPct < 1m && EffectiveEnd is { } end && end < today;

    /// <summary>Monitor: sets or clears <c>late</c>; true when it changed.</summary>
    public bool RefreshLate(DateOnly today)
    {
        if (ValidationStatus != ValidationStatuses.Approved || !IsOpen)
        {
            return false;
        }

        var target = IsLate(today) ? ActionStatuses.Late : AchievedPct > 0 ? ActionStatuses.InProgress : ActionStatuses.NotStarted;
        if (target == Status)
        {
            return false;
        }

        Status = target;
        if (target != ActionStatuses.Late)
        {
            LateNotifiedAt = null;
            PmoEscalatedAt = null;
        }

        return true;
    }

    public void SetPending(string? process) => PendingProcess = process;

    public void RequestCompletion(string? note)
    {
        CompletionNote = CapSource.Clean(note);
        PendingProcess = CapProcesses.Completion;
    }

    public void Complete(DateTimeOffset at, DateOnly today)
    {
        AchievedPct = 1m;
        ActualEnd ??= today;
        Status = ActionStatuses.Completed;
        CompletedAt = at;
        PendingProcess = null;
    }

    public void Extend(DateOnly newEnd)
    {
        RevisedEnd = newEnd;
        LateNotifiedAt = null;
        PmoEscalatedAt = null;
    }

    public void Cancel(string? reason)
    {
        Status = ActionStatuses.Cancelled;
        CancelReason = CapSource.Clean(reason);
        PendingProcess = null;
    }

    public void MarkMonthly(string month) => MonthlyRemindedFor = month;

    public void MarkLateNotified(DateTimeOffset at) => LateNotifiedAt = at;

    public void MarkPmoEscalated(DateTimeOffset at) => PmoEscalatedAt = at;
}

/// <summary>Remark ↔ action (many-to-many, SPEC §9.3 C).</summary>
public sealed class RemarkAction
{
    private RemarkAction()
    {
    }

    public Guid RemarkId { get; private set; }

    public Guid ActionId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public Guid? LinkedBy { get; private set; }

    public static RemarkAction Create(Guid remarkId, Guid actionId, DateTimeOffset at, Guid? by) =>
        new() { RemarkId = remarkId, ActionId = actionId, LinkedAt = at, LinkedBy = by };
}

/// <summary>An achievement update kept as history (C3.2).</summary>
[Audited(EntityType)]
public sealed class AchievementUpdate : AuditableEntity
{
    public const string EntityType = "cap.achievement-update";

    private AchievementUpdate()
    {
    }

    public Guid ActionId { get; private set; }

    public decimal Pct { get; private set; }

    public string? Note { get; private set; }

    public DateOnly? ActualStart { get; private set; }

    public DateOnly? ActualEnd { get; private set; }

    public DateTimeOffset At { get; private set; }

    public Guid? By { get; private set; }

    /// <summary>Written through MCP (ADR 0129); the assignee confirms it in the UI.</summary>
    public bool Draft { get; private set; }

    public static AchievementUpdate Create(Guid actionId, decimal pct, string? note, DateOnly? actualStart, DateOnly? actualEnd, DateTimeOffset at, Guid? by, bool draft = false) =>
        new() { ActionId = actionId, Pct = pct, Note = CapSource.Clean(note), ActualStart = actualStart, ActualEnd = actualEnd, At = at, By = by, Draft = draft };

    public void Confirm(DateTimeOffset at)
    {
        Draft = false;
        At = at;
    }
}

public static class ExtensionStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>An extension request (SPEC §9.3 E): original vs revised dates kept.</summary>
[Audited(EntityType)]
public sealed class ExtensionRequest : AuditableEntity
{
    public const string EntityType = "cap.extension-request";

    private ExtensionRequest()
    {
    }

    public Guid ActionId { get; private set; }

    public DateOnly? PreviousEnd { get; private set; }

    public DateOnly RequestedEnd { get; private set; }

    public string Justification { get; private set; } = string.Empty;

    public string Status { get; private set; } = ExtensionStatuses.Pending;

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionNote { get; private set; }

    public static ExtensionRequest Create(Guid actionId, DateOnly? previousEnd, DateOnly requestedEnd, string justification) =>
        new() { ActionId = actionId, PreviousEnd = previousEnd, RequestedEnd = requestedEnd, Justification = justification.Trim() };

    public void Decide(string status, DateTimeOffset at, string? note)
    {
        Status = status;
        DecidedAt = at;
        DecisionNote = CapSource.Clean(note);
    }
}

/// <summary>Effectiveness verification of a remark (SPEC §9.3 D, off by default).</summary>
[Audited(EntityType)]
public sealed class EffectivenessCheck : AuditableEntity
{
    public const string EntityType = "cap.effectiveness-check";

    private EffectivenessCheck()
    {
    }

    public Guid RemarkId { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public bool Effective { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset VerifiedAt { get; private set; }

    public Guid? VerifiedBy { get; private set; }

    public static EffectivenessCheck Create(Guid remarkId, DateOnly? dueOn, bool effective, string? comment, DateTimeOffset at, Guid? by) =>
        new() { RemarkId = remarkId, DueOn = dueOn, Effective = effective, Comment = CapSource.Clean(comment), VerifiedAt = at, VerifiedBy = by };
}

public static class SuggestionKinds
{
    public const string KpiMiss = "kpi-miss";
    public const string RiskEvent = "risk-event";
    public const string RiskControl = "risk-control";

    public static readonly IReadOnlyList<string> All = [KpiMiss, RiskEvent, RiskControl];
}

public static class SuggestionStatuses
{
    public const string Open = "open";
    public const string Raised = "raised";
    public const string Dismissed = "dismissed";
}

/// <summary>A remark another system's event suggests (KPI miss, risk event, ineffective control) — raised in one click or automatically (ADR 0123).</summary>
[Audited(EntityType)]
public sealed class Suggestion : AuditableEntity
{
    public const string EntityType = "cap.suggestion";

    private Suggestion()
    {
    }

    public string Kind { get; private set; } = SuggestionKinds.KpiMiss;

    /// <summary>The record the remark is about (KPI result, risk event, control).</summary>
    public string ResourceType { get; private set; } = string.Empty;

    public Guid ResourceId { get; private set; }

    /// <summary>The record the source is kept for (KPI card, risk).</summary>
    public string ParentType { get; private set; } = string.Empty;

    public Guid ParentId { get; private set; }

    public string? ParentNumber { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public Guid? UnitId { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public string Priority { get; private set; } = CapPriorities.Medium;

    public string Status { get; private set; } = SuggestionStatuses.Open;

    public Guid? RemarkId { get; private set; }

    public string? DismissReason { get; private set; }

    public static Suggestion Create(
        string kind, string resourceType, Guid resourceId, string parentType, Guid parentId, string? parentNumber, string? titleAr, string? titleEn, string detailsJson,
        Guid? unitId, Guid? ownerEmployeeId, string priority) => new()
        {
            Kind = kind, ResourceType = resourceType, ResourceId = resourceId, ParentType = parentType, ParentId = parentId, ParentNumber = parentNumber, TitleAr = titleAr,
            TitleEn = titleEn, DetailsJson = detailsJson, UnitId = unitId, OwnerEmployeeId = ownerEmployeeId, Priority = priority,
        };

    public void Raised(Guid remarkId)
    {
        Status = SuggestionStatuses.Raised;
        RemarkId = remarkId;
    }

    public void Dismiss(string reason)
    {
        Status = SuggestionStatuses.Dismissed;
        DismissReason = reason.Trim();
    }
}

/// <summary>Approval process keys (docs/design/cap.md §6).</summary>
public static class CapProcesses
{
    public const string Validation = "cap.action-validation";
    public const string Completion = "cap.completion";
    public const string Extension = "cap.extension";
}
