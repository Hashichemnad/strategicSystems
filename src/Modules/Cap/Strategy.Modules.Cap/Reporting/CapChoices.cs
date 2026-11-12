using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Cap.Reporting;

/// <summary>AR / EN labels of the Corrective Action Plan's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/cap.json</c> words them.</summary>
internal static class CapChoices
{
    public static readonly IReadOnlyList<RecordFieldChoice> SourceStatuses =
    [
        new("active", "نشط", "Active"),
        new("closed", "مغلق", "Closed"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Origins =
    [
        new("manual", "إدخال يدوي", "Entered manually"),
        new("system", "من نظام آخر", "Raised by another system"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> RemarkStatuses =
    [
        new(CapRemarkStatuses.Assigned, "مسندة", "Assigned"),
        new(CapRemarkStatuses.RejectionPending, "رفض بانتظار التأكيد", "Rejection pending"),
        new(CapRemarkStatuses.Rejected, "مرفوضة", "Rejected"),
        new(CapRemarkStatuses.Planning, "تخطيط الإجراءات", "Planning actions"),
        new(CapRemarkStatuses.Validation, "قيد الاعتماد", "In validation"),
        new(CapRemarkStatuses.Correction, "معادة للتصحيح", "Returned for correction"),
        new(CapRemarkStatuses.Implementation, "قيد التنفيذ", "In implementation"),
        new(CapRemarkStatuses.Verification, "التحقق", "Verification"),
        new(CapRemarkStatuses.Completed, "منجزة", "Completed"),
        new(CapRemarkStatuses.Cancelled, "ملغاة", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ActionStatuses =
    [
        new(Domain.ActionStatuses.NotStarted, "لم يبدأ", "Not started"),
        new(Domain.ActionStatuses.InProgress, "قيد التنفيذ", "In progress"),
        new(Domain.ActionStatuses.Late, "متأخر", "Late"),
        new(Domain.ActionStatuses.Completed, "منجز", "Completed"),
        new(Domain.ActionStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> ValidationStatuses =
    [
        new(Domain.ValidationStatuses.Draft, "مسودة", "Draft"),
        new(Domain.ValidationStatuses.Pending, "بانتظار المكتب", "Waiting for PMO"),
        new(Domain.ValidationStatuses.Approved, "معتمد", "Approved"),
        new(Domain.ValidationStatuses.Rejected, "مرفوض", "Rejected"),
        new(Domain.ValidationStatuses.Returned, "معاد", "Returned"),
    ];
}
