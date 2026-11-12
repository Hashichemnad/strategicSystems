using Strategy.Platform.Reporting;
using Strategy.Platform.Workflow.Application;

namespace Strategy.Platform.Workflow.Reporting;

/// <summary>Names of approval processes (the names each module registers its process keys with) for the approval history (ADR 0209).</summary>
internal sealed class WorkflowDimensionLabels(ProcessRegistry processes) : IReportingDimensionLabelSource
{
    public const string Process = "process";

    public string SystemCode => "workflow";

    public Task<IReadOnlyDictionary<string, DimensionLabel>> LabelsAsync(string dimension, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, DimensionLabel> labels = dimension != Process || codes.Count == 0
            ? new Dictionary<string, DimensionLabel>()
            : processes.All.Where(p => codes.Contains(p.Key)).GroupBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => new DimensionLabel(g.First().NameAr, g.First().NameEn), StringComparer.Ordinal);
        return Task.FromResult(labels);
    }
}
