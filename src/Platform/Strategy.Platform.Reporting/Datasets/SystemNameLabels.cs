using Strategy.Platform.Access;

namespace Strategy.Platform.Reporting.Datasets;

/// <summary>
/// Names of systems for fields holding a system code (<see cref="FieldLabelSource.SystemNames"/>, ADR 0209): the AR / EN names every system
/// declares in its access catalogue.
/// </summary>
internal sealed class SystemNameLabels(IEnumerable<IAccessCatalogueSource> catalogues) : IReportingDimensionLabelSource
{
    public string SystemCode => FieldLabelSource.SystemNames.SystemCode;

    public Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, DimensionLabel> labels = dimension != FieldLabelSource.SystemNames.Dimension
            ? new Dictionary<string, DimensionLabel>()
            : catalogues.Select(c => c.System).Where(s => codes.Contains(s.Code)).GroupBy(s => s.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().NameAr, g.First().NameEn), StringComparer.Ordinal);
        return Task.FromResult(labels);
    }
}
