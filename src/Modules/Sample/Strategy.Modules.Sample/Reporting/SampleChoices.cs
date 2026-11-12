using Strategy.Modules.Sample.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Sample.Reporting;

/// <summary>AR / EN labels of the sample module's fixed codes for reports and feeds (ADR 0209), as <c>web/src/i18n/*/sample.json</c> words them.</summary>
internal static class SampleChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(SampleItemStatus.Active, "نشط", "Active"),
        new(SampleItemStatus.Inactive, "غير نشط", "Inactive"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ApprovalStatuses =
    [
        new(SampleApprovalStatus.Draft, "مسودة", "Draft"),
        new(SampleApprovalStatus.Pending, "بانتظار الاعتماد", "Pending approval"),
        new(SampleApprovalStatus.Approved, "معتمد", "Approved"),
        new(SampleApprovalStatus.Rejected, "مرفوض", "Rejected"),
        new(SampleApprovalStatus.Returned, "معاد للتصحيح", "Returned for correction"),
    ];
}
