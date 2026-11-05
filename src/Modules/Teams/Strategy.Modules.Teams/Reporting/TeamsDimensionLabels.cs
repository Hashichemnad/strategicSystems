using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>Names of team types (Settings → Teams) for dashboard breakdowns.</summary>
internal sealed class TeamsDimensionLabels(TeamsDbContext db) : IReportingDimensionLabelSource
{
    public string SystemCode => "teams";

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (dimension != "type" || codes.Count == 0)
        {
            return new Dictionary<string, DimensionLabel>();
        }

        var list = codes.ToList();
        var types = await db.Types.AsNoTracking().Where(t => list.Contains(t.Code)).Select(t => new { t.Code, t.NameAr, t.NameEn }).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return types.ToDictionary(t => t.Code, t => new DimensionLabel(t.NameAr, t.NameEn), StringComparer.Ordinal);
    }
}
