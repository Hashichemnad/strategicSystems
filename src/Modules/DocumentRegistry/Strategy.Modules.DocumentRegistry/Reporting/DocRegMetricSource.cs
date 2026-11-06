using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Reporting;

/// <summary>
/// Document library metric (docs/design/reporting.md §2.5): published and expired documents — the figures of the Document dashboard. Unit = the
/// owner unit. Visibility is by sensitivity clearance (ADR 0083), not by unit scope, so documents every reader sees (shared with all, or of a level
/// cleared for all employees) are <see cref="ReportingPartitions.Public"/>; the others are in <see cref="ReportingPartitions.Sensitivity"/> of their
/// level (<c>none</c> when unclassified), readable from the cache only by users cleared for the level everywhere (<see cref="VisiblePartitionsAsync"/>).
/// </summary>
internal sealed class DocRegMetricSource(DocRegDbContext db, DocumentVisibility visibility, ISettingsStore settings) : IReportingMetricSource
{
    public const string Documents = "docreg.documents";

    public const string Level = "level";
    public const string Expiring = "expiring";
    public const string SensitivityDimension = "sensitivity";

    /// <summary>Partition code of documents without a sensitivity level (only the office and their owners see them).</summary>
    public const string Unclassified = "none";

    public string SystemCode => DocRegPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(Documents, DocRegPermissions.SystemCode, "الوثائق المنشورة", "Published documents", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            DocRegPermissions.Read, [Level, ReportingDimensions.Type, ReportingDimensions.Status, Expiring, SensitivityDimension],
            DrillLink: "/documents/dashboard",
            DimensionLookups: new Dictionary<string, string>
            {
                [Level] = DocRegPermissions.LevelLookup,
                [ReportingDimensions.Type] = DocRegPermissions.TypeLookup,
                [SensitivityDimension] = DocRegPermissions.SensitivityLookup,
            },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = DocRegFacts.Labels((DocumentStatuses.Published, "منشورة", "Published"), (DocumentStatuses.Expired, "منتهية الصلاحية", "Expired")),
                [Expiring] = DocRegFacts.YesNo,
            }),
    ];

    public async Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var clearance = await visibility.ClearanceAsync(user, cancellationToken).ConfigureAwait(false);
        if (!clearance.CanRead)
        {
            return [];
        }

        var rules = (await settings.GetAsync<DocRegSettings>(cancellationToken).ConfigureAwait(false)).Sensitivities;
        var restricted = rules.Where(r => !r.AllEmployees).Select(r => r.Code).ToList();
        return clearance.Manage
            ? [.. restricted.Select(ReportingPartitions.Sensitivity), ReportingPartitions.Sensitivity(Unclassified)]
            : clearance.AllCodes.Where(restricted.Contains).Select(ReportingPartitions.Sensitivity).ToList();
    }

    /// <summary>The partition of a document: public when every reader sees it, else its sensitivity level's.</summary>
    public static string PartitionOf(bool shareToAll, string? sensitivity, IReadOnlyCollection<string> clearedForAll) =>
        shareToAll || (sensitivity is not null && clearedForAll.Contains(sensitivity))
            ? ReportingPartitions.Public
            : ReportingPartitions.Sensitivity(sensitivity ?? Unclassified);

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new DocRegFacts();
        if (!computation.CoversCurrentMonth || !computation.Wants(Documents))
        {
            return [];
        }

        var documents = computation.User is { } user ? await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false) : db.Documents.AsNoTracking();
        var soon = computation.Today.AddDays(DocumentService.SoonDays);
        var groups = await documents.Where(d => d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired)
            .GroupBy(d => new
            {
                d.OwnerUnitId,
                d.Level,
                d.DocType,
                d.Sensitivity,
                d.ShareToAll,
                d.Status,
                Expiring = d.Status == DocumentStatuses.Published && d.ExpiryDate != null && d.ExpiryDate <= soon,
            })
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var clearedForAll = (await settings.GetAsync<DocRegSettings>(cancellationToken).ConfigureAwait(false)).Sensitivities.Where(r => r.AllEmployees)
            .Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            facts.Add(Documents, computation.CurrentMonth, g.Key.OwnerUnitId, DocRegFacts.Dims(
                (Level, g.Key.Level ?? Unclassified), (ReportingDimensions.Type, g.Key.DocType ?? Unclassified), (ReportingDimensions.Status, g.Key.Status),
                (Expiring, DocRegFacts.Bool(g.Key.Expiring)), (SensitivityDimension, g.Key.Sensitivity ?? Unclassified)),
                g.Count, partition: PartitionOf(g.Key.ShareToAll, g.Key.Sensitivity, clearedForAll));
        }

        return facts.ToList();
    }
}
