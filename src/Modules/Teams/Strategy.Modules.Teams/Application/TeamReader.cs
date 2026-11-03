using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Teams.Application;

/// <summary>
/// Loads teams within the caller's read scope (ApplyScope in the database query; 404 otherwise) and the people / units / configuration
/// around them. Every handler starts here.
/// </summary>
internal sealed class TeamReader(TeamsDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org)
{
    public async Task<IQueryable<Team>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Teams : db.Teams.AsNoTracking();
        return await access.ApplyScopeAsync(source, user, TeamPermissions.Read, TeamScope.Map, ct).ConfigureAwait(false);
    }

    public async Task<Team?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, Team team, CancellationToken ct) =>
        access.CanAsync(user, permission, TeamScope.Map.RefOf(team), ct);

    /// <summary>The ids of teams (among <paramref name="teams"/>) on which the user holds the results permission.</summary>
    public async Task<HashSet<Guid>> WithResultsAsync(ClaimsPrincipal user, IEnumerable<Team> teams, CancellationToken ct)
    {
        var result = new HashSet<Guid>();
        foreach (var team in teams)
        {
            if (await CanAsync(user, TeamPermissions.Results, team, ct).ConfigureAwait(false))
            {
                result.Add(team.Id);
            }
        }

        return result;
    }

    public async Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new PersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public static PersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, PersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new PersonDto(i, null, null) : null;

    public async Task<UnitDto?> UnitAsync(Guid? id, CancellationToken ct) =>
        id is { } u && await org.GetUnitAsync(u, ct).ConfigureAwait(false) is { } unit ? new UnitDto(unit.Id, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr) : null;

    public Task<List<MemberRole>> RolesAsync(CancellationToken ct) => db.Roles.AsNoTracking().OrderBy(r => r.Sort).ToListAsync(ct);

    public Task<TeamType?> TypeAsync(string code, CancellationToken ct) => db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);

    public Task<List<TeamMember>> MembersAsync(Guid teamId, bool activeOnly, CancellationToken ct) =>
        db.Members.AsNoTracking().Where(m => m.TeamId == teamId && (!activeOnly || m.Status == MemberStatuses.Active)).OrderBy(m => m.Position).ToListAsync(ct);

    public static MemberDto ToDto(TeamMember m, IReadOnlyDictionary<Guid, PersonDto> people, IReadOnlyList<MemberRole> roles)
    {
        var role = roles.FirstOrDefault(r => r.Code == m.RoleCode);
        return new MemberDto(m.Id, Person(m.EmployeeId, people), m.ExternalName, m.ExternalOrganisation, m.ExternalEmail, m.ExternalPhone, m.RoleCode, role?.NameAr,
            role?.NameEn, role?.IsLeader == true, m.Responsibilities, m.RequiredSkills, m.AllocationPercent, m.Position, m.FromDate, m.ToDate, m.Status, m.ReplacedByMemberId,
            m.LeaveReason, m.Version);
    }
}

/// <summary>
/// Keeps the team's record roles in line with its active memberships (CLAUDE.md §7: granted and revoked only by the owning module), in the
/// caller's transaction: <c>member</c> for everyone, <c>leader</c> / <c>secretary</c> / <c>results_viewer</c> from the member role flags (ADR 0042).
/// </summary>
internal sealed class TeamRoleSync(TeamsDbContext db, IRecordRoleService recordRoles)
{
    private static readonly string[] Managed =
        [TeamPermissions.MemberRole, TeamPermissions.LeaderRole, TeamPermissions.SecretaryRole, TeamPermissions.ResultsViewerRole];

    /// <summary>Recomputes the leader on <paramref name="team"/> and (for formed teams) the member record roles. Call before SaveChanges.</summary>
    public async Task SyncAsync(Team team, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var members = db.Members.Local.Where(m => m.TeamId == team.Id).ToList();
        var stored = await db.Members.Where(m => m.TeamId == team.Id).ToListAsync(ct).ConfigureAwait(false);
        var all = stored.Concat(members).DistinctBy(m => m.Id).Where(m => m.IsActive).OrderBy(m => m.Position).ToList();
        var leaderCodes = roles.Where(r => r.IsLeader).Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        team.SetLeader(all.FirstOrDefault(m => m.EmployeeId is not null && leaderCodes.Contains(m.RoleCode))?.EmployeeId);
        if (team.Status is TeamStatuses.Draft or TeamStatuses.PendingApproval or TeamStatuses.Cancelled)
        {
            return;
        }

        var wanted = new HashSet<(Guid Employee, string Role)>();
        foreach (var member in all.Where(m => m.EmployeeId is not null))
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

    public Task GrantAsync(Guid teamId, Guid employeeId, string role, CancellationToken ct) =>
        recordRoles.GrantInTransactionAsync(db, TeamPermissions.ResourceType, teamId, RecordPrincipalKinds.Employee, employeeId, role, ct);

    public Task RevokeAsync(Guid teamId, Guid employeeId, string role, CancellationToken ct) =>
        recordRoles.RevokeInTransactionAsync(db, TeamPermissions.ResourceType, teamId, RecordPrincipalKinds.Employee, employeeId, role, ct);
}
