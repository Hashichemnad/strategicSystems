using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Teams.Application;

/// <summary>
/// Global search over teams / committees (number, names, subject), their recommendations (number, text) and meetings (number, title,
/// location), within the teams the user may see (ADR 0160).
/// </summary>
internal sealed class TeamSearchContributor(TeamReader reader, TeamsDbContext db) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var teams = await visible.WhereQuickSearch(query.Normalised, query.Limit, t => t.Number)
            .Select(t => new { t.Id, t.Number, t.NameAr, t.NameEn, t.Subject }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var recommendations = await db.Recommendations.AsNoTracking().Where(r => visible.Any(t => t.Id == r.TeamId))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number)
            .Select(r => new { r.Id, r.Number, r.Text }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var meetings = await db.Meetings.AsNoTracking().Where(m => visible.Any(t => t.Id == m.TeamId))
            .WhereQuickSearch(query.Normalised, query.Limit, m => m.Number)
            .Select(m => new { m.Id, m.Number, m.Title, m.Location }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return teams.Select(t => SearchRanking.Rank(TeamPermissions.ResourceType, t.Id, query, t.Number, [t.NameAr, t.NameEn], [new SearchField("subject", t.Subject)]))
            .Concat(recommendations.Select(r => SearchRanking.Rank(Recommendation.EntityType, r.Id, query, r.Number, [r.Text])))
            .Concat(meetings.Select(m => SearchRanking.Rank(Meeting.EntityType, m.Id, query, m.Number, [m.Title], [new SearchField("location", m.Location)])))
            .OfType<SearchHit>().ToList();
    }
}
