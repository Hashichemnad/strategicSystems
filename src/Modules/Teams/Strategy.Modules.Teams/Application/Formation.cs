using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Teams.Application;

/// <summary>Formation approval (SPEC §4.5 T1, Appendix A Q-T5): Department Director of the owning unit → General Manager (ADR 0044).</summary>
public static class FormationApprovalProcess
{
    public const string Key = "teams.formation";

    public static readonly ApprovalProcessDefinition Definition = new(
        Key, TeamPermissions.SystemCode, "اعتماد تشكيل فريق أو لجنة", "Team / committee formation", TeamPermissions.ResourceType, TeamPermissions.Read, "/teams/{id}",
        [
            new("typeCode", ExpressionValueType.Text, "نوع الفريق", "Team type"),
            new("permanent", ExpressionValueType.Boolean, "دائم", "Permanent"),
            new("members", ExpressionValueType.Number, "عدد الأعضاء", "Number of members"),
        ],
        [
            new("department-director", "مدير الإدارة المالكة", "Director of the owning department",
                new ApproverRule(ApproverRuleKinds.DepartmentDirector, Of: ApproverRuleSubjects.Record), SlaHours: 24),
            new("general-manager", "المدير العام", "General Manager",
                new ApproverRule(ApproverRuleKinds.HeadOfUnit, Of: ApproverRuleSubjects.Organisation), SlaHours: 40),
        ],
        TeamPermissions.ApproverRole,
        "تشكيل الفرق واللجان: مدير الإدارة المالكة ثم المدير العام (الإعداد الافتراضي).",
        "Team and committee formation: director of the owning department, then the General Manager (default).");
}

public sealed record MemberInput(
    Guid? Id,
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

public sealed record TeamInput(
    string TypeCode,
    string? NameAr,
    string? NameEn,
    bool Permanent,
    string? Subject,
    string? Objectives,
    string? Mandate,
    Guid? OwningUnitId,
    Guid? SponsorEmployeeId,
    string? ApprovedByCode,
    string? MemoMode,
    string? MemoDraft,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Cadence,
    string? ReportTo,
    string? Workload,
    Guid? ReplacesTeamId,
    string? EvaluatorRule,
    Guid? EvaluatorEmployeeId,
    IReadOnlyList<MemberInput>? Members,
    string? LinkedType = null,
    Guid? LinkedId = null);

public sealed record CreateTeam(TeamInput Team) : ICommand<TeamDto>;

public sealed record UpdateTeam(Guid Id, TeamInput Team, uint Version) : ICommand<TeamDto>;

public sealed record SubmitTeam(Guid Id, uint Version, string? Comment) : ICommand<TeamDto>;

public sealed record ChangeTeamStatus(Guid Id, string Action, uint Version, string? Reason) : ICommand<TeamDto>;

public sealed record SetExternalMemo(Guid Id, string Number, DateOnly Date, Guid FileId, uint Version) : ICommand<TeamDto>;

public sealed record RegenerateMemo(Guid Id) : ICommand<TeamDto>;

public sealed record LinkTeamRecord(Guid Id, string Type, Guid RecordId) : ICommand<TeamDto>;

public sealed record UnlinkTeamRecord(Guid Id, string Type, Guid RecordId, string? Reason) : ICommand<TeamDto>;

public sealed class TeamInputValidator : AbstractValidator<TeamInput>
{
    public TeamInputValidator()
    {
        RuleFor(x => x.TypeCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameAr).MaximumLength(Team.NameMaxLength);
        RuleFor(x => x.NameEn).MaximumLength(Team.NameMaxLength);
        RuleFor(x => x.Subject).MaximumLength(Team.NameMaxLength * 2);
        RuleFor(x => x.Objectives).MaximumLength(Team.TextMaxLength);
        RuleFor(x => x.Mandate).MaximumLength(Team.TextMaxLength);
        RuleFor(x => x.MemoDraft).MaximumLength(Team.TextMaxLength);
        RuleFor(x => x.ReportTo).MaximumLength(300);
        RuleFor(x => x.MemoMode).Must(m => m is null || MemoModes.All.Contains(m)).WithMessage("validation.unknownCode");
        RuleFor(x => x.Cadence).Must(c => c is null || Cadences.All.Contains(c)).WithMessage("validation.unknownCode");
        RuleFor(x => x.Workload).Must(w => w is null || Workloads.All.Contains(w)).WithMessage("validation.unknownCode");
        RuleFor(x => x.EvaluatorRule).Must(r => r is null || EvaluatorRules.All.Contains(r)).WithMessage("validation.unknownCode");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.StartDate is not null && x.EndDate is not null);
        RuleFor(x => x.Members).Must(m => m is null || m.Count <= 100);
        RuleForEach(x => x.Members).ChildRules(m =>
        {
            m.RuleFor(x => x.RoleCode).NotEmpty().MaximumLength(100);
            m.RuleFor(x => x.ExternalName).NotEmpty().MaximumLength(300).When(x => x.EmployeeId is null);
            m.RuleFor(x => x.ExternalEmail).EmailAddress().MaximumLength(300).When(x => !string.IsNullOrWhiteSpace(x.ExternalEmail));
            m.RuleFor(x => x.ExternalPhone).MaximumLength(50);
            m.RuleFor(x => x.Responsibilities).MaximumLength(4000);
            m.RuleFor(x => x.RequiredSkills).MaximumLength(2000);
            m.RuleFor(x => x.AllocationPercent).InclusiveBetween(0, 1).When(x => x.AllocationPercent is not null);
        });
    }
}

