using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.Modules.Tasks.Application;

/// <summary>Global search over tasks (number, titles) — only what <see cref="TaskVisibility"/> allows, confidential tasks included (ADR 0160).</summary>
internal sealed class TaskSearchContributor(TaskVisibility visibility) : ISearchContributor
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
            .WhereQuickSearch(query.Normalised, query.Limit, t => t.Number)
            .Select(t => new { t.Id, t.Number, t.TitleAr, t.TitleEn }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(t => SearchRanking.Rank(TaskPermissions.ResourceType, t.Id, query, t.Number, [t.TitleAr, t.TitleEn])).OfType<SearchHit>().ToList();
    }
}
