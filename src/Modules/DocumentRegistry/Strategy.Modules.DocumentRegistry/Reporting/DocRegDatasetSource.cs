using System.Security.Claims;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.DocumentRegistry.Reporting;

/// <summary>Report-builder object "documents" on <see cref="DocumentVisibility.VisibleAsync"/> (sensitivity clearance, ADR 0083). Content and text are never fields.</summary>
internal sealed class DocRegDatasetSource(DocumentVisibility visibility) : IReportingDatasetSource
{
    public const string DocumentsDataset = "docreg.documents";

    private static readonly DatasetQuery<Document> Query = new DatasetQuery<Document>(d => d.Id)
        .Field("number", d => d.DocumentNo)
        .Field("nameAr", d => d.NameAr)
        .Field("nameEn", d => d.NameEn)
        .Field("level", d => d.Level)
        .Field("type", d => d.DocType)
        .Field("sensitivity", d => d.Sensitivity)
        .Field("status", d => d.Status)
        .Field("unit", d => d.OwnerUnitId)
        .Field("owner", d => d.OwnerEmployeeId)
        .Field("version", d => d.CurrentVersionLabel)
        .Field("effectiveDate", d => d.EffectiveDate)
        .Field("expiryDate", d => d.ExpiryDate)
        .Field("publishedAt", d => d.PublishedAt)
        .Field("shareToAll", d => d.ShareToAll)
        .Field("ackRequired", d => d.AckRequired);

    public string SystemCode => DocRegPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(DocumentsDataset, DocRegPermissions.SystemCode, "الوثائق", "Documents", DocRegPermissions.Read,
        [
            new("number", "رمز الوثيقة", "Document ID", DatasetFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", DatasetFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", DatasetFieldType.Text),
            new("level", "المستوى الهرمي", "Hierarchical level", DatasetFieldType.Lookup, Groupable: true, LookupType: DocRegPermissions.LevelLookup),
            new("type", "نوع الوثيقة", "Document type", DatasetFieldType.Lookup, Groupable: true, LookupType: DocRegPermissions.TypeLookup),
            new("sensitivity", "مستوى السرية", "Sensitivity", DatasetFieldType.Lookup, Groupable: true, LookupType: DocRegPermissions.SensitivityLookup),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true),
            new("unit", "الوحدة المالكة", "Owner unit", DatasetFieldType.Unit, Groupable: true),
            new("owner", "مالك الوثيقة", "Document owner", DatasetFieldType.Employee, Groupable: true),
            new("version", "الإصدار الحالي", "Current version", DatasetFieldType.Text),
            new("effectiveDate", "تاريخ السريان", "Effective date", DatasetFieldType.Date),
            new("expiryDate", "تاريخ انتهاء الصلاحية", "Expiry date", DatasetFieldType.Date),
            new("publishedAt", "تاريخ النشر", "Published", DatasetFieldType.DateTime),
            new("shareToAll", "مشاركة مع الجميع", "Shared with everyone", DatasetFieldType.Bool, Groupable: true),
            new("ackRequired", "يتطلب الإقرار بالاطلاع", "Acknowledgement required", DatasetFieldType.Bool, Groupable: true),
        ], "/documents/{id}"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != DocumentsDataset)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        var documents = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await Query.RunAsync(documents, query, cancellationToken).ConfigureAwait(false);
    }
}
