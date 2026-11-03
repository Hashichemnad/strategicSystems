using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Teams.Application;

public static class TeamViews
{
    public const string All = "all";
    public const string Mine = "mine";
    public const string Leading = "leading";
    public const string PendingEvaluation = "pending-evaluation";

    public static readonly IReadOnlyList<string> Codes = [All, Mine, Leading, PendingEvaluation];
}

public sealed record ListTeams(
    PageRequest Page, string? View = null, string? TypeCode = null, string? Status = null, Guid? UnitId = null, string? Search = null) : IQuery<PagedResult<TeamListItemDto>>;

public sealed record GetTeam(Guid Id) : IQuery<TeamDto>;

public sealed record GetTeamCounts : IQuery<TeamCountsDto>;

public sealed record ListMemberships(Guid EmployeeId) : IQuery<IReadOnlyList<MembershipDto>>;

/// <param name="Type">Linked record type.</param>
/// <param name="Id">Linked record id.</param>
public sealed record ListLinkedTeams(string Type, Guid Id) : IQuery<LinkedTeamsDto>;

public sealed record LinkedTeamsDto(IReadOnlyList<LinkedTeamDto> Teams, int Restricted, bool CanLink);

public sealed record LinkedTeamDto(TeamListItemDto Team, IReadOnlyList<MemberDto> Members);

internal static class Like
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}

/// <summary>Builds team DTOs (detail, list items) with the caller's permissions; results fields only for results viewers (ADR 0042).</summary>
internal sealed class TeamQueries(TeamsDbContext db, TeamReader reader, IRequestContext context, ILinkStore linkStore, ILinkRegistry links)
{
    public async Task<Result<TeamDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var user = context.User;
        var team = await reader.FindAsync(id, user, ct).ConfigureAwait(false);
        if (team is null)
        {
            return Error.NotFound();
        }

        var type = await reader.TypeAsync(team.TypeCode, ct).ConfigureAwait(false);
        var members = await reader.MembersAsync(team.Id, activeOnly: true, ct).ConfigureAwait(false);
        var roles = await reader.RolesAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(
            members.Select(m => m.EmployeeId).Concat([team.SponsorEmployeeId, team.RequestedByEmployeeId, team.LeaderEmployeeId, team.EvaluatorEmployeeId]), ct)
            .ConfigureAwait(false);
        var stored = await linkStore.ListFromAsync(new LinkRef(TeamPermissions.ResourceType, team.Id), LinkCapabilities.TeamAble, ct).ConfigureAwait(false);
        var linked = stored.Count == 0 ? [] : await links.ResolveAsync(stored.Select(l => new LinkRef(l.ToType, l.ToId)).ToList(), user, ct).ConfigureAwait(false);
        var permissions = await PermissionsAsync(team, ct).ConfigureAwait(false);
        decimal? lastScore = null;
        if (permissions.CanViewResults)
        {
            lastScore = await db.Evaluations.AsNoTracking().Where(e => e.TeamId == team.Id && e.Status == EvaluationStatuses.Approved)
                .OrderByDescending(e => e.ApprovedAt).Select(e => e.Score).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        return new TeamDto(
            team.Id, team.Number, team.NameAr, team.NameEn, team.TypeCode, type?.NameAr, type?.NameEn, team.Permanent, team.Subject, team.Objectives, team.Mandate,
            await reader.UnitAsync(team.OwningUnitId, ct).ConfigureAwait(false), TeamReader.Person(team.SponsorEmployeeId, people),
            TeamReader.Person(team.RequestedByEmployeeId, people), TeamReader.Person(team.LeaderEmployeeId, people), team.ApprovedByCode, team.MemoMode, team.MemoNumber,
            team.MemoDate, team.MemoFileId, team.MemoDraft, team.StartDate, team.EndDate, team.Cadence, team.ReportTo, team.Workload, team.Status, team.StatusReason,
            team.ReplacesTeamId, team.ReplacedByTeamId, team.EvaluatorRule, TeamReader.Person(team.EvaluatorEmployeeId, people), team.ActivatedAt, team.ClosedAt,
            members.Select(m => TeamReader.ToDto(m, people, roles)).ToList(), linked, await StatsAsync(team.Id, members.Count, ct).ConfigureAwait(false), lastScore,
            permissions, Actions(team, permissions), team.Version);
    }

