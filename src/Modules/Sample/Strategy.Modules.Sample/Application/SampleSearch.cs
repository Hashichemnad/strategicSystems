using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Sample.Application;

/// <summary>Global search over sample items (number, names), read scope applied in the query (ADR 0160).</summary>
internal sealed class SampleSearchContributor(SampleDbContext db, IAccessService access) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var scoped = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        var rows = await scoped.WhereQuickSearch(query.Normalised, query.Limit, x => x.Number)
            .Select(x => new { x.Id, x.Number, x.NameAr, x.NameEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => SearchRanking.Rank(SamplePermissions.ResourceType, x.Id, query, x.Number, [x.NameAr, x.NameEn])).OfType<SearchHit>().ToList();
    }
}
