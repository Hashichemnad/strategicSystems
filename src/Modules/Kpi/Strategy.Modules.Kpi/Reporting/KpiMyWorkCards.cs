using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>
/// "My KPI results to submit" (docs/design/reporting.md §6): the caller's open and returned results within their submit scope — the query of the
/// module's My Work items (<c>KpiMyWork</c>) — with open / overdue / returned counts and the five earliest due.
/// </summary>
internal sealed class KpiMyWorkCards(KpiDbContext db, IAccessService access, IClock clock) : IMyWorkCardSource
{
    public const string ResultsCard = "kpi-results";

    public string SystemCode => KpiPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var scoped = await access.ApplyScopeAsync(db.Results.AsNoTracking(), user, KpiPermissions.ResultSubmit, KpiScope.Result, cancellationToken).ConfigureAwait(false);
        var mine = scoped.Where(r => r.OwnerEmployeeId == me && (r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned));
        var now = clock.UtcNow;
        var counts = await mine.GroupBy(r => new { r.Status, Overdue = r.DueAt != null && r.DueAt < now })
            .Select(g => new { g.Key.Status, g.Key.Overdue, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (counts.Count == 0)
        {
            return [];
        }

        var rows = await (from r in mine
                          join c in db.Cards.AsNoTracking() on r.CardId equals c.Id
                          orderby r.DueOn, c.Code
                          select new { r.Id, r.Status, r.DueOn, r.DueAt, r.PeriodCode, r.Year, c.Code, c.NameAr, c.NameEn })
            .Take(5).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = rows.Select(r =>
        {
            var overdue = r.DueAt is { } due && due < now;
            var key = r.Status == ResultStatuses.Returned ? "kpi.myWork.returned" : overdue ? "kpi.myWork.overdue" : "kpi.myWork.open";
            return new MyWorkCardItem(KpiResult.EntityType, r.Id, $"{r.Code} · {r.PeriodCode.ToUpperInvariant()} {r.Year}", r.NameAr, r.NameEn, key,
                $"/kpi/results/{r.Id:D}", r.DueOn, overdue);
        }).ToList();
        var overdueCount = counts.Where(c => c.Overdue).Sum(c => c.Count);
        var returned = counts.Where(c => c.Status == ResultStatuses.Returned).Sum(c => c.Count);
        return
        [
            new MyWorkCard(ResultsCard, SystemCode, "myWork.cards.kpi-results",
            [
                new MyWorkCount("toSubmit", counts.Sum(c => c.Count), "info", "/kpi/pending"),
                new MyWorkCount("overdue", overdueCount, overdueCount > 0 ? "danger" : "neutral", "/kpi/pending"),
                new MyWorkCount("returned", returned, returned > 0 ? "warning" : "neutral", "/kpi/pending"),
            ], items, "/kpi/pending", 30),
        ];
    }
}
