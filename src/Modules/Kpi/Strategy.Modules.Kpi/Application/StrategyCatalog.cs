using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Strategic objectives for other systems (ADR 0075).</summary>
internal sealed class KpiStrategyCatalog(KpiDbContext db) : IKpiStrategyCatalog
{
    public async Task<IReadOnlyList<StrategicObjective>> ListObjectivesAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var source = db.Elements.AsNoTracking().Where(e => e.Kind == ElementKinds.Objective);
        if (!includeInactive)
        {
            source = source.Where(e => e.Active);
        }

        return await source.OrderBy(e => e.Sort).ThenBy(e => e.Code)
            .Select(e => new StrategicObjective(e.Code, e.NameAr, e.NameEn, e.Active, e.Sort)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> EnsureObjectivesAsync(IReadOnlyList<StrategicObjective> objectives, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        var existing = (await db.Elements.AsNoTracking().Select(e => e.Code).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var objective in objectives.Where(o => !string.IsNullOrWhiteSpace(o.Code)).DistinctBy(o => o.Code.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.Add(objective.Code.Trim()))
            {
                continue;
            }

            var element = StrategyElement.Create(ElementKinds.Objective, objective.Code);
            element.Edit(objective.Code, objective.NameAr, objective.NameEn, null, null, null, null, objective.Sort, objective.Active, null, null);
            db.Elements.Add(element);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return created;
    }
}
