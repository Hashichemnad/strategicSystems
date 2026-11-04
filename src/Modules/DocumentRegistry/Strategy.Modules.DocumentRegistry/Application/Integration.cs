using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Links;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>Documents are task-able, and linkable as audit "criteria" later (SPEC §10.5); badges follow the visibility rule.</summary>
public static class DocumentLinks
{
    public static readonly LinkableTypeDefinition Definition = new(
        Document.EntityType, DocRegPermissions.SystemCode, "وثيقة", "Document", [LinkCapabilities.TaskAble], DocRegPermissions.Read, DocumentResource.LinkTemplate);
}

internal sealed class DocumentLinkTargets(DocumentVisibility visibility) : ILinkTargetSource
{
    public string ResourceType => Document.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(d => d.Id, d => new LinkTarget(d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Status, Label(d.Status, true), Label(d.Status, false),
            d.Status switch
            {
                DocumentStatuses.Published => "success",
                DocumentStatuses.Expired => "warning",
                DocumentStatuses.Rejected or DocumentStatuses.Withdrawn => "neutral",
                _ => "info",
            },
            d.Status is DocumentStatuses.Withdrawn or DocumentStatuses.Rejected));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await visibility.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null && permission == DocRegPermissions.Read;

    public static string Label(string status, bool ar) => status switch
    {
        DocumentStatuses.Submitted => ar ? "مقدمة" : "Submitted",
        DocumentStatuses.UnderReview => ar ? "قيد المراجعة" : "Under review",
        DocumentStatuses.Returned => ar ? "معادة للتصحيح" : "Returned",
        DocumentStatuses.Rejected => ar ? "مرفوضة" : "Rejected",
        DocumentStatuses.Approved => ar ? "معتمدة (بانتظار النشر)" : "Approved (awaiting publication)",
        DocumentStatuses.Published => ar ? "منشورة" : "Published",
        DocumentStatuses.Expired => ar ? "منتهية الصلاحية" : "Expired",
        DocumentStatuses.Withdrawn => ar ? "مسحوبة" : "Withdrawn",
        _ => status,
    };
}

/// <summary>Global search and "Ask the system" over documents the user is cleared for (title, number, description and file text).</summary>
internal sealed class DocumentSearchSource(DocumentVisibility visibility) : IRecordSearchSource
{
    public string ResourceType => Document.EntityType;

    public string SystemCode => DocRegPermissions.SystemCode;

    public string ReadPermission => DocRegPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var term = SearchText.Normalise(text);
        if (term.Length == 0)
        {
            return [];
        }

        var like = "%" + term + "%";
        var visible = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(d => (d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired)
                && ((d.SearchText != null && EF.Functions.ILike(d.SearchText, like)) || (d.FileSearchText != null && EF.Functions.ILike(d.FileSearchText, like))))
            .OrderByDescending(d => d.PublishedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(d => new RecordSearchHit(Document.EntityType, d.Id, DocRegPermissions.SystemCode, d.DocumentNo, d.NameAr, d.NameEn, d.Status,
            DocumentResource.Link(d.Id), 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var words = SearchText.Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).Distinct().Take(8).ToList();
        if (words.Count == 0)
        {
            return [];
        }

        var visible = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var candidates = await visible.Where(d => d.Id != excludeId && (d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired))
            .Select(d => new { d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Status, d.SearchText }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates.Select(c => (c, Score: words.Count(w => c.SearchText?.Contains(w, StringComparison.Ordinal) == true) / (double)words.Count))
            .Where(x => x.Score > 0.25).OrderByDescending(x => x.Score).Take(limit)
            .Select(x => new RecordSearchHit(Document.EntityType, x.c.Id, DocRegPermissions.SystemCode, x.c.DocumentNo, x.c.NameAr, x.c.NameEn, x.c.Status,
                DocumentResource.Link(x.c.Id), x.Score)).ToList();
    }
}

/// <summary>My Work: documents I own that expire soon or expired, and acknowledgements due.</summary>
internal sealed class DocRegMyWork(DocRegDbContext db, DocumentVisibility visibility, DocRegCalendar calendar) : IMyWorkSource
{
    public string SystemCode => DocRegPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var me = user.EmployeeIdOf();
        if (me is null)
        {
            return [];
        }

        var today = await calendar.TodayAsync(cancellationToken).ConfigureAwait(false);
        var soon = today.AddDays(60);
        var mine = await db.Documents.AsNoTracking().Where(d => d.OwnerEmployeeId == me
                && ((d.Status == DocumentStatuses.Published && d.ExpiryDate != null && d.ExpiryDate <= soon) || d.Status == DocumentStatuses.Expired))
            .OrderBy(d => d.ExpiryDate).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = mine.Select(d => new MyWorkItem(DocRegPermissions.SystemCode, Document.EntityType, d.Id, d.DocumentNo, d.NameAr, d.NameEn,
            d.Status == DocumentStatuses.Expired ? "docreg.myWork.expired" : "docreg.myWork.expiring", DocumentResource.Link(d.Id), null, d.ExpiryDate)).ToList();

        var visible = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var acknowledged = db.Acknowledgements.Where(a => a.EmployeeId == me).Select(a => a.VersionId);
        var toAck = await visible.Where(d => d.AckRequired && d.Status == DocumentStatuses.Published && d.CurrentVersionId != null
                && !acknowledged.Contains(d.CurrentVersionId!.Value))
            .OrderByDescending(d => d.PublishedAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(toAck.Select(d => new MyWorkItem(DocRegPermissions.SystemCode, Document.EntityType, d.Id, d.DocumentNo, d.NameAr, d.NameEn,
            "docreg.myWork.acknowledge", DocumentResource.Link(d.Id), d.PublishedAt)));
        return items;
    }
}

/// <summary><see cref="IDocumentDirectory"/> for other systems (visibility rule applies).</summary>
internal sealed class DocumentDirectory(DocumentVisibility visibility, DocumentSearchSource search) : IDocumentDirectory
{
    public async Task<DocumentBadge?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await visibility.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } d
            ? new DocumentBadge(d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Status, d.CurrentVersionLabel, DocumentResource.Link(d.Id))
            : null;

    public async Task<IReadOnlyList<DocumentBadge>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken = default) =>
        (await search.SimilarAsync(text, null, user, limit, cancellationToken).ConfigureAwait(false))
        .Select(h => new DocumentBadge(h.Id, h.Number, h.TitleAr, h.TitleEn, h.Status ?? string.Empty, null, h.Link)).ToList();
}
