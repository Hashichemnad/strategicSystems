using System.Security.Claims;

namespace Strategy.Platform.AI;

/// <summary>One field of a record as sent to an AI feature. <c>Name</c> is the audited property name (e.g. <c>NameEn</c>).</summary>
public sealed record AiRecordField(string Name, string LabelAr, string LabelEn, string? Value);

/// <summary>A record's content for AI features and the <c>get_record</c> tool.</summary>
/// <remarks><c>EntityType</c>: Audit entity type (for "what changed" from the audit trail).</remarks>
/// <remarks><c>People</c>: Names of people on the record, masked before sending when masking is on.</remarks>
public sealed record AiRecordContent(
    string ResourceType,
    Guid Id,
    string SystemCode,
    string EntityType,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string Link,
    IReadOnlyList<AiRecordField> Fields,
    IReadOnlyList<string> People);

/// <summary>Implemented by the owning module per record type. Returns null when the record does not exist or the user may not read it (404).</summary>
public interface IAiRecordSource
{
    string ResourceType { get; }

    Task<AiRecordContent?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>A record found by search or similarity, already filtered to what the user may read.</summary>
public sealed record RecordSearchHit(
    string ResourceType, Guid Id, string SystemCode, string? Number, string? TitleAr, string? TitleEn, string? Status, string Link, double Score);

/// <summary>
/// Full-text search and similar-record lookup for one record type (implemented by the owning module). The data scope is applied in the
/// database query (CLAUDE.md §7).
/// </summary>
public interface IRecordSearchSource
{
    string ResourceType { get; }

    string SystemCode { get; }

    /// <summary>The permission needed to read records of this type (checked before searching).</summary>
    string ReadPermission { get; }

    Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken);

    /// <summary>Records similar to <c>text</c> (trigram similarity), excluding <c>excludeId</c>.</summary>
    Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken);
}
