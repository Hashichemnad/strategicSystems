using System.Security.Claims;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Cap.Feeds;

/// <summary>
/// Reportable record types of the CAP (ADR 0190): sources, remarks and corrective actions for analytics feeds and extracts, on the CAP reader's
/// scoped queries (the same as the screens and the report builder).
/// </summary>
internal sealed class CapFeeds(CapReader reader) : IReportableRecordSource
{
    public const string SourceType = CapSource.EntityType;
    public const string RemarkType = Remark.EntityType;
    public const string ActionType = CapAction.EntityType;

    private static readonly RecordTypeQuery<CapSource> Sources = new RecordTypeQuery<CapSource>(s => s.Id, s => s.UpdatedAt)
        .Field("number", s => s.Number)
        .Field("titleAr", s => s.TitleAr)
        .Field("titleEn", s => s.TitleEn)
        .Field("sourceType", s => s.SourceTypeCode)
        .Field("receivedOn", s => s.ReceivedOn)
        .Field("owner", s => s.OwnerEmployeeId)
        .Field("unit", s => s.UnitId)
        .Field("origin", s => s.Origin)
        .Field("linkedType", s => s.LinkedType)
        .Field("linkedId", s => s.LinkedId)
        .Field("status", s => s.Status)
        .Field("createdAt", s => s.CreatedAt);

    private static readonly RecordTypeQuery<Remark> Remarks = new RecordTypeQuery<Remark>(r => r.Id, r => r.UpdatedAt)
        .Field("number", r => r.Number)
        .Field("sourceId", r => r.SourceId)
        .Field("textAr", r => r.TextAr)
        .Field("textEn", r => r.TextEn)
        .Field("status", r => r.Status)
        .Field("priority", r => r.Priority)
        .Field("type", r => r.TypeCode)
        .Field("criterion", r => r.CriterionCode)
        .Field("department", r => r.DepartmentUnitId)
        .Field("section", r => r.SectionUnitId)
        .Field("owner", r => r.OwnerEmployeeId)
        .Field("assignedOn", r => r.AssignedOn)
        .Field("dueOn", r => r.DueOn)
        .Field("respondedAt", r => r.RespondedAt)
        .Field("completedAt", r => r.CompletedAt)
        .Field("createdAt", r => r.CreatedAt)
        .Deleted(r => r.Status == CapRemarkStatuses.Cancelled || r.Status == CapRemarkStatuses.Rejected);

    private static readonly RecordTypeQuery<CapAction> Actions = new RecordTypeQuery<CapAction>(a => a.Id, a => a.UpdatedAt)
        .Field("number", a => a.Number)
        .Field("homeRemarkId", a => a.HomeRemarkId)
        .Field("detailsAr", a => a.DetailsAr)
        .Field("detailsEn", a => a.DetailsEn)
        .Field("type", a => a.TypeCode)
        .Field("status", a => a.Status)
        .Field("validationStatus", a => a.ValidationStatus)
        .Field("assignee", a => a.AssignedEmployeeId)
        .Field("plannedStart", a => a.PlannedStart)
        .Field("plannedEnd", a => a.PlannedEnd)
        .Field("revisedEnd", a => a.RevisedEnd)
        .Field("actualStart", a => a.ActualStart)
        .Field("actualEnd", a => a.ActualEnd)
        .Field("achieved", a => a.AchievedPct)
        .Field("budgetRequired", a => a.BudgetRequired)
        .Field("budgetAvailable", a => a.BudgetAvailable)
        .Field("estimatedCost", a => a.EstimatedCost)
        .Field("currency", a => a.CurrencyCode)
        .Field("projectId", a => a.ProjectId)
        .Field("completedAt", a => a.CompletedAt)
        .Deleted(a => a.Status == ActionStatuses.Cancelled);

