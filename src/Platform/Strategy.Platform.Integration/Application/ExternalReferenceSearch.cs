using System.Security.Claims;
using Strategy.Platform.Search;

namespace Strategy.Platform.Integration.Application;

/// <summary>
/// Global search by external ID (SPEC-ADDENDUM A1 / B, ADR 0160 / 0164): records of any type whose external reference starts with the query.
/// Cross-system contributor: the search service resolves every hit through the Links registry and keeps only records the user may read, so an
/// external ID never reveals a record outside the user's scope. External IDs are near-unique, so the unscoped prefix lookup is bounded by
/// over-fetching a few times the group limit.
/// </summary>
internal sealed class ExternalReferenceSearch(IExternalReferenceStore store) : ISearchContributor
{
    public const string MatchedOn = "externalId";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matches = await store.FindByExternalIdAsync(query.Text, Math.Clamp(query.Limit * 4, 1, 100), cancellationToken).ConfigureAwait(false);
        return matches
            .Select(m => new SearchHit(m.EntityType, m.RecordId,
                string.Equals(m.ExternalId, query.Text.Trim(), StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.9, MatchedOn, $"{m.SourceSystem}: {m.ExternalId}"))
            .ToList();
    }
}
