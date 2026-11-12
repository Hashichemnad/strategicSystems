using Strategy.Modules.Portfolio.Application;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>Names of codes kept in Portfolio settings (ADR 0209): the priority bands of program requests (Settings → Portfolio).</summary>
internal sealed class PortfolioDimensionLabels(RequestReader reader) : IReportingDimensionLabelSource
{
    public const string PriorityBand = "priority-band";

    public string SystemCode => PortfolioPermissions.SystemCode;

    public async Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (dimension != PriorityBand || codes.Count == 0)
        {
            return new Dictionary<string, DimensionLabel>();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        return settings.PriorityBands.Where(b => codes.Contains(b.Code)).GroupBy(b => b.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().LabelAr, g.First().LabelEn), StringComparer.Ordinal);
    }
}
