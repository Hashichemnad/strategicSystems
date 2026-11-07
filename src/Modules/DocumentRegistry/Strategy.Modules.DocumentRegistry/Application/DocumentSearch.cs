using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>
/// Global search over the documents the user is cleared for (ADR 0083 visibility): number, names and description through the existing
/// normalised <c>search_text</c> column (ADR 0160).
/// </summary>
internal sealed class DocumentSearchContributor(DocumentVisibility visibility) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, d => d.DocumentNo, nameof(Document.SearchText))
            .Select(d => new { d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Description }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(d => SearchRanking.Rank(Document.EntityType, d.Id, query, d.DocumentNo, [d.NameAr, d.NameEn], [new SearchField("description", d.Description)]))
            .OfType<SearchHit>().ToList();
    }
}
