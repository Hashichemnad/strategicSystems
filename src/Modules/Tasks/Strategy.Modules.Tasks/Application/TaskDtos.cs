using Strategy.Platform.Links;

namespace Strategy.Modules.Tasks.Application;

public sealed record PersonDto(Guid EmployeeId, string? NameAr, string? NameEn);

public sealed record TaskListItemDto(
    Guid Id,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string Template,
    string Status,
    string Priority,
    string? Indicator,
    string TypeCode,
    string SourceCode,
    string? CategoryCode,
    bool Confidential,
    PersonDto Assignee,
    PersonDto Creator,
    DateOnly? PlannedStart,
    DateOnly? DueDate,
    DateTimeOffset? DueAt,
    Guid? GroupId,
    Guid? ParentTaskId,
    string? LinkedType,
    Guid? LinkedId,
    int? PercentComplete,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    IReadOnlyList<string> Actions);

public sealed record ChecklistItemDto(Guid Id, string Text, int Sort, bool Done, DateTimeOffset? DoneAt);

public sealed record FollowUpDto(Guid EmployeeId, string? NameAr, string? NameEn, bool CanUpload, DateTimeOffset Since);

public sealed record TaskRefDto(Guid Id, string? Number, string? TitleAr, string? TitleEn, string Status, string Template, bool Closed);

public sealed record GroupSummaryDto(Guid Id, string? TitleAr, string? TitleEn, int Total, int Finished, bool Complete);

/// <summary>What the current user may do on the task besides the transitions in <see cref="TaskDto.Actions"/>.</summary>
public sealed record TaskCapabilitiesDto(
    bool CanEdit, bool CanComment, bool CanUpload, bool CanManageFollowUps, bool CanManageChecklist, bool CanManageDependencies, bool CanCreateSubtask);

public sealed record TaskDto(
    Guid Id,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string? Description,
    string Template,
    string Status,
    string Priority,
    string? Indicator,
    string TypeCode,
    string SourceCode,
    string? CategoryCode,
    bool Confidential,
    PersonDto Assignee,
    PersonDto Creator,
    PersonDto? EnteredBy,
    DateOnly? PlannedStart,
    DateOnly? DueDate,
    DateTimeOffset? DueAt,
    DateTimeOffset? DueSoonAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ClosedAt,
    string? RejectionReason,
    string? CancelReason,
    string? CompletionComment,
    int? FeedbackPoint,
    string? FeedbackComment,
    int? PercentComplete,
    decimal? EstimatedHours,
    long ActualSeconds,
    long? TimeToAcceptSeconds,
    long? TimeToCompleteSeconds,
    bool? CompletedOnTime,
    RecurrenceDto? Recurrence,
    Guid? RecurrenceSourceId,
    LinkView? Link,
    TaskRefDto? Parent,
    GroupSummaryDto? Group,
    IReadOnlyList<ChecklistItemDto> Checklist,
    IReadOnlyList<TaskListItemDto> Subtasks,
    IReadOnlyList<TaskRefDto> Dependencies,
    IReadOnlyList<FollowUpDto> FollowUps,
    IReadOnlyList<string> Actions,
    TaskCapabilitiesDto Capabilities,
    int FeedbackScaleMax,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record RecurrenceDto(string Frequency, int Interval, DateOnly? Until, int? Count, DateOnly? NextOccurrenceOn, int OccurrenceCount);

public sealed record TaskEventDto(
    Guid Id, DateTimeOffset OccurredAt, string Event, string? FromStatus, string? ToStatus, PersonDto? Actor, string? Comment, string? Data, long? SecondsSincePrevious);

public sealed record TimesheetPeriodDto(Guid Id, string? Number, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, long DurationSeconds, string? HoldReason, string? EndEvent);

public sealed record TimesheetDto(IReadOnlyList<TimesheetPeriodDto> Periods, long TotalSeconds, bool Running);

public sealed record TimelineDto(IReadOnlyList<TaskEventDto> Events, TimesheetDto Timesheet);

public sealed record CommentDto(
    Guid Id, string Text, string Kind, PersonDto? Author, IReadOnlyList<PersonDto> Mentions, IReadOnlyList<Guid> FileIds, DateTimeOffset CreatedAt);

public sealed record TaskCountsDto(int AssignedOpen, int AssignedOverdue, int AwaitingMyApproval, int CreatedOpen, int FollowUp, int Team, int TeamOverdue);

public sealed record CreatedTasksDto(IReadOnlyList<TaskListItemDto> Tasks, Guid? GroupId);

public sealed record GroupMemberDto(TaskListItemDto Task, bool Finished);

public sealed record GroupDto(
    Guid Id, string? TitleAr, string? TitleEn, PersonDto Creator, string CompletionRule, DateTimeOffset CreatedAt, int Total, int Finished, int Cancelled,
    bool Complete, IReadOnlyList<GroupMemberDto> Members, bool CanManage, int Hidden);

public sealed record CountByDto(string Key, int Open, int Overdue);

public sealed record WorkloadDto(PersonDto Employee, int Open, int Overdue, int OnHold);

public sealed record TaskDashboardDto(
    int Open,
    int Overdue,
    int DueSoon,
    int ClosedLast90Days,
    double? AverageHoursToAccept,
    double? AverageHoursToComplete,
    double? OnTimeRate,
    double? AverageFeedback,
    IReadOnlyList<CountByDto> ByUnit,
    IReadOnlyList<CountByDto> BySource,
    IReadOnlyList<CountByDto> ByStatus,
    IReadOnlyList<CountByDto> ByPriority,
    IReadOnlyList<WorkloadDto> Workload,
    IReadOnlyDictionary<string, BilingualName> UnitNames);

public sealed record BilingualName(string? Ar, string? En);

public sealed record PerformanceDto(
    PersonDto Employee, int Completed, int CompletedOnTime, double? OnTimeRate, double? AverageHoursToAccept, double? AverageFeedback, int FeedbackCount);