    public string SystemCode => CapPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(SourceType, CapPermissions.SystemCode, "Sources", "مصادر الملاحظات", "Remark sources", CapPermissions.Read,
        [
            new("number", "رقم المصدر", "Source number", RecordFieldType.Text),
            new("titleAr", "العنوان بالعربية", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان بالإنجليزية", "Title (English)", RecordFieldType.Text),
            new("sourceType", "نوع المصدر", "Source type", RecordFieldType.Lookup, LookupType: CapPermissions.SourceTypeLookup),
            new("receivedOn", "تاريخ الاستلام", "Received on", RecordFieldType.Date),
            new("owner", "مالك المصدر", "Source owner", RecordFieldType.Employee),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("origin", "المنشأ", "Origin", RecordFieldType.Lookup, Choices: CapChoices.Origins),
            new("linkedType", "نوع السجل المرتبط", "Linked record type", RecordFieldType.Text),
            new("linkedId", "السجل المرتبط", "Linked record", RecordFieldType.Id),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: CapChoices.SourceStatuses),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ],
        [
            new("remarks", "الملاحظات", "Remarks", RemarkType, RecordFields.Id, "sourceId", Many: true),
        ], "/cap/sources/{id}"),
        new(RemarkType, CapPermissions.SystemCode, "Remarks", "ملاحظات خطة العمل التصحيحية", "CAP remarks", CapPermissions.Read,
        [
            new("number", "رقم الملاحظة", "Remark number", RecordFieldType.Text),
            new("sourceId", "المصدر", "Source", RecordFieldType.Id),
            new("textAr", "نص الملاحظة (عربي)", "Remark (Arabic)", RecordFieldType.Text),
            new("textEn", "نص الملاحظة (إنجليزي)", "Remark (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: CapChoices.RemarkStatuses),
            new("priority", "الأولوية", "Priority", RecordFieldType.Lookup, LookupType: CapPermissions.PriorityLookup),
            new("type", "نوع الملاحظة", "Remark type", RecordFieldType.Lookup, LookupType: CapPermissions.RemarkTypeLookup),
            new("criterion", "المعيار", "Criterion", RecordFieldType.Lookup, LookupType: CapPermissions.CriterionLookup),
            new("department", "الإدارة المعنية", "Department", RecordFieldType.Unit),
            new("section", "القسم", "Section", RecordFieldType.Unit),
            new("owner", "مالك الملاحظة", "Remark owner", RecordFieldType.Employee),
            new("assignedOn", "تاريخ الإسناد", "Assigned on", RecordFieldType.Date),
            new("dueOn", "تاريخ الاستحقاق", "Due date", RecordFieldType.Date),
            new("respondedAt", "تاريخ الاستجابة", "Responded at", RecordFieldType.DateTime),
            new("completedAt", "تاريخ الاكتمال", "Completed at", RecordFieldType.DateTime),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
        ],
        [
            new("source", "المصدر", "Source", SourceType, "sourceId", RecordFields.Id, Many: false),
            new("actions", "الإجراءات التصحيحية", "Corrective actions", ActionType, RecordFields.Id, "homeRemarkId", Many: true),
        ], "/cap/remarks/{id}"),
        new(ActionType, CapPermissions.SystemCode, "Actions", "الإجراءات التصحيحية", "CAP corrective actions", CapPermissions.Read,
        [
            new("number", "رقم الإجراء", "Action number", RecordFieldType.Text),
            new("homeRemarkId", "الملاحظة الأساسية", "Home remark", RecordFieldType.Id),
            new("detailsAr", "تفاصيل الإجراء (عربي)", "Action (Arabic)", RecordFieldType.Text),
            new("detailsEn", "تفاصيل الإجراء (إنجليزي)", "Action (English)", RecordFieldType.Text),
            new("type", "نوع الإجراء", "Action type", RecordFieldType.Lookup, LookupType: CapPermissions.ActionTypeLookup),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: CapChoices.ActionStatuses),
            new("validationStatus", "حالة الاعتماد", "Validation status", RecordFieldType.Lookup, Choices: CapChoices.ValidationStatuses),
            new("assignee", "المسؤول", "Assigned to", RecordFieldType.Employee),
            new("plannedStart", "تاريخ البدء المخطط", "Planned start", RecordFieldType.Date),
            new("plannedEnd", "تاريخ الانتهاء المخطط", "Planned end", RecordFieldType.Date),
            new("revisedEnd", "تاريخ الانتهاء المعدل", "Revised end", RecordFieldType.Date),
            new("actualStart", "تاريخ البدء الفعلي", "Actual start", RecordFieldType.Date),
            new("actualEnd", "تاريخ الانتهاء الفعلي", "Actual end", RecordFieldType.Date),
            new("achieved", "نسبة الإنجاز", "Achieved", RecordFieldType.Ratio),
            new("budgetRequired", "يتطلب ميزانية", "Budget required", RecordFieldType.Bool),
            new("budgetAvailable", "الميزانية متوفرة", "Budget available", RecordFieldType.Bool),
            new("estimatedCost", "التكلفة التقديرية", "Estimated cost", RecordFieldType.Money),
            new("currency", "العملة", "Currency", RecordFieldType.Text),
            new("projectId", "المشروع المرتبط", "Linked project", RecordFieldType.Id),
            new("completedAt", "تاريخ الاكتمال", "Completed at", RecordFieldType.DateTime),
        ],
        [
            new("homeRemark", "الملاحظة الأساسية", "Home remark", RemarkType, "homeRemarkId", RecordFields.Id, Many: false),
        ], "/cap/actions/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.RecordType switch
        {
            SourceType => await Sources.RunAsync(await reader.SourcesAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false),
            RemarkType => await Remarks.RunAsync(await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false),
            ActionType => await Actions.RunAsync(await reader.ActionsAsync(user, cancellationToken).ConfigureAwait(false), query, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query)),
        };
    }
}
