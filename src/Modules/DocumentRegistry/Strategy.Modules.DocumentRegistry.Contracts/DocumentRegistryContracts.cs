using System.Security.Claims;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.DocumentRegistry.Contracts;

/// <summary>Facts other systems use to refer to documents (links, criteria, IP).</summary>
public static class DocumentResource
{
    public const string Type = "docreg.document";

    public const string LinkTemplate = "/documents/{id}";

    public static string Link(Guid id) => LinkTemplate.Replace("{id}", id.ToString("D"), StringComparison.Ordinal);
}

/// <summary>
/// Integration event (ADR 0043, 0087): a document version was published. <see cref="CreatesIpEntry"/> is the document type's flag in Settings →
/// Document Registry; IP creates its record only when it is true. Published in the publish transaction through the outbox.
/// </summary>
public sealed record DocumentPublished(
    Guid DocumentId,
    Guid VersionId,
    string DocumentNo,
    string VersionLabel,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DocType,
    Guid? OwnerUnitId,
    Guid? OwnerEmployeeId,
    Guid? RequesterEmployeeId,
    bool CreatesIpEntry,
    DateTimeOffset PublishedAt) : IIntegrationEvent;

/// <summary>A document as another system may show it (only when the user may see it — sensitivity clearance, ADR 0083).</summary>
public sealed record DocumentBadge(Guid Id, string? DocumentNo, string? NameAr, string? NameEn, string Status, string? CurrentVersion, string Link);

/// <summary>Read access to documents for other systems; the visibility rule applies (null outside it).</summary>
public interface IDocumentDirectory
{
    Task<DocumentBadge?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Published documents the user may see whose title / number match (for similarity checks of other systems).</summary>
    Task<IReadOnlyList<DocumentBadge>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken = default);
}
