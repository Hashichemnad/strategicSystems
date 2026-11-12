using System.Security.Claims;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;

namespace Strategy.Modules.Requests.Reporting;

/// <summary>
/// Report-builder object "requests" on <see cref="RequestReader.VisibleAsync"/>. Form data is never a field (it may hold secret values, ADR 0086):
/// only the request's header.
/// </summary>
internal sealed class RequestDatasetSource(RequestReader reader, IClock clock) : IReportingDatasetSource
{
    public const string RequestsDataset = "requests.requests";

    public string SystemCode => RequestPermissions.SystemCode;

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(RequestsDataset, RequestPermissions.SystemCode, "الطلبات", "Requests", RequestPermissions.Read,
        [
            new("number", "الرقم", "Number", DatasetFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", DatasetFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", DatasetFieldType.Text),
            new("type", "نوع الطلب", "Request type", DatasetFieldType.Text, Groupable: true, LabelSource: RequestChoices.Types),
            new("system", "النظام", "System", DatasetFieldType.Text, Groupable: true, LabelSource: FieldLabelSource.SystemNames),
            new("status", "الحالة", "Status", DatasetFieldType.Lookup, Groupable: true, Choices: RequestChoices.Statuses),
            new("stage", "المرحلة", "Stage", DatasetFieldType.Text, Groupable: true, LabelSource: RequestChoices.Stages),
            new("outcome", "النتيجة", "Outcome", DatasetFieldType.Text, Groupable: true, Choices: RequestChoices.Outcomes),
            new("requester", "مقدم الطلب", "Requester", DatasetFieldType.Employee, Groupable: true),
            new("unit", "الوحدة", "Unit", DatasetFieldType.Unit, Groupable: true),
            new("source", "القناة", "Channel", DatasetFieldType.Text, Groupable: true, Choices: RequestChoices.Channels),
            new("createdAt", "تاريخ الإنشاء", "Created", DatasetFieldType.DateTime),
            new("submittedAt", "تاريخ التقديم", "Submitted", DatasetFieldType.DateTime),
            new("stageDueAt", "استحقاق المرحلة", "Stage due", DatasetFieldType.DateTime),
            new("completedAt", "تاريخ الإنجاز", "Completed", DatasetFieldType.DateTime),
            new("overdue", "متأخر", "Overdue", DatasetFieldType.Bool, Groupable: true),
            new("id", "المعرّف", "Id", DatasetFieldType.Reference, Sortable: false),
        ], "/requests/{id}") { KeyField = "id" },
    ];

    public IReadOnlyList<DatasetRelationship> Relationships { get; } =
    [
        new("requests.request-approvals", RequestsDataset, "id", "workflow.approvals", "resourceId", "سجل الاعتماد", "Approval history"),
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != RequestsDataset)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        var now = clock.UtcNow;
        var open = new List<string> { RequestStatuses.Draft, RequestStatuses.InReview, RequestStatuses.Returned, RequestStatuses.InProgress };

        // Built per run: "overdue" compares with the current time.
        var dataset = new DatasetQuery<Request>(r => r.Id)
            .Field("number", r => r.Number)
            .Field("titleAr", r => r.TitleAr)
            .Field("titleEn", r => r.TitleEn)
            .Field("type", r => r.TypeCode)
            .Field("system", r => r.SystemCode)
            .Field("status", r => r.Status)
            .Field("stage", r => r.Stage)
            .Field("outcome", r => r.Outcome)
            .Field("requester", r => r.RequesterEmployeeId)
            .Field("unit", r => r.UnitId)
            .Field("source", r => r.Source)
            .Field("createdAt", r => r.CreatedAt)
            .Field("submittedAt", r => r.SubmittedAt)
            .Field("stageDueAt", r => r.StageDueAt)
            .Field("completedAt", r => r.CompletedAt)
            .Field("overdue", r => r.StageDueAt != null && r.StageDueAt < now && open.Contains(r.Status))
            .Field("id", r => r.Id);
        var requests = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await dataset.RunAsync(requests, query, cancellationToken).ConfigureAwait(false);
    }
}
