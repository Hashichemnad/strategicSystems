using System.Security.Claims;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.DocumentRegistry.Feeds;

/// <summary>
/// Reportable record types of the Document Registry (ADR 0190) on <see cref="DocumentVisibility.VisibleAsync"/> (sensitivity clearance,
/// ADR 0083), as the report builder. Content and text are never fields.
/// </summary>
internal sealed class DocRegFeeds(DocumentVisibility visibility) : IReportableRecordSource
{
    public const string DocumentType = Document.EntityType;

    private static readonly RecordTypeQuery<Document> Documents = new RecordTypeQuery<Document>(d => d.Id, d => d.UpdatedAt)
        .Field("number", d => d.DocumentNo)
        .Field("nameAr", d => d.NameAr)
        .Field("nameEn", d => d.NameEn)
        .Field("level", d => d.Level)
        .Field("type", d => d.DocType)
        .Field("sensitivity", d => d.Sensitivity)
        .Field("status", d => d.Status)
        .Field("unit", d => d.OwnerUnitId)
        .Field("owner", d => d.OwnerEmployeeId)
        .Field("requester", d => d.RequesterEmployeeId)
        .Field("version", d => d.CurrentVersionLabel)
        .Field("effectiveDate", d => d.EffectiveDate)
        .Field("expiryDate", d => d.ExpiryDate)
        .Field("publishedAt", d => d.PublishedAt)
        .Field("shareToAll", d => d.ShareToAll)
        .Field("ackRequired", d => d.AckRequired)
        .Field("createdAt", d => d.CreatedAt)
        .Deleted(d => d.Status == DocumentStatuses.Withdrawn || d.Status == DocumentStatuses.Rejected);

    public string SystemCode => DocRegPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(DocumentType, DocRegPermissions.SystemCode, "Documents", "الوثائق", "Documents", DocRegPermissions.Read,
        [
            new("number", "رمز الوثيقة", "Document ID", RecordFieldType.Text),
            new("nameAr", "الاسم (عربي)", "Name (Arabic)", RecordFieldType.Text),
            new("nameEn", "الاسم (إنجليزي)", "Name (English)", RecordFieldType.Text),
            new("level", "المستوى الهرمي", "Hierarchical level", RecordFieldType.Lookup, LookupType: DocRegPermissions.LevelLookup),
            new("type", "نوع الوثيقة", "Document type", RecordFieldType.Lookup, LookupType: DocRegPermissions.TypeLookup),
            new("sensitivity", "مستوى السرية", "Sensitivity", RecordFieldType.Lookup, LookupType: DocRegPermissions.SensitivityLookup),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: DocRegChoices.DocumentStatuses),
            new("unit", "الوحدة المالكة", "Owner unit", RecordFieldType.Unit),
            new("owner", "مالك الوثيقة", "Document owner", RecordFieldType.Employee),
            new("requester", "مقدم الطلب", "Requester", RecordFieldType.Employee),
            new("version", "الإصدار الحالي", "Current version", RecordFieldType.Text),
            new("effectiveDate", "تاريخ السريان", "Effective date", RecordFieldType.Date),
            new("expiryDate", "تاريخ انتهاء الصلاحية", "Expiry date", RecordFieldType.Date),
            new("publishedAt", "تاريخ النشر", "Published", RecordFieldType.DateTime),
            new("shareToAll", "مشاركة مع الجميع", "Shared with everyone", RecordFieldType.Bool),
            new("ackRequired", "يتطلب الإقرار بالاطلاع", "Acknowledgement required", RecordFieldType.Bool),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ], [], "/documents/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != DocumentType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var documents = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await Documents.RunAsync(documents, query, cancellationToken).ConfigureAwait(false);
    }
}
