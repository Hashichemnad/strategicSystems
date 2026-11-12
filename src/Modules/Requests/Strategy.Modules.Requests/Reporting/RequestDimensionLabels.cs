using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Requests.Reporting;

/// <summary>
/// Names of request types and of their stages (Settings → Request types) for reports and dossiers (ADR 0209). A stage code is named by the first
/// type (in type-code order) that defines it.
/// </summary>
internal sealed class RequestDimensionLabels(RequestsDbContext db) : IReportingDimensionLabelSource
{
    public const string Type = "type";
    public const string Stage = "stage";

    public string SystemCode => RequestPermissions.SystemCode;

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (codes.Count == 0 || dimension is not (Type or Stage))
        {
            return new Dictionary<string, DimensionLabel>();
        }

        var labels = new Dictionary<string, DimensionLabel>(StringComparer.Ordinal);
        if (dimension == Type)
        {
            var list = codes.ToList();
            foreach (var t in await db.Types.AsNoTracking().Where(t => list.Contains(t.Code)).Select(t => new { t.Code, t.NameAr, t.NameEn })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                labels.TryAdd(t.Code, new DimensionLabel(t.NameAr ?? t.NameEn ?? t.Code, t.NameEn ?? t.NameAr ?? t.Code));
            }

            return labels;
        }

        var types = await db.Types.AsNoTracking().OrderBy(t => t.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var stage in types.SelectMany(t => t.Stages).Where(s => codes.Contains(s.Code)))
        {
            labels.TryAdd(stage.Code, new DimensionLabel(stage.NameAr ?? stage.NameEn ?? stage.Code, stage.NameEn ?? stage.NameAr ?? stage.Code));
        }

        return labels;
    }
}
