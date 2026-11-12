using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.DocumentRegistry.Reporting;

/// <summary>Report-builder object "documents" on <see cref="DocumentVisibility.VisibleAsync"/> (sensitivity clearance, ADR 0083). Content and text are never fields.</summary>
internal sealed class DocRegDatasetSource(DocumentVisibility visibility, DocRegDbContext db) : IReportingDatasetSource
{
    public const string DocumentsDataset = "docreg.documents";
    public const string VersionsDataset = "docreg.versions";

    private static readonly DatasetQuery<DocumentVersionRow> VersionQuery = new DatasetQuery<DocumentVersionRow>(v => v.Document.Id)
        .Field("documentNumber", v => v.Document.DocumentNo)
        .Field("documentNameAr", v => v.Document.NameAr)
        .Field("documentNameEn", v => v.Document.NameEn)
        .Field("unit", v => v.Document.OwnerUnitId)
        .Field("label", v => v.Version.Label)
        .Field("major", v => v.Version.Major)
        .Field("minor", v => v.Version.Minor)
        .Field("requestNumber", v => v.Version.RequestNo)
        .Field("status", v => v.Version.Status)
        .Field("approvedAt", v => v.Version.ApprovedAt)
        .Field("publishedAt", v => v.Version.PublishedAt)
        .Field("newDocument", v => v.Version.IsNewDocument)
        .Field("documentId", v => v.Version.DocumentId);

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
        .Field("ackRequired", d => d.AckRequired)
        .Field("id", d => d.Id);

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
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true, Choices: DocRegChoices.DocumentStatuses),
            new("unit", "الوحدة المالكة", "Owner unit", DatasetFieldType.Unit, Groupable: true),
            new("owner", "مالك الوثيقة", "Document owner", DatasetFieldType.Employee, Groupable: true),
            new("version", "الإصدار الحالي", "Current version", DatasetFieldType.Text),
            new("effectiveDate", "تاريخ السريان", "Effective date", DatasetFieldType.Date),
            new("expiryDate", "تاريخ انتهاء الصلاحية", "Expiry date", DatasetFieldType.Date),
            new("publishedAt", "تاريخ النشر", "Published", DatasetFieldType.DateTime),
            new("shareToAll", "مشاركة مع الجميع", "Shared with everyone", DatasetFieldType.Bool, Groupable: true),
            new("ackRequired", "يتطلب الإقرار بالاطلاع", "Acknowledgement required", DatasetFieldType.Bool, Groupable: true),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/documents/{id}") { KeyField = "id" },
        new(VersionsDataset, DocRegPermissions.SystemCode, "إصدارات الوثائق", "Document versions", DocRegPermissions.Read,
        [
            new("documentNumber", "رقم الوثيقة", "Document number", DatasetFieldType.Text, Groupable: true),
            new("documentNameAr", "الوثيقة (عربي)", "Document (Arabic)", DatasetFieldType.Text),
            new("documentNameEn", "الوثيقة (إنجليزي)", "Document (English)", DatasetFieldType.Text),
            new("unit", "الوحدة المالكة", "Owning unit", DatasetFieldType.Unit, Groupable: true),
            new("label", "الإصدار", "Version", DatasetFieldType.Text),
            new("major", "الإصدار الرئيسي", "Major", DatasetFieldType.Number),
            new("minor", "الإصدار الفرعي", "Minor", DatasetFieldType.Number),
            new("requestNumber", "رقم الطلب", "Request number", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true, Choices: DocRegChoices.VersionStatuses),
            new("approvedAt", "تاريخ الاعتماد", "Approved on", DatasetFieldType.DateTime),
            new("publishedAt", "تاريخ النشر", "Published on", DatasetFieldType.DateTime),
            new("newDocument", "وثيقة جديدة", "New document", DatasetFieldType.Bool, Groupable: true),
            new("documentId", "الوثيقة", "Document", DatasetFieldType.Reference, Sortable: false),
        ], "/documents/{id}?tab=versions"),
    ];

    public IReadOnlyList<DatasetRelationship> Relationships { get; } =
    [
        new("docreg.document-versions", DocumentsDataset, "id", VersionsDataset, "documentId", "الإصدارات", "Versions"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var documents = await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        switch (query.Dataset)
        {
            case DocumentsDataset:
                return await Query.RunAsync(documents, query, cancellationToken).ConfigureAwait(false);
            case VersionsDataset:
                var versions = from v in db.Versions.AsNoTracking()
                               join d in documents on v.DocumentId equals d.Id
                               select new DocumentVersionRow { Document = d, Version = v };
                var stable = query.Sort.Count > 0 || query.GroupBy.Count > 0
                    ? query
                    : query with { Sort = [new ReportSort("documentNumber", false), new ReportSort("major", false), new ReportSort("minor", false)] };
                return await VersionQuery.RunAsync(versions, stable, cancellationToken).ConfigureAwait(false);
            default:
                throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }
    }
}

/// <summary>A version with its (visible) document.</summary>
internal sealed class DocumentVersionRow
{
    public Document Document { get; init; } = null!;

    public DocumentVersion Version { get; init; } = null!;
}
