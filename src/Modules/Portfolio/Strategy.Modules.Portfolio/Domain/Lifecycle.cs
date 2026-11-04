using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Domain;

/// <summary>
/// The stage template of a classification (Q-P2 / Q-P3, ADR 0070): which optional steps run. Snapshotted on the project when its lifecycle starts.
/// </summary>
public sealed record StageTemplate(bool Charter, bool Lpo, bool TeamFormation, bool Closure, bool Impact)
{
    public static readonly StageTemplate ProjectDefault = new(true, true, true, true, true);

    public static readonly StageTemplate InitiativeDefault = new(false, false, false, false, false);

    /// <summary>Stepper path: [initiation] planning execution [closure] [impact] completed.</summary>
    public IReadOnlyList<string> Path()
    {
        var path = new List<string>();
        if (Charter)
        {
            path.Add(ProjectStages.Initiation);
        }

        path.Add(ProjectStages.Planning);
        path.Add(ProjectStages.Execution);
        if (Closure)
        {
            path.Add(ProjectStages.Closure);
        }

        if (Closure && Impact)
        {
            path.Add(ProjectStages.Impact);
        }

        path.Add(ProjectStages.Completed);
        return path;
    }

    /// <summary>LPO and team formation need the charter; Impact needs the closure.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if ((Lpo || TeamFormation) && !Charter)
        {
            problems.Add("portfolio.errors.templateNeedsCharter");
        }

        if (Impact && !Closure)
        {
            problems.Add("portfolio.errors.templateImpactNeedsClosure");
        }

        return problems;
    }
}

/// <summary>Where a project is inside its stage (docs/design/portfolio.md §16).</summary>
public static class SubStates
{
    public const string Ownership = "ownership";
    public const string CharterDraft = "charter-draft";
    public const string CharterApproval = "charter-approval";
    public const string Lpo = "lpo";
    public const string TeamFormation = "team-formation";
    public const string PlanningDraft = "planning-draft";
    public const string PlanningApproval = "planning-approval";
    public const string Execution = "execution";
    public const string Completion = "completion";
    public const string ClosureDraft = "closure-draft";
    public const string ClosureApproval = "closure-approval";
    public const string ImpactDraft = "impact-draft";
    public const string ImpactApproval = "impact-approval";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All =
    [
        Ownership, CharterDraft, CharterApproval, Lpo, TeamFormation, PlanningDraft, PlanningApproval, Execution, Completion, ClosureDraft, ClosureApproval,
        ImpactDraft, ImpactApproval, Done,
    ];

    public static bool IsApproval(string subState) => subState is CharterApproval or PlanningApproval or ClosureApproval or ImpactApproval;
}

/// <summary>Lifecycle actions; approval outcomes arrive from the engine's hooks.</summary>
public static class ProjectActions
{
    public const string TakeOwnership = "take-ownership";
    public const string SubmitCharter = "submit-charter";
    public const string CharterApproved = "charter-approved";
    public const string LpoUploaded = "lpo-uploaded";
    public const string TeamFormed = "team-formed";
    public const string SubmitPlanning = "submit-planning";
    public const string PlanningApproved = "planning-approved";
    public const string AchievementComplete = "achievement-complete";
    public const string ConfirmCompletion = "confirm-completion";
    public const string SubmitClosure = "submit-closure";
    public const string ClosureApproved = "closure-approved";
    public const string SubmitImpact = "submit-impact";
    public const string ImpactApproved = "impact-approved";

    /// <summary>Returned or rejected: back to the stage's draft.</summary>
    public const string ApprovalReturned = "approval-returned";

    public static readonly IReadOnlyList<string> All =
    [
        TakeOwnership, SubmitCharter, CharterApproved, LpoUploaded, TeamFormed, SubmitPlanning, PlanningApproved, AchievementComplete, ConfirmCompletion,
        SubmitClosure, ClosureApproved, SubmitImpact, ImpactApproved, ApprovalReturned,
    ];
}

/// <summary>Status-level actions (W8).</summary>
public static class ProjectStatusActions
{
    public const string Hold = "hold";
    public const string Resume = "resume";
    public const string Cancel = "cancel";
    public const string Complete = "complete";

    public static readonly IReadOnlyList<string> All = [Hold, Resume, Cancel, Complete];
}

