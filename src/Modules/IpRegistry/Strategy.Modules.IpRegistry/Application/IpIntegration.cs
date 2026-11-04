using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.IpRegistry.Contracts;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;

namespace Strategy.Modules.IpRegistry.Application;

/// <summary>
/// <see cref="DocumentPublished"/> → IP record when the document type is flagged "Creates IP register entry" (SPEC §0.0, ADR 0087). Idempotent: one
/// record per document version (unique index), linked to the document through the Links registry.
/// </summary>
internal sealed class DocumentPublishedHandler(IpDbContext db, INumberingService numbering, ILinkStore links, IpNotifier notifier, IClock clock)
    : IIntegrationEventHandler<DocumentPublished>
{
    public async Task HandleAsync(DocumentPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!integrationEvent.CreatesIpEntry
            || await db.Records.AnyAsync(r => r.DocumentVersionId == integrationEvent.VersionId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, IpPermissions.RecordNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
        var record = IpRecord.FromDocument(number, integrationEvent.DocumentId, integrationEvent.VersionId, integrationEvent.DocumentNo, integrationEvent.NameAr,
            integrationEvent.NameEn, integrationEvent.DescriptionAr, integrationEvent.OwnerUnitId, integrationEvent.RequesterEmployeeId, IpPermissions.DocumentPropertyType);
        db.Records.Add(record);
        var authors = new[] { integrationEvent.RequesterEmployeeId, integrationEvent.OwnerEmployeeId }.Where(e => e is not null).Select(e => e!.Value).Distinct().ToList();
        for (var i = 0; i < authors.Count; i++)
        {
            db.Contributors.Add(Contributor.Create(record.Id, authors[i], null, null, "author", Math.Round(1m / authors.Count, 4), i));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await links.AddAsync(IpResource.SourceDocumentLink, new LinkRef(IpRecord.EntityType, record.Id), new LinkRef(DocumentResource.Type, integrationEvent.DocumentId), db,
            cancellationToken).ConfigureAwait(false);
        await notifier.FromDocumentAsync(record, db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>IP records are task-able; badges follow the IP visibility.</summary>
public static class IpLinks
{
    public static readonly LinkableTypeDefinition Definition = new(
        IpRecord.EntityType, IpPermissions.SystemCode, "سجل ملكية فكرية", "IP record", [LinkCapabilities.TaskAble], IpPermissions.Read, IpResource.LinkTemplate);
}

internal sealed class IpLinkTargets(IpReader reader) : ILinkTargetSource
{
    public string ResourceType => IpRecord.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, r.Number, r.NameAr, r.NameEn, r.Status, Label(r.Status, true), Label(r.Status, false),
            IpStatuses.IsProtected(r.Status) ? "success" : r.Status is IpStatuses.Expired or IpStatuses.RejectedByMoet ? "warning" : "info",
            r.Status is IpStatuses.Withdrawn or IpStatuses.Rejected));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null && permission == IpPermissions.Read;

    public static string Label(string status, bool ar) => status switch
    {
        IpStatuses.Submitted => ar ? "مقدم" : "Submitted",
        IpStatuses.UnderReview => ar ? "قيد المراجعة" : "Under review",
        IpStatuses.Returned => ar ? "معاد للتصحيح" : "Returned",
        IpStatuses.Rejected => ar ? "مرفوض" : "Rejected",
        IpStatuses.ApprovedToFile => ar ? "معتمد – للتقديم" : "Approved – to file",
        IpStatuses.Filed => ar ? "مقدم للوزارة" : "Filed with MOET",
        IpStatuses.Registered => ar ? "مسجل" : "Registered",
        IpStatuses.RejectedByMoet => ar ? "مرفوض من الوزارة" : "Rejected by MOET",
        IpStatuses.Expired => ar ? "منتهي" : "Expired",
        IpStatuses.Renewed => ar ? "مجدد" : "Renewed",
        IpStatuses.Withdrawn => ar ? "مسحوب" : "Withdrawn",
        _ => status,
    };
}

/// <summary>Search over IP records the user may see (global search, Ask, similarity checks).</summary>
internal sealed class IpSearchSource(IpReader reader) : IRecordSearchSource
{
    public string ResourceType => IpRecord.EntityType;

    public string SystemCode => IpPermissions.SystemCode;

    public string ReadPermission => IpPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var term = SearchText.Normalise(text);
        if (term.Length == 0)
        {
            return [];
        }

        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => r.SearchText != null && EF.Functions.ILike(r.SearchText, "%" + term + "%")).OrderByDescending(r => r.CreatedAt).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new RecordSearchHit(IpRecord.EntityType, r.Id, IpPermissions.SystemCode, r.Number, r.NameAr, r.NameEn, r.Status, IpResource.Link(r.Id), 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var words = SearchText.Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Distinct().Take(8).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => r.Id != excludeId).Select(r => new { r.Id, r.Number, r.NameAr, r.NameEn, r.Status, r.SearchText }).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => (r, Score: words.Count(w => r.SearchText?.Contains(w, StringComparison.Ordinal) == true) / (double)words.Count))
            .Where(x => x.Score > 0.25).OrderByDescending(x => x.Score).Take(limit)
            .Select(x => new RecordSearchHit(IpRecord.EntityType, x.r.Id, IpPermissions.SystemCode, x.r.Number, x.r.NameAr, x.r.NameEn, x.r.Status, IpResource.Link(x.r.Id), x.Score))
            .ToList();
    }
}

/// <summary>Files of an IP record (supporting documents, certificates): read with the record; written by the office.</summary>
internal sealed class IpFiles(IpReader reader) : IFileOwnerAccess
{
    public static readonly FilePurpose Documents = new("ip.document", IpRecord.EntityType, IpPermissions.SystemCode, FileKinds.Documents, 25 * 1024 * 1024);

    public string OwnerType => IpRecord.EntityType;

    public string AttachmentPurpose => Documents.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await CanReadAsync(user, ownerId, cancellationToken).ConfigureAwait(false)
        && (await reader.CanAsync(user, IpPermissions.Process, cancellationToken).ConfigureAwait(false)
            || await reader.CanAsync(user, IpPermissions.Manage, cancellationToken).ConfigureAwait(false));
}