public sealed class CreateTeamValidator : AbstractValidator<CreateTeam>
{
    public CreateTeamValidator() => RuleFor(x => x.Team).NotNull().SetValidator(new TeamInputValidator());
}

public sealed class UpdateTeamValidator : AbstractValidator<UpdateTeam>
{
    public UpdateTeamValidator() => RuleFor(x => x.Team).NotNull().SetValidator(new TeamInputValidator());
}

public sealed class SetExternalMemoValidator : AbstractValidator<SetExternalMemo>
{
    public SetExternalMemoValidator() => RuleFor(x => x.Number).NotEmpty().MaximumLength(100);
}

/// <summary>Formation request: create / edit drafts, submit, status actions, memo and links.</summary>
internal sealed class FormationHandlers(
    TeamsDbContext db,
    TeamReader reader,
    TeamRoleSync roleSync,
    TeamQueries queries,
    TeamActivation activation,
    EvaluationOpener opener,
    IRecordRoleService recordRoles,
    IRequestContext context,
    INumberingService numbering,
    IApprovalEngine engine,
    ILinkRegistry links,
    ILinkStore linkStore,
    IOrgDirectory org,
    ILookupService lookups,
    Platform.Files.IFileService files,
    IClock clock)
    : ICommandHandler<CreateTeam, TeamDto>,
        ICommandHandler<UpdateTeam, TeamDto>,
        ICommandHandler<SubmitTeam, TeamDto>,
        ICommandHandler<ChangeTeamStatus, TeamDto>,
        ICommandHandler<SetExternalMemo, TeamDto>,
        ICommandHandler<RegenerateMemo, TeamDto>,
        ICommandHandler<LinkTeamRecord, TeamDto>,
        ICommandHandler<UnlinkTeamRecord, TeamDto>
{
    public async Task<Result<TeamDto>> HandleAsync(CreateTeam command, CancellationToken cancellationToken)
    {
        var input = command.Team;
        var type = await reader.TypeAsync(input.TypeCode, cancellationToken).ConfigureAwait(false);
        var checkedInput = await CheckAsync(input, type, null, cancellationToken).ConfigureAwait(false);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error;
        }

        if (input.LinkedType is not null && input.LinkedId is { } linkedId
            && !await CanLinkAsync(type!, input.LinkedType, linkedId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("linkedId", "teams.errors.cannotLink", "teams.errors.cannotLink")]);
        }

        var user = context.User;
        var team = Team.Create(type!.Code, Permanent(type, input.Permanent), user.EmployeeIdOf(), user.UserId());
        team.Edit(Values(input, type));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        team.AssignNumber(await numbering.NextAsync(db, TeamPermissions.TeamNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false));
        db.Teams.Add(team);
        ApplyMembers(team, input.Members ?? [], []);
        await roleSync.SyncAsync(team, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (user.EmployeeIdOf() is { } requester)
        {
            await recordRoles.GrantInTransactionAsync(db, TeamPermissions.ResourceType, team.Id, RecordPrincipalKinds.Employee, requester, TeamPermissions.RequesterRole,
                cancellationToken).ConfigureAwait(false);
        }
        else if (user.UserId() is { } requesterUser)
        {
            await recordRoles.GrantInTransactionAsync(db, TeamPermissions.ResourceType, team.Id, RecordPrincipalKinds.User, requesterUser, TeamPermissions.RequesterRole,
                cancellationToken).ConfigureAwait(false);
        }

        if (input.LinkedType is not null && input.LinkedId is { } link)
        {
            await linkStore.AddAsync(LinkCapabilities.TeamAble, new LinkRef(TeamPermissions.ResourceType, team.Id), new LinkRef(input.LinkedType, link), db, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(UpdateTeam command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var draft = team.Status == TeamStatuses.Draft;
        if (!await reader.CanAsync(context.User, draft ? TeamPermissions.Submit : TeamPermissions.Manage, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (TeamStatuses.IsFinished(team.Status) || team.Status == TeamStatuses.PendingApproval)
        {
            return Error.Rule("teams.locked", "teams.errors.locked");
        }

        var input = command.Team with { TypeCode = team.TypeCode };
        var type = await reader.TypeAsync(team.TypeCode, cancellationToken).ConfigureAwait(false);
        var checkedInput = await CheckAsync(input, type, team.Id, cancellationToken).ConfigureAwait(false);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error;
        }

        db.Entry(team).Property(x => x.Version).OriginalValue = command.Version;
        team.Edit(Values(input, type!) with { Permanent = draft ? Permanent(type!, input.Permanent) : team.Permanent });
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (draft && input.Members is { } members)
        {
            var existing = await db.Members.Where(m => m.TeamId == team.Id && m.Status == MemberStatuses.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
            ApplyMembers(team, members, existing);
        }

        await roleSync.SyncAsync(team, cancellationToken).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(SubmitTeam command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Submit, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var type = await reader.TypeAsync(team.TypeCode, cancellationToken).ConfigureAwait(false);
        var members = await reader.MembersAsync(team.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
        var roles = await reader.RolesAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<FieldError>();
        if (team.NameAr is null && team.NameEn is null)
        {
            errors.Add(new FieldError("nameEn", "validation.notEmpty", "validation.notEmpty"));
        }

        if (team.OwningUnitId is null)
        {
            errors.Add(new FieldError("owningUnitId", "validation.notEmpty", "validation.notEmpty"));
        }

        if (team.StartDate is null)
        {
            errors.Add(new FieldError("startDate", "validation.notEmpty", "validation.notEmpty"));
        }

        if (!team.Permanent && team.EndDate is null)
        {
            errors.Add(new FieldError("endDate", "teams.errors.endDateRequired", "teams.errors.endDateRequired"));
        }

        if (!members.Any(m => m.EmployeeId is not null && roles.Any(r => r.Code == m.RoleCode && r.IsLeader)))
        {
            errors.Add(new FieldError("members", "teams.errors.leaderRequired", "teams.errors.leaderRequired"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(team).Property(x => x.Version).OriginalValue = command.Version;
        var requiresApproval = type?.RequiresApproval ?? true;
        var applied = team.Apply(requiresApproval ? TeamActions.Submit : TeamActions.Activate, at: clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
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

        if (requiresApproval)
        {
            var started = await engine.SubmitAsync(
                new ApprovalSubmission(
                    FormationApprovalProcess.Key,
                    new ApprovalSubject(TeamPermissions.ResourceType, team.Id, team.Number, team.NameAr, team.NameEn, team.LeaderEmployeeId, team.RequestedByUserId,
                        team.OwningUnitId),
                    new Dictionary<string, object?> { ["typeCode"] = team.TypeCode, ["permanent"] = team.Permanent, ["members"] = members.Count },
                    command.Comment),
                db,
                cancellationToken).ConfigureAwait(false);
            if (started.IsFailure)
            {
                return started.Error;
            }
        }
        else
        {
            await activation.ActivateAsync(team, type, db, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(ChangeTeamStatus command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var action = command.Action;
        var permission = action is TeamActions.Cancel or TeamActions.Withdraw ? TeamPermissions.Submit : TeamPermissions.Manage;
        if (action is not (TeamActions.Cancel or TeamActions.Withdraw or TeamActions.Deactivate or TeamActions.Reactivate or TeamActions.Close))
        {
            return Error.Validation([new FieldError("action", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (!await reader.CanAsync(context.User, permission, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (action is TeamActions.Cancel or TeamActions.Withdraw or TeamActions.Deactivate or TeamActions.Close && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(team).Property(x => x.Version).OriginalValue = command.Version;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (action == TeamActions.Cancel && team.Status == TeamStatuses.PendingApproval)
        {
            action = TeamActions.Withdraw;
            var cancelled = await engine.CancelAsync(FormationApprovalProcess.Key, team.Id, command.Reason!, db, cancellationToken).ConfigureAwait(false);
            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }
        }

        if (action == TeamActions.Close)
        {
            var type = await reader.TypeAsync(team.TypeCode, cancellationToken).ConfigureAwait(false);
            var trigger = EvaluationTriggers.Effective(type?.EvaluationTrigger ?? EvaluationTriggers.None, team.Permanent);
            var evaluatesOnCompletion = !team.Permanent && trigger is EvaluationTriggers.EndDate or EvaluationTriggers.LinkedRecordClosed;
            var completionDone = await db.Evaluations.AnyAsync(
                e => e.TeamId == team.Id && e.Cycle == Cycles.Completion && e.Status != EvaluationStatuses.Cancelled, cancellationToken).ConfigureAwait(false);
            if (evaluatesOnCompletion && !completionDone)
            {
                var opened = await opener.OpenAsync(team, EvaluationTriggers.Manual, null, closeAfter: true, cancellationToken).ConfigureAwait(false);
                if (opened.IsFailure)
                {
                    return opened.Error;
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        var applied = team.Apply(action, command.Reason, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(SetExternalMemo command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var permission = team.Status == TeamStatuses.Draft ? TeamPermissions.Submit : TeamPermissions.Manage;
        if (!await reader.CanAsync(context.User, permission, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var file = await files.FindAsync(command.FileId, cancellationToken).ConfigureAwait(false);
        if (file is null || file.OwnerType != TeamPermissions.ResourceType || file.OwnerId != team.Id.ToString("D") || file.ContentType != "application/pdf")
        {
            return Error.Validation([new FieldError("fileId", "teams.errors.memoFile", "teams.errors.memoFile")]);
        }

        db.Entry(team).Property(x => x.Version).OriginalValue = command.Version;
        team.UseExternalMemo(command.Number, command.Date, command.FileId);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(RegenerateMemo command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Manage, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (team.ActivatedAt is null || team.MemoMode != MemoModes.Generate)
        {
            return Error.Rule("teams.memoNotGenerated", "teams.errors.memoNotGenerated");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await activation.EnqueueMemoAsync(team, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(LinkTeamRecord command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var permission = team.Status == TeamStatuses.Draft ? TeamPermissions.Submit : TeamPermissions.Manage;
        if (!await reader.CanAsync(context.User, permission, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var type = await reader.TypeAsync(team.TypeCode, cancellationToken).ConfigureAwait(false);
        if (TeamStatuses.IsFinished(team.Status) || type is null || !await CanLinkAsync(type, command.Type, command.RecordId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("recordId", "teams.errors.cannotLink", "teams.errors.cannotLink")]);
        }

        var from = new LinkRef(TeamPermissions.ResourceType, team.Id);
        if ((await linkStore.ListFromAsync(from, LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false)).Any(l => l.ToType == command.Type && l.ToId == command.RecordId))
        {
            return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
        }

        await linkStore.AddAsync(LinkCapabilities.TeamAble, from, new LinkRef(command.Type, command.RecordId), null, cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamDto>> HandleAsync(UnlinkTeamRecord command, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(command.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var permission = team.Status == TeamStatuses.Draft ? TeamPermissions.Submit : TeamPermissions.Manage;
        if (!await reader.CanAsync(context.User, permission, team, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        await linkStore.RemoveAsync(LinkCapabilities.TeamAble, new LinkRef(TeamPermissions.ResourceType, team.Id), new LinkRef(command.Type, command.RecordId),
            command.Reason ?? "unlinked", null, cancellationToken).ConfigureAwait(false);
        return await queries.GetAsync(team.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CanLinkAsync(TeamType type, string linkedType, Guid linkedId, CancellationToken ct) =>
        (type.LinkableTypes.Length == 0 || type.LinkableTypes.Contains(linkedType, StringComparer.Ordinal))
        && await links.CanLinkAsync(context.User, linkedType, linkedId, LinkCapabilities.TeamAble, ct).ConfigureAwait(false);

    private async Task<Result> CheckAsync(TeamInput input, TeamType? type, Guid? teamId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (type is null || !type.Active)
        {
            errors.Add(new FieldError("typeCode", "validation.unknownReference", "validation.unknownReference"));
            return Error.Validation(errors);
        }

        if ((type.Permanence == Permanence.Permanent && !input.Permanent) || (type.Permanence == Permanence.Temporary && input.Permanent))
        {
            errors.Add(new FieldError("permanent", "teams.errors.permanenceOfType", "teams.errors.permanenceOfType"));
        }

        if (input.OwningUnitId is { } unit && await org.GetUnitAsync(unit, ct).ConfigureAwait(false) is null)
        {
            errors.Add(new FieldError("owningUnitId", "validation.unknownReference", "validation.unknownReference"));
        }

        if (input.ApprovedByCode is { } approvedBy && !await lookups.IsActiveCodeAsync(TeamPermissions.ApprovedByLookup, approvedBy, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("approvedByCode", "validation.unknownCode", "validation.unknownCode"));
        }

        if (input.ReplacesTeamId is { } replaces && (replaces == teamId
            || !await db.Teams.AnyAsync(t => t.Id == replaces && (t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive), ct).ConfigureAwait(false)))
        {
            errors.Add(new FieldError("replacesTeamId", "validation.unknownReference", "validation.unknownReference"));
        }

        var roles = await db.Roles.AsNoTracking().Where(r => r.Active).Select(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        var members = input.Members ?? [];
        for (var i = 0; i < members.Count; i++)
        {
            if (!roles.Contains(members[i].RoleCode))
            {
                errors.Add(new FieldError($"members[{i}].roleCode", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        if (members.Where(m => m.EmployeeId is not null).GroupBy(m => m.EmployeeId).Any(g => g.Count() > 1))
        {
            errors.Add(new FieldError("members", "teams.errors.duplicateMember", "teams.errors.duplicateMember"));
        }

        return errors.Count > 0 ? Error.Validation(errors) : Result.Success();
    }

    private void ApplyMembers(Team team, IReadOnlyList<MemberInput> inputs, IReadOnlyList<TeamMember> existing)
    {
        var position = 0;
        foreach (var input in inputs)
        {
            var current = input.Id is { } id ? existing.FirstOrDefault(m => m.Id == id) : null;
            if (current is not null)
            {
                current.Update(input.RoleCode, input.Responsibilities, input.RequiredSkills, input.AllocationPercent, input.FromDate);
            }
            else
            {
                db.Members.Add(TeamMember.Create(team.Id, MembershipRules.ToValues(input, team.StartDate), position));
            }

            position++;
        }

        foreach (var removed in existing.Where(m => inputs.All(i => i.Id != m.Id)))
        {
            removed.Leave(clock.Today, "removed-from-request");
        }
    }

    private static bool Permanent(TeamType type, bool requested) => type.Permanence switch
    {
        Permanence.Permanent => true,
        Permanence.Temporary => false,
        _ => requested,
    };

    private static TeamValues Values(TeamInput input, TeamType type) => new(
        input.NameAr, input.NameEn, Permanent(type, input.Permanent), input.Subject, input.Objectives, input.Mandate, input.OwningUnitId, input.SponsorEmployeeId,
        input.ApprovedByCode, input.MemoMode ?? MemoModes.Generate, input.MemoDraft, input.StartDate, input.EndDate, input.Cadence ?? type.DefaultCadence, input.ReportTo,
        input.Workload ?? Workloads.Medium, input.ReplacesTeamId, input.EvaluatorRule, input.EvaluatorEmployeeId);
}

/// <summary>Turning a team Active (formation approved, or a type without approval): roles, memo, notifications, replacement.</summary>
internal sealed class TeamActivation(TeamsDbContext db, TeamRoleSync roleSync, TeamNotifier notifier, INumberingService numbering, IOutbox outbox, IClock clock)
{
    public const string RenderMemo = "teams.render-memo";

    public async Task ActivateAsync(Team team, TeamType? type, object transactionContext, CancellationToken ct)
    {
        await roleSync.SyncAsync(team, ct).ConfigureAwait(false);
        if ((type?.RequiresMemo ?? true) && team.MemoMode == MemoModes.Generate)
        {
            if (team.MemoNumber is null)
            {
                team.SetMemo(await numbering.NextAsync(db, TeamPermissions.MemoNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false), clock.Today, null);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await EnqueueMemoAsync(team, ct).ConfigureAwait(false);
        }

        if (team.ReplacesTeamId is { } replaced && await db.Teams.FirstOrDefaultAsync(t => t.Id == replaced, ct).ConfigureAwait(false) is { } old
            && old.Apply(TeamActions.Replace, $"replaced-by:{team.Number}", clock.UtcNow).IsSuccess)
        {
            old.MarkReplacedBy(team.Id);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var members = await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id && m.Status == MemberStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        await notifier.SendAsync(TeamNotifications.Activated, team, members.Select(m => m.EmployeeId), null, null, null, transactionContext, ct).ConfigureAwait(false);
    }

    public Task EnqueueMemoAsync(Team team, CancellationToken ct) =>
        outbox.EnqueueAsync(db, new OutboxEnvelope(RenderMemo, new RenderMemoMessage(team.Id)), ct);
}

public sealed record RenderMemoMessage(Guid TeamId);

/// <summary>Moves the team with the formation approval (inside the engine's transaction).</summary>
internal sealed class FormationApprovalHook(TeamsDbContext db, TeamActivation activation, TeamNotifier notifier, IClock clock) : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [FormationApprovalProcess.Key];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        var action = approvalEvent.Kind switch
        {
            ApprovalEventKinds.Approved => TeamActions.FormationApproved,
            ApprovalEventKinds.Returned => TeamActions.FormationReturned,
            ApprovalEventKinds.Rejected => TeamActions.FormationRejected,
            _ => null,
        };
        if (action is null)
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var applied = team.Apply(action, approvalEvent.Comment, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        switch (action)
        {
            case TeamActions.FormationApproved:
                var type = await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == team.TypeCode, cancellationToken).ConfigureAwait(false);
                await activation.ActivateAsync(team, type, transactionContext, cancellationToken).ConfigureAwait(false);
                break;
            case TeamActions.FormationReturned:
                await notifier.SendAsync(TeamNotifications.FormationReturned, team, [team.RequestedByEmployeeId], null, approvalEvent.Comment, null, transactionContext,
                    cancellationToken).ConfigureAwait(false);
                break;
            case TeamActions.FormationRejected:
                await notifier.SendAsync(TeamNotifications.FormationRejected, team, [team.RequestedByEmployeeId], null, approvalEvent.Comment, null, transactionContext,
                    cancellationToken).ConfigureAwait(false);
                break;
        }

        return Result.Success();
    }
}
