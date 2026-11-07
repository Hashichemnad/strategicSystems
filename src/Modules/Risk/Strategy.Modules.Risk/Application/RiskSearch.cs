using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Risk.Application;

/// <summary>Global search over risks (number, titles, event) and treatment actions (number, titles), within the risks the user may read (ADR 0160).</summary>
internal sealed class RiskSearchContributor(RiskReader reader, RiskDbContext db) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false);
        var risks = await visible.WhereQuickSearch(query.Normalised, query.Limit, r => r.Number)
            .Select(r => new { r.Id, r.Number, r.TitleAr, r.TitleEn, r.EventText }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actions = await db.Actions.AsNoTracking().Where(a => visible.Any(r => r.Id == a.RiskId))
            .WhereQuickSearch(query.Normalised, query.Limit, a => a.Number)
            .Select(a => new { a.Id, a.Number, a.TitleAr, a.TitleEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return risks.Select(r => SearchRanking.Rank(RiskRecord.EntityType, r.Id, query, r.Number, [r.TitleAr, r.TitleEn], [new SearchField("event", r.EventText)]))
            .Concat(actions.Select(a => SearchRanking.Rank(TreatmentAction.EntityType, a.Id, query, a.Number, [a.TitleAr, a.TitleEn])))
            .OfType<SearchHit>().ToList();
    }
}
