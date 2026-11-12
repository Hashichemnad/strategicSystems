using System.Security.Claims;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Records;

namespace Strategy.Modules.Requests.Feeds;

/// <summary>
/// Reportable record types of the Request Framework (ADR 0190) on <see cref="RequestReader.VisibleAsync"/>, as the report builder. Form data is
/// never a field (it may hold secret values, ADR 0086): only the request's header.
/// </summary>
internal sealed class RequestFeeds(RequestReader reader) : IReportableRecordSource
{
    public const string RequestType = Request.EntityType;

    private static readonly RecordTypeQuery<Request> Requests = new RecordTypeQuery<Request>(r => r.Id, r => r.UpdatedAt)
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
        .Deleted(r => r.Status == RequestStatuses.Cancelled);

    public string SystemCode => RequestPermissions.SystemCode;

    public IReadOnlyList<ReportableRecordType> RecordTypes { get; } =
    [
        new(RequestType, RequestPermissions.SystemCode, "Requests", "الطلبات", "Requests", RequestPermissions.Read,
        [
            new("number", "الرقم", "Number", RecordFieldType.Text),
            new("titleAr", "العنوان (عربي)", "Title (Arabic)", RecordFieldType.Text),
            new("titleEn", "العنوان (إنجليزي)", "Title (English)", RecordFieldType.Text),
            new("type", "نوع الطلب", "Request type", RecordFieldType.Text),
            new("system", "النظام", "System", RecordFieldType.Text),
            new("status", "الحالة", "Status", RecordFieldType.Lookup, Choices:
            [
                new(RequestStatuses.Draft, "مسودة", "Draft"),
                new(RequestStatuses.InReview, "قيد المراجعة", "In review"),
                new(RequestStatuses.Returned, "معاد للتصحيح", "Returned"),
                new(RequestStatuses.InProgress, "قيد التنفيذ", "In progress"),
                new(RequestStatuses.Completed, "مكتمل", "Completed"),
                new(RequestStatuses.Rejected, "مرفوض", "Rejected"),
                new(RequestStatuses.Cancelled, "ملغى", "Cancelled"),
            ]),
            new("stage", "المرحلة", "Stage", RecordFieldType.Text),
            new("outcome", "النتيجة", "Outcome", RecordFieldType.Text),
            new("requester", "مقدم الطلب", "Requester", RecordFieldType.Employee),
            new("unit", "الوحدة", "Unit", RecordFieldType.Unit),
            new("source", "القناة", "Channel", RecordFieldType.Text),
            new("createdAt", "تاريخ الإنشاء", "Created", RecordFieldType.DateTime),
            new("submittedAt", "تاريخ التقديم", "Submitted", RecordFieldType.DateTime),
            new("stageDueAt", "استحقاق المرحلة", "Stage due", RecordFieldType.DateTime),
            new("completedAt", "تاريخ الإنجاز", "Completed", RecordFieldType.DateTime),
        ], [], "/requests/{id}"),
    ];

    public async Task<RecordPage> QueryAsync(RecordQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.RecordType != RequestType)
        {
            throw new ArgumentException($"Unknown record type '{query.RecordType}'.", nameof(query));
        }

        var requests = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        return await Requests.RunAsync(requests, query, cancellationToken).ConfigureAwait(false);
    }
}
