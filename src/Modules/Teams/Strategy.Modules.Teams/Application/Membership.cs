using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Teams.Application;

internal static class MembershipRules
{
    public static MemberValues ToValues(MemberInput input, DateOnly? defaultFrom) => new(
        input.EmployeeId, input.ExternalName, input.ExternalOrganisation, input.ExternalEmail, input.ExternalPhone, input.RoleCode, input.Responsibilities,
        input.RequiredSkills, input.AllocationPercent, input.FromDate ?? defaultFrom);
}

public sealed record AddMember(Guid TeamId, MemberInput Member) : ICommand<IReadOnlyList<MemberDto>>;

public sealed record UpdateMember(Guid TeamId, Guid MemberId, string RoleCode, string? Responsibilities, string? RequiredSkills, decimal? AllocationPercent, uint Version)
    : ICommand<IReadOnlyList<MemberDto>>;

public sealed record EndMembership(Guid TeamId, Guid MemberId, DateOnly? On, string Reason, MemberInput? Successor, uint Version) : ICommand<IReadOnlyList<MemberDto>>;

public sealed record ListMembers(Guid TeamId, bool History) : IQuery<IReadOnlyList<MemberDto>>;

public sealed class AddMemberValidator : AbstractValidator<AddMember>
{
    public AddMemberValidator()
    {
        RuleFor(x => x.Member.RoleCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Member.ExternalName).NotEmpty().MaximumLength(300).When(x => x.Member.EmployeeId is null);
        RuleFor(x => x.Member.Responsibilities).MaximumLength(4000);
        RuleFor(x => x.Member.RequiredSkills).MaximumLength(2000);
    }
}

public sealed class EndMembershipValidator : AbstractValidator<EndMembership>
{
    public EndMembershipValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Successor!.ExternalName).NotEmpty().When(x => x.Successor is not null && x.Successor.EmployeeId is null);
    }
}

