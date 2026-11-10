using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.AuditManagement.Application;

/// <summary>Global search over audit reports (number, titles, auditor body) and findings (number, titles) the user may read (ADR 0160).</summary>
internal sealed class AuditSearchContributor(AuditReader reader) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var reports = await (await reader.ReportsAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, r => r.Number)
            .Select(r => new { r.Id, r.Number, r.TitleAr, r.TitleEn, r.AuditorBody }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var findings = await (await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, f => f.Number)
            .Select(f => new { f.Id, f.Number, f.TitleAr, f.TitleEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return reports.Select(r => SearchRanking.Rank(AuditReport.EntityType, r.Id, query, r.Number, [r.TitleAr, r.TitleEn], [new SearchField("auditor", r.AuditorBody)]))
            .Concat(findings.Select(f => SearchRanking.Rank(Finding.EntityType, f.Id, query, f.Number, [f.TitleAr, f.TitleEn])))
            .OfType<SearchHit>().ToList();
    }
}
