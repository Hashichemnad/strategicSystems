using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Teams.Application;

/// <summary>Evaluation approval (SPEC §4.3: default single step HR) and reward approval (§4.5 T4, configurable).</summary>
public static class EvaluationApprovalProcesses
{
    public const string EvaluationKey = "teams.evaluation";
    public const string RewardKey = "teams.reward";

    public static readonly ApprovalProcessDefinition Evaluation = new(
        EvaluationKey, TeamPermissions.SystemCode, "اعتماد تقييم فريق", "Team evaluation approval", TeamPermissions.ResourceType, TeamPermissions.Read, "/teams/{id}",
        [new("score", Platform.Kernel.Expressions.ExpressionValueType.Number, "درجة الفريق", "Team score")],
        [new("hr", "الموارد البشرية", "Human resources", new ApproverRule(ApproverRuleKinds.Role, RoleKey: TeamPermissions.AdministratorRoleKey), SlaHours: 40)],
        TeamPermissions.EvaluationApproverRole,
        "اعتماد تقييم الفريق وأعضائه، أو إعادته لإعادة التقييم.", "Approve the team and member evaluation, or return it for re-evaluation.");

    public static readonly ApprovalProcessDefinition Reward = new(
        RewardKey, TeamPermissions.SystemCode, "اعتماد مكافآت الفريق", "Team reward approval", TeamPermissions.ResourceType, TeamPermissions.Read, "/teams/{id}",
        [new("total", Platform.Kernel.Expressions.ExpressionValueType.Number, "إجمالي المكافآت", "Reward total")],
        [new("owning-unit", "رئيس الوحدة المالكة للنظام", "Head of the system's owning unit", new ApproverRule(ApproverRuleKinds.OwningUnit), SlaHours: 40)],
        TeamPermissions.RewardApproverRole,
        "اعتماد المكافآت المحسوبة من مصفوفة المكافآت.", "Approve the rewards calculated from the reward matrix.");
}

/// <summary>Activity of a team in a period: the data behind suggested levels (ADR 0045) and attendance rates.</summary>
internal sealed class TeamActivity(TeamsDbContext db, IClock clock)
{
    public async Task<CycleActivity> ComputeAsync(Guid teamId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var recommendations = await db.Recommendations.AsNoTracking()
            .Where(r => r.TeamId == teamId && r.Kind == RecommendationKinds.Recommendation && r.CreatedAt >= start && r.CreatedAt < end)
            .Select(r => r.Status).ToListAsync(ct).ConfigureAwait(false);
        var meetings = await db.Meetings.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.Status != MeetingStatuses.Cancelled && m.EndsAt >= start && m.EndsAt < end && m.EndsAt <= clock.UtcNow)
            .Select(m => new { m.EndsAt, m.MinutesDelayHours }).ToListAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var delays = meetings.Select(m => m.MinutesDelayHours ?? Meeting.DelayHours(m.EndsAt, now)).ToList();
        return new CycleActivity(
            recommendations.Count(s => s == RecommendationStatuses.Closed), recommendations.Count(s => s != RecommendationStatuses.Rejected), delays);
    }

    /// <summary>Present ÷ meetings held in the period while the member was active (null when no meeting).</summary>
    public async Task<Dictionary<Guid, decimal?>> AttendanceAsync(Guid teamId, IReadOnlyList<TeamMember> members, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var meetings = await db.Meetings.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.Status == MeetingStatuses.Held && m.ScheduledAt >= start && m.ScheduledAt < end)
            .Select(m => new { m.Id, m.ScheduledAt }).ToListAsync(ct).ConfigureAwait(false);
        var ids = meetings.Select(m => m.Id).ToList();
        var rows = await db.Attendance.AsNoTracking().Where(a => ids.Contains(a.MeetingId)).ToListAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<Guid, decimal?>();
        foreach (var member in members)
        {
            var held = meetings.Where(m => member.ActiveDuring(DateOnly.FromDateTime(m.ScheduledAt.UtcDateTime), DateOnly.FromDateTime(m.ScheduledAt.UtcDateTime))).ToList();
            result[member.Id] = held.Count == 0
                ? null
                : Math.Round((decimal)rows.Count(r => r.MemberId == member.Id && r.Status == AttendanceStatuses.Present && held.Any(h => h.Id == r.MeetingId)) / held.Count, 4);
        }

        return result;
    }
}