    public async Task<TeamPermissionsDto> PermissionsAsync(Team team, CancellationToken ct)
    {
        var user = context.User;
        async Task<bool> Can(string permission) => await reader.CanAsync(user, permission, team, ct).ConfigureAwait(false);
        var canSubmit = await Can(TeamPermissions.Submit).ConfigureAwait(false);
        var canManage = await Can(TeamPermissions.Manage).ConfigureAwait(false);
        return new TeamPermissionsDto(
            (team.Status == TeamStatuses.Draft && canSubmit) || (TeamStatuses.IsOperating(team.Status) && canManage),
            canSubmit, canManage, await Can(TeamPermissions.Operate).ConfigureAwait(false), await Can(TeamPermissions.Minutes).ConfigureAwait(false),
            await Can(TeamPermissions.EvaluationOpen).ConfigureAwait(false), await Can(TeamPermissions.Results).ConfigureAwait(false),
            await Can(TeamPermissions.RewardManage).ConfigureAwait(false));
    }

    public static IReadOnlyList<string> Actions(Team team, TeamPermissionsDto p)
    {
        var actions = new List<string>();
        switch (team.Status)
        {
            case TeamStatuses.Draft when p.CanSubmit:
                actions.Add(TeamActions.Submit);
                actions.Add(TeamActions.Cancel);
                break;
            case TeamStatuses.PendingApproval when p.CanSubmit:
                actions.Add(TeamActions.Cancel);
                break;
            case TeamStatuses.Active:
                if (p.CanOpenEvaluation)
                {
                    actions.Add(TeamActions.OpenEvaluation);
                }

                if (p.CanManage)
                {
                    actions.Add(TeamActions.Deactivate);
                    actions.Add(TeamActions.Close);
                }

                break;
            case TeamStatuses.Inactive:
                if (p.CanOpenEvaluation)
                {
                    actions.Add(TeamActions.OpenEvaluation);
                }

                if (p.CanManage)
                {
                    actions.Add(TeamActions.Reactivate);
                    actions.Add(TeamActions.Close);
                }

                break;
        }

        return actions;
    }

    public async Task<TeamStatsDto> StatsAsync(Guid teamId, int activeMembers, CancellationToken ct)
    {
        var meetings = await db.Meetings.AsNoTracking().Where(m => m.TeamId == teamId && m.Status == MeetingStatuses.Held)
            .Select(m => m.MinutesDelayHours).ToListAsync(ct).ConfigureAwait(false);
        var recommendations = await db.Recommendations.AsNoTracking().Where(r => r.TeamId == teamId && r.Kind == RecommendationKinds.Recommendation)
            .Select(r => r.Status).ToListAsync(ct).ConfigureAwait(false);
        var open = recommendations.Count(RecommendationStatuses.IsOpen);
        var closed = recommendations.Count(s => s == RecommendationStatuses.Closed);
        var counted = recommendations.Count(s => s != RecommendationStatuses.Rejected);
        var delays = meetings.Where(d => d is not null).Select(d => d!.Value).ToList();
        return new TeamStatsDto(activeMembers, meetings.Count, open, closed, counted > 0 ? Math.Round(100m * closed / counted, 2) : null,
            delays.Count > 0 ? Math.Round(delays.Average(), 2) : null);
    }

