using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Requests.Reporting;

/// <summary>
/// Request Framework metrics (docs/design/reporting.md §2.5): submitted requests by type / status (drafts are private to their author and never
/// counted) and the cycle time of completed requests. Unit = the request's unit (<see cref="RequestScope.Map"/>). Live path: <see cref="RequestReader"/>
/// (scope, requester, processors of the type), the visibility of My Requests and the queue.
/// </summary>
internal sealed class RequestMetricSource(RequestsDbContext db, RequestReader reader, RequestTypeCache types) : IReportingMetricSource
{
    public const string Requests = "requests.requests";
    public const string CycleDays = "requests.cycle-days";

    public const string System = "system";

    private static readonly IReadOnlyDictionary<string, DimensionLabel> StatusLabels = RequestFacts.Labels(
        (RequestStatuses.InReview, "قيد المراجعة", "In review"), (RequestStatuses.Returned, "معاد للتصحيح", "Returned for correction"),
        (RequestStatuses.InProgress, "قيد التنفيذ", "In progress"), (RequestStatuses.Completed, "مكتمل", "Completed"), (RequestStatuses.Rejected, "مرفوض", "Rejected"),
        (RequestStatuses.Cancelled, "ملغى", "Cancelled"));

    private IReadOnlyList<MetricDefinition>? _metrics;

    public string SystemCode => RequestPermissions.SystemCode;

    /// <summary>Type labels come from the (cached) request types, which administrators define at run time.</summary>
    public IReadOnlyList<MetricDefinition> Metrics => _metrics ??= Build(types.All);

    private static List<MetricDefinition> Build(IReadOnlyList<TypeModel> all)
    {
        var typeLabels = all.ToDictionary(t => t.Code, t => new DimensionLabel(t.NameAr ?? t.NameEn ?? t.Code, t.NameEn ?? t.NameAr ?? t.Code), StringComparer.Ordinal);
        return
        [
            new(Requests, RequestPermissions.SystemCode, "الطلبات المقدمة", "Submitted requests", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
                RequestPermissions.Read, [ReportingDimensions.Type, ReportingDimensions.Status, System], DrillLink: "/requests/queue",
                DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
                {
                    [ReportingDimensions.Type] = typeLabels,
                    [ReportingDimensions.Status] = StatusLabels,
                }),
            new(CycleDays, RequestPermissions.SystemCode, "متوسط مدة إنجاز الطلب (أيام)", "Average request cycle time (days)", MetricKind.Flow, MetricAggregation.Ratio,
                MetricValueKind.Days, RequestPermissions.Read, [ReportingDimensions.Type], HigherIsBetter: false, DrillLink: "/requests/queue",
                DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> { [ReportingDimensions.Type] = typeLabels }),
        ];
    }

    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new RequestFacts();
        var requests = (computation.User is { } user ? await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false) : db.Requests.AsNoTracking())
            .Where(r => r.Status != RequestStatuses.Draft);

        if (computation.CoversCurrentMonth && computation.Wants(Requests))
        {
            var groups = await requests.GroupBy(r => new { r.UnitId, r.TypeCode, r.Status, r.SystemCode }).Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var g in groups)
            {
                facts.Add(Requests, computation.CurrentMonth, g.Key.UnitId, RequestFacts.Dims(
                    (ReportingDimensions.Type, g.Key.TypeCode), (ReportingDimensions.Status, g.Key.Status), (System, g.Key.SystemCode)), g.Count);
            }
        }

        if (computation.Wants(CycleDays))
        {
            var start = RequestFacts.Start(computation.FromMonth);
            var until = RequestFacts.Start(computation.ToMonth).AddMonths(1);
            var rows = await requests.Where(r => r.Status == RequestStatuses.Completed && r.SubmittedAt != null && r.CompletedAt >= start && r.CompletedAt < until)
                .Select(r => new { r.UnitId, r.TypeCode, SubmittedAt = r.SubmittedAt!.Value, CompletedAt = r.CompletedAt!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var r in rows)
            {
                facts.Add(CycleDays, MetricComputation.MonthOf(r.CompletedAt), r.UnitId, RequestFacts.Dims((ReportingDimensions.Type, r.TypeCode)),
                    CycleTime(r.SubmittedAt, r.CompletedAt), 1);
            }
        }

        return facts.ToList();
    }

    /// <summary>Calendar days from submission to completion (fractional, never negative).</summary>
    public static decimal CycleTime(DateTimeOffset submittedAt, DateTimeOffset completedAt) =>
        Math.Max(0m, (decimal)(completedAt - submittedAt).TotalDays);
}
