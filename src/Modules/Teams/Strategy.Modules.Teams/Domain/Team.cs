using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Teams.Domain;

public static class TeamStatuses
{
    public const string Draft = "draft";
    public const string PendingApproval = "pending_approval";
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string PendingEvaluation = "pending_evaluation";
    public const string EvaluationWaitingApproval = "evaluation_waiting_approval";
    public const string Closed = "closed";
    public const string Replaced = "replaced";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All =
        [Draft, PendingApproval, Active, Inactive, PendingEvaluation, EvaluationWaitingApproval, Closed, Replaced, Cancelled];

    /// <summary>The team exists and works (formed, not finished).</summary>
    public static bool IsOperating(string status) => status is Active or Inactive or PendingEvaluation or EvaluationWaitingApproval;

    public static bool IsFinished(string status) => status is Closed or Replaced or Cancelled;
}

public static class TeamActions
{
    public const string Submit = "submit";
    public const string Activate = "activate";
    public const string FormationApproved = "formation-approved";
    public const string FormationReturned = "formation-returned";
    public const string FormationRejected = "formation-rejected";
    public const string Withdraw = "withdraw";
    public const string Cancel = "cancel";
    public const string Deactivate = "deactivate";
    public const string Reactivate = "reactivate";
    public const string OpenEvaluation = "open-evaluation";
    public const string EvaluationSubmitted = "evaluation-submitted";
    public const string EvaluationReturned = "evaluation-returned";
    public const string EvaluationApprovedContinue = "evaluation-approved-continue";
    public const string EvaluationApprovedClose = "evaluation-approved-close";
    public const string EvaluationCancelled = "evaluation-cancelled";
    public const string Close = "close";
    public const string Replace = "replace";
}

/// <summary>The team lifecycle (docs/design/teams.md §4, ADR 0046) as an explicit transition table.</summary>
public static class TeamLifecycle
{
    public static readonly IReadOnlyList<(string From, string Action, string To)> Transitions =
    [
        (TeamStatuses.Draft, TeamActions.Submit, TeamStatuses.PendingApproval),
        (TeamStatuses.Draft, TeamActions.Activate, TeamStatuses.Active),
        (TeamStatuses.Draft, TeamActions.Cancel, TeamStatuses.Cancelled),
        (TeamStatuses.PendingApproval, TeamActions.FormationApproved, TeamStatuses.Active),
        (TeamStatuses.PendingApproval, TeamActions.FormationReturned, TeamStatuses.Draft),
        (TeamStatuses.PendingApproval, TeamActions.FormationRejected, TeamStatuses.Cancelled),
        (TeamStatuses.PendingApproval, TeamActions.Withdraw, TeamStatuses.Cancelled),
        (TeamStatuses.Active, TeamActions.Deactivate, TeamStatuses.Inactive),
        (TeamStatuses.Inactive, TeamActions.Reactivate, TeamStatuses.Active),
        (TeamStatuses.Active, TeamActions.OpenEvaluation, TeamStatuses.PendingEvaluation),
        (TeamStatuses.Inactive, TeamActions.OpenEvaluation, TeamStatuses.PendingEvaluation),
        (TeamStatuses.PendingEvaluation, TeamActions.EvaluationSubmitted, TeamStatuses.EvaluationWaitingApproval),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationReturned, TeamStatuses.PendingEvaluation),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationApprovedContinue, TeamStatuses.Active),
        (TeamStatuses.EvaluationWaitingApproval, TeamActions.EvaluationApprovedClose, TeamStatuses.Closed),
        (TeamStatuses.PendingEvaluation, TeamActions.EvaluationCancelled, TeamStatuses.Active),
        (TeamStatuses.Active, TeamActions.Close, TeamStatuses.Closed),
        (TeamStatuses.Inactive, TeamActions.Close, TeamStatuses.Closed),
        (TeamStatuses.Active, TeamActions.Replace, TeamStatuses.Replaced),
        (TeamStatuses.Inactive, TeamActions.Replace, TeamStatuses.Replaced),
        (TeamStatuses.PendingEvaluation, TeamActions.Replace, TeamStatuses.Replaced),
    ];

    public static string? Next(string from, string action) =>
        Transitions.FirstOrDefault(t => t.From == from && t.Action == action) is { To: { } to } ? to : null;

    public static IReadOnlyList<string> ActionsFrom(string from) => Transitions.Where(t => t.From == from).Select(t => t.Action).ToList();
}

