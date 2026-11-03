using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Calendar;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Application;

public sealed record AttendanceInput(Guid MemberId, string Status, string? Note);

public sealed record ListMeetings(Guid TeamId) : IQuery<IReadOnlyList<MeetingDto>>;

public sealed record GetMeeting(Guid Id) : IQuery<MeetingDto>;

public sealed record CreateMeeting(Guid TeamId, string Title, DateTimeOffset ScheduledAt, DateTimeOffset EndsAt, string? Location, string? Agenda) : ICommand<MeetingDto>;

public sealed record UpdateMeeting(Guid Id, string Title, DateTimeOffset ScheduledAt, DateTimeOffset EndsAt, string? Location, string? Agenda, uint Version) : ICommand<MeetingDto>;

public sealed record SaveMinutes(Guid Id, string? Minutes, string? Decisions, IReadOnlyList<AttendanceInput>? Attendance, bool AiDrafted, uint Version) : ICommand<MeetingDto>;

public sealed record SubmitMinutes(Guid Id, uint Version) : ICommand<MeetingDto>;

public sealed record CancelMeeting(Guid Id, string Reason, uint Version) : ICommand<MeetingDto>;

public sealed class CreateMeetingValidator : AbstractValidator<CreateMeeting>
{
    public CreateMeetingValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Team.NameMaxLength);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.ScheduledAt);
        RuleFor(x => x.Location).MaximumLength(300);
        RuleFor(x => x.Agenda).MaximumLength(Meeting.TextMaxLength);
    }
}

public sealed class UpdateMeetingValidator : AbstractValidator<UpdateMeeting>
{
    public UpdateMeetingValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Team.NameMaxLength);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.ScheduledAt);
        RuleFor(x => x.Location).MaximumLength(300);
        RuleFor(x => x.Agenda).MaximumLength(Meeting.TextMaxLength);
    }
}

public sealed class SaveMinutesValidator : AbstractValidator<SaveMinutes>
{
    public SaveMinutesValidator()
    {
        RuleFor(x => x.Minutes).MaximumLength(Meeting.TextMaxLength);
        RuleFor(x => x.Decisions).MaximumLength(Meeting.TextMaxLength);
        RuleForEach(x => x.Attendance).ChildRules(a =>
        {
            a.RuleFor(x => x.Status).Must(s => AttendanceStatuses.All.Contains(s)).WithMessage("validation.unknownCode");
            a.RuleFor(x => x.Note).MaximumLength(1000);
        });
    }
}

