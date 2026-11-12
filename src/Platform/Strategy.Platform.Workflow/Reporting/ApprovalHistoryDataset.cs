using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Reporting;
using Strategy.Platform.Reporting.Datasets;
using Strategy.Platform.Workflow.Domain;
using Strategy.Platform.Workflow.Infrastructure;

namespace Strategy.Platform.Workflow.Reporting;

/// <summary>A decided approval step with its instance (report row).</summary>
internal sealed class ApprovalHistoryRow
{
    public ApprovalInstance Instance { get; init; } = null!;

    public ApprovalTask Task { get; init; } = null!;
}

/// <summary>
/// Join-only dataset <c>workflow.approvals</c> (ADR 0180): the decided steps of the approval instances of a record. It has no scope of its own —
/// the reporting engine reads it only through a relationship (e.g. requests → approvals), with <c>resourceId in</c> the keys of parent rows the
/// runner already sees, and refuses it as a primary object.
/// </summary>
internal sealed class ApprovalHistoryDataset(WorkflowDbContext db) : IReportingDatasetSource
{
    public const string Code = "workflow.approvals";

    private static readonly DatasetQuery<ApprovalHistoryRow> Query = new DatasetQuery<ApprovalHistoryRow>(r => r.Instance.ResourceId)
        .Field("process", r => r.Instance.ProcessKey)
        .Field("attempt", r => r.Instance.Attempt)
        .Field("submittedAt", r => r.Instance.SubmittedAt)
        .Field("instanceStatus", r => r.Instance.Status)
        .Field("stepSort", r => r.Task.StepSort)
        .Field("stepAr", r => r.Task.StepNameAr)
        .Field("stepEn", r => r.Task.StepNameEn)
        .Field("outcome", r => r.Task.Outcome)
        .Field("actor", r => r.Task.ActedByName)
        .Field("onBehalfOf", r => r.Task.OnBehalfOfName)
        .Field("comment", r => r.Task.Comment)
        .Field("completedAt", r => r.Task.CompletedAt)
        .Field("resourceId", r => r.Instance.ResourceId);

    public string SystemCode => "workflow";

    public IReadOnlyList<DatasetDefinition> Datasets { get; } =
    [
        new(Code, "workflow", "سجل الاعتماد", "Approval history", "reporting.report.run",
        [
            new("process", "الإجراء", "Process", DatasetFieldType.Text, Groupable: true),
            new("attempt", "المحاولة", "Attempt", DatasetFieldType.Number, Groupable: true),
            new("submittedAt", "تاريخ التقديم", "Submitted on", DatasetFieldType.DateTime),
            new("instanceStatus", "حالة الاعتماد", "Approval status", DatasetFieldType.Lookup, Groupable: true),
            new("stepSort", "ترتيب الخطوة", "Step order", DatasetFieldType.Number),
            new("stepAr", "الخطوة (عربي)", "Step (Arabic)", DatasetFieldType.Text, Groupable: true),
            new("stepEn", "الخطوة (إنجليزي)", "Step (English)", DatasetFieldType.Text, Groupable: true),
            new("outcome", "القرار", "Decision", DatasetFieldType.Lookup, Groupable: true),
            new("actor", "بواسطة", "By", DatasetFieldType.Text, Groupable: true),
            new("onBehalfOf", "نيابة عن", "On behalf of", DatasetFieldType.Text),
            new("comment", "التعليق", "Comment", DatasetFieldType.Text, Filterable: false, Sortable: false),
            new("completedAt", "تاريخ القرار", "Decided on", DatasetFieldType.DateTime),
            new("resourceId", "السجل", "Record", DatasetFieldType.Reference, Sortable: false),
        ], string.Empty) { JoinOnly = true },
    ];

    public async Task<ReportResult> RunAsync(ReportQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Dataset != Code)
        {
            throw new ArgumentException($"Unknown dataset '{query.Dataset}'.", nameof(query));
        }

        // Defence in depth: without the engine's key filter nothing is returned (join-only).
        if (!query.Filters.Any(f => f.Field == "resourceId" && f.Operator is ReportOperators.In or ReportOperators.Equal))
        {
            return new ReportResult(query.Columns, [], 0, []);
        }

        var rows = from t in db.Tasks.AsNoTracking()
                   join i in db.Instances.AsNoTracking() on t.InstanceId equals i.Id
                   where t.CompletedAt != null && t.Outcome != null
                   select new ApprovalHistoryRow { Instance = i, Task = t };
        var stable = query.Sort.Count > 0 || query.GroupBy.Count > 0
            ? query
            : query with { Sort = [new ReportSort("submittedAt", false), new ReportSort("completedAt", false)] };
        return await Query.RunAsync(rows, stable, cancellationToken).ConfigureAwait(false);
    }
}