public static class MemoModes
{
    public const string Generate = "generate";
    public const string External = "external";

    public static readonly IReadOnlyList<string> All = [Generate, External];
}

/// <summary>A team / committee (SPEC §4.4 A).</summary>
[Audited(EntityType)]
public sealed class Team : AuditableEntity
{
    public const string EntityType = "teams.team";
    public const int NameMaxLength = 300;
    public const int TextMaxLength = 8000;

    private Team()
    {
    }

    public string? Number { get; private set; }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    public bool Permanent { get; private set; }

    public string? Subject { get; private set; }

    public string? Objectives { get; private set; }

    public string? Mandate { get; private set; }

    public Guid? OwningUnitId { get; private set; }

    public Guid? SponsorEmployeeId { get; private set; }

    public Guid? RequestedByEmployeeId { get; private set; }

    public Guid? RequestedByUserId { get; private set; }

    public Guid? LeaderEmployeeId { get; private set; }

    public string? ApprovedByCode { get; private set; }

    public string MemoMode { get; private set; } = MemoModes.Generate;

    public string? MemoNumber { get; private set; }

    public DateOnly? MemoDate { get; private set; }

    public Guid? MemoFileId { get; private set; }

    public string? MemoDraft { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public string Cadence { get; private set; } = Cadences.Quarterly;

    public string? ReportTo { get; private set; }

    public string Workload { get; private set; } = Workloads.Medium;

    public string Status { get; private set; } = TeamStatuses.Draft;

    public string? StatusReason { get; private set; }

    public Guid? ReplacesTeamId { get; private set; }

    public Guid? ReplacedByTeamId { get; private set; }

    public string? EvaluatorRule { get; private set; }

    public Guid? EvaluatorEmployeeId { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>A closure was requested (or the linked record closed) while the completion evaluation runs: close after approval.</summary>
    public bool CloseAfterEvaluation { get; private set; }

    public static Team Create(string typeCode, bool permanent, Guid? requestedByEmployeeId, Guid? requestedByUserId) =>
        new()
        {
            TypeCode = typeCode,
            Permanent = permanent,
            RequestedByEmployeeId = requestedByEmployeeId,
            RequestedByUserId = requestedByUserId,
        };

    public void Edit(TeamValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = Clean(v.NameAr);
        NameEn = Clean(v.NameEn);
        Permanent = v.Permanent;
        Subject = Clean(v.Subject);
        Objectives = Clean(v.Objectives);
        Mandate = Clean(v.Mandate);
        OwningUnitId = v.OwningUnitId;
        SponsorEmployeeId = v.SponsorEmployeeId;
        ApprovedByCode = Clean(v.ApprovedByCode);
        MemoMode = v.MemoMode;
        MemoDraft = Clean(v.MemoDraft);
        StartDate = v.StartDate;
        EndDate = v.Permanent ? null : v.EndDate;
        Cadence = v.Cadence;
        ReportTo = Clean(v.ReportTo);
        Workload = v.Workload;
        ReplacesTeamId = v.ReplacesTeamId;
        EvaluatorRule = Clean(v.EvaluatorRule);
        EvaluatorEmployeeId = v.EvaluatorRule == EvaluatorRules.Employee ? v.EvaluatorEmployeeId : null;
    }

    public void AssignNumber(string number) => Number ??= number;

    public void SetLeader(Guid? employeeId) => LeaderEmployeeId = employeeId;

    public Result Apply(string action, string? reason = null, DateTimeOffset? at = null)
    {
        var next = TeamLifecycle.Next(Status, action);
        if (next is null)
        {
            return Error.Rule("teams.invalidTransition", "teams.errors.invalidTransition",
                new Dictionary<string, object?> { ["status"] = Status, ["action"] = action });
        }

        Status = next;
        if (reason is not null)
        {
            StatusReason = reason.Trim();
        }

        if (next == TeamStatuses.Active && ActivatedAt is null)
        {
            ActivatedAt = at;
        }

        if (next is TeamStatuses.Closed or TeamStatuses.Replaced or TeamStatuses.Cancelled)
        {
            ClosedAt = at;
            CloseAfterEvaluation = false;
        }

        return Result.Success();
    }

    public void RequestCloseAfterEvaluation() => CloseAfterEvaluation = true;

    public void SetMemo(string? number, DateOnly? date, Guid? fileId)
    {
        MemoNumber = number ?? MemoNumber;
        MemoDate = date ?? MemoDate;
        MemoFileId = fileId ?? MemoFileId;
    }

    public void UseExternalMemo(string number, DateOnly date, Guid fileId)
    {
        MemoMode = MemoModes.External;
        MemoNumber = number.Trim();
        MemoDate = date;
        MemoFileId = fileId;
    }

    public void MarkReplacedBy(Guid successorId) => ReplacedByTeamId = successorId;

    public void SetWorkload(string workload) => Workload = workload;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TeamValues(
    string? NameAr,
    string? NameEn,
    bool Permanent,
    string? Subject,
    string? Objectives,
    string? Mandate,
    Guid? OwningUnitId,
    Guid? SponsorEmployeeId,
    string? ApprovedByCode,
    string MemoMode,
    string? MemoDraft,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Cadence,
    string? ReportTo,
    string Workload,
    Guid? ReplacesTeamId,
    string? EvaluatorRule,
    Guid? EvaluatorEmployeeId);

public static class MemberStatuses
{
    public const string Active = "active";
    public const string Left = "left";
    public const string Replaced = "replaced";
}

/// <summary>A membership (SPEC §4.4 B). Never deleted: leave / replace end it and keep the history.</summary>
[Audited(EntityType)]
[AuditParent(Team.EntityType, nameof(TeamId))]
public sealed class TeamMember : AuditableEntity
{
    public const string EntityType = "teams.team_member";

    private TeamMember()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid? EmployeeId { get; private set; }

    public string? ExternalName { get; private set; }

    public string? ExternalOrganisation { get; private set; }

    public string? ExternalEmail { get; private set; }

    public string? ExternalPhone { get; private set; }

    public string RoleCode { get; private set; } = string.Empty;

    public string? Responsibilities { get; private set; }

    public string? RequiredSkills { get; private set; }

    public decimal? AllocationPercent { get; private set; }

    public int Position { get; private set; }

    public DateOnly? FromDate { get; private set; }

    public DateOnly? ToDate { get; private set; }

    public string Status { get; private set; } = MemberStatuses.Active;

    public Guid? ReplacedByMemberId { get; private set; }

    public string? LeaveReason { get; private set; }

    public bool IsExternal => EmployeeId is null;

    public bool IsActive => Status == MemberStatuses.Active;

    public static TeamMember Create(Guid teamId, MemberValues v, int position)
    {
        ArgumentNullException.ThrowIfNull(v);
        var member = new TeamMember { TeamId = teamId, Position = position };
        member.EmployeeId = v.EmployeeId;
        if (v.EmployeeId is null)
        {
            member.ExternalName = v.ExternalName?.Trim();
            member.ExternalOrganisation = v.ExternalOrganisation?.Trim();
            member.ExternalEmail = v.ExternalEmail?.Trim();
            member.ExternalPhone = v.ExternalPhone?.Trim();
        }

        member.Update(v.RoleCode, v.Responsibilities, v.RequiredSkills, v.AllocationPercent, v.FromDate);
        return member;
    }

    public void Update(string roleCode, string? responsibilities, string? requiredSkills, decimal? allocation, DateOnly? fromDate)
    {
        RoleCode = roleCode;
        Responsibilities = string.IsNullOrWhiteSpace(responsibilities) ? null : responsibilities.Trim();
        RequiredSkills = string.IsNullOrWhiteSpace(requiredSkills) ? null : requiredSkills.Trim();
        AllocationPercent = allocation;
        FromDate = fromDate ?? FromDate;
    }

    public void ChangeRole(string roleCode) => RoleCode = roleCode;

    public void Leave(DateOnly on, string reason)
    {
        Status = MemberStatuses.Left;
        ToDate = on;
        LeaveReason = reason.Trim();
    }

    public void ReplaceWith(Guid successorId, DateOnly on, string reason)
    {
        Status = MemberStatuses.Replaced;
        ToDate = on;
        LeaveReason = reason.Trim();
        ReplacedByMemberId = successorId;
    }

    /// <summary>Active at any time between the two dates (inclusive).</summary>
    public bool ActiveDuring(DateOnly from, DateOnly to) =>
        (FromDate is null || FromDate <= to) && (ToDate is null || ToDate >= from) && (Status == MemberStatuses.Active || ToDate is not null);
}

public sealed record MemberValues(
    Guid? EmployeeId,
    string? ExternalName,
    string? ExternalOrganisation,
    string? ExternalEmail,
    string? ExternalPhone,
    string RoleCode,
    string? Responsibilities,
    string? RequiredSkills,
    decimal? AllocationPercent,
    DateOnly? FromDate);
