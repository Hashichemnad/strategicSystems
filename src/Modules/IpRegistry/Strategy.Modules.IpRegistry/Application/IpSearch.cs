using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.IpRegistry.Application;

/// <summary>Global search over the IP records the user may see: number, names, description and document number through the existing <c>search_text</c> (ADR 0160).</summary>
internal sealed class IpSearchContributor(IpReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number, nameof(IpRecord.SearchText))
            .Select(r => new { r.Id, r.Number, r.NameAr, r.NameEn, r.Description, r.DocumentNo }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => SearchRanking.Rank(IpRecord.EntityType, r.Id, query, r.Number, [r.NameAr, r.NameEn],
                [new SearchField("description", r.Description), new SearchField("documentNo", r.DocumentNo)]))
            .OfType<SearchHit>().ToList();
    }
}