/// <summary>Opens a cycle (manual, annual, end date, linked record closed): template snapshot, evaluator, suggestions, member rows, roles.</summary>
internal sealed class EvaluationOpener(
    TeamsDbContext db, TeamActivity activity, TeamRoleSync roleSync, TeamNotifier notifier, INumberingService numbering, IRoleHolderQuery roleHolders,
    IClock clock, IRequestContext context)
{
    public async Task<Result<TeamEvaluation>> OpenAsync(Team team, string trigger, int? year, bool closeAfter, CancellationToken ct)
    {
        if (team.Status is not (TeamStatuses.Active or TeamStatuses.Inactive))
        {
            return Error.Rule("teams.cannotOpenEvaluation", "teams.errors.cannotOpenEvaluation");
        }

        var type = await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == team.TypeCode, ct).ConfigureAwait(false);
        var today = clock.Today;
        string cycle;
        DateOnly from, to;
        if (year is { } y)
        {
            cycle = Cycles.Year(y);
            if (TeamCalendar.AnnualPeriod(y, team.StartDate, team.EndDate) is not { } period)
            {
                return Error.Rule("teams.noPeriod", "teams.errors.noPeriod");
            }

            (from, to) = period;
        }
        else
        {
            cycle = Cycles.Completion;
            from = team.StartDate ?? DateOnly.FromDateTime((team.ActivatedAt ?? clock.UtcNow).UtcDateTime);
            to = team.EndDate is { } e && e < today ? e : today;
        }

        if (await db.Evaluations.AnyAsync(e => e.TeamId == team.Id && e.Cycle == cycle && e.Status != EvaluationStatuses.Cancelled, ct).ConfigureAwait(false))
        {
            return Error.Rule("teams.cycleExists", "teams.errors.cycleExists", new Dictionary<string, object?> { ["cycle"] = cycle });
        }

        var templateYear = year ?? today.Year;
        var template = await db.Templates.AsNoTracking().Where(t => t.Status == VersionStatuses.Published && t.Year == templateYear).OrderByDescending(t => t.VersionNo)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await db.Templates.AsNoTracking().Where(t => t.Status == VersionStatuses.Published).OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (template is null)
        {
            return Error.Rule("teams.noTemplate", "teams.errors.noTemplate");
        }

        var content = template.Read();
        var data = await activity.ComputeAsync(team.Id, from, to, ct).ConfigureAwait(false);
        var criteria = content.Criteria.Select(c =>
        {
            var (level, basis) = EvaluationSuggestions.Suggest(c, data);
            return new CriterionResult(c.Code, null, null, null, level, basis, null);
        }).ToList();

        var members = await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id).OrderBy(m => m.Position).ToListAsync(ct).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var evaluated = members.Where(m => m.ActiveDuring(from, to)).ToList();
        var rule = team.EvaluatorRule ?? type?.EvaluatorRule ?? EvaluatorRules.Leader;
        var evaluator = Resolve(rule, team.EvaluatorRule is null ? type?.EvaluatorEmployeeId : team.EvaluatorEmployeeId, type?.EvaluatorMemberRole, team, members);
        var leaderRule = type?.LeaderEvaluatorRule ?? EvaluatorRules.Same;
        var leaderEvaluator = leaderRule == EvaluatorRules.Same
            ? evaluator
            : Resolve(leaderRule, type?.LeaderEvaluatorEmployeeId, type?.LeaderEvaluatorMemberRole, team, members);
        if (rule != EvaluatorRules.Administrator && evaluator is null)
        {
            return Error.Rule("teams.noEvaluator", "teams.errors.noEvaluator");
        }

        var number = await numbering.NextAsync(db, TeamPermissions.EvaluationNumbering, new NumberingContext(today), ct).ConfigureAwait(false);
        var evaluation = TeamEvaluation.Open(team.Id, number, cycle, from, to, template.Id, trigger, context.User.UserId(), clock.UtcNow, rule, evaluator,
            leaderEvaluator == evaluator ? null : leaderEvaluator, team.Workload, criteria);
        db.Evaluations.Add(evaluation);
        var attendance = await activity.AttendanceAsync(team.Id, evaluated, from, to, ct).ConfigureAwait(false);
        foreach (var member in evaluated)
        {
            var isLeader = roles.FirstOrDefault(r => r.Code == member.RoleCode)?.IsLeader == true;
            db.MemberEvaluations.Add(MemberEvaluation.Create(evaluation.Id, team.Id, member, isLeader, attendance.GetValueOrDefault(member.Id)));
        }

        var applied = team.Apply(TeamActions.OpenEvaluation);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        if (closeAfter)
        {
            team.RequestCloseAfterEvaluation();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (evaluator is { } ev)
        {
            await roleSync.GrantAsync(team.Id, ev, TeamPermissions.EvaluatorRole, ct).ConfigureAwait(false);
        }

        if (evaluation.LeaderEvaluatorEmployeeId is { } le)
        {
            await roleSync.GrantAsync(team.Id, le, TeamPermissions.LeaderEvaluatorRole, ct).ConfigureAwait(false);
        }

        var link = $"/teams/evaluations/{evaluation.Id:D}";
        if (rule == EvaluatorRules.Administrator)
        {
            var holders = await roleHolders.GetHoldersAsync(TeamPermissions.AdministratorRoleKey, ct).ConfigureAwait(false);
            var recipients = holders.UserIds.Select(Platform.Notifications.NotificationRecipient.User).ToList();
            await notifier.SendToUsersAsync(TeamNotifications.EvaluationOpened, team, recipients, null, cycle, null, db, ct, link).ConfigureAwait(false);
        }

        await notifier.SendAsync(TeamNotifications.EvaluationOpened, team, [evaluator, evaluation.LeaderEvaluatorEmployeeId], null, cycle, null, db, ct, link)
            .ConfigureAwait(false);
        return evaluation;
    }

    private static Guid? Resolve(string rule, Guid? employee, string? memberRole, Team team, IReadOnlyList<TeamMember> members) => rule switch
    {
        EvaluatorRules.Leader => team.LeaderEmployeeId,
        EvaluatorRules.Sponsor => team.SponsorEmployeeId,
        EvaluatorRules.Employee => employee,
        EvaluatorRules.MemberRole => members.FirstOrDefault(m => m.IsActive && m.RoleCode == memberRole && m.EmployeeId is not null)?.EmployeeId,
        _ => null,
    };
}

