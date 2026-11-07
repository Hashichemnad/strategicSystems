using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Cap.Application;

/// <summary>Global search over remark sources (number, titles), remarks (number, text) and corrective actions (number, details) the user may read (ADR 0160).</summary>
internal sealed class CapSearchContributor(CapReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var sources = await (await reader.SourcesAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, s => s.Number)
            .Select(s => new { s.Id, s.Number, s.TitleAr, s.TitleEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var remarks = await (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number)
            .Select(r => new { r.Id, r.Number, r.TextAr, r.TextEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actions = await (await reader.ActionsAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, a => a.Number)
            .Select(a => new { a.Id, a.Number, a.DetailsAr, a.DetailsEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return sources.Select(s => SearchRanking.Rank(CapSource.EntityType, s.Id, query, s.Number, [s.TitleAr, s.TitleEn]))
            .Concat(remarks.Select(r => SearchRanking.Rank(Remark.EntityType, r.Id, query, r.Number, [r.TextAr, r.TextEn])))
            .Concat(actions.Select(a => SearchRanking.Rank(CapAction.EntityType, a.Id, query, a.Number, [a.DetailsAr, a.DetailsEn])))
            .OfType<SearchHit>().ToList();
    }
}
