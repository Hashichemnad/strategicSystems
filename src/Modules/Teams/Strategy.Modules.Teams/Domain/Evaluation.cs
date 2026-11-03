using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Teams.Domain;

public static class EvaluationStatuses
{
    public const string Open = "open";
    public const string Submitted = "submitted";
    public const string Returned = "returned";
    public const string Approved = "approved";
    public const string Cancelled = "cancelled";

    /// <summary>The evaluator may still edit.</summary>
    public static bool IsEditable(string status) => status is Open or Returned;
}

public static class EvaluationActions
{
    public const string Save = "save";
    public const string Submit = "submit";
    public const string Return = "return";
    public const string Approve = "approve";
    public const string Cancel = "cancel";
}

/// <summary>Evaluation status table (unit-tested like the team lifecycle).</summary>
public static class EvaluationLifecycle
{
    public static readonly IReadOnlyList<(string From, string Action, string To)> Transitions =
    [
        (EvaluationStatuses.Open, EvaluationActions.Save, EvaluationStatuses.Open),
        (EvaluationStatuses.Returned, EvaluationActions.Save, EvaluationStatuses.Returned),
        (EvaluationStatuses.Open, EvaluationActions.Submit, EvaluationStatuses.Submitted),
        (EvaluationStatuses.Returned, EvaluationActions.Submit, EvaluationStatuses.Submitted),
        (EvaluationStatuses.Submitted, EvaluationActions.Return, EvaluationStatuses.Returned),
        (EvaluationStatuses.Submitted, EvaluationActions.Approve, EvaluationStatuses.Approved),
        (EvaluationStatuses.Open, EvaluationActions.Cancel, EvaluationStatuses.Cancelled),
        (EvaluationStatuses.Returned, EvaluationActions.Cancel, EvaluationStatuses.Cancelled),
    ];

    public static string? Next(string from, string action) =>
        Transitions.FirstOrDefault(t => t.From == from && t.Action == action) is { To: { } to } ? to : null;
}

public static class RewardStatuses
{
    public const string None = "none";
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
}

/// <summary>Annual cycles are the year ("2026"); completion cycles are "completion".</summary>
public static class Cycles
{
    public const string Completion = "completion";

    public static string Year(int year) => year.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The evaluator's choice for one criterion, with the system's suggestion and its basis.</summary>
public sealed record CriterionResult(
    string Code,
    string? Level,
    decimal? Score,
    string? Comment,
    string? SuggestedLevel,
    SuggestionBasis? Basis,
    string? OverrideJustification);

/// <summary>The data behind a suggested level: e.g. 12 of 13 closed = 92.31 %, or 30 h average over 6 meetings.</summary>
public sealed record SuggestionBasis(string Source, decimal Value, int Count, int Total);

public sealed record EvaluationFlag(string Code, Guid? MemberEvaluationId = null);

/// <summary>A team evaluation cycle (SPEC §4.4 D): its own record, history kept per cycle.</summary>
[Audited(EntityType)]
public sealed class TeamEvaluation : AuditableEntity
{
    public const string EntityType = "teams.team_evaluation";

    private TeamEvaluation()
    {
    }

    public string? Number { get; private set; }

    public Guid TeamId { get; private set; }

    public string Cycle { get; private set; } = string.Empty;

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public Guid TemplateId { get; private set; }

    public string Trigger { get; private set; } = EvaluationTriggers.Manual;

