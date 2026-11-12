using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Infrastructure;

/// <summary>
/// Teams record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): teams / committees, memberships and the yearly team and member
/// evaluations. Only the listed properties are written; synced records arrive in their Salesforce state without starting approvals, raising
/// integration events or sending notifications. Derived data (number, permanence, leader, evaluation period / template, member snapshot and the
/// record roles) is computed by the hooks below in the same transaction.
/// </summary>
internal static class TeamsSyncTargets
{
    private static readonly IReadOnlyList<string> EvaluationStatusCodes =
        [EvaluationStatuses.Open, EvaluationStatuses.Submitted, EvaluationStatuses.Returned, EvaluationStatuses.Approved, EvaluationStatuses.Cancelled];

    public static IServiceCollection AddTeamsSyncTargets(this IServiceCollection services)
    {
        const string System = TeamPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<TeamsDbContext, Team>(Team.EntityType, System, "فريق / لجنة", "Team / committee")
            .Text("number", nameof(Team.Number))
            .Text("nameAr", nameof(Team.NameAr)).Text("nameEn", nameof(Team.NameEn))
            .Text("type", nameof(Team.TypeCode), required: true) // a team type code (Settings → Teams), not a lookup
            .Text("subject", nameof(Team.Subject)).Text("objectives", nameof(Team.Objectives))
            .Lookup("approvedBy", nameof(Team.ApprovedByCode), TeamPermissions.ApprovedByLookup)
            .Text("memoNumber", nameof(Team.MemoNumber)).Date("memoDate", nameof(Team.MemoDate))
            .Date("startDate", nameof(Team.StartDate)).Date("endDate", nameof(Team.EndDate))
            .Choice("cadence", nameof(Team.Cadence), Cadences.All)
            .Text("reportTo", nameof(Team.ReportTo))
            .Choice("workload", nameof(Team.Workload), Workloads.All)
            .Choice("status", nameof(Team.Status), TeamStatuses.All, required: true)
            .Text("statusReason", nameof(Team.StatusReason))
            .Unit("owningUnitId", nameof(Team.OwningUnitId))
            .Person("sponsorEmployeeId", nameof(Team.SponsorEmployeeId))
            .Person("leaderEmployeeId", nameof(Team.LeaderEmployeeId))
            .Default("status", TeamStatuses.Active)
            .After<TeamSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<TeamsDbContext, TeamMember>(TeamMember.EntityType, System, "عضو فريق", "Team member")
            .Reference("teamId", nameof(TeamMember.TeamId), Team.EntityType, required: true)
            .Person("employeeId", nameof(TeamMember.EmployeeId))
            .Text("externalName", nameof(TeamMember.ExternalName))
            .Text("role", nameof(TeamMember.RoleCode), required: true) // a member role code (Settings → Teams), not a lookup
            .Text("responsibilities", nameof(TeamMember.Responsibilities)).Text("requiredSkills", nameof(TeamMember.RequiredSkills))
            .WholeNumber("position", nameof(TeamMember.Position))
            .Date("fromDate", nameof(TeamMember.FromDate)).Date("toDate", nameof(TeamMember.ToDate))
            .Choice("status", nameof(TeamMember.Status), [MemberStatuses.Active, MemberStatuses.Left, MemberStatuses.Replaced])
            .Default("status", MemberStatuses.Active)
            .After<TeamMemberSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<TeamsDbContext, TeamEvaluation>(TeamEvaluation.EntityType, System, "تقييم فريق (سنوي)", "Team evaluation (yearly)")
            .Reference("teamId", nameof(TeamEvaluation.TeamId), Team.EntityType, required: true)
            .Text("cycle", nameof(TeamEvaluation.Cycle), required: true) // the evaluation year, e.g. "2025"
            .Number("score", nameof(TeamEvaluation.Score)) // the source's overall score (0–100) is kept as the evaluation score
            .Choice("workload", nameof(TeamEvaluation.Workload), Workloads.All)
            .Choice("status", nameof(TeamEvaluation.Status), EvaluationStatusCodes, required: true)
            .Timestamp("submittedAt", nameof(TeamEvaluation.SubmittedAt)).Timestamp("approvedAt", nameof(TeamEvaluation.ApprovedAt))
            .Person("evaluatorEmployeeId", nameof(TeamEvaluation.EvaluatorEmployeeId))
            .Default("status", EvaluationStatuses.Approved)
            .After<TeamEvaluationSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<TeamsDbContext, MemberEvaluation>(MemberEvaluation.EntityType, System, "تقييم عضو (سنوي)", "Member evaluation (yearly)")
            .Reference("teamId", nameof(MemberEvaluation.TeamId), Team.EntityType, required: true)
            .Reference("memberId", nameof(MemberEvaluation.MemberId), TeamMember.EntityType, required: true)
            .WholeNumber("points", nameof(MemberEvaluation.Points)) // the source's evaluation point is kept as the member's points
            .Text("grade", nameof(MemberEvaluation.Grade)) // a grade band code of the evaluation template
            .Text("comment", nameof(MemberEvaluation.Comment))
            .Timestamp("evaluatedAt", nameof(MemberEvaluation.EvaluatedAt))
            .Bool("certificate", nameof(MemberEvaluation.Certificate)).Bool("financialReward", nameof(MemberEvaluation.FinancialReward))
            .Number("calculatedReward", nameof(MemberEvaluation.CalculatedReward)).Number("approvedReward", nameof(MemberEvaluation.ApprovedReward))
            .After<MemberEvaluationSyncHook>());

