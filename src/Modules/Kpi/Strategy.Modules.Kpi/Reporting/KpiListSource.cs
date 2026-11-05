using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>
/// Top and bottom performers (docs/design/reporting.md §2.5) as on the KPI dashboard: the annual plans of the active cards the user reads, for the
/// year of the period's end, scored (approved results only); top by actual performance, bottom by capped performance.
/// </summary>
internal sealed class KpiListSource(KpiDbContext db, KpiReader reader) : IReportingListSource
{
    public const string TopPerformers = "kpi.top-performers";
    public const string BottomPerformers = "kpi.bottom-performers";

    public string SystemCode => KpiPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(TopPerformers, KpiPermissions.SystemCode, "أفضل المؤشرات أداءً", "Top performing KPIs", KpiPermissions.CardRead),
        new(BottomPerformers, KpiPermissions.SystemCode, "أضعف المؤشرات أداءً", "Lowest performing KPIs", KpiPermissions.CardRead),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (code is not (TopPerformers or BottomPerformers))
        {
            return [];
        }

        var year = query.To.Year;
        var cards = (await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false)).Where(c => c.Status == CardStatuses.Active);
        if (query.UnitIds is { Count: > 0 } units)
        {
            cards = cards.Where(c => (c.ResponsibleUnitId != null && units.Contains(c.ResponsibleUnitId.Value))
                || (c.DepartmentUnitId != null && units.Contains(c.DepartmentUnitId.Value)));
        }

        var scored = from p in db.Plans.AsNoTracking()
                     join c in cards on p.CardId equals c.Id
                     where p.Year == year && p.CappedPerformance != null
                     select new
                     {
                         c.Id,
                         c.Code,
                         c.NameAr,
                         c.NameEn,
                         c.ResponsibleUnitId,
                         p.ActualPerformance,
                         Capped = p.CappedPerformance!.Value,
                         p.Rag,
                     };
        var ordered = code == TopPerformers
            ? scored.OrderByDescending(p => p.ActualPerformance).ThenBy(p => p.Code)
            : scored.OrderBy(p => p.Capped).ThenBy(p => p.Code);
        var rows = await ordered.Take(Math.Clamp(query.Take, 1, 200)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(p => new ReportingListItem(KpiCard.EntityType, p.Id, p.Code, p.NameAr, p.NameEn, p.Rag, Tone(p.Rag),
            code == TopPerformers ? p.ActualPerformance : p.Capped, null, p.ResponsibleUnitId, $"/kpi/cards/{p.Id:D}")).ToList();
    }

    private static string Tone(string rag) => rag switch
    {
        RagCodes.Green => "success",
        RagCodes.Amber => "warning",
        RagCodes.Red => "danger",
        _ => "neutral",
    };
}