    public Guid? OpenedByUserId { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public string EvaluatorRule { get; private set; } = EvaluatorRules.Leader;

    public Guid? EvaluatorEmployeeId { get; private set; }

    public Guid? LeaderEvaluatorEmployeeId { get; private set; }

    public string Criteria { get; private set; } = "[]";

    public decimal? Score { get; private set; }

    public string Workload { get; private set; } = Workloads.Medium;

    public Guid[] EvidenceFileIds { get; private set; } = [];

    public string Status { get; private set; } = EvaluationStatuses.Open;

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedByEmployeeId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public string? ReturnComment { get; private set; }

    public string? CancelReason { get; private set; }

    public string RewardStatus { get; private set; } = RewardStatuses.None;

    public decimal? RewardTotal { get; private set; }

    public Guid? RewardMatrixId { get; private set; }

    public string Flags { get; private set; } = "[]";

    public DateTimeOffset? RemindedAt { get; private set; }

    public IReadOnlyList<CriterionResult> ReadCriteria() => JsonSerializer.Deserialize<List<CriterionResult>>(Criteria, TeamJson.Options) ?? [];

    public IReadOnlyList<EvaluationFlag> ReadFlags() => JsonSerializer.Deserialize<List<EvaluationFlag>>(Flags, TeamJson.Options) ?? [];

    public static TeamEvaluation Open(
        Guid teamId, string number, string cycle, DateOnly periodStart, DateOnly periodEnd, Guid templateId, string trigger, Guid? openedBy, DateTimeOffset at,
        string evaluatorRule, Guid? evaluator, Guid? leaderEvaluator, string workload, IReadOnlyList<CriterionResult> criteria) =>
        new()
        {
            TeamId = teamId,
            Number = number,
            Cycle = cycle,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TemplateId = templateId,
            Trigger = trigger,
            OpenedByUserId = openedBy,
            OpenedAt = at,
            EvaluatorRule = evaluatorRule,
            EvaluatorEmployeeId = evaluator,
            LeaderEvaluatorEmployeeId = leaderEvaluator,
            Workload = workload,
            Criteria = JsonSerializer.Serialize(criteria, TeamJson.Options),
        };

    public Result SaveDraft(IReadOnlyList<CriterionResult> criteria, decimal? score, string workload, IEnumerable<Guid> evidence, IReadOnlyList<EvaluationFlag> flags)
    {
        if (!EvaluationStatuses.IsEditable(Status))
        {
            return Error.Rule("teams.evaluationLocked", "teams.errors.evaluationLocked");
        }

        Criteria = JsonSerializer.Serialize(criteria, TeamJson.Options);
        Score = score;
        Workload = workload;
        EvidenceFileIds = [.. evidence.Distinct()];
        Flags = JsonSerializer.Serialize(flags, TeamJson.Options);
        return Result.Success();
    }

    public void RefreshSuggestions(IReadOnlyList<CriterionResult> criteria) => Criteria = JsonSerializer.Serialize(criteria, TeamJson.Options);

    public Result Apply(string action, DateTimeOffset at, Guid? by = null, string? comment = null)
    {
        var next = EvaluationLifecycle.Next(Status, action);
        if (next is null)
        {
            return Error.Rule("teams.invalidEvaluationTransition", "teams.errors.invalidTransition",
                new Dictionary<string, object?> { ["status"] = Status, ["action"] = action });
        }

        Status = next;
        switch (action)
        {
            case EvaluationActions.Submit:
                SubmittedAt = at;
                SubmittedByEmployeeId = by;
                break;
            case EvaluationActions.Return:
                ReturnComment = comment;
                break;
            case EvaluationActions.Approve:
                ApprovedAt = at;
                break;
            case EvaluationActions.Cancel:
                CancelReason = comment;
                break;
        }

        return Result.Success();
    }

    public void SetRewards(string status, decimal? total, Guid? matrixId)
    {
        RewardStatus = status;
        RewardTotal = total;
        RewardMatrixId = matrixId ?? RewardMatrixId;
    }

    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;
}

/// <summary>One member's evaluation in a cycle (SPEC §4.4 E).</summary>
[Audited(EntityType)]
public sealed class MemberEvaluation : AuditableEntity
{
    public const string EntityType = "teams.member_evaluation";

    private MemberEvaluation()
    {
    }

    public Guid EvaluationId { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid MemberId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string? ExternalName { get; private set; }

    public string RoleCode { get; private set; } = string.Empty;

    public bool IsLeader { get; private set; }

    public string Answers { get; private set; } = "{}";

    public string? Comment { get; private set; }

    public int? Points { get; private set; }

    public string? Grade { get; private set; }

    public decimal? AttendanceRate { get; private set; }

    public Guid? EvaluatedByEmployeeId { get; private set; }

    public DateTimeOffset? EvaluatedAt { get; private set; }

    public decimal? CalculatedReward { get; private set; }

    public decimal? ApprovedReward { get; private set; }

    public string? AdjustmentReason { get; private set; }

    public bool Certificate { get; private set; }

    public string? CertificateNumber { get; private set; }

    public Guid? CertificateFileId { get; private set; }

    public bool FinancialReward { get; private set; }

    public IReadOnlyDictionary<string, int> ReadAnswers() =>
        JsonSerializer.Deserialize<Dictionary<string, int>>(Answers, TeamJson.Options) ?? [];

    public static MemberEvaluation Create(Guid evaluationId, Guid teamId, TeamMember member, bool isLeader, decimal? attendanceRate) =>
        new()
        {
            EvaluationId = evaluationId,
            TeamId = teamId,
            MemberId = member.Id,
            EmployeeId = member.EmployeeId,
            ExternalName = member.ExternalName,
            RoleCode = member.RoleCode,
            IsLeader = isLeader,
            AttendanceRate = attendanceRate,
        };

    public void Record(IReadOnlyDictionary<string, int> answers, string? comment, int? points, string? grade, Guid? by, DateTimeOffset at)
    {
        Answers = JsonSerializer.Serialize(answers, TeamJson.Options);
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Points = points;
        Grade = grade;
        EvaluatedByEmployeeId = by;
        EvaluatedAt = at;
    }

    public void SetAttendance(decimal? rate) => AttendanceRate = rate;

    public void SetCalculatedReward(decimal amount, bool certificate)
    {
        CalculatedReward = amount;
        ApprovedReward = amount;
        AdjustmentReason = null;
        Certificate = certificate;
        FinancialReward = amount > 0;
    }

    public void Adjust(decimal approved, string? reason, bool certificate, bool financial)
    {
        ApprovedReward = approved;
        AdjustmentReason = approved == CalculatedReward ? null : reason?.Trim();
        Certificate = certificate;
        FinancialReward = financial;
    }

    public void SetCertificate(string number, Guid? fileId)
    {
        CertificateNumber = number;
        CertificateFileId = fileId ?? CertificateFileId;
    }
}
