using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.DocumentRegistry.Contracts;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Contracts;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Integration;

namespace Strategy.Modules.IpRegistry.Infrastructure;

/// <summary>
/// IP Registry record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): the IP record with its MOET filing and registration
/// data. Only the listed properties are written; a synced record arrives in its Salesforce status without review requests, certificate
/// e-mails, renewal reminders or notifications.
/// </summary>
internal static class IpSyncTargets
{
    private static readonly IReadOnlyList<string> SourceCodes = [IpSources.Request, IpSources.Document];

    public static IServiceCollection AddIpSyncTargets(this IServiceCollection services)
    {
        services.AddSyncTarget(SyncTarget.Entity<IpDbContext, IpRecord>(IpResource.Type, IpPermissions.SystemCode, "سجل ملكية فكرية", "Intellectual property record")
            .Text("number", nameof(IpRecord.Number), required: true)
            .Text("nameAr", nameof(IpRecord.NameAr)).Text("nameEn", nameof(IpRecord.NameEn))
            .Text("description", nameof(IpRecord.Description))
            .Text("descriptionAr", nameof(IpRecord.DescriptionAr)).Text("descriptionEn", nameof(IpRecord.DescriptionEn))
            .Lookup("propertyType", nameof(IpRecord.PropertyType), IpPermissions.PropertyTypeLookup)
            .Lookup("protectionType", nameof(IpRecord.ProtectionType), IpPermissions.ProtectionTypeLookup)
            .Choice("source", nameof(IpRecord.Source), SourceCodes)
            .Reference("documentId", nameof(IpRecord.DocumentId), DocumentResource.Type)
            .Text("documentNo", nameof(IpRecord.DocumentNo))
            .Unit("ownerUnitId", nameof(IpRecord.OwnerUnitId))
            .Person("requesterEmployeeId", nameof(IpRecord.RequesterEmployeeId))
            .Choice("status", nameof(IpRecord.Status), IpStatuses.All, required: true)
            .Date("filingDate", nameof(IpRecord.FilingDate)).Text("applicationNo", nameof(IpRecord.ApplicationNo))
            .Text("registrationNo", nameof(IpRecord.RegistrationNo)).Date("registrationDate", nameof(IpRecord.RegistrationDate))
            .Date("firstPublicationDate", nameof(IpRecord.FirstPublicationDate)).Date("expiryDate", nameof(IpRecord.ExpiryDate))
            .Bool("internal", nameof(IpRecord.Internal))
            .Text("notes", nameof(IpRecord.Notes))
            .Text("moetRejectionReason", nameof(IpRecord.MoetRejectionReason))
            .Default("source", IpSources.Request)
            .Default("status", IpStatuses.Submitted)
            .After<IpRecordSyncHook>());

        return services;
    }
}

/// <summary>Refreshes the normalised search text of a synced IP record (register search, global search), as the module does on every edit.</summary>
internal sealed class IpRecordSyncHook : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        var db = (IpDbContext)transactionContext;
        var record = await db.Records.FirstAsync(r => r.Id == recordId, cancellationToken).ConfigureAwait(false);
        record.Describe(record.NameAr, record.NameEn, record.Description);
    }
}
