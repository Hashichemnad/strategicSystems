using System.Security.Claims;

namespace Strategy.Modules.Teams.Contracts;

/// <summary>How another module reads a team (by reference, never through the Teams DbContext). docs/design/teams.md §9.</summary>
public interface ITeamsDirectory
{
    /// <summary>The team with its active internal members, or null when it does not exist or <paramref name="user"/> may not read it.</summary>
    Task<TeamSummary?> GetTeamAsync(Guid teamId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

/// <summary>A team as other modules see it. Never carries evaluation results.</summary>
public sealed record TeamSummary(
    Guid Id, string? Number, string? NameAr, string? NameEn, string Status, Guid? LeaderEmployeeId, IReadOnlyList<Guid> ActiveMemberEmployeeIds);
