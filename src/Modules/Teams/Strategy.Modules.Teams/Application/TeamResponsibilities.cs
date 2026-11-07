using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Teams.Application;

/// <summary>
/// Teams → Settings → Responsibilities (ADR 0168): the leader of operating teams and the evaluator of open evaluations. A new leader replaces
/// the leader member with the same role, as *End membership* with a successor does (SPEC §4.5 T2: members are never deleted), and the record
/// roles follow through <see cref="TeamRoleSync"/>; approvals with team-role rules are re-routed.
/// </summary>
internal sealed class TeamLeaderResponsibility(TeamsDbContext db, IAccessService access, TeamRoleSync roleSync, IApprovalRerouting rerouting, IClock clock, IResponsibilityNotifier notifier)
    : ResponsibilitySource<Team>(notifier)
{
    public override string SystemCode => TeamPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("team-leader", "قائد الفريق", "Team leader", Statuses:
    [
        new(TeamStatuses.Active, "نشط", "Active"), new(TeamStatuses.Inactive, "غير نشط", "Inactive"), new(TeamStatuses.PendingEvaluation, "بانتظار التقييم", "Pending evaluation"),
    ]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var teams = (await TeamResponsibilityScope.TeamsAsync(db, access, user, false, cancellationToken).ConfigureAwait(false)).Where(t => t.LeaderEmployeeId != null);
        teams = TeamResponsibilityScope.Operating(teams);
        if (query.PersonId is { } person)
        {
            teams = teams.Where(t => t.LeaderEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            teams = teams.Where(t => t.OwningUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            teams = teams.Where(t => t.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = TeamResponsibilityScope.Pattern(text);
            teams = teams.Where(t => EF.Functions.ILike((t.Number ?? "") + " " + (t.NameAr ?? "") + " " + (t.NameEn ?? ""), pattern, "\\"));
        }

        var total = await teams.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await teams.OrderBy(t => t.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Team>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await TeamResponsibilityScope.TeamsAsync(db, access, user, tracked, cancellationToken).ConfigureAwait(false)).Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(Team record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.NameAr, record.NameEn, record.Status, record.LeaderEmployeeId, record.OwningUnitId, $"/teams/{record.Id:D}");
    }

    protected override async Task<ResponsibilityOutcome> CheckAsync(Team record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!TeamStatuses.IsOperating(record.Status) || await LeaderMemberAsync(record, false, cancellationToken).ConfigureAwait(false) is null)
        {
            return Blocked(row, ResponsibilityMessages.NotEditable);
        }

        return await db.Members.AnyAsync(m => m.TeamId == record.Id && m.EmployeeId == newId && m.Status == MemberStatuses.Active, cancellationToken).ConfigureAwait(false)
            ? Blocked(row, "teams.errors.duplicateMember")
            : Allowed(row);
    }

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Team> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var today = clock.Today;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var team in batch.Records)
        {
            var leader = (await LeaderMemberAsync(team, true, cancellationToken).ConfigureAwait(false))!;
            var successor = TeamMember.Create(team.Id, new MemberValues(batch.NewId, null, null, null, null, leader.RoleCode, leader.Responsibilities, leader.RequiredSkills,
                leader.AllocationPercent, today), leader.Position);
            leader.ReplaceWith(successor.Id, today, batch.Reason);
            db.Members.Add(successor);
            await roleSync.SyncAsync(team, cancellationToken).ConfigureAwait(false);
        }

        if (!await TeamResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await rerouting.RerouteAsync(new ApprovalReroute(TeamPermissions.ResourceType, batch.Records.Select(t => t.Id).ToList(), batch.Reason, batch.NewId), db,
            cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/teams/{batch.Records[0].Id:D}" : "/teams").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    /// <summary>The active member holding a leader role who is the team's leader.</summary>
    private async Task<TeamMember?> LeaderMemberAsync(Team team, bool tracked, CancellationToken ct)
    {
        var leaderRoles = await db.Roles.AsNoTracking().Where(r => r.IsLeader).Select(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        return await (tracked ? db.Members : db.Members.AsNoTracking())
            .Where(m => m.TeamId == team.Id && m.Status == MemberStatuses.Active && m.EmployeeId == team.LeaderEmployeeId && leaderRoles.Contains(m.RoleCode))
            .OrderBy(m => m.Position).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>The evaluator of open or returned team evaluations (the evaluator record role moves with it).</summary>
internal sealed class TeamEvaluatorResponsibility(TeamsDbContext db, IAccessService access, TeamRoleSync roleSync, IApprovalRerouting rerouting, IResponsibilityNotifier notifier)
    : ResponsibilitySource<TeamEvaluation>(notifier)
{
    public override string SystemCode => TeamPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("evaluator", "مقيّم الفريق", "Team evaluator", Statuses:
        [new(EvaluationStatuses.Open, "مفتوح", "Open"), new(EvaluationStatuses.Returned, "معاد", "Returned")]);

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var teams = await TeamResponsibilityScope.TeamsAsync(db, access, user, false, cancellationToken).ConfigureAwait(false);
        if (query.UnitId is { } unit)
        {
            teams = teams.Where(t => t.OwningUnitId == unit);
        }

        var evaluations = db.Evaluations.AsNoTracking()
            .Where(e => (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned) && e.EvaluatorEmployeeId != null && teams.Any(t => t.Id == e.TeamId));
        if (query.PersonId is { } person)
        {
            evaluations = evaluations.Where(e => e.EvaluatorEmployeeId == person);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            evaluations = evaluations.Where(e => e.Status == query.Status);
        }

        var joined = evaluations.Join(db.Teams.AsNoTracking(), e => e.TeamId, t => t.Id, (e, t) => new { e, t.Number, t.NameAr, t.NameEn, t.OwningUnitId });
        if (query.Text is { Length: > 0 } text)
        {
            var pattern = TeamResponsibilityScope.Pattern(text);
            joined = joined.Where(x => EF.Functions.ILike((x.e.Number ?? "") + " " + (x.Number ?? "") + " " + (x.NameAr ?? "") + " " + (x.NameEn ?? ""), pattern, "\\"));
        }

        var total = await joined.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await joined.OrderBy(x => x.e.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(x => Describe(x.e) with
        {
            TitleAr = x.NameAr, TitleEn = x.NameEn, UnitId = x.OwningUnitId, ContextAr = x.Number, ContextEn = x.Number,
        }).ToList(), total);
    }

    protected override async Task<IReadOnlyList<TeamEvaluation>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        // The subquery shares the query's tracking: AsNoTracking anywhere would leave the loaded evaluations untracked.
        var teams = await TeamResponsibilityScope.TeamsAsync(db, access, user, tracked, cancellationToken).ConfigureAwait(false);
        return await (tracked ? db.Evaluations : db.Evaluations.AsNoTracking()).Where(e => ids.Contains(e.Id) && teams.Any(t => t.Id == e.TeamId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(TeamEvaluation record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.Cycle, record.Cycle, record.Status, record.EvaluatorEmployeeId, null, $"/teams/evaluations/{record.Id:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(
        TeamEvaluation record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(EvaluationStatuses.IsEditable(record.Status) ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(
        ResponsibilityBatch<TeamEvaluation> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var evaluation in batch.Records)
        {
            if (batch.OutcomeOf(evaluation).PreviousId is { } previous)
            {
                await roleSync.RevokeAsync(evaluation.TeamId, previous, TeamPermissions.EvaluatorRole, cancellationToken).ConfigureAwait(false);
            }

            evaluation.AssignEvaluator(batch.NewId);
            await roleSync.GrantAsync(evaluation.TeamId, batch.NewId, TeamPermissions.EvaluatorRole, cancellationToken).ConfigureAwait(false);
        }

        if (!await TeamResponsibilityScope.SaveAsync(db, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        await rerouting.RerouteAsync(new ApprovalReroute(TeamPermissions.ResourceType, batch.Records.Select(e => e.TeamId).Distinct().ToList(), batch.Reason), db,
            cancellationToken).ConfigureAwait(false);
        await batch.NotifyAsync(db, cancellationToken, link: batch.Records.Count == 1 ? $"/teams/evaluations/{batch.Records[0].Id:D}" : "/teams").ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }
}

internal static class TeamResponsibilityScope
{
    public static async Task<IQueryable<Team>> TeamsAsync(TeamsDbContext db, IAccessService access, ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Teams : db.Teams.AsNoTracking(), user, ResponsibilityPermissions.Manage(TeamPermissions.SystemCode), TeamScope.Map, ct)
            .ConfigureAwait(false);

    public static IQueryable<Team> Operating(IQueryable<Team> teams) => teams.Where(t =>
        t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive || t.Status == TeamStatuses.PendingEvaluation || t.Status == TeamStatuses.EvaluationWaitingApproval);

    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";

    public static async Task<bool> SaveAsync(TeamsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