/// <summary>
/// The project / initiative state machine (docs/design/portfolio.md §16, ADR 0070). Targets starting with <c>@</c> depend on the template.
/// </summary>
public static class ProjectLifecycle
{
    private const string AfterOwnership = "@after-ownership";
    private const string AfterCharter = "@after-charter";
    private const string AfterLpo = "@after-lpo";
    private const string AfterExecution = "@after-execution";
    private const string AfterClosure = "@after-closure";
    private const string BackToDraft = "@draft";

    private static readonly Dictionary<(string SubState, string Action), string> Transitions = new()
    {
        [(SubStates.Ownership, ProjectActions.TakeOwnership)] = AfterOwnership,
        [(SubStates.CharterDraft, ProjectActions.SubmitCharter)] = SubStates.CharterApproval,
        [(SubStates.CharterApproval, ProjectActions.CharterApproved)] = AfterCharter,
        [(SubStates.CharterApproval, ProjectActions.ApprovalReturned)] = BackToDraft,
        [(SubStates.Lpo, ProjectActions.LpoUploaded)] = AfterLpo,
        [(SubStates.TeamFormation, ProjectActions.TeamFormed)] = SubStates.PlanningDraft,
        [(SubStates.PlanningDraft, ProjectActions.SubmitPlanning)] = SubStates.PlanningApproval,
        [(SubStates.PlanningApproval, ProjectActions.PlanningApproved)] = SubStates.Execution,
        [(SubStates.PlanningApproval, ProjectActions.ApprovalReturned)] = BackToDraft,
        [(SubStates.Execution, ProjectActions.AchievementComplete)] = AfterExecution,
        [(SubStates.Completion, ProjectActions.ConfirmCompletion)] = SubStates.Done,
        [(SubStates.ClosureDraft, ProjectActions.SubmitClosure)] = SubStates.ClosureApproval,
        [(SubStates.ClosureApproval, ProjectActions.ClosureApproved)] = AfterClosure,
        [(SubStates.ClosureApproval, ProjectActions.ApprovalReturned)] = BackToDraft,
        [(SubStates.ImpactDraft, ProjectActions.SubmitImpact)] = SubStates.ImpactApproval,
        [(SubStates.ImpactApproval, ProjectActions.ImpactApproved)] = SubStates.Done,
        [(SubStates.ImpactApproval, ProjectActions.ApprovalReturned)] = BackToDraft,
    };

    private static readonly Dictionary<(string Status, string Action), string> StatusTransitions = new()
    {
        [(ProjectStatuses.Active, ProjectStatusActions.Hold)] = ProjectStatuses.OnHold,
        [(ProjectStatuses.OnHold, ProjectStatusActions.Resume)] = ProjectStatuses.Active,
        [(ProjectStatuses.Active, ProjectStatusActions.Cancel)] = ProjectStatuses.Cancelled,
        [(ProjectStatuses.OnHold, ProjectStatusActions.Cancel)] = ProjectStatuses.Cancelled,
        [(ProjectStatuses.Active, ProjectStatusActions.Complete)] = ProjectStatuses.Completed,
    };

    public static bool Allows(string subState, string action) => Transitions.ContainsKey((subState, action));

    /// <summary>The next sub-state; lifecycle actions need an active project.</summary>
    public static Result<string> Next(string status, string subState, string action, StageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (status != ProjectStatuses.Active)
        {
            return NotAllowed(status, subState, action);
        }

        if (!Transitions.TryGetValue((subState, action), out var target))
        {
            return NotAllowed(status, subState, action);
        }

        return target switch
        {
            AfterOwnership => template.Charter ? SubStates.CharterDraft : SubStates.PlanningDraft,
            AfterCharter => template.Lpo ? SubStates.Lpo : template.TeamFormation ? SubStates.TeamFormation : SubStates.PlanningDraft,
            AfterLpo => template.TeamFormation ? SubStates.TeamFormation : SubStates.PlanningDraft,
            AfterExecution => template.Closure ? SubStates.ClosureDraft : SubStates.Completion,
            AfterClosure => template.Impact ? SubStates.ImpactDraft : SubStates.Done,
            BackToDraft => DraftOf(subState),
            _ => target,
        };
    }