/// <summary>
/// Membership changes of a formed team (SPEC §4.5 T2): add, leave, replace, change role. Members are never deleted; record roles move in the
/// same transaction; the person is notified. Draft requests edit their members through the request.
/// </summary>
internal sealed class MembershipHandlers(
    TeamsDbContext db, TeamReader reader, TeamRoleSync roleSync, TeamNotifier notifier, IRequestContext context, IEmployeeDirectory employees, IClock clock)
    : ICommandHandler<AddMember, IReadOnlyList<MemberDto>>,
        ICommandHandler<UpdateMember, IReadOnlyList<MemberDto>>,
        ICommandHandler<EndMembership, IReadOnlyList<MemberDto>>,
        IQueryHandler<ListMembers, IReadOnlyList<MemberDto>>
{
    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(ListMembers query, CancellationToken cancellationToken)
    {
        var team = await reader.FindAsync(query.TeamId, context.User, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        return Result<IReadOnlyList<MemberDto>>.Success(await ListAsync(team.Id, query.History, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(AddMember command, CancellationToken cancellationToken)
    {
        var (team, error) = await LoadAsync(command.TeamId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return error!;
        }

        var check = await CheckMemberAsync(team.Id, command.Member.RoleCode, command.Member.EmployeeId, null, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var position = await db.Members.Where(m => m.TeamId == team.Id).CountAsync(cancellationToken).ConfigureAwait(false);
        var member = TeamMember.Create(team.Id, MembershipRules.ToValues(command.Member, clock.Today), position);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Members.Add(member);
        await roleSync.SyncAsync(team, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Code == member.RoleCode, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(TeamNotifications.MemberAdded, team, [member.EmployeeId], await ActorAsync(cancellationToken).ConfigureAwait(false),
            role?.NameEn ?? member.RoleCode, null, db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<MemberDto>>.Success(await ListAsync(team.Id, false, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(UpdateMember command, CancellationToken cancellationToken)
    {
        var (team, error) = await LoadAsync(command.TeamId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return error!;
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == command.MemberId && m.TeamId == team.Id, cancellationToken).ConfigureAwait(false);
        if (member is null || !member.IsActive)
        {
            return Error.NotFound();
        }

        var check = await CheckMemberAsync(team.Id, command.RoleCode, null, member.Id, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        db.Entry(member).Property(x => x.Version).OriginalValue = command.Version;
        member.Update(command.RoleCode, command.Responsibilities, command.RequiredSkills, command.AllocationPercent, null);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
        return Result<IReadOnlyList<MemberDto>>.Success(await ListAsync(team.Id, false, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(EndMembership command, CancellationToken cancellationToken)
    {
        var (team, error) = await LoadAsync(command.TeamId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return error!;
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == command.MemberId && m.TeamId == team.Id, cancellationToken).ConfigureAwait(false);
        if (member is null || !member.IsActive)
        {
            return Error.NotFound();
        }

        var on = command.On ?? clock.Today;
        db.Entry(member).Property(x => x.Version).OriginalValue = command.Version;
        TeamMember? successor = null;
        if (command.Successor is { } input)
        {
            var successorInput = input with { RoleCode = string.IsNullOrWhiteSpace(input.RoleCode) ? member.RoleCode : input.RoleCode };
            var check = await CheckMemberAsync(team.Id, successorInput.RoleCode, successorInput.EmployeeId, member.Id, cancellationToken).ConfigureAwait(false);
            if (check.IsFailure)
            {
                return check.Error;
            }

            successor = TeamMember.Create(team.Id, MembershipRules.ToValues(successorInput with { FromDate = successorInput.FromDate ?? on }, on), member.Position);
            member.ReplaceWith(successor.Id, on, command.Reason);
        }
        else
        {
            member.Leave(on, command.Reason);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (successor is not null)
        {
            db.Members.Add(successor);
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

        var actor = await ActorAsync(cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(TeamNotifications.MemberLeft, team, [member.EmployeeId], actor, command.Reason,
            on.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), db, cancellationToken).ConfigureAwait(false);
        if (successor is not null)
        {
            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Code == successor.RoleCode, cancellationToken).ConfigureAwait(false);
            await notifier.SendAsync(TeamNotifications.MemberAdded, team, [successor.EmployeeId], actor, role?.NameEn ?? successor.RoleCode, null, db, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<MemberDto>>.Success(await ListAsync(team.Id, false, cancellationToken).ConfigureAwait(false));
    }

    private async Task<(Team? Team, Error? Error)> LoadAsync(Guid teamId, CancellationToken ct)
    {
        var team = await reader.FindAsync(teamId, context.User, ct, tracked: true).ConfigureAwait(false);
        if (team is null)
        {
            return (null, Error.NotFound());
        }

        if (!await reader.CanAsync(context.User, TeamPermissions.Manage, team, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        if (!TeamStatuses.IsOperating(team.Status))
        {
            return (null, Error.Rule("teams.notOperating", "teams.errors.notOperating"));
        }

        return (team, null);
    }

    private async Task<Result> CheckMemberAsync(Guid teamId, string roleCode, Guid? employeeId, Guid? excludeMember, CancellationToken ct)
    {
        if (!await db.Roles.AnyAsync(r => r.Code == roleCode && r.Active, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("roleCode", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (employeeId is { } e)
        {
            if (await employees.GetAsync(e, ct).ConfigureAwait(false) is null)
            {
                return Error.Validation([new FieldError("employeeId", "validation.unknownReference", "validation.unknownReference")]);
            }

            if (await db.Members.AnyAsync(m => m.TeamId == teamId && m.EmployeeId == e && m.Status == MemberStatuses.Active && m.Id != excludeMember, ct).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("employeeId", "teams.errors.duplicateMember", "teams.errors.duplicateMember")]);
            }
        }

        return Result.Success();
    }

    private async Task<string?> ActorAsync(CancellationToken ct) =>
        context.User.EmployeeIdOf() is { } id && await employees.GetAsync(id, ct).ConfigureAwait(false) is { } e ? e.NameEn ?? e.NameAr : null;

    private async Task<IReadOnlyList<MemberDto>> ListAsync(Guid teamId, bool history, CancellationToken ct)
    {
        var members = await reader.MembersAsync(teamId, activeOnly: !history, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), ct).ConfigureAwait(false);
        var roles = await reader.RolesAsync(ct).ConfigureAwait(false);
        return members.Select(m => TeamReader.ToDto(m, people, roles)).ToList();
    }
}
