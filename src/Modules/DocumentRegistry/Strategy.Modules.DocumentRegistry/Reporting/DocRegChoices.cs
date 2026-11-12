using Strategy.Platform.Reporting;

namespace Strategy.Modules.DocumentRegistry.Reporting;

/// <summary>AR / EN labels of the Document Registry's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/docreg.json</c> words them.</summary>
internal static class DocRegChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> DocumentStatuses =
    [
        new(Domain.DocumentStatuses.Submitted, "مقدَّمة", "Submitted"),
        new(Domain.DocumentStatuses.UnderReview, "قيد المراجعة", "Under review"),
        new(Domain.DocumentStatuses.Returned, "معادة للتصحيح", "Returned for correction"),
        new(Domain.DocumentStatuses.Rejected, "مرفوضة", "Rejected"),
        new(Domain.DocumentStatuses.Approved, "معتمدة – بانتظار النشر", "Approved – awaiting publication"),
        new(Domain.DocumentStatuses.Published, "منشورة", "Published"),
        new(Domain.DocumentStatuses.Expired, "منتهية الصلاحية", "Expired"),
        new(Domain.DocumentStatuses.Withdrawn, "مسحوبة", "Withdrawn"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> VersionStatuses =
    [
        new(Domain.VersionStatuses.Submitted, "مقدَّم", "Submitted"),
        new(Domain.VersionStatuses.UnderReview, "قيد المراجعة", "Under review"),
        new(Domain.VersionStatuses.Returned, "معاد", "Returned"),
        new(Domain.VersionStatuses.Rejected, "مرفوض", "Rejected"),
        new(Domain.VersionStatuses.Approved, "معتمد", "Approved"),
        new(Domain.VersionStatuses.Current, "الحالي", "Current"),
        new(Domain.VersionStatuses.Superseded, "مستبدَل", "Superseded"),
        new(Domain.VersionStatuses.Withdrawn, "مسحوب", "Withdrawn"),
        new(Domain.VersionStatuses.Cancelled, "ملغى", "Cancelled"),
    ];
}
