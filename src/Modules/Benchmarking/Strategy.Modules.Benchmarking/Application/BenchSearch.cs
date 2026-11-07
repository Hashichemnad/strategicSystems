using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>Global search over benchmarking studies (number, titles, request number) the user may read (ADR 0160).</summary>
internal sealed class BenchSearchContributor(BenchReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, s => s.Number)
            .Select(s => new { s.Id, s.Number, s.TitleAr, s.TitleEn, s.RequestNumber }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(s => SearchRanking.Rank(Study.EntityType, s.Id, query, s.Number, [s.TitleAr, s.TitleEn], [new SearchField("requestNumber", s.RequestNumber)]))
            .OfType<SearchHit>().ToList();
    }
}
