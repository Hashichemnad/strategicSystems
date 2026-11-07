using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Kpi.Application;

/// <summary>
/// Global search over KPI cards (code, names, process) and their results (found through the card, latest periods first), within the
/// user's read scopes (ADR 0160). A result ranks below its card.
/// </summary>
internal sealed class KpiSearchContributor(KpiReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var cards = await (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, c => c.Code)
            .Select(c => new { c.Id, c.Code, c.NameAr, c.NameEn, c.ProcessName }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var hits = cards.Select(c => SearchRanking.Rank(KpiCard.EntityType, c.Id, query, c.Code, [c.NameAr, c.NameEn], [new SearchField("process", c.ProcessName)]))
            .OfType<SearchHit>().ToList();
        if (hits.Count == 0)
        {
            return hits;
        }

        var matched = hits.ToDictionary(h => h.Id);
        var cardIds = matched.Keys.ToList();
        var results = await (await reader.ResultsAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => cardIds.Contains(r.CardId)).OrderByDescending(r => r.Year).ThenByDescending(r => r.PeriodCode).Take(query.Limit)
            .Select(r => new { r.Id, r.CardId, r.Year, r.PeriodCode }).ToListAsync(cancellationToken).ConfigureAwait(false);
        hits.AddRange(results.Select(r => new SearchHit(KpiResult.EntityType, r.Id, Math.Round(matched[r.CardId].Score * 0.5, 4), "card",
            r.PeriodCode.ToUpperInvariant() + " " + r.Year.ToString(CultureInfo.InvariantCulture))));
        return hits;
    }
}