public sealed record CriterionInput(string Code, string? Level, string? Comment, string? OverrideJustification);

public sealed record MemberAnswersInput(Guid Id, IReadOnlyDictionary<string, int>? Answers, string? Comment);

public sealed record ListEvaluations(Guid TeamId) : IQuery<IReadOnlyList<EvaluationSummaryDto>>;

public sealed record OpenEvaluation(Guid TeamId, int? Year) : ICommand<EvaluationSummaryDto>;

public sealed record GetEvaluation(Guid Id) : IQuery<EvaluationDto>;

public sealed record GetEvaluationStatus(Guid Id) : IQuery<EvaluationStatusDto>;

public sealed record SaveEvaluation(
    Guid Id, IReadOnlyList<CriterionInput>? Criteria, string? Workload, IReadOnlyList<Guid>? EvidenceFileIds, IReadOnlyList<MemberAnswersInput>? Members, uint Version)
    : ICommand<EvaluationDto>;

public sealed record SubmitEvaluation(Guid Id, uint Version, string? Comment) : ICommand<EvaluationDto>;

public sealed record CancelEvaluation(Guid Id, string Reason, uint Version) : ICommand<EvaluationDto>;

public sealed record RewardInput(Guid Id, decimal ApprovedReward, string? AdjustmentReason, bool Certificate, bool FinancialReward);

public sealed record UpdateRewards(Guid Id, IReadOnlyList<RewardInput> Rewards, uint Version) : ICommand<EvaluationDto>;

public sealed record SubmitRewards(Guid Id, uint Version, string? Comment) : ICommand<EvaluationDto>;

public sealed class SaveEvaluationValidator : AbstractValidator<SaveEvaluation>
{
    public SaveEvaluationValidator()
    {
        RuleFor(x => x.Workload).Must(w => w is null || Workloads.All.Contains(w)).WithMessage("validation.unknownCode");
        RuleForEach(x => x.Criteria).ChildRules(c =>
        {
            c.RuleFor(x => x.Comment).MaximumLength(4000);
            c.RuleFor(x => x.OverrideJustification).MaximumLength(4000);
        });
        RuleForEach(x => x.Members).ChildRules(m => m.RuleFor(x => x.Comment).MaximumLength(4000));
    }
}

