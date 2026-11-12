using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>Names of team types and member roles (Settings → Teams) for dashboard breakdowns, reports and dossiers (ADR 0209).</summary>
internal sealed class TeamsDimensionLabels(TeamsDbContext db) : IReportingDimensionLabelSource
{
    public const string Type = "type";
    public const string Role = "role";

    public string SystemCode => "teams";

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (dimension is not (Type or Role) || codes.Count == 0)
        {
            return new Dictionary<string, DimensionLabel>();
        }

        var list = codes.ToList();
        var names = dimension == Type
            ? await db.Types.AsNoTracking().Where(t => list.Contains(t.Code)).Select(t => new { t.Code, t.NameAr, t.NameEn }).ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : await db.Roles.AsNoTracking().Where(r => list.Contains(r.Code)).Select(r => new { r.Code, r.NameAr, r.NameEn }).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        return names.GroupBy(n => n.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().NameAr, g.First().NameEn), StringComparer.Ordinal);
    }
}
