using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Teams.Domain;

public static class MeetingStatuses
{
    public const string Scheduled = "scheduled";
    public const string Held = "held";
    public const string Cancelled = "cancelled";
}

public static class AttendanceStatuses
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Excused = "excused";

    public static readonly IReadOnlyList<string> All = [Present, Absent, Excused];
}

/// <summary>A meeting with minutes (SPEC §4.4 C). Minutes delay = hours from the meeting's end to "submit minutes" (ADR 0045).</summary>
[Audited(EntityType)]
[AuditParent(Team.EntityType, nameof(TeamId))]
public sealed class Meeting : AuditableEntity
{
    public const string EntityType = "teams.meeting";
    public const int TextMaxLength = 20000;

    private Meeting()
    {
    }

    public string? Number { get; private set; }

    public Guid TeamId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DateTimeOffset ScheduledAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public string? Location { get; private set; }

    public string? Agenda { get; private set; }

    public string? Minutes { get; private set; }

    public string? Decisions { get; private set; }

    public string Status { get; private set; } = MeetingStatuses.Scheduled;

    public DateTimeOffset? MinutesSubmittedAt { get; private set; }

    public decimal? MinutesDelayHours { get; private set; }

    public Guid? MinutesByEmployeeId { get; private set; }

    public bool AiDrafted { get; private set; }

    public string? CancelReason { get; private set; }

    public static Meeting Create(Guid teamId, string number, string title, DateTimeOffset scheduledAt, DateTimeOffset endsAt, string? location, string? agenda) =>
        new() { TeamId = teamId, Number = number, Title = title.Trim(), ScheduledAt = scheduledAt, EndsAt = endsAt, Location = location?.Trim(), Agenda = agenda?.Trim() };

    public bool MinutesSubmitted => MinutesSubmittedAt is not null;

    public void Reschedule(string title, DateTimeOffset scheduledAt, DateTimeOffset endsAt, string? location, string? agenda)
    {
        Title = title.Trim();
        ScheduledAt = scheduledAt;
        EndsAt = endsAt;
        Location = location?.Trim();
        Agenda = agenda?.Trim();
    }

    public void WriteMinutes(string? minutes, string? decisions, bool aiDrafted)
    {
        Minutes = string.IsNullOrWhiteSpace(minutes) ? null : minutes.Trim();
        Decisions = string.IsNullOrWhiteSpace(decisions) ? null : decisions.Trim();
        AiDrafted = aiDrafted;
    }

    public void SubmitMinutes(DateTimeOffset at, Guid? byEmployeeId)
    {
        Status = MeetingStatuses.Held;
        MinutesSubmittedAt = at;
        MinutesByEmployeeId = byEmployeeId;
        MinutesDelayHours = DelayHours(EndsAt, at);
        AiDrafted = false;
    }

    public void Cancel(string reason)
    {
        Status = MeetingStatuses.Cancelled;
        CancelReason = reason.Trim();
    }

    /// <summary>Elapsed hours (≥ 0, two decimals) from the meeting's end.</summary>
    public static decimal DelayHours(DateTimeOffset endsAt, DateTimeOffset at) =>
        Math.Round((decimal)Math.Max(0, (at - endsAt).TotalHours), 2);
}

/// <summary>Attendance of one member at one meeting.</summary>
[Audited(EntityType)]
[AuditParent(Team.EntityType, nameof(TeamId))]
public sealed class Attendance : AuditableEntity
{
    public const string EntityType = "teams.attendance";

    private Attendance()
    {
    }

    public Guid TeamId { get; private set; }

    public Guid MeetingId { get; private set; }

    public Guid MemberId { get; private set; }

    public string Status { get; private set; } = AttendanceStatuses.Present;

    public string? Note { get; private set; }

    public static Attendance Create(Guid teamId, Guid meetingId, Guid memberId, string status, string? note) =>
        new() { TeamId = teamId, MeetingId = meetingId, MemberId = memberId, Status = status, Note = note?.Trim() };

    public void Set(string status, string? note)
    {
        Status = status;
        Note = note?.Trim();
    }
}

public static class RecommendationStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Closed = "closed";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = [Open, InProgress, Closed, Rejected];

    public static bool IsOpen(string status) => status is Open or InProgress;
}

public static class RecommendationKinds
{
    public const string Recommendation = "recommendation";
    public const string Decision = "decision";

    public static readonly IReadOnlyList<string> All = [Recommendation, Decision];
}

/// <summary>An entry of the recommendations / decisions register (SPEC §4.4 C).</summary>
[Audited(EntityType)]
[AuditParent(Team.EntityType, nameof(TeamId))]
public sealed class Recommendation : AuditableEntity
{
    public const string EntityType = "teams.recommendation";
    public const int TextMaxLength = 4000;

    private Recommendation()
    {
    }

    public string? Number { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid? MeetingId { get; private set; }

    public string Kind { get; private set; } = RecommendationKinds.Recommendation;

    public string Text { get; private set; } = string.Empty;

    public Guid? OwnerEmployeeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public string Status { get; private set; } = RecommendationStatuses.Open;

    public DateOnly? ClosedOn { get; private set; }

    public string? ClosureNote { get; private set; }

    public Guid[] EvidenceFileIds { get; private set; } = [];

    public static Recommendation Create(Guid teamId, string number, Guid? meetingId, string kind, string text, Guid? owner, DateOnly? due) =>
        new() { TeamId = teamId, Number = number, MeetingId = meetingId, Kind = kind, Text = text.Trim(), OwnerEmployeeId = owner, DueDate = due };

    public void Edit(string kind, string text, Guid? owner, DateOnly? due)
    {
        Kind = kind;
        Text = text.Trim();
        OwnerEmployeeId = owner;
        DueDate = due;
    }

    public void SetStatus(string status, DateOnly today, string? note, IEnumerable<Guid> evidence)
    {
        Status = status;
        ClosedOn = status == RecommendationStatuses.Closed ? today : null;
        ClosureNote = string.IsNullOrWhiteSpace(note) ? ClosureNote : note.Trim();
        EvidenceFileIds = [.. EvidenceFileIds.Concat(evidence).Distinct()];
    }
}

public static class ReportStatuses
{
    public const string Due = "due";
    public const string Submitted = "submitted";
}

/// <summary>A periodic report per reporting cadence (SPEC §4.4 C).</summary>
[Audited(EntityType)]
[AuditParent(Team.EntityType, nameof(TeamId))]
public sealed class TeamReport : AuditableEntity
{
    public const string EntityType = "teams.team_report";

    private TeamReport()
    {
    }

    public Guid TeamId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public DateOnly DueDate { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public DateTimeOffset RemindAt { get; private set; }

    public string Status { get; private set; } = ReportStatuses.Due;

    public string? Summary { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public Guid? SubmittedByEmployeeId { get; private set; }

    public DateTimeOffset? RemindedAt { get; private set; }

    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    public static TeamReport Create(Guid teamId, DateOnly start, DateOnly end, DateTimeOffset dueAt, DateTimeOffset remindAt) =>
        new() { TeamId = teamId, PeriodStart = start, PeriodEnd = end, DueDate = end, DueAt = dueAt, RemindAt = remindAt };

    public void Submit(string summary, DateTimeOffset at, Guid? by)
    {
        Status = ReportStatuses.Submitted;
        Summary = summary.Trim();
        SubmittedAt = at;
        SubmittedByEmployeeId = by;
    }

    public void MarkReminded(DateTimeOffset at) => RemindedAt = at;

    public void MarkOverdueNotified(DateTimeOffset at) => OverdueNotifiedAt = at;
}
