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

/// <summary>
/// Integration event (ADR 0072): the members or the status of a formed team changed (activation, member added / left / replaced, closure). Linked
/// records (e.g. a project) use it to follow team membership — project-team members get project access. Delivered at least once.
/// </summary>
public sealed record TeamMembershipChanged(
    Guid TeamId, string Status, Guid? LeaderEmployeeId, IReadOnlyList<Guid> ActiveMemberEmployeeIds, DateTimeOffset ChangedAt) : Strategy.Platform.Kernel.Messaging.IIntegrationEvent;