public sealed class CancelEvaluationValidator : AbstractValidator<CancelEvaluation>
{
    public CancelEvaluationValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed class UpdateRewardsValidator : AbstractValidator<UpdateRewards>
{
    public UpdateRewardsValidator() => RuleForEach(x => x.Rewards).ChildRules(r =>
    {
        r.RuleFor(x => x.ApprovedReward).GreaterThanOrEqualTo(0);
        r.RuleFor(x => x.AdjustmentReason).MaximumLength(2000);
    });
}

/// <summary>The evaluation wizard, HR review and rewards (SPEC §4.5 T3–T4).</summary>
internal sealed class EvaluationHandlers(
    TeamsDbContext db, TeamReader reader, EvaluationOpener opener, EvaluationReader evaluations, IRequestContext context, IApprovalEngine engine,
    Platform.Files.IFileService files, IClock clock)
    : IQueryHandler<ListEvaluations, IReadOnlyList<EvaluationSummaryDto>>,
        ICommandHandler<OpenEvaluation, EvaluationSummaryDto>,
        IQueryHandler<GetEvaluation, EvaluationDto>,
        IQueryHandler<GetEvaluationStatus, EvaluationStatusDto>,
        ICommandHandler<SaveEvaluation, EvaluationDto>,
        ICommandHandler<SubmitEvaluation, EvaluationDto>,
        ICommandHandler<CancelEvaluation, EvaluationDto>,
        ICommandHandler<UpdateRewards, EvaluationDto>,
        ICommandHandler<SubmitRewards, EvaluationDto>
{
    public async Task<Result<IReadOnlyList<EvaluationSummaryDto>>> HandleAsync(ListEvaluations query, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(query.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var results = await reader.CanAsync(context.User, TeamPermissions.Results, team, cancellationToken).ConfigureAwait(false);
        var list = await db.Evaluations.AsNoTracking().Where(e => e.TeamId == team.Id).OrderByDescending(e => e.OpenedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await evaluations.SummariesAsync(list, results, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationSummaryDto>> HandleAsync(OpenEvaluation command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.TeamId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.EvaluationOpen, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var opened = await opener.OpenAsync(team, EvaluationTriggers.Manual, command.Year, closeAfter: false, cancellationToken).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await evaluations.SummariesAsync([opened.Value], true, cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<EvaluationStatusDto>> HandleAsync(GetEvaluationStatus query, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(query.Id, false, cancellationToken).ConfigureAwait(false);
        return team is null || evaluation is null
            ? Error.NotFound()
            : new EvaluationStatusDto(evaluation.Id, evaluation.TeamId, evaluation.Number, evaluation.Cycle, evaluation.Status, evaluation.OpenedAt, evaluation.ApprovedAt);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(GetEvaluation query, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(query.Id, false, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Results, team, cancellationToken).ConfigureAwait(false)
            && !await evaluations.IsEvaluatorAsync(team, evaluation, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden("teams.resultsHidden");
        }

        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(SaveEvaluation command, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        var isEvaluator = await evaluations.IsEvaluatorAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
        var isLeaderEvaluator = evaluation.LeaderEvaluatorEmployeeId is { } le && le == context.User.EmployeeIdOf()
            && await reader.CanAsync(context.User, TeamPermissions.Evaluate, team, cancellationToken).ConfigureAwait(false);
        if (!isEvaluator && !isLeaderEvaluator)
        {
            return await reader.CanAsync(context.User, TeamPermissions.Results, team, cancellationToken).ConfigureAwait(false) ? Error.Forbidden() : Error.Forbidden("teams.resultsHidden");
        }

        if (!EvaluationStatuses.IsEditable(evaluation.Status))
        {
            return Error.Rule("teams.evaluationLocked", "teams.errors.evaluationLocked");
        }

        var template = (await db.Templates.AsNoTracking().FirstAsync(t => t.Id == evaluation.TemplateId, cancellationToken).ConfigureAwait(false)).Read();
        var criteria = evaluation.ReadCriteria().ToList();
        if (isEvaluator && command.Criteria is { } inputs)
        {
            for (var i = 0; i < criteria.Count; i++)
            {
                var current = criteria[i];
                var definition = template.Criteria.First(c => c.Code == current.Code);
                if (inputs.FirstOrDefault(c => c.Code == current.Code) is not { } input)
                {
                    continue;
                }

                var level = input.Level is null ? null : definition.Levels.FirstOrDefault(l => l.Code == input.Level);
                if (input.Level is not null && level is null)
                {
                    return Error.Validation([new FieldError($"criteria[{i}].level", "validation.unknownCode", "validation.unknownCode")]);
                }

                criteria[i] = current with { Level = level?.Code, Score = level?.Score, Comment = input.Comment, OverrideJustification = input.OverrideJustification };
            }
        }

        var evidence = isEvaluator ? command.EvidenceFileIds ?? evaluation.EvidenceFileIds : evaluation.EvidenceFileIds;
        foreach (var id in evidence.Except(evaluation.EvidenceFileIds))
        {
            var file = await files.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (file is null || file.OwnerType != TeamEvaluation.EntityType || file.OwnerId != evaluation.Id.ToString("D"))
            {
                return Error.Validation([new FieldError("evidenceFileIds", "validation.unknownReference", "validation.unknownReference")]);
            }
        }

        var rows = await db.MemberEvaluations.Where(m => m.EvaluationId == evaluation.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var input in command.Members ?? [])
        {
            var row = rows.FirstOrDefault(r => r.Id == input.Id);
            if (row is null)
            {
                return Error.Validation([new FieldError("members", "validation.unknownReference", "validation.unknownReference")]);
            }

            var mayAnswer = EvaluationReader.MayAnswer(evaluation, row, isEvaluator, isLeaderEvaluator);
            if (!mayAnswer)
            {
                continue;
            }

            var answers = (input.Answers ?? new Dictionary<string, int>()).Where(a => template.Questions.Any(q => q.Code == a.Key))
                .ToDictionary(a => a.Key, a => a.Value);
            if (answers.Values.Any(v => v is < TemplateContent.AnswerMin or > TemplateContent.AnswerMax))
            {
                return Error.Validation([new FieldError($"members[{input.Id:D}].answers", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = TemplateContent.AnswerMin, ["to"] = TemplateContent.AnswerMax })]);
            }

            var points = EvaluationCalculator.Points(template, answers);
            row.Record(answers, input.Comment, points, EvaluationCalculator.Grade(template, points)?.Code, context.User.EmployeeIdOf(), clock.UtcNow);
        }

        var score = EvaluationCalculator.TeamScore(template, criteria);
        var flags = InconsistencyRules.Flags(template, criteria, rows.Select(r => (r.Id, r.Points, r.AttendanceRate)).ToList());
        db.Entry(evaluation).Property(x => x.Version).OriginalValue = command.Version;
        var saved = evaluation.SaveDraft(criteria, score, isEvaluator ? command.Workload ?? evaluation.Workload : evaluation.Workload, evidence, flags);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(SubmitEvaluation command, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await evaluations.IsEvaluatorAsync(team, evaluation, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var template = (await db.Templates.AsNoTracking().FirstAsync(t => t.Id == evaluation.TemplateId, cancellationToken).ConfigureAwait(false)).Read();
        var rows = await db.MemberEvaluations.AsNoTracking().Where(m => m.EvaluationId == evaluation.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var errors = EvaluationCalculator.ValidateSubmission(template, evaluation.ReadCriteria(), evaluation.EvidenceFileIds.Length,
            rows.Select(r => (r.Id, r.Points)).ToList());
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(evaluation).Property(x => x.Version).OriginalValue = command.Version;
        var applied = evaluation.Apply(EvaluationActions.Submit, clock.UtcNow, context.User.EmployeeIdOf());
        var teamApplied = applied.IsSuccess ? team.Apply(TeamActions.EvaluationSubmitted) : applied;
        if (teamApplied.IsFailure)
        {
            return teamApplied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var started = await engine.SubmitAsync(
            new ApprovalSubmission(EvaluationApprovalProcesses.EvaluationKey,
                new ApprovalSubject(TeamPermissions.ResourceType, team.Id, evaluation.Number, team.NameAr, team.NameEn, team.LeaderEmployeeId, team.RequestedByUserId, team.OwningUnitId),
                new Dictionary<string, object?> { ["score"] = evaluation.Score }, command.Comment),
            db, cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(CancelEvaluation command, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.EvaluationOpen, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(evaluation).Property(x => x.Version).OriginalValue = command.Version;
        var applied = evaluation.Apply(EvaluationActions.Cancel, clock.UtcNow, comment: command.Reason);
        var teamApplied = applied.IsSuccess ? team.Apply(TeamActions.EvaluationCancelled) : applied;
        if (teamApplied.IsFailure)
        {
            return teamApplied.Error;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await evaluations.RevokeEvaluatorsAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(UpdateRewards command, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.RewardManage, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (evaluation.RewardStatus != RewardStatuses.Draft)
        {
            return Error.Rule("teams.rewardsLocked", "teams.errors.rewardsLocked");
        }

        var rows = await db.MemberEvaluations.Where(m => m.EvaluationId == evaluation.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<FieldError>();
        foreach (var input in command.Rewards)
        {
            var row = rows.FirstOrDefault(r => r.Id == input.Id);
            if (row is null)
            {
                errors.Add(new FieldError($"rewards[{input.Id:D}]", "validation.unknownReference", "validation.unknownReference"));
                continue;
            }

            if (input.ApprovedReward != row.CalculatedReward && string.IsNullOrWhiteSpace(input.AdjustmentReason))
            {
                errors.Add(new FieldError($"rewards[{input.Id:D}].adjustmentReason", "teams.errors.adjustmentReason", "teams.errors.adjustmentReason"));
                continue;
            }

            row.Adjust(input.ApprovedReward, input.AdjustmentReason, input.Certificate, input.FinancialReward && input.ApprovedReward > 0);
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(evaluation).Property(x => x.Version).OriginalValue = command.Version;
        evaluation.SetRewards(RewardStatuses.Draft, rows.Sum(r => r.FinancialReward ? r.ApprovedReward ?? 0 : 0), null);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EvaluationDto>> HandleAsync(SubmitRewards command, CancellationToken cancellationToken)
    {
        var (team, evaluation) = await evaluations.LoadAsync(command.Id, true, cancellationToken).ConfigureAwait(false);
        if (team is null || evaluation is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.RewardManage, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (evaluation.RewardStatus != RewardStatuses.Draft)
        {
            return Error.Rule("teams.rewardsLocked", "teams.errors.rewardsLocked");
        }

        db.Entry(evaluation).Property(x => x.Version).OriginalValue = command.Version;
        evaluation.SetRewards(RewardStatuses.Pending, evaluation.RewardTotal, null);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var started = await engine.SubmitAsync(
            new ApprovalSubmission(EvaluationApprovalProcesses.RewardKey,
                new ApprovalSubject(TeamPermissions.ResourceType, team.Id, evaluation.Number, team.NameAr, team.NameEn, team.LeaderEmployeeId, team.RequestedByUserId, team.OwningUnitId),
                new Dictionary<string, object?> { ["total"] = evaluation.RewardTotal ?? 0 }, command.Comment),
            db, cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await evaluations.ToDtoAsync(team, evaluation, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Loading and mapping evaluations (shared by handlers, hooks, AI and MCP).</summary>
internal sealed class EvaluationReader(TeamsDbContext db, TeamReader reader, TeamRoleSync roleSync, IRequestContext context, IClock clock)
{
    public async Task<(Team? Team, TeamEvaluation? Evaluation)> LoadAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var evaluation = tracked
            ? await db.Evaluations.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false)
            : await db.Evaluations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (evaluation is null)
        {
            return (null, null);
        }

        var team = await reader.FindAsync(evaluation.TeamId, context.User, ct, tracked).ConfigureAwait(false);
        return team is null ? (null, null) : (team, evaluation);
    }

    public async Task<bool> IsEvaluatorAsync(Team team, TeamEvaluation evaluation, CancellationToken ct)
    {
        if (evaluation.EvaluatorRule == EvaluatorRules.Administrator)
        {
            return await reader.CanAsync(context.User, TeamPermissions.EvaluationOpen, team, ct).ConfigureAwait(false);
        }

        return evaluation.EvaluatorEmployeeId is { } e && e == context.User.EmployeeIdOf()
            && await reader.CanAsync(context.User, TeamPermissions.Evaluate, team, ct).ConfigureAwait(false);
    }

    /// <summary>The leader's own row goes to the leader evaluator when one is configured; everything else to the evaluator.</summary>
    public static bool MayAnswer(TeamEvaluation evaluation, MemberEvaluation row, bool isEvaluator, bool isLeaderEvaluator) =>
        evaluation.LeaderEvaluatorEmployeeId is not null && row.IsLeader ? isLeaderEvaluator : isEvaluator;

    public async Task RevokeEvaluatorsAsync(Team team, TeamEvaluation evaluation, CancellationToken ct)
    {
        if (evaluation.EvaluatorEmployeeId is { } e)
        {
            await roleSync.RevokeAsync(team.Id, e, TeamPermissions.EvaluatorRole, ct).ConfigureAwait(false);
        }

        if (evaluation.LeaderEvaluatorEmployeeId is { } l)
        {
            await roleSync.RevokeAsync(team.Id, l, TeamPermissions.LeaderEvaluatorRole, ct).ConfigureAwait(false);
        }
    }

    public async Task<List<EvaluationSummaryDto>> SummariesAsync(IReadOnlyList<TeamEvaluation> list, bool results, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(list.Select(e => e.EvaluatorEmployeeId), ct).ConfigureAwait(false);
        return list.Select(e => new EvaluationSummaryDto(e.Id, e.TeamId, e.Number, e.Cycle, e.PeriodStart, e.PeriodEnd, e.Trigger, e.Status, e.OpenedAt, e.SubmittedAt,
            e.ApprovedAt, TeamReader.Person(e.EvaluatorEmployeeId, people), results ? e.Score : null, results ? e.RewardStatus : null, results ? e.RewardTotal : null)).ToList();
    }

    public async Task<RewardBudgetDto?> BudgetAsync(TeamEvaluation evaluation, CancellationToken ct)
    {
        var year = RewardYear(evaluation, clock.Today);
        var matrix = evaluation.RewardMatrixId is { } id
            ? await db.RewardMatrices.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false)
            : await PublishedMatrixAsync(year, ct).ConfigureAwait(false);
        if (matrix is null)
        {
            return null;
        }

        var content = matrix.Read();
        var yearIds = await db.RewardMatrices.AsNoTracking().Where(m => m.Year == matrix.Year).Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        var used = await db.Evaluations.AsNoTracking()
            .Where(e => e.RewardStatus == RewardStatuses.Approved && e.RewardMatrixId != null && yearIds.Contains(e.RewardMatrixId.Value))
            .SumAsync(e => e.RewardTotal ?? 0, ct).ConfigureAwait(false);
        return new RewardBudgetDto(matrix.Year, content.AnnualBudget, used, content.AnnualBudget - used, content.Currency, matrix.Id, matrix.VersionNo);
    }

    public Task<RewardMatrix?> PublishedMatrixAsync(int year, CancellationToken ct) =>
        db.RewardMatrices.AsNoTracking().Where(m => m.Status == VersionStatuses.Published && m.Year == year).OrderByDescending(m => m.VersionNo).FirstOrDefaultAsync(ct);

    public static int RewardYear(TeamEvaluation evaluation, DateOnly today) =>
        int.TryParse(evaluation.Cycle, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : (evaluation.ApprovedAt?.Year ?? today.Year);

    public async Task<EvaluationDto> ToDtoAsync(Team team, TeamEvaluation evaluation, CancellationToken ct)
    {
        var template = await db.Templates.AsNoTracking().FirstAsync(t => t.Id == evaluation.TemplateId, ct).ConfigureAwait(false);
        var content = template.Read();
        var rows = await db.MemberEvaluations.AsNoTracking().Where(m => m.EvaluationId == evaluation.Id).ToListAsync(ct).ConfigureAwait(false);
        var members = await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        rows = rows.OrderBy(r => members.GetValueOrDefault(r.MemberId)?.Position ?? 0).ToList();
        var people = await reader.PeopleAsync(rows.Select(r => r.EmployeeId).Concat(rows.Select(r => r.EvaluatedByEmployeeId))
            .Concat([evaluation.EvaluatorEmployeeId, evaluation.LeaderEvaluatorEmployeeId]), ct).ConfigureAwait(false);
        var roles = await reader.RolesAsync(ct).ConfigureAwait(false);
        var isEvaluator = await IsEvaluatorAsync(team, evaluation, ct).ConfigureAwait(false);
        var isLeaderEvaluator = evaluation.LeaderEvaluatorEmployeeId is { } le && le == context.User.EmployeeIdOf();
        var editable = EvaluationStatuses.IsEditable(evaluation.Status);
        var canRewards = await reader.CanAsync(context.User, TeamPermissions.RewardManage, team, ct).ConfigureAwait(false);
        var canOpen = await reader.CanAsync(context.User, TeamPermissions.EvaluationOpen, team, ct).ConfigureAwait(false);
        var previous = await db.Evaluations.AsNoTracking().Where(e => e.TeamId == team.Id && e.Id != evaluation.Id && e.Status == EvaluationStatuses.Approved)
            .OrderByDescending(e => e.ApprovedAt).Take(5).ToListAsync(ct).ConfigureAwait(false);
        var memberDtos = rows.Select(r =>
        {
            var role = roles.FirstOrDefault(x => x.Code == r.RoleCode);
            return new MemberEvaluationDto(r.Id, r.MemberId, TeamReader.Person(r.EmployeeId, people), r.ExternalName, r.RoleCode, role?.NameAr, role?.NameEn, r.IsLeader,
                r.ReadAnswers(), r.Comment, r.Points, r.Grade, r.AttendanceRate, TeamReader.Person(r.EvaluatedByEmployeeId, people),
                editable && MayAnswer(evaluation, r, isEvaluator, isLeaderEvaluator), r.CalculatedReward, r.ApprovedReward, r.AdjustmentReason, r.Certificate,
                r.CertificateNumber, r.CertificateFileId, r.FinancialReward, r.Version);
        }).ToList();
        var summary = (await SummariesAsync([evaluation], true, ct).ConfigureAwait(false))[0];
        return new EvaluationDto(summary, team.Number, team.NameAr, team.NameEn, content, template.Year, template.VersionNo, evaluation.ReadCriteria(), evaluation.Workload,
            evaluation.EvidenceFileIds, evaluation.ReadFlags(), memberDtos, TeamReader.Person(evaluation.LeaderEvaluatorEmployeeId, people), evaluation.ReturnComment,
            evaluation.RewardStatus == RewardStatuses.None ? null : await BudgetAsync(evaluation, ct).ConfigureAwait(false),
            await SummariesAsync(previous, true, ct).ConfigureAwait(false),
            new EvaluationPermissionsDto(editable && (isEvaluator || isLeaderEvaluator), editable && isEvaluator, editable && canOpen,
                canRewards && evaluation.RewardStatus == RewardStatuses.Draft, canRewards && evaluation.RewardStatus == RewardStatuses.Draft,
                EvaluationCalculator.EvidenceRequired(content, evaluation.Score)),
            evaluation.Version);
    }
}

/// <summary>Evaluation and reward approvals move the cycle, the team and the rewards (inside the engine's transaction).</summary>
internal sealed class EvaluationApprovalHook(
    TeamsDbContext db, EvaluationReaderFactory readers, TeamNotifier notifier, INumberingService numbering, IOutbox outbox, IClock clock) : IApprovalHook
{
    public const string RenderCertificates = "teams.render-certificates";

    public IReadOnlyCollection<string> ProcessKeys { get; } = [EvaluationApprovalProcesses.EvaluationKey, EvaluationApprovalProcesses.RewardKey];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Returned or ApprovalEventKinds.Rejected))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        return approvalEvent.ProcessKey == EvaluationApprovalProcesses.EvaluationKey
            ? await OnEvaluationAsync(approvalEvent, team, transactionContext, cancellationToken).ConfigureAwait(false)
            : await OnRewardAsync(approvalEvent, team, transactionContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> OnEvaluationAsync(ApprovalEvent approvalEvent, Team team, object transactionContext, CancellationToken ct)
    {
        var evaluation = await db.Evaluations.FirstOrDefaultAsync(e => e.TeamId == team.Id && e.Status == EvaluationStatuses.Submitted, ct).ConfigureAwait(false);
        if (evaluation is null)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        var link = $"/teams/evaluations/{evaluation.Id:D}";
        if (approvalEvent.Kind != ApprovalEventKinds.Approved)
        {
            var returned = evaluation.Apply(EvaluationActions.Return, now, comment: approvalEvent.Comment);
            var teamReturned = returned.IsSuccess ? team.Apply(TeamActions.EvaluationReturned) : returned;
            if (teamReturned.IsFailure)
            {
                return teamReturned.Error;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.SendAsync(TeamNotifications.EvaluationReturned, team, [evaluation.EvaluatorEmployeeId], null, approvalEvent.Comment, null, transactionContext, ct, link)
                .ConfigureAwait(false);
            return Result.Success();
        }

        var approved = evaluation.Apply(EvaluationActions.Approve, now);
        if (approved.IsFailure)
        {
            return approved.Error;
        }

        var close = !team.Permanent && (team.CloseAfterEvaluation || evaluation.Cycle == Cycles.Completion || team.EndDate < clock.Today);
        var teamApplied = team.Apply(close ? TeamActions.EvaluationApprovedClose : TeamActions.EvaluationApprovedContinue, at: now);
        if (teamApplied.IsFailure)
        {
            return teamApplied.Error;
        }

        // Rewards from the matrix published for the cycle's year (ADR 0041), as a draft HR reviews.
        var reader = readers.Create();
        var matrix = await reader.PublishedMatrixAsync(EvaluationReader.RewardYear(evaluation, clock.Today), ct).ConfigureAwait(false);
        var rows = await db.MemberEvaluations.Where(m => m.EvaluationId == evaluation.Id).ToListAsync(ct).ConfigureAwait(false);
        if (matrix is not null)
        {
            var content = matrix.Read();
            foreach (var row in rows)
            {
                var (amount, certificate) = RewardCalculator.Calculate(content, row.Grade, evaluation.Workload, row.RoleCode);
                row.SetCalculatedReward(amount, certificate);
            }

            evaluation.SetRewards(RewardStatuses.Draft, rows.Sum(r => r.FinancialReward ? r.ApprovedReward ?? 0 : 0), matrix.Id);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await reader.RevokeEvaluatorsAsync(team, evaluation, ct).ConfigureAwait(false);
        await notifier.SendAsync(TeamNotifications.EvaluationApproved, team, [evaluation.EvaluatorEmployeeId], null, evaluation.Cycle, null, transactionContext, ct, link)
            .ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result> OnRewardAsync(ApprovalEvent approvalEvent, Team team, object transactionContext, CancellationToken ct)
    {
        var evaluation = await db.Evaluations.FirstOrDefaultAsync(e => e.TeamId == team.Id && e.RewardStatus == RewardStatuses.Pending, ct).ConfigureAwait(false);
        if (evaluation is null)
        {
            return Error.NotFound();
        }

        if (approvalEvent.Kind != ApprovalEventKinds.Approved)
        {
            evaluation.SetRewards(RewardStatuses.Draft, evaluation.RewardTotal, null);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }

        evaluation.SetRewards(RewardStatuses.Approved, evaluation.RewardTotal, null);
        var rows = await db.MemberEvaluations.Where(m => m.EvaluationId == evaluation.Id && m.Certificate).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows.Where(r => r.CertificateNumber is null))
        {
            row.SetCertificate(await numbering.NextAsync(db, TeamPermissions.CertificateNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false), null);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (rows.Count > 0)
        {
            await outbox.EnqueueAsync(db, new OutboxEnvelope(RenderCertificates, new RenderCertificatesMessage(evaluation.Id)), ct).ConfigureAwait(false);
        }

        var link = $"/teams/evaluations/{evaluation.Id:D}";
        var holders = await db.Evaluations.AsNoTracking().Where(e => e.Id == evaluation.Id).Select(e => e.CreatedBy).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (holders is { } opener)
        {
            await notifier.SendToUsersAsync(TeamNotifications.RewardsApproved, team, [Platform.Notifications.NotificationRecipient.User(opener)], null, evaluation.Cycle, null,
                transactionContext, ct, link).ConfigureAwait(false);
        }

        return Result.Success();
    }
}

public sealed record RenderCertificatesMessage(Guid EvaluationId);

/// <summary>Creates an <see cref="EvaluationReader"/> lazily (the hook is resolved by the engine; the reader needs the request context).</summary>
internal sealed class EvaluationReaderFactory(IServiceProvider services)
{
    public EvaluationReader Create() => (EvaluationReader)services.GetService(typeof(EvaluationReader))!;
}
