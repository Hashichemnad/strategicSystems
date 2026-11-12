using System.Security.Claims;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.IpRegistry.Feeds;

/// <summary>
/// Reportable record types of the IP Registry (ADR 0190) on <see cref="IpReader.VisibleAsync"/>, as the report builder. Contributors (and their
/// Emirates IDs, ADR 0086) are never fields.
/// </summary>
internal sealed class IpFeeds(IpReader reader) : IReportableRecordSource
{
    public const string RecordType = IpRecord.EntityType;

    private static readonly RecordTypeQuery<IpRecord> Records = new RecordTypeQuery<IpRecord>(r => r.Id, r => r.UpdatedAt)
        .Field("number", r => r.Number)
        .Field("nameAr", r => r.NameAr)
        .Field("nameEn", r => r.NameEn)
        .Field("type", r => r.PropertyType)
        .Field("protection", r => r.ProtectionType)
        .Field("status", r => r.Status)
        .Field("source", r => r.Source)
        .Field("unit", r => r.OwnerUnitId)
        .Field("requester", r => r.RequesterEmployeeId)
        .Field("filingDate", r => r.FilingDate)
        .Field("applicationNo", r => r.ApplicationNo)
        .Field("registrationNo", r => r.RegistrationNo)
        .Field("registrationDate", r => r.RegistrationDate)
        .Field("firstPublicationDate", r => r.FirstPublicationDate)
        .Field("expiryDate", r => r.ExpiryDate)
        .Field("internal", r => r.Internal)
        .Field("createdAt", r => r.CreatedAt)
        .Deleted(r => r.Status == IpStatuses.Withdrawn || r.Status == IpStatuses.Rejected);

    public string SystemCode => IpPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(RecordType, IpPermissions.SystemCode, "IpRecords", "سجلات الملكية الفكرية", "IP records", IpPermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("type", "نوع الملكية", "Property type", RecordFieldType.Lookup, LookupType: IpPermissions.PropertyTypeLookup),
            new("protection", "نوع الحماية", "Protection type", RecordFieldType.Lookup, LookupType: IpPermissions.ProtectionTypeLookup),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: IpStatuses.All.Select(s => new RecordFieldChoice(s, IpLinkTargets.Label(s, true),
                IpLinkTargets.Label(s, false))).ToList()),
            new("source", "المصدر", "Source", RecordFieldType.Lookup, Choices:
            [
                new(IpSources.Request, "طلب", "Request"), new(IpSources.Document, "وثيقة", "Document"),
            ]),
            new("unit", "الوحدة المالكة", "Owner unit", RecordFieldType.Unit),
            new("requester", "مقدم الطلب", "Requester", RecordFieldType.Employee),
            new("filingDate", "تاريخ الإيداع", "Filing date", RecordFieldType.Date),
            new("applicationNo", "رقم الطلب لدى الوزارة", "Application number", RecordFieldType.Text),
            new("registrationNo", "رقم التسجيل", "Registration number", RecordFieldType.Text),
            new("registrationDate", "تاريخ التسجيل", "Registration date", RecordFieldType.Date),
            new("firstPublicationDate", "تاريخ أول نشر", "First publication", RecordFieldType.Date),
            new("expiryDate", "تاريخ انتهاء الحماية", "Protection expiry", RecordFieldType.Date),
            new("internal", "تسجيل داخلي", "Internal registration", RecordFieldType.Bool),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ], [], "/ip/records/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != RecordType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var records = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await Records.RunAsync(records, query, cancellationToken).ConfigureAwait(false);
    }
}