        return services;
    }

    /// <summary>
    /// The year an evaluation's member rows belong to: the latest cycle not after the year the member was evaluated (an annual cycle is
    /// evaluated early the next year), else the latest cycle. Null when the team has no yearly cycle.
    /// </summary>
    public static string? PickCycle(IEnumerable<string> cycles, DateTimeOffset? evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(cycles);
        var years = cycles.Select(c => int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : (int?)null)
            .Where(y => y is not null).Select(y => y!.Value).OrderByDescending(y => y).ToList();
        if (years.Count == 0)
        {
            return null;
        }

        var notAfter = evaluatedAt is { } at ? years.Where(y => y <= at.UtcDateTime.Year).Select(y => (int?)y).FirstOrDefault() : null;
        return Cycles.Year(notAfter ?? years[0]);
    }
}

/// <summary>
/// Record roles of a synced team (as <c>TeamRoleSync</c> grants them for local teams, without its integration event — synced records raise
/// none, §G3): member, leader, secretary and results viewer from the active memberships and their member roles. The leader comes from a member
/// holding a leader role; without one the source's team leader is kept and holds the leader role.
/// </summary>
internal static class SyncedTeamAccess
{
    private static readonly string[] Managed =
        [TeamPermissions.MemberRole, TeamPermissions.LeaderRole, TeamPermissions.SecretaryRole, TeamPermissions.ResultsViewerRole];

