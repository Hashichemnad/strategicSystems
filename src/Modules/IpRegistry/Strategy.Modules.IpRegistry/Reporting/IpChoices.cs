using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.IpRegistry.Reporting;

/// <summary>AR / EN labels of the IP Registry's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/ip.json</c> words them.</summary>
internal static class IpChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(IpStatuses.Submitted, "مقدَّم", "Submitted"),
        new(IpStatuses.UnderReview, "قيد المراجعة", "Under review"),
        new(IpStatuses.Returned, "معاد للتصحيح", "Returned for correction"),
        new(IpStatuses.Rejected, "مرفوض", "Rejected"),
        new(IpStatuses.ApprovedToFile, "معتمد للإيداع", "Approved to file"),
        new(IpStatuses.Filed, "مودَع لدى الوزارة", "Filed with MOET"),
        new(IpStatuses.Registered, "مسجَّل", "Registered"),
        new(IpStatuses.RejectedByMoet, "مرفوض من الوزارة", "Rejected by MOET"),
        new(IpStatuses.Expired, "منتهي الحماية", "Expired"),
        new(IpStatuses.Renewed, "مجدَّد", "Renewed"),
        new(IpStatuses.Withdrawn, "مغلق", "Closed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Sources =
    [
        new(IpSources.Request, "طلب ملكية فكرية", "IP request"),
        new(IpSources.Document, "وثيقة منشورة", "Published document"),
    ];
}