    public static Result<string> NextStatus(string status, string action) =>
        StatusTransitions.TryGetValue((status, action), out var next)
            ? next
            : Error.Rule("portfolio.project.status", "portfolio.errors.notAllowedInStatus", new Dictionary<string, object?> { ["status"] = status, ["action"] = action });

    /// <summary>The stage a sub-state belongs to.</summary>
    public static string StageOf(string subState, StageTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return subState switch
        {
            SubStates.Ownership => template.Charter ? ProjectStages.Initiation : ProjectStages.Planning,
            SubStates.CharterDraft or SubStates.CharterApproval or SubStates.Lpo or SubStates.TeamFormation => ProjectStages.Initiation,
            SubStates.PlanningDraft or SubStates.PlanningApproval => ProjectStages.Planning,
            SubStates.Execution or SubStates.Completion => ProjectStages.Execution,
            SubStates.ClosureDraft or SubStates.ClosureApproval => ProjectStages.Closure,
            SubStates.ImpactDraft or SubStates.ImpactApproval => ProjectStages.Impact,
            _ => ProjectStages.Completed,
        };
    }

    /// <summary>The draft a returned / rejected approval goes back to.</summary>
    public static string DraftOf(string approvalSubState) => approvalSubState switch
    {
        SubStates.CharterApproval => SubStates.CharterDraft,
        SubStates.PlanningApproval => SubStates.PlanningDraft,
        SubStates.ClosureApproval => SubStates.ClosureDraft,
        SubStates.ImpactApproval => SubStates.ImpactDraft,
        _ => approvalSubState,
    };

    /// <summary>KPIs, risks, stakeholders, members and planning items can be edited (not while a stage approval runs, never after the end).</summary>
    public static bool CanEditContent(string status, string subState) =>
        status == ProjectStatuses.Active && subState is not (SubStates.Ownership or SubStates.Done) && !SubStates.IsApproval(subState);

    /// <summary>The charter (details, high-level plan) is edited while drafting the charter or planning.</summary>
    public static bool CanEditCharter(string status, string subState) =>
        status == ProjectStatuses.Active && subState is SubStates.CharterDraft or SubStates.PlanningDraft;

    /// <summary>Milestone structure (create, weights, dates) is planned before execution; execution changes come from monthly updates.</summary>
    public static bool CanPlanMilestones(string status, string subState) =>
        status == ProjectStatuses.Active && subState is SubStates.CharterDraft or SubStates.PlanningDraft;

    private static Error NotAllowed(string status, string subState, string action) =>
        Error.Rule("portfolio.project.transition", "portfolio.errors.notAllowedInStage",
            new Dictionary<string, object?> { ["status"] = status, ["stage"] = subState, ["action"] = action });
}

/// <summary>Counts the charter and planning gates need (docs/design/portfolio.md §17).</summary>
public sealed record GateFacts(
    bool HasPm,
    bool HasScope,
    int ActiveKpis,
    int ActiveRisks,
    int ActiveStakeholders,
    int ProposedMembers,
    int HighLevelMilestones,
    decimal HighLevelWeight,
    int InScopeItems,
    int DetailedLeaves,
    decimal DetailedWeight,
    int LeavesWithBadDates,
    int DeliverablesIncomplete);

/// <summary>A gate item as the record page shows it (live checklist).</summary>
public sealed record GateItem(string Code, string MessageKey, bool Met, IReadOnlyDictionary<string, object?>? Params = null);

public static class ProjectGates
{
    public const int MinimumMembers = 3;

    public static IReadOnlyList<GateItem> Charter(GateFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return
        [
            new("pm", "portfolio.errors.gatePm", f.HasPm),
            new("scope", "portfolio.errors.gateScopeText", f.HasScope),
            new("kpis", "portfolio.errors.gateKpi", f.ActiveKpis >= 1),
            new("risks", "portfolio.errors.gateRisk", f.ActiveRisks >= 1),
            new("stakeholders", "portfolio.errors.gateStakeholder", f.ActiveStakeholders >= 1),
            new("members", "portfolio.errors.gateMembers", f.ProposedMembers >= MinimumMembers,
                new Dictionary<string, object?> { ["count"] = f.ProposedMembers, ["min"] = MinimumMembers }),
            new("high-level-plan", "portfolio.errors.gateHighLevelPlan", f.HighLevelMilestones >= 1 && f.HighLevelWeight == 1m,
                new Dictionary<string, object?> { ["total"] = Percent(f.HighLevelWeight) }),
        ];
    }

