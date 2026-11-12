using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Integration;

namespace Strategy.Modules.DocumentRegistry.Infrastructure;

/// <summary>
/// Document Registry record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): the controlled document's register entry
/// (number, names, classification, status, validity). Only the listed properties are written; a synced document arrives in its Salesforce
/// status without submission / review requests, publication events (no IP entry is created) or notifications.
/// </summary>
internal static class DocRegSyncTargets
{
    public static IServiceCollection AddDocRegSyncTargets(this IServiceCollection services)
    {
        services.AddSyncTarget(SyncTarget.Entity<DocRegDbContext, Document>(DocumentResource.Type, DocRegPermissions.SystemCode, "وثيقة مؤسسية", "Corporate document")
            .Text("documentNo", nameof(Document.DocumentNo))
            .Text("nameAr", nameof(Document.NameAr)).Text("nameEn", nameof(Document.NameEn))
            .Text("description", nameof(Document.Description))
            .Unit("ownerUnitId", nameof(Document.OwnerUnitId))
            .Person("ownerEmployeeId", nameof(Document.OwnerEmployeeId))
            .Person("requesterEmployeeId", nameof(Document.RequesterEmployeeId))
            .Lookup("level", nameof(Document.Level), DocRegPermissions.LevelLookup)
            .Lookup("docType", nameof(Document.DocType), DocRegPermissions.TypeLookup)
            .Lookup("sensitivity", nameof(Document.Sensitivity), DocRegPermissions.SensitivityLookup)
            .Text("currentVersionLabel", nameof(Document.CurrentVersionLabel))
            .Choice("status", nameof(Document.Status), DocumentStatuses.All, required: true)
            .Date("effectiveDate", nameof(Document.EffectiveDate)).Date("expiryDate", nameof(Document.ExpiryDate))
            .Bool("shareToAll", nameof(Document.ShareToAll)).Bool("ackRequired", nameof(Document.AckRequired))
            .Timestamp("publishedAt", nameof(Document.PublishedAt))
            .Text("withdrawnReason", nameof(Document.WithdrawnReason))
            .Default("status", DocumentStatuses.Submitted)
            .After<DocumentSyncHook>());

        return services;
    }
}

/// <summary>Refreshes the normalised search text of a synced document (library search, global search — ADR 0160), as the module does on every edit.</summary>
internal sealed class DocumentSyncHook : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (DocRegDbContext)transactionContext;
        var document = await db.Documents.FirstAsync(d => d.Id == recordId, cancellationToken).ConfigureAwait(false);
        document.RefreshSearch(null);
    }
}