    public async Task<List<TeamListItemDto>> ItemsAsync(IReadOnlyList<Team> teams, CancellationToken ct)
    {
        var user = context.User;
        var ids = teams.Select(t => t.Id).ToList();
        var types = await db.Types.AsNoTracking().ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        var counts = await db.Members.AsNoTracking().Where(m => ids.Contains(m.TeamId) && m.Status == MemberStatuses.Active)
            .GroupBy(m => m.TeamId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var me = user.EmployeeIdOf();
        var mine = me is null
            ? []
            : await db.Members.AsNoTracking().Where(m => ids.Contains(m.TeamId) && m.EmployeeId == me && m.Status == MemberStatuses.Active)
                .Select(m => m.TeamId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var withResults = await reader.WithResultsAsync(user, teams, ct).ConfigureAwait(false);
        var scores = withResults.Count == 0
            ? []
            : (await db.Evaluations.AsNoTracking().Where(e => withResults.Contains(e.TeamId) && e.Status == EvaluationStatuses.Approved)
                .Select(e => new { e.TeamId, e.Score, e.ApprovedAt }).ToListAsync(ct).ConfigureAwait(false))
                .GroupBy(e => e.TeamId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ApprovedAt).First().Score);
        var people = await reader.PeopleAsync(teams.Select(t => t.LeaderEmployeeId), ct).ConfigureAwait(false);
        var units = new Dictionary<Guid, UnitDto?>();
        foreach (var unit in teams.Where(t => t.OwningUnitId is not null).Select(t => t.OwningUnitId!.Value).Distinct())
        {
            units[unit] = await reader.UnitAsync(unit, ct).ConfigureAwait(false);
        }

        return teams.Select(t => new TeamListItemDto(
            t.Id, t.Number, t.NameAr, t.NameEn, t.TypeCode, types.GetValueOrDefault(t.TypeCode)?.NameAr, types.GetValueOrDefault(t.TypeCode)?.NameEn, t.Permanent,
            t.Status, t.OwningUnitId is { } u ? units.GetValueOrDefault(u) : null, TeamReader.Person(t.LeaderEmployeeId, people), t.StartDate, t.EndDate,
            counts.GetValueOrDefault(t.Id), scores.GetValueOrDefault(t.Id), mine.Contains(t.Id))).ToList();
    }
}

internal sealed class TeamQueryHandlers(
    TeamsDbContext db, TeamReader reader, TeamQueries queries, IRequestContext context, IAccessService access, IEmployeeDirectory employees, ILinkStore linkStore,
    ILinkRegistry links)
    : IQueryHandler<ListTeams, PagedResult<TeamListItemDto>>,
        IQueryHandler<GetTeam, TeamDto>,
        IQueryHandler<GetTeamCounts, TeamCountsDto>,
        IQueryHandler<ListMemberships, IReadOnlyList<MembershipDto>>,
        IQueryHandler<ListLinkedTeams, LinkedTeamsDto>
{
    public Task<Result<TeamDto>> HandleAsync(GetTeam query, CancellationToken cancellationToken) => queries.GetAsync(query.Id, cancellationToken);

    public async Task<Result<PagedResult<TeamListItemDto>>> HandleAsync(ListTeams query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var page = query.Page.Normalised();
        var source = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var me = user.EmployeeIdOf();
        switch (query.View ?? TeamViews.All)
        {
            case TeamViews.Mine:
                source = source.Where(t => db.Members.Any(m => m.TeamId == t.Id && m.EmployeeId == me && m.Status == MemberStatuses.Active)
                    || t.RequestedByEmployeeId == me);
                break;
            case TeamViews.Leading:
                source = source.Where(t => t.LeaderEmployeeId == me);
                break;
            case TeamViews.PendingEvaluation:
                source = source.Where(t => t.Status == TeamStatuses.PendingEvaluation || t.Status == TeamStatuses.EvaluationWaitingApproval);
                break;
            case TeamViews.All:
                break;
            default:
                return Error.Validation([new FieldError("view", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (!string.IsNullOrWhiteSpace(query.TypeCode))
        {
            source = source.Where(t => t.TypeCode == query.TypeCode);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(t => t.Status == query.Status);
        }

        if (query.UnitId is { } unit)
        {
            source = source.Where(t => t.OwningUnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(t => EF.Functions.ILike(t.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(t.Number ?? string.Empty, pattern, "\\") || EF.Functions.ILike(t.Subject ?? string.Empty, pattern, "\\"));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        source = page.SortOrder switch
        {
            ("number", false) => source.OrderBy(t => t.Number),
            ("number", true) => source.OrderByDescending(t => t.Number),
            ("name", false) => source.OrderBy(t => t.NameEn).ThenBy(t => t.NameAr),
            ("name", true) => source.OrderByDescending(t => t.NameEn).ThenByDescending(t => t.NameAr),
            ("startDate", false) => source.OrderBy(t => t.StartDate),
            ("startDate", true) => source.OrderByDescending(t => t.StartDate),
            _ => source.OrderByDescending(t => t.CreatedAt),
        };
        var teams = await source.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<TeamListItemDto>(await queries.ItemsAsync(teams, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<Result<TeamCountsDto>> HandleAsync(GetTeamCounts query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var me = user.EmployeeIdOf();
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var mine = await visible.CountAsync(t => db.Members.Any(m => m.TeamId == t.Id && m.EmployeeId == me && m.Status == MemberStatuses.Active), cancellationToken)
            .ConfigureAwait(false);
        var leading = await visible.CountAsync(t => t.LeaderEmployeeId == me && t.Status != TeamStatuses.Closed && t.Status != TeamStatuses.Replaced, cancellationToken)
            .ConfigureAwait(false);
        var pending = await visible.CountAsync(t => t.Status == TeamStatuses.PendingEvaluation || t.Status == TeamStatuses.EvaluationWaitingApproval, cancellationToken)
            .ConfigureAwait(false);
        var myEvaluations = me is null
            ? 0
            : await db.Evaluations.CountAsync(
                e => (e.EvaluatorEmployeeId == me || e.LeaderEvaluatorEmployeeId == me) && (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned),
                cancellationToken).ConfigureAwait(false);
        var reportsDue = me is null
            ? 0
            : await db.Reports.CountAsync(r => r.Status == ReportStatuses.Due && db.Teams.Any(t => t.Id == r.TeamId && t.LeaderEmployeeId == me), cancellationToken)
                .ConfigureAwait(false);
        return new TeamCountsDto(mine, leading, pending, myEvaluations, reportsDue);
    }

    public async Task<Result<IReadOnlyList<MembershipDto>>> HandleAsync(ListMemberships query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var employee = await employees.GetAsync(query.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound();
        }

        var self = user.EmployeeIdOf() == employee.Id;
        var canSeeAll = self || await access.CanAsync(user, TeamPermissions.MembershipRead,
            new ResourceRef(TeamPermissions.MembershipResource, employee.Id, employee.Id, null, employee.UnitId), cancellationToken).ConfigureAwait(false);
        var memberships = await db.Members.AsNoTracking().Where(m => m.EmployeeId == employee.Id).OrderByDescending(m => m.FromDate).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var teamIds = memberships.Select(m => m.TeamId).Distinct().ToList();
        var allTeams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id) && t.Status != TeamStatuses.Cancelled).ToListAsync(cancellationToken).ConfigureAwait(false);
        var readable = canSeeAll
            ? allTeams
            : await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => teamIds.Contains(t.Id)).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        if (!canSeeAll && readable.Count == 0)
        {
            return Error.NotFound();
        }

        var withResults = await reader.WithResultsAsync(user, readable, cancellationToken).ConfigureAwait(false);
        var results = withResults.Count == 0
            ? []
            : await (from me in db.MemberEvaluations.AsNoTracking()
                     join e in db.Evaluations.AsNoTracking() on me.EvaluationId equals e.Id
                     where me.EmployeeId == employee.Id && withResults.Contains(e.TeamId) && e.Status != EvaluationStatuses.Cancelled
                     select new { e.TeamId, e.Id, e.Cycle, e.Status, e.Score, me.Points, me.Grade, me.ApprovedReward, me.Certificate, e.OpenedAt })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = await reader.RolesAsync(cancellationToken).ConfigureAwait(false);
        var teams = readable.ToDictionary(t => t.Id);
        return memberships.Where(m => teams.ContainsKey(m.TeamId)).Select(m =>
        {
            var team = teams[m.TeamId];
            var role = roles.FirstOrDefault(r => r.Code == m.RoleCode);
            IReadOnlyList<MembershipResultDto>? teamResults = withResults.Contains(team.Id)
                ? results.Where(r => r.TeamId == team.Id).OrderByDescending(r => r.OpenedAt)
                    .Select(r => new MembershipResultDto(r.Id, r.Cycle, r.Status, r.Score, r.Points, r.Grade, r.ApprovedReward, r.Certificate)).ToList()
                : null;
            return new MembershipDto(team.Id, team.Number, team.NameAr, team.NameEn, team.Status, team.TypeCode, m.Id, m.RoleCode, role?.NameAr, role?.NameEn, m.FromDate,
                m.ToDate, m.Status, teamResults);
        }).ToList();
    }

    public async Task<Result<LinkedTeamsDto>> HandleAsync(ListLinkedTeams query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var resolved = await links.ResolveAsync([new LinkRef(query.Type, query.Id)], user, cancellationToken).ConfigureAwait(false);
        var target = resolved.Count > 0 ? resolved[0] : null;
        if (target is null || target.Visibility != LinkVisibility.Visible)
        {
            return Error.NotFound();
        }

        var stored = await linkStore.ListToAsync(new LinkRef(query.Type, query.Id), LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false);
        var ids = stored.Where(l => l.FromType == TeamPermissions.ResourceType).Select(l => l.FromId).Distinct().ToList();
        var visible = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => ids.Contains(t.Id) && t.Status != TeamStatuses.Cancelled)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = await queries.ItemsAsync(visible, cancellationToken).ConfigureAwait(false);
        var roles = await reader.RolesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<LinkedTeamDto>();
        foreach (var item in items)
        {
            var members = await reader.MembersAsync(item.Id, activeOnly: true, cancellationToken).ConfigureAwait(false);
            var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), cancellationToken).ConfigureAwait(false);
            result.Add(new LinkedTeamDto(item, members.Select(m => TeamReader.ToDto(m, people, roles)).ToList()));
        }

        var canLink = await links.CanLinkAsync(user, query.Type, query.Id, LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false)
            && await access.CanAsync(user, TeamPermissions.Request, null, cancellationToken).ConfigureAwait(false);
        return new LinkedTeamsDto(result, ids.Count - visible.Count, canLink);
    }
}
