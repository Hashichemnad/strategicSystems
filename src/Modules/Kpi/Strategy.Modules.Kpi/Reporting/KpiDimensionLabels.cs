using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>Names of strategic objectives and perspectives (strategy elements) for dashboard breakdowns and the strategy map.</summary>
internal sealed class KpiDimensionLabels(KpiDbContext db) : IReportingDimensionLabelSource
{
    public string SystemCode => KpiPermissions.SystemCode;

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (dimension is not ("objective" or "perspective") || codes.Count == 0)
        {
            return new Dictionary<string, DimensionLabel>();
        }

        var list = codes.ToList();
        var elements = await db.Elements.AsNoTracking().Where(e => list.Contains(e.Code)).Select(e => new { e.Code, e.NameAr, e.NameEn }).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return elements.GroupBy(e => e.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().NameAr ?? g.First().NameEn ?? g.Key, g.First().NameEn ?? g.First().NameAr ?? g.Key), StringComparer.Ordinal);
    }
}