public sealed class CancelMeetingValidator : AbstractValidator<CancelMeeting>
{
    public CancelMeetingValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

/// <summary>Meetings and minutes (SPEC §4.4 C): the leader schedules, leader / secretary record attendance and minutes; submission stamps the delay.</summary>
internal sealed class MeetingHandlers(TeamsDbContext db, TeamReader reader, TeamNotifier notifier, IRequestContext context, INumberingService numbering, IClock clock)
    : IQueryHandler<ListMeetings, IReadOnlyList<MeetingDto>>,
        IQueryHandler<GetMeeting, MeetingDto>,
        ICommandHandler<CreateMeeting, MeetingDto>,
        ICommandHandler<UpdateMeeting, MeetingDto>,
        ICommandHandler<SaveMinutes, MeetingDto>,
        ICommandHandler<SubmitMinutes, MeetingDto>,
        ICommandHandler<CancelMeeting, MeetingDto>
{
    public async Task<Result<IReadOnlyList<MeetingDto>>> HandleAsync(ListMeetings query, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(query.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var meetings = await db.Meetings.AsNoTracking().Where(m => m.TeamId == team.Id).OrderByDescending(m => m.ScheduledAt).Take(200).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var list = new List<MeetingDto>();
        foreach (var meeting in meetings)
        {
            list.Add(await ToDtoAsync(team, meeting, withAttendance: false, cancellationToken).ConfigureAwait(false));
        }

        return list;
    }

    public async Task<Result<MeetingDto>> HandleAsync(GetMeeting query, CancellationToken cancellationToken)
    {
        var (team, meeting) = await LoadAsync(query.Id, false, cancellationToken).ConfigureAwait(false);
        return team is null || meeting is null ? Error.NotFound() : await ToDtoAsync(team, meeting, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MeetingDto>> HandleAsync(CreateMeeting command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!TeamStatuses.IsOperating(team.Status))
        {
            return Error.Rule("teams.notOperating", "teams.errors.notOperating");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, TeamPermissions.MeetingNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
        var meeting = Meeting.Create(team.Id, number, command.Title, command.ScheduledAt, command.EndsAt, command.Location, command.Agenda);
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var members = await reader.MembersAsync(team.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(TeamNotifications.MeetingScheduled, team, members.Select(m => m.EmployeeId).Where(e => e != context.User.EmployeeIdOf()), null,
            meeting.Title, meeting.ScheduledAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), db, cancellationToken, $"/teams/meetings/{meeting.Id:D}")
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(team, meeting, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MeetingDto>> HandleAsync(UpdateMeeting command, CancellationToken cancellationToken)
    {
        var (team, meeting) = await LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || meeting is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (meeting.Status != MeetingStatuses.Scheduled || meeting.MinutesSubmitted)
        {
            return Error.Rule("teams.meetingLocked", "teams.errors.meetingLocked");
        }

        db.Entry(meeting).Property(x => x.Version).OriginalValue = command.Version;
        meeting.Reschedule(command.Title, command.ScheduledAt, command.EndsAt, command.Location, command.Agenda);
        return await SaveAsync(team, meeting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MeetingDto>> HandleAsync(SaveMinutes command, CancellationToken cancellationToken)
    {
        var (team, meeting) = await LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || meeting is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Minutes, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (meeting.Status == MeetingStatuses.Cancelled || meeting.MinutesSubmitted)
        {
            return Error.Rule("teams.meetingLocked", "teams.errors.meetingLocked");
        }

        db.Entry(meeting).Property(x => x.Version).OriginalValue = command.Version;
        meeting.WriteMinutes(command.Minutes, command.Decisions, command.AiDrafted);
        if (command.Attendance is { } attendance)
        {
            var memberIds = await db.Members.Where(m => m.TeamId == team.Id).Select(m => m.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            if (attendance.Any(a => !memberIds.Contains(a.MemberId)))
            {
                return Error.Validation([new FieldError("attendance", "validation.unknownReference", "validation.unknownReference")]);
            }

            var stored = await db.Attendance.Where(a => a.MeetingId == meeting.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var input in attendance)
            {
                if (stored.FirstOrDefault(a => a.MemberId == input.MemberId) is { } row)
                {
                    row.Set(input.Status, input.Note);
                }
                else
                {
                    db.Attendance.Add(Domain.Attendance.Create(team.Id, meeting.Id, input.MemberId, input.Status, input.Note));
                }
            }
        }

        return await SaveAsync(team, meeting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MeetingDto>> HandleAsync(SubmitMinutes command, CancellationToken cancellationToken)
    {
        var (team, meeting) = await LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || meeting is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Minutes, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (meeting.Status == MeetingStatuses.Cancelled || meeting.MinutesSubmitted)
        {
            return Error.Rule("teams.meetingLocked", "teams.errors.meetingLocked");
        }

        if (string.IsNullOrWhiteSpace(meeting.Minutes))
        {
            return Error.Validation([new FieldError("minutes", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (clock.UtcNow < meeting.EndsAt)
        {
            return Error.Rule("teams.meetingNotEnded", "teams.errors.meetingNotEnded");
        }

        db.Entry(meeting).Property(x => x.Version).OriginalValue = command.Version;
        meeting.SubmitMinutes(clock.UtcNow, context.User.EmployeeIdOf());
        return await SaveAsync(team, meeting, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<MeetingDto>> HandleAsync(CancelMeeting command, CancellationToken cancellationToken)
    {
        var (team, meeting) = await LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || meeting is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (meeting.Status != MeetingStatuses.Scheduled || meeting.MinutesSubmitted)
        {
            return Error.Rule("teams.meetingLocked", "teams.errors.meetingLocked");
        }

        db.Entry(meeting).Property(x => x.Version).OriginalValue = command.Version;
        meeting.Cancel(command.Reason);
        return await SaveAsync(team, meeting, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<MeetingDto>> SaveAsync(Team team, Meeting meeting, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ToDtoAsync(team, meeting, true, ct).ConfigureAwait(false);
    }

    private async Task<(Team? Team, Meeting? Meeting)> LoadAsync(Guid meetingId, bool tracked, CancellationToken ct)
    {
        var meeting = tracked
            ? await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId, ct).ConfigureAwait(false)
            : await db.Meetings.AsNoTracking().FirstOrDefaultAsync(m => m.Id == meetingId, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return (null, null);
        }

        var team = await reader.FindAsync(meeting.TeamId, context.User, ct).ConfigureAwait(false);
        return team is null ? (null, null) : (team, meeting);
    }

    private async Task<MeetingDto> ToDtoAsync(Team team, Meeting meeting, bool withAttendance, CancellationToken ct)
    {
        var attendance = new List<AttendanceDto>();
        if (withAttendance)
        {
            var day = DateOnly.FromDateTime(meeting.ScheduledAt.UtcDateTime);
            var members = (await reader.MembersAsync(team.Id, activeOnly: false, ct).ConfigureAwait(false)).Where(m => m.ActiveDuring(day, day)).ToList();
            var rows = await db.Attendance.AsNoTracking().Where(a => a.MeetingId == meeting.Id).ToListAsync(ct).ConfigureAwait(false);
            var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), ct).ConfigureAwait(false);
            attendance.AddRange(members.Select(m =>
            {
                var row = rows.FirstOrDefault(r => r.MemberId == m.Id);
                return new AttendanceDto(m.Id, TeamReader.Person(m.EmployeeId, people), m.ExternalName, row?.Status, row?.Note);
            }));
        }

        var by = await reader.PeopleAsync([meeting.MinutesByEmployeeId], ct).ConfigureAwait(false);
        return new MeetingDto(meeting.Id, meeting.TeamId, meeting.Number, meeting.Title, meeting.ScheduledAt, meeting.EndsAt, meeting.Location, meeting.Agenda, meeting.Minutes,
            meeting.Decisions, meeting.Status, meeting.MinutesSubmittedAt, meeting.MinutesDelayHours, TeamReader.Person(meeting.MinutesByEmployeeId, by), meeting.AiDrafted,
            attendance, await reader.CanAsync(context.User, TeamPermissions.Minutes, team, ct).ConfigureAwait(false),
            await reader.CanAsync(context.User, TeamPermissions.Operate, team, ct).ConfigureAwait(false), meeting.Version);
    }
}

public sealed record ListRecommendations(Guid TeamId) : IQuery<IReadOnlyList<RecommendationDto>>;

public sealed record GetRecommendation(Guid Id) : IQuery<RecommendationDto>;

public sealed record CreateRecommendation(Guid TeamId, Guid? MeetingId, string? Kind, string Text, Guid? OwnerEmployeeId, DateOnly? DueDate) : ICommand<RecommendationDto>;

public sealed record UpdateRecommendation(Guid Id, string? Kind, string Text, Guid? OwnerEmployeeId, DateOnly? DueDate, uint Version) : ICommand<RecommendationDto>;

public sealed record SetRecommendationStatus(Guid Id, string Status, string? Note, IReadOnlyList<Guid>? EvidenceFileIds, uint Version) : ICommand<RecommendationDto>;

public sealed class CreateRecommendationValidator : AbstractValidator<CreateRecommendation>
{
    public CreateRecommendationValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Recommendation.TextMaxLength);
        RuleFor(x => x.Kind).Must(k => k is null || RecommendationKinds.All.Contains(k)).WithMessage("validation.unknownCode");
    }
}

public sealed class UpdateRecommendationValidator : AbstractValidator<UpdateRecommendation>
{
    public UpdateRecommendationValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Recommendation.TextMaxLength);
        RuleFor(x => x.Kind).Must(k => k is null || RecommendationKinds.All.Contains(k)).WithMessage("validation.unknownCode");
    }
}

public sealed class SetRecommendationStatusValidator : AbstractValidator<SetRecommendationStatus>
{
    public SetRecommendationStatusValidator()
    {
        RuleFor(x => x.Status).Must(s => RecommendationStatuses.All.Contains(s)).WithMessage("validation.unknownCode");
        RuleFor(x => x.Note).MaximumLength(4000);
        RuleFor(x => x.Note).NotEmpty().When(x => x.Status == RecommendationStatuses.Rejected);
    }
}

/// <summary>The recommendations / decisions register; its closure % feeds the recommendations criterion (ADR 0045).</summary>
internal sealed class RecommendationHandlers(
    TeamsDbContext db, TeamReader reader, TeamRoleSync roleSync, TeamNotifier notifier, IRequestContext context, INumberingService numbering, ISettingsStore settings,
    Platform.Files.IFileService files, IEmployeeDirectory employees, IClock clock)
    : IQueryHandler<ListRecommendations, IReadOnlyList<RecommendationDto>>,
        IQueryHandler<GetRecommendation, RecommendationDto>,
        ICommandHandler<CreateRecommendation, RecommendationDto>,
        ICommandHandler<UpdateRecommendation, RecommendationDto>,
        ICommandHandler<SetRecommendationStatus, RecommendationDto>
{
    public async Task<Result<IReadOnlyList<RecommendationDto>>> HandleAsync(ListRecommendations query, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(query.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var items = await db.Recommendations.AsNoTracking().Where(r => r.TeamId == team.Id).OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var people = await reader.PeopleAsync(items.Select(i => i.OwnerEmployeeId), cancellationToken).ConfigureAwait(false);
        var canOperate = await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false);
        return items.Select(i => ToDto(i, people, canOperate)).ToList();
    }

    public async Task<Result<RecommendationDto>> HandleAsync(GetRecommendation query, CancellationToken cancellationToken)
    {
        var (team, item) = await LoadAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return team is null || item is null ? Error.NotFound() : await OneAsync(team, item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RecommendationDto>> HandleAsync(CreateRecommendation command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false)
            && !await reader.CanAsync(context.User, TeamPermissions.Minutes, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!TeamStatuses.IsOperating(team.Status))
        {
            return Error.Rule("teams.notOperating", "teams.errors.notOperating");
        }

        var check = await CheckAsync(team.Id, command.MeetingId, command.OwnerEmployeeId, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var defaults = await settings.GetAsync<TeamSettings>(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, TeamPermissions.RecommendationNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
        var item = Recommendation.Create(team.Id, number, command.MeetingId, command.Kind ?? RecommendationKinds.Recommendation, command.Text, command.OwnerEmployeeId,
            command.DueDate ?? clock.Today.AddDays(defaults.RecommendationDefaultDueDays));
        db.Recommendations.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await GrantOwnerAsync(team, item, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await OneAsync(team, item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RecommendationDto>> HandleAsync(UpdateRecommendation command, CancellationToken cancellationToken)
    {
        var (team, item) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (team is null || item is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var check = await CheckAsync(team.Id, null, command.OwnerEmployeeId, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        db.Entry(item).Property(x => x.Version).OriginalValue = command.Version;
        var ownerChanged = item.OwnerEmployeeId != command.OwnerEmployeeId;
        item.Edit(command.Kind ?? item.Kind, command.Text, command.OwnerEmployeeId, command.DueDate);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (ownerChanged)
        {
            await GrantOwnerAsync(team, item, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await OneAsync(team, item, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RecommendationDto>> HandleAsync(SetRecommendationStatus command, CancellationToken cancellationToken)
    {
        var (team, item) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (team is null || item is null)
        {
            return Error.NotFound();
        }

        var isOwner = item.OwnerEmployeeId is not null && item.OwnerEmployeeId == context.User.EmployeeIdOf();
        if (!isOwner && !await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var evidence = command.EvidenceFileIds ?? [];
        foreach (var id in evidence)
        {
            var file = await files.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (file is null || file.OwnerType != TeamPermissions.ResourceType || file.OwnerId != team.Id.ToString("D"))
            {
                return Error.Validation([new FieldError("evidenceFileIds", "validation.unknownReference", "validation.unknownReference")]);
            }
        }

        db.Entry(item).Property(x => x.Version).OriginalValue = command.Version;
        item.SetStatus(command.Status, clock.Today, command.Note, evidence);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await OneAsync(team, item, cancellationToken).ConfigureAwait(false);
    }

    private async Task GrantOwnerAsync(Team team, Recommendation item, CancellationToken ct)
    {
        if (item.OwnerEmployeeId is not { } owner)
        {
            return;
        }

        await roleSync.GrantAsync(team.Id, owner, TeamPermissions.RecommendationOwnerRole, ct).ConfigureAwait(false);
        var actor = context.User.EmployeeIdOf() is { } me && await employees.GetAsync(me, ct).ConfigureAwait(false) is { } e ? e.NameEn ?? e.NameAr : null;
        await notifier.SendAsync(TeamNotifications.RecommendationAssigned, team, [owner], actor, item.Text.Length > 200 ? item.Text[..200] + "…" : item.Text,
            item.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), db, ct, number: item.Number).ConfigureAwait(false);
    }

    private async Task<Result> CheckAsync(Guid teamId, Guid? meetingId, Guid? owner, CancellationToken ct)
    {
        if (meetingId is { } m && !await db.Meetings.AnyAsync(x => x.Id == m && x.TeamId == teamId, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("meetingId", "validation.unknownReference", "validation.unknownReference")]);
        }

        if (owner is { } o && await employees.GetAsync(o, ct).ConfigureAwait(false) is null)
        {
            return Error.Validation([new FieldError("ownerEmployeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        return Result.Success();
    }

    private async Task<(Team? Team, Recommendation? Item)> LoadAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Recommendations.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (item is null)
        {
            return (null, null);
        }

        var team = await reader.FindAsync(item.TeamId, context.User, ct).ConfigureAwait(false);
        return team is null ? (null, null) : (team, item);
    }

    private async Task<RecommendationDto> OneAsync(Team team, Recommendation item, CancellationToken ct)
    {
        var people = await reader.PeopleAsync([item.OwnerEmployeeId], ct).ConfigureAwait(false);
        return ToDto(item, people, await reader.CanAsync(context.User, TeamPermissions.Operate, team, ct).ConfigureAwait(false));
    }

    private RecommendationDto ToDto(Recommendation r, IReadOnlyDictionary<Guid, TeamPersonDto> people, bool canOperate)
    {
        var isOwner = r.OwnerEmployeeId is not null && r.OwnerEmployeeId == context.User.EmployeeIdOf();
        return new RecommendationDto(r.Id, r.TeamId, r.Number, r.MeetingId, r.Kind, r.Text, TeamReader.Person(r.OwnerEmployeeId, people), r.DueDate, r.Status,
            RecommendationStatuses.IsOpen(r.Status) && r.DueDate < clock.Today, r.ClosedOn, r.ClosureNote, r.EvidenceFileIds, canOperate, canOperate || isOwner, r.Version);
    }
}

public sealed record ListReports(Guid TeamId) : IQuery<IReadOnlyList<ReportDto>>;

public sealed record SubmitReport(Guid Id, string Summary, uint Version) : ICommand<ReportDto>;

public sealed class SubmitReportValidator : AbstractValidator<SubmitReport>
{
    public SubmitReportValidator() => RuleFor(x => x.Summary).NotEmpty().MaximumLength(Meeting.TextMaxLength);
}

internal sealed class ReportHandlers(TeamsDbContext db, TeamReader reader, IRequestContext context, IClock clock)
    : IQueryHandler<ListReports, IReadOnlyList<ReportDto>>, ICommandHandler<SubmitReport, ReportDto>
{
    public async Task<Result<IReadOnlyList<ReportDto>>> HandleAsync(ListReports query, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(query.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var reports = await db.Reports.AsNoTracking().Where(r => r.TeamId == team.Id).OrderByDescending(r => r.PeriodStart).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var people = await reader.PeopleAsync(reports.Select(r => r.SubmittedByEmployeeId), cancellationToken).ConfigureAwait(false);
        var canOperate = await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false);
        return reports.Select(r => ToDto(r, people, canOperate)).ToList();
    }

    public async Task<Result<ReportDto>> HandleAsync(SubmitReport command, CancellationToken cancellationToken)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken).ConfigureAwait(false);
        var team = report is null ? null : await reader.FindAsync(report.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (report is null || team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Operate, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (report.Status != ReportStatuses.Due)
        {
            return Error.Rule("teams.reportSubmitted", "teams.errors.reportSubmitted");
        }

        db.Entry(report).Property(x => x.Version).OriginalValue = command.Version;
        report.Submit(command.Summary, clock.UtcNow, context.User.EmployeeIdOf());
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var people = await reader.PeopleAsync([report.SubmittedByEmployeeId], cancellationToken).ConfigureAwait(false);
        return ToDto(report, people, true);
    }

    private ReportDto ToDto(TeamReport r, IReadOnlyDictionary<Guid, TeamPersonDto> people, bool canOperate) =>
        new(r.Id, r.TeamId, r.PeriodStart, r.PeriodEnd, r.DueDate, r.DueAt, r.Status, r.Status == ReportStatuses.Due && clock.UtcNow > r.DueAt, r.Summary, r.SubmittedAt,
            TeamReader.Person(r.SubmittedByEmployeeId, people), canOperate && r.Status == ReportStatuses.Due, r.Version);
}

/// <summary>Working-time dates (ADR 0027) of reports and reminders: end of working time on a day, N working days before / after.</summary>
internal sealed class TeamDeadlines(IBusinessHoursCalculator calculator)
{
    private const int MaxDaysScanned = 400;

    public async Task<(DateTimeOffset DueAt, DateTimeOffset RemindAt)> ReportAsync(Guid? unitId, DateOnly due, int remindDaysBefore, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = due;
        for (var i = 0; i < MaxDaysScanned && await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false) == TimeSpan.Zero; i++)
        {
            day = day.AddDays(1);
        }

        var working = await WorkingOnAsync(hours.Id, zone, day, ct).ConfigureAwait(false);
        var dueAt = working > TimeSpan.Zero ? await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, day), working, ct).ConfigureAwait(false) : Midnight(zone, day.AddDays(1));
        var remindDay = await StepAsync(hours.Id, zone, day, -remindDaysBefore, ct).ConfigureAwait(false);
        var remindAt = await calculator.AddWorkingTimeAsync(hours.Id, Midnight(zone, remindDay), TimeSpan.Zero, ct).ConfigureAwait(false);
        return (dueAt, remindAt);
    }

    /// <summary><paramref name="from"/> + N working days (same time of day rule as the calculator: working time added).</summary>
    public async Task<DateTimeOffset> AfterWorkingDaysAsync(Guid? unitId, DateTimeOffset from, int days, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);
        var day = await StepAsync(hours.Id, zone, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).DateTime), days, ct).ConfigureAwait(false);
        return Midnight(zone, day.AddDays(1));
    }

    private async Task<DateOnly> StepAsync(Guid hoursId, TimeZoneInfo zone, DateOnly from, int count, CancellationToken ct)
    {
        var step = count < 0 ? -1 : 1;
        var remaining = Math.Abs(count);
        var day = from;
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(step);
            if (await WorkingOnAsync(hoursId, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return day;
    }

    private Task<TimeSpan> WorkingOnAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct) =>
        calculator.WorkingTimeBetweenAsync(hoursId, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct);

    private static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