    public static IReadOnlyList<GateItem> Planning(GateFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return
        [
            new("pm", "portfolio.errors.gatePm", f.HasPm),
            new("scope-items", "portfolio.errors.gateScope", f.InScopeItems >= 1),
            new("weights", "portfolio.errors.weightsNot100", f.DetailedLeaves >= 1 && f.DetailedWeight == 1m,
                new Dictionary<string, object?> { ["total"] = Percent(f.DetailedWeight) }),
            new("dates", "portfolio.errors.milestoneDates", f.LeavesWithBadDates == 0),
            new("deliverables", "portfolio.errors.deliverableFields", f.DeliverablesIncomplete == 0),
        ];
    }

    /// <summary>The failed items as a validation error (all of them together).</summary>
    public static Error? Failure(IReadOnlyList<GateItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var failed = items.Where(i => !i.Met).ToList();
        return failed.Count == 0
            ? null
            : Error.Validation(failed.Select(i => new FieldError($"gate.{i.Code}", i.MessageKey, i.MessageKey, i.Params)).ToList());
    }

    public static decimal Percent(decimal ratio) => decimal.Round(ratio * 100m, 2);
}

/// <summary>A detailed leaf milestone as the progress calculators see it.</summary>
public sealed record MilestoneProgressInput(decimal Weight, decimal Approved, DateOnly? PlannedStart, DateOnly? PlannedEnd, bool Cancelled = false);

/// <summary>Project progress (docs/design/portfolio.md §20, Q-P6, ADR 0071). Ratios: 1.0 = 100 %.</summary>
public static class ProgressCalculator
{
    public const string OnTrack = "on-track";
    public const string Delayed = "delayed";

    public const decimal DefaultTolerance = 0.05m;

    /// <summary>Σ approved achievement of the active leaves, capped at 100 %.</summary>
    public static decimal Performance(IEnumerable<MilestoneProgressInput> leaves) =>
        Math.Min(1m, decimal.Round(leaves.Where(l => !l.Cancelled).Sum(l => l.Approved), 4));

    /// <summary>Linear planned progress at <paramref name="date"/>: weight × elapsed share of each leaf's planned dates.</summary>
    public static decimal PlannedToDate(IEnumerable<MilestoneProgressInput> leaves, DateOnly date)
    {
        decimal total = 0;
        foreach (var leaf in leaves.Where(l => !l.Cancelled))
        {
            if (leaf.PlannedStart is not { } start || leaf.PlannedEnd is not { } end)
            {
                continue;
            }

            if (date < start)
            {
                continue;
            }

            if (date >= end)
            {
                total += leaf.Weight;
                continue;
            }

            var duration = end.DayNumber - start.DayNumber + 1;
            var elapsed = date.DayNumber - start.DayNumber + 1;
            total += leaf.Weight * elapsed / duration;
        }

        return Math.Min(1m, decimal.Round(total, 4));
    }

    /// <summary>Delayed when more than the tolerance below plan, or any active leaf is past its planned end below 100 %.</summary>
    public static string Status(IReadOnlyCollection<MilestoneProgressInput> leaves, DateOnly today, decimal tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        var performance = Performance(leaves);
        var planned = PlannedToDate(leaves, today);
        if (performance < planned - tolerance)
        {
            return Delayed;
        }

        return leaves.Any(l => !l.Cancelled && l.PlannedEnd is { } end && today > end && l.Approved < l.Weight) ? Delayed : OnTrack;
    }

    /// <summary>Achieve ÷ weight (0 when the weight is 0).</summary>
    public static decimal CompletionRate(decimal achieve, decimal weight) => weight <= 0 ? 0 : decimal.Round(achieve / weight, 4);
}

/// <summary>A milestone progress update as the PM enters it in the monthly window (ADR 0071).</summary>
public sealed record MilestoneProgressValues(
    decimal Achieve, DateOnly? ActualStart, DateOnly? ActualEnd, string? Causality, string? CorrectiveActions, string? EvidenceNote);

