using Strategy.Platform.Integration;
using Strategy.Platform.Links;

namespace Strategy.Platform.Reporting.Datasets;

/// <summary>
/// Generic report-builder columns "Source system" and "External ID" (ADR 0164) for every dataset whose record type can carry external references:
/// the dataset's <see cref="DatasetDefinition.ResourceType"/>, or the linkable type whose link template equals the dataset's link. The module never
/// sees these columns: the engine fills them from the row ids after the module ran its scoped query.
/// </summary>
public sealed class ExternalReferenceColumns(IExternalReferenceStore store, ILinkRegistry links)
{
    public const string SourceCode = "externalSource";
    public const string IdCode = "externalId";

    public static readonly IReadOnlyList<DatasetField> Fields =
    [
        new(SourceCode, "النظام المصدر", "Source system", DatasetFieldType.Text, Filterable: false, Groupable: false, Sortable: false),
        new(IdCode, "المعرّف الخارجي", "External ID", DatasetFieldType.Text, Filterable: false, Groupable: false, Sortable: false),
    ];

    public static bool IsVirtual(string code) => code is SourceCode or IdCode;

    /// <summary>The referenceable record type of a dataset, or null (then no external columns are offered).</summary>
    public async Task<string?> ResourceTypeOfAsync(DatasetDefinition dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.ResourceType is { } explicitType)
        {
            return store.IsReferenceable(explicitType) ? explicitType : null;
        }

        var types = await links.TypesAsync(null, cancellationToken).ConfigureAwait(false);
        return types.FirstOrDefault(t => string.Equals(t.LinkTemplate, dataset.Link, StringComparison.Ordinal))?.ResourceType;
    }

    /// <summary>
    /// The module's result with the requested external columns put back in the requested order (values joined with "; " when a record has
    /// several references).
    /// </summary>
    public async Task<ReportResult> FillAsync(string resourceType, IReadOnlyList<string> requested, ReportResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(result);
        var ids = result.RowIds ?? [];
        var refs = await store.ListForRecordsAsync(resourceType, ids, cancellationToken).ConfigureAwait(false);
        var index = result.Columns.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i, StringComparer.Ordinal);
        var rows = result.Rows.Select((row, r) =>
        {
            var found = r < ids.Count && refs.TryGetValue(ids[r], out var list) ? list : [];
            return (IReadOnlyList<object?>)requested.Select(c => c switch
            {
                SourceCode => found.Count == 0 ? null : string.Join("; ", found.Select(x => x.SourceSystem)),
                IdCode => found.Count == 0 ? null : string.Join("; ", found.Select(x => x.ExternalId)),
                _ => index.TryGetValue(c, out var i) ? row[i] : null,
            }).ToList();
        }).ToList();
        return new ReportResult(requested, rows, result.Total, result.RowIds);
    }
}
