using System.Security.Claims;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>Report-builder object "tasks" on <see cref="TaskVisibility"/> (data scope, record roles and the confidential rule, ADR 0039).</summary>
internal sealed class TaskDatasetSource(TaskVisibility visibility) : IReportingDatasetSource
{
    public const string TasksDataset = "tasks.tasks";

    private static readonly DatasetQuery<TaskItem> Query = new DatasetQuery<TaskItem>(t => t.Id)
        .Field("number", t => t.Number)
        .Field("titleAr", t => t.TitleAr)
        .Field("titleEn", t => t.TitleEn)
        .Field("status", t => t.Status)
        .Field("priority", t => t.Priority)
        .Field("type", t => t.TypeCode)
        .Field("source", t => t.SourceCode)
        .Field("category", t => t.CategoryCode)
        .Field("confidential", t => t.Confidential)
        .Field("unit", t => t.UnitId)
        .Field("assignee", t => t.AssigneeEmployeeId)
        .Field("creator", t => t.CreatorEmployeeId)
        .Field("plannedStart", t => t.PlannedStart)
        .Field("dueDate", t => t.DueDate)
        .Field("createdAt", t => t.CreatedAt)
        .Field("acceptedAt", t => t.AcceptedAt)
        .Field("closedAt", t => t.ClosedAt)
        .Field("percentComplete", t => t.PercentComplete)
        .Field("estimatedHours", t => t.EstimatedHours)
        .Field("completedOnTime", t => t.CompletedOnTime)
        .Field("feedbackPoint", t => t.FeedbackPoint)
        .Field("id", t => t.Id)
        .Field("linkedId", t => t.LinkedId);

    public string SystemCode => TaskPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(TasksDataset, TaskPermissions.SystemCode, "المهام", "Tasks", TaskPermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true, Choices: TaskChoices.Statuses),
            new("priority", "الأولوية", "Priority", DatasetFieldType.Lookup, Groupable: true, Choices: TaskChoices.Priorities),
            new("type", "النوع", "Type", DatasetFieldType.Lookup, Groupable: true, LookupType: TaskPermissions.TypeLookup),
            new("source", "المصدر", "Source", DatasetFieldType.Lookup, Groupable: true, LookupType: TaskPermissions.SourceLookup),
            new("category", "الفئة", "Category", DatasetFieldType.Lookup, Groupable: true, LookupType: TaskPermissions.CategoryLookup),
            new("confidential", "سرية", "Confidential", DatasetFieldType.Bool, Groupable: true),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("assignee", "المكلف", "Assignee", DatasetFieldType.Employee, Groupable: true),
            new("creator", "المنشئ", "Creator", DatasetFieldType.Employee, Groupable: true),
            new("plannedStart", "البداية المخططة", "Planned start", DatasetFieldType.Date),
            new("dueDate", "تاريخ الاستحقاق", "Due date", DatasetFieldType.Date),
            new("createdAt", "تاريخ الإنشاء", "Created", DatasetFieldType.DateTime),
            new("acceptedAt", "تاريخ القبول", "Accepted", DatasetFieldType.DateTime),
            new("closedAt", "تاريخ الإغلاق", "Closed", DatasetFieldType.DateTime),
            new("percentComplete", "نسبة الإنجاز (%)", "Percent complete", DatasetFieldType.Number),
            new("estimatedHours", "الساعات المقدرة", "Estimated hours", DatasetFieldType.Number),
            new("completedOnTime", "أنجزت في الموعد", "Completed on time", DatasetFieldType.Bool, Groupable: true),
            new("feedbackPoint", "نقاط التقييم", "Feedback points", DatasetFieldType.Number),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
            new("linkedId", "السجل المرتبط", "Linked record", DatasetFieldType.Reference, Sortable: false),
        ], "/tasks/{id}") { KeyField = "id" },
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != TasksDataset)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        var tasks = await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false);
        return await Query.RunAsync(tasks, query, cancellationToken).ConfigureAwait(false);
    }
}