    public static async Task ApplyAsync(TeamsDbContext db, IRecordRoleService recordRoles, Team team, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var members = (await db.Members.Where(m => m.TeamId == team.Id).ToListAsync(ct).ConfigureAwait(false))
            .Where(m => m.IsActive).OrderBy(m => m.Position).ToList();
        var leaderCodes = roles.Where(r => r.IsLeader).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        if (members.FirstOrDefault(m => m.EmployeeId is not null && leaderCodes.Contains(m.RoleCode))?.EmployeeId is { } leader)
        {
            team.SetLeader(leader);
        }

        if (team.Status is TeamStatuses.Draft or TeamStatuses.PendingApproval or TeamStatuses.Cancelled)
        {
            return;
        }

        var wanted = new HashSet<(Guid Employee, string Role)>();
        foreach (var member in members.Where(m => m.EmployeeId is not null))
        {
            var role = roles.FirstOrDefault(r => r.Code == member.RoleCode);
            var employee = member.EmployeeId!.Value;
            wanted.Add((employee, TeamPermissions.MemberRole));
            if (role?.IsLeader == true)
            {
                wanted.Add((employee, TeamPermissions.LeaderRole));
            }

            if (role?.IsSecretary == true)
            {
                wanted.Add((employee, TeamPermissions.SecretaryRole));
            }

            if (role?.CanViewResults == true)
            {
                wanted.Add((employee, TeamPermissions.ResultsViewerRole));
            }
        }

        if (team.LeaderEmployeeId is { } teamLeader)
        {
            wanted.Add((teamLeader, TeamPermissions.LeaderRole));
            wanted.Add((teamLeader, TeamPermissions.ResultsViewerRole));
        }

        var held = (await recordRoles.GetHoldersAsync(TeamPermissions.ResourceType, team.Id, ct).ConfigureAwait(false))
            .Where(h => h.PrincipalKind == RecordPrincipalKinds.Employee && Managed.Contains(h.RoleCode) && h.ValidTo is null)
            .Select(h => (h.PrincipalId, h.RoleCode)).ToHashSet();
        foreach (var (employee, role) in wanted.Where(w => !held.Contains(w)))
        {
            await recordRoles.GrantInTransactionAsync(db, TeamPermissions.ResourceType, team.Id, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }

        foreach (var (employee, role) in held.Where(h => !wanted.Contains(h)))
        {
            await recordRoles.RevokeInTransactionAsync(db, TeamPermissions.ResourceType, team.Id, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }
    }

    public static async Task GrantOnceAsync(TeamsDbContext db, IRecordRoleService recordRoles, Guid teamId, Guid employeeId, string role, CancellationToken ct)
    {
        var held = await recordRoles.GetHoldersAsync(TeamPermissions.ResourceType, teamId, ct).ConfigureAwait(false);
        if (!held.Any(h => h.RoleCode == role && h.PrincipalKind == RecordPrincipalKinds.Employee && h.PrincipalId == employeeId && h.ValidTo is null))
        {
            await recordRoles.GrantInTransactionAsync(db, TeamPermissions.ResourceType, teamId, RecordPrincipalKinds.Employee, employeeId, role, ct).ConfigureAwait(false);
        }
    }

    /// <summary>A synced record that cannot be completed: the engine records it as a write failure and the transaction rolls back.</summary>
    public static DbUpdateException Fail(string message) => new(message);
}

/// <summary>A synced team: number when the source gives none, permanence from the team type and the end date, leader and record roles.</summary>
internal sealed class TeamSyncHook(IRecordRoleService recordRoles, INumberingService numbering, IClock clock) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (TeamsDbContext)transactionContext;
        var team = await db.Teams.SingleAsync(t => t.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (team.Number is null)
        {
            team.AssignNumber(await numbering.NextAsync(db, TeamPermissions.TeamNumbering, new NumberingContext(team.StartDate ?? clock.Today), cancellationToken)
                .ConfigureAwait(false));
        }

        var type = await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == team.TypeCode, cancellationToken).ConfigureAwait(false)
            ?? throw SyncedTeamAccess.Fail($"Unknown team type '{team.TypeCode}'.");
        var entry = db.Entry(team);
        entry.Property(nameof(Team.Permanent)).CurrentValue = team.EndDate is null && type.Permanence != Permanence.Temporary;
        if (TeamStatuses.IsFinished(team.Status) && team.ClosedAt is null)
        {
            entry.Property(nameof(Team.ClosedAt)).CurrentValue = team.EndDate is { } end ? new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : clock.UtcNow;
        }

        await SyncedTeamAccess.ApplyAsync(db, recordRoles, team, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A synced membership: checks the member role, keeps external details for non-employees only, then the team's leader and record roles.</summary>
internal sealed class TeamMemberSyncHook(IRecordRoleService recordRoles) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (TeamsDbContext)transactionContext;
        var member = await db.Members.SingleAsync(m => m.Id == recordId, cancellationToken).ConfigureAwait(false);
        if (!await db.Roles.AnyAsync(r => r.Code == member.RoleCode, cancellationToken).ConfigureAwait(false))
        {
            throw SyncedTeamAccess.Fail($"Unknown member role '{member.RoleCode}'.");
        }

        if (member.EmployeeId is not null && member.ExternalName is not null)
        {
            db.Entry(member).Property(nameof(TeamMember.ExternalName)).CurrentValue = null;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var team = await db.Teams.SingleAsync(t => t.Id == member.TeamId, cancellationToken).ConfigureAwait(false);
        await SyncedTeamAccess.ApplyAsync(db, recordRoles, team, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A synced yearly team evaluation (SPEC-ADDENDUM §G3: Salesforce keeps the scores on the team row; here each year is its own evaluation):
/// annual period of the cycle within the team's dates, the published template of that year, number, opening time, the team leader as
/// evaluator, and the evaluator record role while the evaluation is still being written.
/// </summary>
internal sealed class TeamEvaluationSyncHook(IRecordRoleService recordRoles, INumberingService numbering, IClock clock) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (TeamsDbContext)transactionContext;
        var evaluation = await db.Evaluations.SingleAsync(e => e.Id == recordId, cancellationToken).ConfigureAwait(false);
        var team = await db.Teams.AsNoTracking().SingleAsync(t => t.Id == evaluation.TeamId, cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(evaluation.Cycle, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 2000 or > 2100)
        {
            throw SyncedTeamAccess.Fail($"Invalid evaluation year '{evaluation.Cycle}'.");
        }

        var entry = db.Entry(evaluation);
        var (start, end) = TeamCalendar.AnnualPeriod(year, team.StartDate, team.EndDate) ?? (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        entry.Property(nameof(TeamEvaluation.PeriodStart)).CurrentValue = start;
        entry.Property(nameof(TeamEvaluation.PeriodEnd)).CurrentValue = end;
        entry.Property(nameof(TeamEvaluation.Trigger)).CurrentValue = team.Permanent ? EvaluationTriggers.Annual : EvaluationTriggers.EndDate;
        if (evaluation.TemplateId == Guid.Empty)
        {
            var template = await db.Templates.AsNoTracking().Where(t => t.Status == VersionStatuses.Published && t.Year == year).OrderByDescending(t => t.VersionNo)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? await db.Templates.AsNoTracking().Where(t => t.Status == VersionStatuses.Published).OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw SyncedTeamAccess.Fail("No published evaluation template.");
            entry.Property(nameof(TeamEvaluation.TemplateId)).CurrentValue = template.Id;
        }

        if (evaluation.Number is null)
        {
            entry.Property(nameof(TeamEvaluation.Number)).CurrentValue =
                await numbering.NextAsync(db, TeamPermissions.EvaluationNumbering, new NumberingContext(end), cancellationToken).ConfigureAwait(false);
        }

        if (evaluation.OpenedAt == default)
        {
            entry.Property(nameof(TeamEvaluation.OpenedAt)).CurrentValue = evaluation.SubmittedAt ?? clock.UtcNow;
        }

        // A source evaluation not approved and without an overall score has not been written yet: it stays open for the evaluator.
        if (evaluation.Status == EvaluationStatuses.Submitted && evaluation.Score is null)
        {
            entry.Property(nameof(TeamEvaluation.Status)).CurrentValue = EvaluationStatuses.Open;
        }

        if (evaluation.Status == EvaluationStatuses.Approved && evaluation.ApprovedAt is null && evaluation.SubmittedAt is { } submitted)
        {
            entry.Property(nameof(TeamEvaluation.ApprovedAt)).CurrentValue = submitted;
        }

        if (evaluation.EvaluatorEmployeeId is null && team.LeaderEmployeeId is { } leader)
        {
            entry.Property(nameof(TeamEvaluation.EvaluatorEmployeeId)).CurrentValue = leader;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (EvaluationStatuses.IsEditable(evaluation.Status) && evaluation.EvaluatorEmployeeId is { } evaluator)
        {
            await SyncedTeamAccess.GrantOnceAsync(db, recordRoles, team.Id, evaluator, TeamPermissions.EvaluatorRole, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// A synced yearly member evaluation: attached to the team's evaluation of that year (see <see cref="TeamsSyncTargets.PickCycle"/>) and given
/// the member snapshot (employee, external name, role, leader flag) the module takes when it opens an evaluation.
/// </summary>
internal sealed class MemberEvaluationSyncHook : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (TeamsDbContext)transactionContext;
        var evaluation = await db.MemberEvaluations.SingleAsync(e => e.Id == recordId, cancellationToken).ConfigureAwait(false);
        var member = await db.Members.AsNoTracking().SingleAsync(m => m.Id == evaluation.MemberId, cancellationToken).ConfigureAwait(false);
        if (member.TeamId != evaluation.TeamId)
        {
            throw SyncedTeamAccess.Fail("The member does not belong to the evaluated team.");
        }

        var entry = db.Entry(evaluation);
        if (evaluation.EvaluationId == Guid.Empty)
        {
            var cycles = await db.Evaluations.AsNoTracking().Where(e => e.TeamId == evaluation.TeamId && e.Status != EvaluationStatuses.Cancelled)
                .Select(e => new { e.Id, e.Cycle }).ToListAsync(cancellationToken).ConfigureAwait(false);
            var cycle = TeamsSyncTargets.PickCycle(cycles.Select(c => c.Cycle), evaluation.EvaluatedAt)
                ?? throw SyncedTeamAccess.Fail("The team has no synced evaluation for this member evaluation.");
            entry.Property(nameof(MemberEvaluation.EvaluationId)).CurrentValue = cycles.First(c => c.Cycle == cycle).Id;
        }

        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Code == member.RoleCode, cancellationToken).ConfigureAwait(false);
        entry.Property(nameof(MemberEvaluation.EmployeeId)).CurrentValue = member.EmployeeId;
        entry.Property(nameof(MemberEvaluation.ExternalName)).CurrentValue = member.ExternalName;
        entry.Property(nameof(MemberEvaluation.RoleCode)).CurrentValue = member.RoleCode;
        entry.Property(nameof(MemberEvaluation.IsLeader)).CurrentValue = role?.IsLeader == true;
    }
}
