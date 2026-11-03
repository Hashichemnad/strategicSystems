using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Links;

namespace Strategy.Modules.Teams.Application;

public sealed record TeamPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record UnitDto(Guid Id, string? NameAr, string? NameEn);

public sealed record TeamListItemDto(
    Guid Id,
    string? Number,
    string? NameAr,
    string? NameEn,
    string TypeCode,
    string? TypeNameAr,
    string? TypeNameEn,
    bool Permanent,
    string Status,
    UnitDto? OwningUnit,
    TeamPersonDto? Leader,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int ActiveMembers,
    decimal? LastScore,
    bool MyMembership);

public sealed record MemberDto(
    Guid Id,
    TeamPersonDto? Employee,
    string? ExternalName,
    string? ExternalOrganisation,
    string? ExternalEmail,
    string? ExternalPhone,
    string RoleCode,
    string? RoleNameAr,
    string? RoleNameEn,
    bool IsLeader,
    string? Responsibilities,
    string? RequiredSkills,
    decimal? AllocationPercent,
    int Position,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string Status,
    Guid? ReplacedByMemberId,
    string? LeaveReason,
    uint Version);

public sealed record TeamStatsDto(int ActiveMembers, int MeetingsHeld, int RecommendationsOpen, int RecommendationsClosed, decimal? ClosurePercent, decimal? AverageMinutesDelay);

public sealed record TeamPermissionsDto(
    bool CanEdit, bool CanSubmit, bool CanManage, bool CanOperate, bool CanMinute, bool CanOpenEvaluation, bool CanViewResults, bool CanManageRewards);

public sealed record TeamDto(
    Guid Id,
    string? Number,
    string? NameAr,
    string? NameEn,
    string TypeCode,
    string? TypeNameAr,
    string? TypeNameEn,
    bool Permanent,
    string? Subject,
    string? Objectives,
    string? Mandate,
    UnitDto? OwningUnit,
    TeamPersonDto? Sponsor,
    TeamPersonDto? RequestedBy,
    TeamPersonDto? Leader,
    string? ApprovedByCode,
    string MemoMode,
    string? MemoNumber,
    DateOnly? MemoDate,
    Guid? MemoFileId,
    string? MemoDraft,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Cadence,
    string? ReportTo,
    string Workload,
    string Status,
    string? StatusReason,
    Guid? ReplacesTeamId,
    Guid? ReplacedByTeamId,
    string? EvaluatorRule,
    TeamPersonDto? Evaluator,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<LinkView> LinkedRecords,
    TeamStatsDto Stats,
    decimal? LastScore,
    TeamPermissionsDto Permissions,
    IReadOnlyList<string> Actions,
    uint Version);

public sealed record AttendanceDto(Guid MemberId, TeamPersonDto? Employee, string? ExternalName, string? Status, string? Note);

public sealed record MeetingDto(
    Guid Id,
    Guid TeamId,
    string? Number,
    string Title,
    DateTimeOffset ScheduledAt,
    DateTimeOffset EndsAt,
    string? Location,
    string? Agenda,
    string? Minutes,
    string? Decisions,
    string Status,
    DateTimeOffset? MinutesSubmittedAt,
    decimal? MinutesDelayHours,
    TeamPersonDto? MinutesBy,
    bool AiDrafted,
    IReadOnlyList<AttendanceDto> Attendance,
    bool CanMinute,
    bool CanManage,
    uint Version);

public sealed record RecommendationDto(
    Guid Id,
    Guid TeamId,
    string? Number,
    Guid? MeetingId,
    string Kind,
    string Text,
    TeamPersonDto? Owner,
    DateOnly? DueDate,
    string Status,
    bool Overdue,
    DateOnly? ClosedOn,
    string? ClosureNote,
    IReadOnlyList<Guid> EvidenceFileIds,
    bool CanEdit,
    bool CanUpdateStatus,
    uint Version);

public sealed record ReportDto(
    Guid Id, Guid TeamId, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate, DateTimeOffset DueAt, string Status, bool Overdue, string? Summary,
    DateTimeOffset? SubmittedAt, TeamPersonDto? SubmittedBy, bool CanSubmit, uint Version);

public sealed record EvaluationSummaryDto(
    Guid Id,
    Guid TeamId,
    string? Number,
    string Cycle,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Trigger,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    TeamPersonDto? Evaluator,
    decimal? Score,
    string? RewardStatus,
    decimal? RewardTotal);

public sealed record MemberEvaluationDto(
    Guid Id,
    Guid MemberId,
    TeamPersonDto? Employee,
    string? ExternalName,
    string RoleCode,
    string? RoleNameAr,
    string? RoleNameEn,
    bool IsLeader,
    IReadOnlyDictionary<string, int> Answers,
    string? Comment,
    int? Points,
    string? Grade,
    decimal? AttendanceRate,
    TeamPersonDto? EvaluatedBy,
    bool CanEvaluate,
    decimal? CalculatedReward,
    decimal? ApprovedReward,
    string? AdjustmentReason,
    bool Certificate,
    string? CertificateNumber,
    Guid? CertificateFileId,
    bool FinancialReward,
    uint Version);

public sealed record RewardBudgetDto(int Year, decimal AnnualBudget, decimal Used, decimal Remaining, string Currency, Guid? MatrixId, int? MatrixVersion);

public sealed record EvaluationPermissionsDto(bool CanEvaluate, bool CanSubmit, bool CanCancel, bool CanManageRewards, bool CanSubmitRewards, bool EvidenceRequired);

public sealed record EvaluationDto(
    EvaluationSummaryDto Summary,
    string? TeamNumber,
    string? TeamNameAr,
    string? TeamNameEn,
    TemplateContent Template,
    int TemplateYear,
    int TemplateVersion,
    IReadOnlyList<CriterionResult> Criteria,
    string Workload,
    IReadOnlyList<Guid> EvidenceFileIds,
    IReadOnlyList<EvaluationFlag> Flags,
    IReadOnlyList<MemberEvaluationDto> Members,
    TeamPersonDto? LeaderEvaluator,
    string? ReturnComment,
    RewardBudgetDto? Budget,
    IReadOnlyList<EvaluationSummaryDto> PreviousCycles,
    EvaluationPermissionsDto Permissions,
    uint Version);

/// <summary>An evaluation as a person without the results permission sees it: status only.</summary>
public sealed record EvaluationStatusDto(Guid Id, Guid TeamId, string? Number, string Cycle, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ApprovedAt);

public sealed record MembershipDto(
    Guid TeamId,
    string? TeamNumber,
    string? TeamNameAr,
    string? TeamNameEn,
    string TeamStatus,
    string TypeCode,
    Guid MemberId,
    string RoleCode,
    string? RoleNameAr,
    string? RoleNameEn,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string Status,
    IReadOnlyList<MembershipResultDto>? Results);

public sealed record MembershipResultDto(Guid EvaluationId, string Cycle, string Status, decimal? TeamScore, int? Points, string? Grade, decimal? ApprovedReward, bool Certificate);

public sealed record TeamCountsDto(int Mine, int Leading, int PendingEvaluation, int MyEvaluations, int ReportsDue);
