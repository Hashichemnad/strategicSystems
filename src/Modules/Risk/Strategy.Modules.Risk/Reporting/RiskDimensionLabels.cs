using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>Names of rating bands (current framework) and risk categories for dashboard breakdowns.</summary>
internal sealed class RiskDimensionLabels(RiskDbContext db, FrameworkStore frameworks) : IReportingDimensionLabelSource
{
    public string SystemCode => RiskPermissions.SystemCode;

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        switch (dimension)
        {
            case "band":
                var framework = await frameworks.ScorerAsync(cancellationToken).ConfigureAwait(false);
                return framework.Content.Bands.Where(b => codes.Contains(b.Code))
                    .ToDictionary(b => b.Code, b => new DimensionLabel(b.LabelAr ?? b.LabelEn ?? b.Code, b.LabelEn ?? b.LabelAr ?? b.Code), StringComparer.Ordinal);
            case "category":
                var list = codes.ToList();
                var categories = await db.Categories.AsNoTracking().Where(c => list.Contains(c.Code)).Select(c => new { c.Code, c.NameAr, c.NameEn })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return categories.GroupBy(c => c.Code, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().NameAr ?? g.First().NameEn ?? g.Key, g.First().NameEn ?? g.First().NameAr ?? g.Key), StringComparer.Ordinal);
            default:
                return new Dictionary<string, DimensionLabel>();
        }
    }
}
