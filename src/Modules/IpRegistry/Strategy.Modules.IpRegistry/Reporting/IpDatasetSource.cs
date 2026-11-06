using System.Security.Claims;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.IpRegistry.Reporting;

/// <summary>
/// Report-builder object "IP records" on <see cref="IpReader.VisibleAsync"/>. Contributors (and their Emirates IDs, ADR 0086) are never fields.
/// </summary>
internal sealed class IpDatasetSource(IpReader reader) : IReportingDatasetSource
{
    public const string RecordsDataset = "ip.records";

    private static readonly DatasetQuery<IpRecord> Query = new DatasetQuery<IpRecord>(r => r.Id)
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
        .Field("createdAt", r => r.CreatedAt);

    public string SystemCode => IpPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(RecordsDataset, IpPermissions.SystemCode, "سجلات الملكية الفكرية", "IP records", IpPermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("type", "نوع الملكية", "Property type", DatasetFieldType.Lookup, Groupable: true, LookupType: IpPermissions.PropertyTypeLookup),
            new("protection", "نوع الحماية", "Protection type", DatasetFieldType.Lookup, Groupable: true, LookupType: IpPermissions.ProtectionTypeLookup),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("source", "المصدر", "Source", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الوحدة المالكة", "Owner unit", DatasetFieldType.Unit, Groupable: true),
            new("requester", "مقدم الطلب", "Requester", DatasetFieldType.Employee, Groupable: true),
            new("filingDate", "تاريخ الإيداع", "Filing date", DatasetFieldType.Date),
            new("applicationNo", "رقم الطلب لدى الوزارة", "Application number", DatasetFieldType.Text),
            new("registrationNo", "رقم التسجيل", "Registration number", DatasetFieldType.Text),
            new("registrationDate", "تاريخ التسجيل", "Registration date", DatasetFieldType.Date),
            new("firstPublicationDate", "تاريخ أول نشر", "First publication", DatasetFieldType.Date),
            new("expiryDate", "تاريخ انتهاء الحماية", "Protection expiry", DatasetFieldType.Date),
            new("internal", "تسجيل داخلي", "Internal registration", DatasetFieldType.Bool, Groupable: true),
            new("createdAt", "تاريخ الإنشاء", "Created", DatasetFieldType.DateTime),
        ], "/ip/records/{id}"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != RecordsDataset)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        var records = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await Query.RunAsync(records, query, cancellationToken).ConfigureAwait(false);
    }
}
