using System.Security.Claims;

namespace Strategy.Modules.Teams.Contracts;

/// <summary>How another module reads a team (by reference, never through the Teams DbContext). docs/design/teams.md §9.</summary>
public interface ITeamsDirectory
{
    /// <summary>The team with its active internal members, or null when it does not exist or <paramref name="user"/> may not read it.</summary>
    Task<TeamSummary?> GetTeamAsync(Guid teamId, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teams whose Arabic / English name or number contains <paramref name="text"/> (all teams when empty), filtered in the database by
    /// <paramref name="user"/>'s <c>teams.team.read</c> scope — a team the user may not read is never returned. Closed, cancelled and
    /// replaced teams are left out unless <paramref name="includeEnded"/>. At most <paramref name="limit"/> (1–50) rows, by number.
    /// </summary>
    Task<IReadOnlyList<TeamListEntry>> SearchTeamsAsync(
        string? text, ClaimsPrincipal user, int limit = 20, bool includeEnded = false, CancellationToken cancellationToken = default);
}

/// <summary>A team in a picker list: identity and status only (no members, never evaluation results).</summary>
public sealed record TeamListEntry(Guid Id, string? Number, string? NameAr, string? NameEn, string Status);

/// <summary>A team as other modules see it. Never carries evaluation results.</summary>
public sealed record TeamSummary(
    Guid Id, string? Number, string? NameAr, string? NameEn, string Status, Guid? LeaderEmployeeId, IReadOnlyList<Guid> ActiveMemberEmployeeIds);

/// <summary>
/// Integration event (ADR 0072): the members or the status of a formed team changed (activation, member added / left / replaced, closure). Linked
/// records (e.g. a project) use it to follow team membership — project-team members get project access. Delivered at least once.
/// </summary>
public sealed record TeamMembershipChanged(
    Guid TeamId, string Status, Guid? LeaderEmployeeId, IReadOnlyList<Guid> ActiveMemberEmployeeIds, DateTimeOffset ChangedAt) : Strategy.Platform.Kernel.Messaging.IIntegrationEvent;
