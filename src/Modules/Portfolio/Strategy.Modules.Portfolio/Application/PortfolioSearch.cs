using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>
/// Global search over program requests (number, names, partner, previous programme), projects (number, names), milestones (code, titles,
/// main deliverable) and user stories (code, title), within the requests and projects the user may see (ADR 0160).
/// </summary>
internal sealed class PortfolioSearchContributor(RequestReader requests, ProjectLoader projects, PortfolioDbContext db) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var requestRows = await (await requests.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number)
            .Select(r => new { r.Id, r.Number, r.NameAr, r.NameEn, r.PartnerName, r.PreviousProgramName }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var visible = await projects.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var projectRows = await visible.WhereQuickSearch(query.Normalised, query.Limit, p => p.Number)
            .Select(p => new { p.Id, p.Number, p.NameAr, p.NameEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var milestones = await db.Milestones.AsNoTracking().Where(m => visible.Any(p => p.Id == m.ProjectId))
            .WhereQuickSearch(query.Normalised, query.Limit, m => m.Code)
            .Select(m => new { m.Id, m.Code, m.TitleAr, m.TitleEn, m.MainDeliverable }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var stories = await db.Stories.AsNoTracking().Where(s => visible.Any(p => p.Id == s.ProjectId))
            .WhereQuickSearch(query.Normalised, query.Limit, s => s.Code)
            .Select(s => new { s.Id, s.Code, s.Title }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return requestRows.Select(r => SearchRanking.Rank(RequestLinks.Definition.ResourceType, r.Id, query, r.Number, [r.NameAr, r.NameEn],
                [new SearchField("partner", r.PartnerName), new SearchField("previousProgram", r.PreviousProgramName)]))
            .Concat(projectRows.Select(p => SearchRanking.Rank(ProjectLinks.Project.ResourceType, p.Id, query, p.Number, [p.NameAr, p.NameEn])))
            .Concat(milestones.Select(m => SearchRanking.Rank(ProjectLinks.Milestone.ResourceType, m.Id, query, m.Code, [m.TitleAr, m.TitleEn],
                [new SearchField("deliverable", m.MainDeliverable)])))
            .Concat(stories.Select(s => SearchRanking.Rank(ProjectLinks.Story.ResourceType, s.Id, query, s.Code, [s.Title])))
            .OfType<SearchHit>().ToList();
    }
}