/// <summary>Monthly update rules for one milestone (SPEC §1.4 C, W4.2). All failures together.</summary>
public static class MilestoneRules
{
    public const int TextMaxLength = 4000;

    public static List<FieldError> ValidateProgress(
        MilestoneProgressValues values, decimal weight, decimal maxAchieved, DateOnly? plannedEnd, DateOnly today, int evidenceFiles)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new List<FieldError>();
        void Add(string field, string key, IReadOnlyDictionary<string, object?>? parameters = null) => errors.Add(new FieldError(field, key, key, parameters));

        if (values.Achieve < 0 || values.Achieve > weight)
        {
            Add("achieve", "portfolio.errors.achieveAboveWeight", new Dictionary<string, object?> { ["weight"] = ProjectGates.Percent(weight) });
        }

        if (values.Achieve < maxAchieved)
        {
            Add("achieve", "portfolio.errors.achieveBelowApproved", new Dictionary<string, object?> { ["approved"] = ProjectGates.Percent(maxAchieved) });
        }

        if (decimal.Round(values.Achieve, 4) != values.Achieve)
        {
            Add("achieve", "validation.precisionScale");
        }

        if (values.Achieve > 0 && values.ActualStart is null)
        {
            Add("actualStart", "portfolio.errors.actualStartRequired");
        }

        var complete = weight > 0 && values.Achieve == weight;
        if (complete && values.ActualEnd is null)
        {
            Add("actualEnd", "portfolio.errors.actualEndRequired");
        }

        if (complete && evidenceFiles == 0)
        {
            Add("evidence", "portfolio.errors.evidenceRequired");
        }

        if (values.ActualStart is { } s && values.ActualEnd is { } e && e < s)
        {
            Add("actualEnd", "portfolio.errors.endBeforeStart");
        }

        if (values.ActualEnd is { } end && end > today)
        {
            Add("actualEnd", "portfolio.errors.actualEndInFuture");
        }

        if (IsDelayed(values.Achieve, weight, plannedEnd, today))
        {
            if (string.IsNullOrWhiteSpace(values.Causality))
            {
                Add("causality", "portfolio.errors.causalityRequired");
            }

            if (string.IsNullOrWhiteSpace(values.CorrectiveActions))
            {
                Add("correctiveActions", "portfolio.errors.correctiveActionsRequired");
            }
        }

        foreach (var (field, text) in new[] { ("causality", values.Causality), ("correctiveActions", values.CorrectiveActions), ("evidenceNote", values.EvidenceNote) })
        {
            if (text is { Length: > TextMaxLength })
            {
                Add(field, "validation.maximumLength", new Dictionary<string, object?> { ["max"] = TextMaxLength });
            }
        }

        return errors;
    }

    /// <summary>Past its planned end and below 100 %.</summary>
    public static bool IsDelayed(decimal achieve, decimal weight, DateOnly? plannedEnd, DateOnly today) =>
        plannedEnd is { } end && today > end && achieve < weight;
}

/// <summary>Inherent / residual score and band of a project risk (ADR 0074). Phase 13 replaces it with the shared risk framework.</summary>
public sealed record RiskScore(int Inherent, string InherentBand, decimal Residual, string ResidualBand);

public static class RiskBands
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";

    public static readonly IReadOnlyList<string> All = [Low, Medium, High, Critical];
}

/// <summary>Default project risk scoring: 5 × 5 likelihood × impact, control evaluation 1–10 reduces the residual.</summary>
public static class DefaultRiskScoring
{
    public static RiskScore Score(int probability, int impact, int? controlEvaluation)
    {
        var inherent = Math.Clamp(probability, 1, 5) * Math.Clamp(impact, 1, 5);
        var residual = controlEvaluation is { } control ? decimal.Round(inherent * (11 - Math.Clamp(control, 1, 10)) / 10m, 2) : inherent;
        return new RiskScore(inherent, Band(inherent), residual, Band(residual));
    }

    /// <summary>Low 1–4 · Medium 5–9 · High 10–16 · Critical 17–25 (fractions round up to the next band only above the boundary).</summary>
    public static string Band(decimal score) => score switch
    {
        > 16 => RiskBands.Critical,
        > 9 => RiskBands.High,
        > 4 => RiskBands.Medium,
        _ => RiskBands.Low,
    };
}
