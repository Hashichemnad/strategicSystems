using System.Security.Claims;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Reporting;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Tasks.Feeds;

/// <summary>
/// Reportable record type "tasks" (ADR 0190) for analytics feeds and extracts, on <see cref="TaskVisibility"/> (data scope, record roles and
/// the confidential rule, ADR 0039) — the same as the screens and the report builder.
/// </summary>
internal sealed class TaskFeeds(TaskVisibility visibility) : IReportableRecordSource
{
    public const string TaskType = TaskItem.EntityType;

    private static readonly RecordTypeQuery<TaskItem> Tasks = new RecordTypeQuery<TaskItem>(t => t.Id, t => t.UpdatedAt)
        .Field("number", t => t.Number)
        .Field("titleAr", t => t.TitleAr)
        .Field("titleEn", t => t.TitleEn)
        .Field("status", t => t.Status)
        .Field("template", t => t.Template)
        .Field("priority", t => t.Priority)
        .Field("type", t => t.TypeCode)
        .Field("source", t => t.SourceCode)
        .Field("category", t => t.CategoryCode)
        .Field("confidential", t => t.Confidential)
        .Field("unit", t => t.UnitId)
        .Field("assignee", t => t.AssigneeEmployeeId)
        .Field("creator", t => t.CreatorEmployeeId)
        .Field("parentTaskId", t => t.ParentTaskId)
        .Field("linkedType", t => t.LinkedType)
        .Field("linkedId", t => t.LinkedId)
        .Field("plannedStart", t => t.PlannedStart)
        .Field("dueDate", t => t.DueDate)
        .Field("createdAt", t => t.CreatedAt)
        .Field("acceptedAt", t => t.AcceptedAt)
        .Field("completedAt", t => t.CompletedAt)
        .Field("closedAt", t => t.ClosedAt)
        .Field("percentComplete", t => t.PercentComplete)
        .Field("estimatedHours", t => t.EstimatedHours)
        .Field("completedOnTime", t => t.CompletedOnTime)
        .Field("feedbackPoint", t => t.FeedbackPoint)
        .Deleted(t => t.Status == TaskStatuses.Cancelled);

    public string SystemCode => TaskPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(TaskType, TaskPermissions.SystemCode, "Tasks", "المهام", "Tasks", TaskPermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices: TaskChoices.Statuses),
            new("template", "مسار العمل", "Workflow template", RecordFieldType.Lookup, Choices:
            [
                new(TaskTemplates.Full, "كامل", "Full"), new(TaskTemplates.Simple, "مبسط", "Simple"),
            ]),
            new("priority", "الأولوية", "Priority", RecordFieldType.Lookup, Choices: TaskChoices.Priorities),
            new("type", "النوع", "Type", RecordFieldType.Lookup, LookupType: TaskPermissions.TypeLookup),
            new("source", "المصدر", "Source", RecordFieldType.Lookup, LookupType: TaskPermissions.SourceLookup),
            new("category", "الفئة", "Category", RecordFieldType.Lookup, LookupType: TaskPermissions.CategoryLookup),
            new("confidential", "سرية", "Confidential", RecordFieldType.Bool),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("assignee", "المكلف", "Assignee", RecordFieldType.Employee),
            new("creator", "المنشئ", "Creator", RecordFieldType.Employee),
            new("parentTaskId", "المهمة الرئيسية", "Parent task", RecordFieldType.Id),
            new("linkedType", "نوع السجل المرتبط", "Linked record type", RecordFieldType.Text),
            new("linkedId", "السجل المرتبط", "Linked record", RecordFieldType.Id),
            new("plannedStart", "البداية المخططة", "Planned start", RecordFieldType.Date),
            new("dueDate", "تاريخ الاستحقاق", "Due date", RecordFieldType.Date),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
            new("acceptedAt", "تاريخ القبول", "Accepted", RecordFieldType.DateTime),
            new("completedAt", "تاريخ الإنجاز", "Completed", RecordFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed", RecordFieldType.DateTime),
            new("percentComplete", "نسبة الإنجاز (%)", "Percent complete", RecordFieldType.Number),
            new("estimatedHours", "الساعات المقدرة", "Estimated hours", RecordFieldType.Number),
            new("completedOnTime", "أنجزت في الموعد", "Completed on time", RecordFieldType.Bool),
            new("feedbackPoint", "نقاط التقييم", "Feedback points", RecordFieldType.Number),
        ],
        [
            new("parent", "المهمة الرئيسية", "Parent task", TaskType, "parentTaskId", RecordFields.Id, Many: false),
            new("subtasks", "المهام الفرعية", "Subtasks", TaskType, RecordFields.Id, "parentTaskId", Many: true),
        ], "/tasks/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != TaskType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var tasks = await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false);
        return await Tasks.RunAsync(tasks, query, cancellationToken).ConfigureAwait(false);
    }
}
