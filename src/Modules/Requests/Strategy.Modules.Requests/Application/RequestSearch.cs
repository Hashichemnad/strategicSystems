using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Requests.Application;

/// <summary>Global search over requests (number, titles) through the existing normalised <c>title_search</c> column, visible requests only (ADR 0160).</summary>
internal sealed class RequestSearchContributor(RequestReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number, nameof(Request.TitleSearch))
            .Select(r => new { r.Id, r.Number, r.TitleAr, r.TitleEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => SearchRanking.Rank(Request.EntityType, r.Id, query, r.Number, [r.TitleAr, r.TitleEn])).OfType<SearchHit>().ToList();
    }
}
