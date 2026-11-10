namespace Strategy.Platform.Integration;

/// <summary>A row's (source system, external ID) pair.</summary>
public sealed record ExternalKey(string SourceSystem, string ExternalId);

/// <summary>A problem with a row's external-reference columns: <c>Column</c> is the column key, <c>MessageKey</c> a translatable key.</summary>
public sealed record ExternalImportIssue(int Row, string Column, string MessageKey)
{
    public string Code => MessageKey.Replace(".errors.", ".", StringComparison.Ordinal);
}

/// <summary>
/// The optional "Source system" / "External ID" columns of every Excel / CSV import (ADR 0165) and the checks shared by the importers:
/// both or neither, known and active source, length, no duplicate (source, external ID) in the file, and the two uniqueness rules against
/// what is already stored. One instance per import run (it caches sources and remembers the file's keys).
/// </summary>
public sealed class ExternalImport(IExternalReferenceStore store, string entityType)
{
    public const string SourceColumn = "source_system";
    public const string IdColumn = "external_id";

    /// <summary>Header labels (English, Arabic) of the two columns.</summary>
    public static readonly (string Key, string LabelEn, string LabelAr) Source = (SourceColumn, "Source system (code)", "النظام المصدر (الرمز)");

    public static readonly (string Key, string LabelEn, string LabelAr) Id = (IdColumn, "External ID", "المعرّف الخارجي");

    private readonly Dictionary<string, SourceSystemInfo?> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), int> _seen = [];

    public string EntityType { get; } = entityType;

    /// <summary>
    /// Validates the pair of a row. Returns the key when both are given and valid, null when the row has none (or is invalid — issues added).
    /// </summary>
    public async Task<ExternalKey?> ReadAsync(int row, string? source, string? externalId, List<ExternalImportIssue> issues, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issues);
        source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        externalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        if (source is null && externalId is null)
        {
            return null;
        }

        if (source is null)
        {
            issues.Add(new ExternalImportIssue(row, SourceColumn, ExternalReferenceErrors.SourceRequired));
            return null;
        }

        if (externalId is null)
        {
            issues.Add(new ExternalImportIssue(row, IdColumn, ExternalReferenceErrors.ExternalIdRequired));
            return null;
        }

        var failed = false;
        if (externalId.Length > ExternalReferenceErrors.ExternalIdMaxLength)
        {
            issues.Add(new ExternalImportIssue(row, IdColumn, ExternalReferenceErrors.ExternalIdTooLong));
            failed = true;
        }

        if (!_sources.TryGetValue(source, out var info))
        {
            info = _sources[source] = await store.GetSourceAsync(source, cancellationToken).ConfigureAwait(false);
        }

        if (info is null)
        {
            issues.Add(new ExternalImportIssue(row, SourceColumn, ExternalReferenceErrors.UnknownSource));
            failed = true;
        }
        else if (!info.Active)
        {
            issues.Add(new ExternalImportIssue(row, SourceColumn, ExternalReferenceErrors.InactiveSource));
            failed = true;
        }

        if (failed)
        {
            return null;
        }

        var key = (source, externalId);
        if (_seen.TryGetValue(key, out var first) && first != row)
        {
            issues.Add(new ExternalImportIssue(row, IdColumn, ExternalReferenceErrors.DuplicateInFile));
            return null;
        }

        _seen[key] = row;
        return new ExternalKey(source, externalId);
    }

    /// <summary>The record already linked to the key, if any.</summary>
    public async Task<Guid?> FindAsync(ExternalKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return (await store.FindAsync(key.SourceSystem, EntityType, key.ExternalId, cancellationToken).ConfigureAwait(false))?.RecordId;
    }

    /// <summary>
    /// Checks a key against an existing record the importer matched by its own key (code, employee no.): the external ID must not belong to another
    /// record and the record must not carry another external ID of that source.
    /// </summary>
    public async Task CheckMatchAsync(int row, ExternalKey key, Guid recordId, List<ExternalImportIssue> issues, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(issues);
        var linked = await FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (linked is { } other && other != recordId)
        {
            issues.Add(new ExternalImportIssue(row, IdColumn, ExternalReferenceErrors.ExternalIdTaken));
            return;
        }

        var refs = await store.ListForRecordAsync(EntityType, recordId, cancellationToken).ConfigureAwait(false);
        if (refs.Any(r => r.SourceSystem == key.SourceSystem && r.ExternalId != key.ExternalId))
        {
            issues.Add(new ExternalImportIssue(row, IdColumn, ExternalReferenceErrors.RecordAlreadyReferenced));
        }
    }

    /// <summary>Links the key to the record in the importer's transaction (<paramref name="transactionContext"/>).</summary>
    public Task<Kernel.Results.Result<ExternalReference>> LinkAsync(ExternalKey key, Guid recordId, bool created, object? transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return store.LinkAsync(new ExternalReferenceInput(key.SourceSystem, EntityType, key.ExternalId, recordId, created, SyncStatus: SyncStatuses.Synced),
            transactionContext, cancellationToken);
    }
}
