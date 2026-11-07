using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Integration;

/// <summary>Sync state of a reference (codes; Phase 23 sets them, imports and the REST upsert write <see cref="Synced"/>).</summary>
public static class SyncStatuses
{
    public const string Synced = "synced";
    public const string Pending = "pending";
    public const string Error = "error";
    public const string DeletedInSource = "deleted_in_source";

    public static readonly IReadOnlyList<string> All = [Synced, Pending, Error, DeletedInSource];
}

/// <summary>A source system (Settings → Integrations → Source systems). <c>Code</c> is stable; <c>TypeCode</c> is a lookup code.</summary>
public sealed record SourceSystemInfo(Guid Id, string Code, string? NameAr, string? NameEn, string TypeCode, bool Active);

/// <summary>
/// One external reference (ADR 0164): <c>ExternalId</c> of <c>SourceSystem</c> is the record <c>RecordId</c> of type <c>EntityType</c>.
/// <c>IsOrigin</c>: the record was created from this source (origin badge).
/// </summary>
public sealed record ExternalReference(
    Guid Id,
    string SourceSystem,
    string EntityType,
    string ExternalId,
    Guid RecordId,
    bool IsOrigin,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? SourceModifiedAt,
    string? SourceHash,
    string SyncStatus,
    uint Version);

/// <summary>
/// What to link. Linking the same (source, type, external ID) to the same record again refreshes the sync fields; <c>IsOrigin</c> is only
/// ever switched on. Null sync fields keep their stored value.
/// </summary>
public sealed record ExternalReferenceInput(
    string SourceSystem,
    string EntityType,
    string ExternalId,
    Guid RecordId,
    bool IsOrigin = false,
    DateTimeOffset? SourceModifiedAt = null,
    string? SourceHash = null,
    string? SyncStatus = null);

/// <summary>A record found by an external ID (global search, stream S).</summary>
public sealed record ExternalRecordMatch(string EntityType, Guid RecordId, string SourceSystem, string ExternalId);

/// <summary>Stable codes and message keys of the reference rules (the server returns keys, never sentences).</summary>
public static class ExternalReferenceErrors
{
    public const int ExternalIdMaxLength = 255;
    public const int SourceHashMaxLength = 128;

    public const string UnknownSource = "integration.errors.unknownSource";
    public const string InactiveSource = "integration.errors.inactiveSource";
    public const string SourceRequired = "integration.errors.sourceRequired";
    public const string UnknownType = "integration.errors.unknownType";
    public const string ExternalIdRequired = "integration.errors.externalIdRequired";
    public const string ExternalIdTooLong = "integration.errors.externalIdTooLong";

    /// <summary>The (source, type, external ID) is already linked to another record.</summary>
    public const string ExternalIdTaken = "integration.errors.externalIdTaken";

    /// <summary>The record already carries another external ID from the same source.</summary>
    public const string RecordAlreadyReferenced = "integration.errors.recordAlreadyReferenced";

    /// <summary>Import: the same (source, external ID) appears twice in the file.</summary>
    public const string DuplicateInFile = "integration.errors.duplicateExternalId";

    public static FieldError Field(string field, string messageKey, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(field, messageKey.Replace(".errors.", ".", StringComparison.Ordinal), messageKey, parameters);

    public static Error Of(string field, string messageKey, IReadOnlyDictionary<string, object?>? parameters = null) =>
        Error.Validation([Field(field, messageKey, parameters)]);
}

/// <summary>
/// External references (<c>integration.external_ref</c>, ADR 0164). Writes join the caller's transaction when <c>transactionContext</c> is the
/// caller's DbContext with an open transaction (ADR 0023), so the reference commits with the record.
/// </summary>
public interface IExternalReferenceStore
{
    Task<ExternalReference?> FindAsync(string sourceSystem, string entityType, string externalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalReference>> ListForRecordAsync(string entityType, Guid recordId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ExternalReference>>> ListForRecordsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the reference, or refreshes it when it already links the same record. Both uniqueness rules are validation errors
    /// (<see cref="ExternalReferenceErrors.ExternalIdTaken"/>, <see cref="ExternalReferenceErrors.RecordAlreadyReferenced"/>), also when a
    /// concurrent writer wins the unique index.
    /// </summary>
    Task<Result<ExternalReference>> LinkAsync(ExternalReferenceInput input, object? transactionContext = null, CancellationToken cancellationToken = default);

    /// <summary>Removes the mapping (audited). Not found → <c>NotFound</c>.</summary>
    Task<Result> UnlinkAsync(string sourceSystem, string entityType, string externalId, object? transactionContext = null, CancellationToken cancellationToken = default);

    /// <summary>Records whose external ID equals the text (case-insensitive) or starts with it, newest sync first (global search).</summary>
    Task<IReadOnlyList<ExternalRecordMatch>> FindByExternalIdAsync(string text, int limit = 20, CancellationToken cancellationToken = default);

    Task<SourceSystemInfo?> GetSourceAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Unknown or inactive source → validation error on field <paramref name="field"/>.</summary>
    Task<Result<SourceSystemInfo>> ValidateSourceAsync(string? code, string field = "sourceSystem", CancellationToken cancellationToken = default);

    /// <summary>Is <paramref name="entityType"/> a linkable or registered referenceable type?</summary>
    bool IsReferenceable(string entityType);

    /// <summary>
    /// Serialises upserts of one external ID across requests and instances (PostgreSQL session advisory lock) until disposed: find, create and
    /// link run without a concurrent writer creating a second record.
    /// </summary>
    Task<IAsyncDisposable> LockAsync(string sourceSystem, string entityType, string externalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A record type that can carry external references although it is not a linkable type of the Links registry (e.g. organisation units,
/// employees, holidays). Linkable types are referenceable without registration.
/// </summary>
/// <remarks><c>LinkTemplate</c>: SPA deep link with <c>{id}</c>, or null when the record has no page of its own.</remarks>
public sealed record ReferenceableTypeDefinition(
    string ResourceType, string SystemCode, string NameAr, string NameEn, string ReadPermission, string? LinkTemplate)
{
    public string? LinkOf(Guid id) => LinkTemplate?.Replace("{id}", id.ToString("D"), StringComparison.Ordinal);
}

/// <summary>A referenced record's summary as its owner reports it.</summary>
public sealed record ReferencedRecord(Guid Id, string? Number, string? TitleAr, string? TitleEn);

/// <summary>Implemented by the owner of a registered referenceable type: returns only the records <c>user</c> may read.</summary>
public interface IReferenceableRecordSource
{
    string ResourceType { get; }

    Task<IReadOnlyDictionary<Guid, ReferencedRecord>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>A visible referenced record (linkable or registered type).</summary>
public sealed record ResolvedRecord(string ResourceType, Guid Id, string SystemCode, string? TypeNameAr, string? TypeNameEn, string? Number, string? TitleAr,
    string? TitleEn, string? Link);

/// <summary>The one visibility check for referenced records: Links registry for linkable types, the registered source otherwise.</summary>
public interface IReferencedRecordResolver
{
    /// <summary>Only the records <paramref name="user"/> may read (others are left out); unknown types or disabled systems give nothing.</summary>
    Task<IReadOnlyDictionary<Guid, ResolvedRecord>> ResolveAsync(string resourceType, IReadOnlyCollection<Guid> ids, ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}

public static class IntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddReferenceableType(this IServiceCollection services, ReferenceableTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        return services;
    }

    public static IServiceCollection AddReferenceableRecordSource<TSource>(this IServiceCollection services)
        where TSource : class, IReferenceableRecordSource =>
        services.AddScoped<IReferenceableRecordSource, TSource>();
}
