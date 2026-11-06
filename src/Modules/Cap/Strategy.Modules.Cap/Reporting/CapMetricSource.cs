using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Cap.Reporting;

/// <summary>Metric codes of the CAP (docs/design/reporting.md §2.5) and the dimension values they use.</summary>
public static class CapMetrics
{
    public const string Remarks = "cap.remarks";
    public const string Actions = "cap.actions";
    public const string Closure = "cap.closure";
    public const string ActionsOnTime = "cap.actions-on-time";

    public const string Budget = "budget";
    public const string Validated = "validated";
    public const string Open = "open";

    /// <summary>Action status dimension values (catalogue codes; the stored codes use underscores).</summary>
    public static string ActionStatusCode(string status) => status switch
    {
        ActionStatuses.NotStarted => "not-started",
        ActionStatuses.InProgress => "in-progress",
        _ => status,
    };

    /// <summary>Remark age: days since assignment while open; final remarks are <c>closed</c>.</summary>
    public static string AgeOf(bool open, DateOnly assignedOn, DateOnly today) =>
        open ? ReportingDimensions.AgeBucketOf(Math.Max(0, today.DayNumber - assignedOn.DayNumber)) : "closed";

    /// <summary>On time: achieved (or, without an achieved date, completed) by the end date in force.</summary>
    public static bool OnTime(DateOnly? actualEnd, DateTimeOffset completedAt, DateOnly? end) =>
        end is null || (actualEnd ?? DateOnly.FromDateTime(completedAt.UtcDateTime)) <= end;
}

/// <summary>
/// CAP metrics (ADR 0140 / 0141) on the CAP's own data. Live path: the remarks the user reads (the CAP reader, as the dashboard) and the
/// actions linked to them; unit = the remark's department (the unit column of <c>cap.remark.read</c>), for actions the home remark's.
/// </summary>
internal sealed class CapMetricSource(CapDbContext db, CapReader reader, IClock clock) : IReportingMetricSource
{
    private static readonly string[] FinalStatuses = [.. CapRemarkStatuses.Final];

    public string SystemCode => CapPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(CapMetrics.Remarks, CapPermissions.SystemCode, "الملاحظات", "Remarks", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, CapPermissions.Read,
            [ReportingDimensions.Status, ReportingDimensions.Source, ReportingDimensions.Priority, ReportingDimensions.Overdue, ReportingDimensions.Type,
                ReportingDimensions.AgeBucket, CapMetrics.Open],
            HigherIsBetter: null, DrillLink: "/cap/remarks?unitId={unit}&status={dim:status}&priority={dim:priority}&sourceType={dim:source}",
            DimensionLookups: new Dictionary<string, string>
            {
                [ReportingDimensions.Source] = CapPermissions.SourceTypeLookup,
                [ReportingDimensions.Priority] = CapPermissions.PriorityLookup,
                [ReportingDimensions.Type] = CapPermissions.RemarkTypeLookup,
            },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = CapReportingLabels.RemarkStatusLabels,
                [ReportingDimensions.Overdue] = CapReportingLabels.YesNo,
                [CapMetrics.Open] = CapReportingLabels.YesNo,
                [ReportingDimensions.AgeBucket] = CapReportingLabels.Ages,
            }),
        new(CapMetrics.Actions, CapPermissions.SystemCode, "الإجراءات التصحيحية", "Corrective actions", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            CapPermissions.Read, [ReportingDimensions.Status, CapMetrics.Budget, CapMetrics.Validated], HigherIsBetter: null, DrillLink: "/cap/my-actions?view=all",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = CapReportingLabels.ActionStatusLabels,
                [CapMetrics.Budget] = CapReportingLabels.YesNo,
                [CapMetrics.Validated] = CapReportingLabels.YesNo,
            }),
        new(CapMetrics.Closure, CapPermissions.SystemCode, "نسبة إغلاق الإجراءات", "Action closure rate", MetricKind.Stock, MetricAggregation.Ratio, MetricValueKind.Ratio,
            CapPermissions.Read, [], HigherIsBetter: true, DrillLink: "/cap"),
        new(CapMetrics.ActionsOnTime, CapPermissions.SystemCode, "الإجراءات المنجزة في موعدها", "Actions completed on time", MetricKind.Flow, MetricAggregation.Ratio,
            MetricValueKind.Ratio, CapPermissions.Read, [], HigherIsBetter: true, DrillLink: "/cap/my-actions?view=all"),
    ];

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new List<MetricFact>();
        var remarks = computation.User is { } user ? await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false) : db.Remarks.AsNoTracking();
        var remarkIds = remarks.Select(r => r.Id);
        var linked = db.RemarkActions.AsNoTracking().Where(l => remarkIds.Contains(l.RemarkId)).Select(l => l.ActionId);
        var actions = db.Actions.AsNoTracking().Where(a => linked.Contains(a.Id) && a.Status != ActionStatuses.Cancelled);
        var month = computation.CurrentMonth;
        if (computation.CoversCurrentMonth && computation.Wants(CapMetrics.Remarks))
        {
            facts.AddRange(await RemarkFactsAsync(remarks, month, computation.Today, cancellationToken).ConfigureAwait(false));
        }

        if (computation.CoversCurrentMonth && (computation.Wants(CapMetrics.Actions) || computation.Wants(CapMetrics.Closure)))
        {
            var rows = await actions.Join(db.Remarks.AsNoTracking(), a => a.HomeRemarkId, r => r.Id, (a, r) => new { a, r.DepartmentUnitId })
                .GroupBy(x => new { x.DepartmentUnitId, x.a.Status, x.a.BudgetRequired, Validated = x.a.ValidationStatus == ValidationStatuses.Approved })
                .Select(g => new { g.Key.DepartmentUnitId, g.Key.Status, g.Key.BudgetRequired, g.Key.Validated, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (computation.Wants(CapMetrics.Actions))
            {
                facts.AddRange(rows.Select(r => new MetricFact(CapMetrics.Actions, month, r.DepartmentUnitId, ReportingPartitions.Public, new Dictionary<string, string>
                {
                    [ReportingDimensions.Status] = CapMetrics.ActionStatusCode(r.Status),
                    [CapMetrics.Budget] = Bool(r.BudgetRequired),
                    [CapMetrics.Validated] = Bool(r.Validated),
                }, r.Count)));
            }

            if (computation.Wants(CapMetrics.Closure))
            {
                facts.AddRange(rows.GroupBy(r => r.DepartmentUnitId).Select(g => new MetricFact(CapMetrics.Closure, month, g.Key, ReportingPartitions.Public, Empty,
                    g.Where(r => r.Status == ActionStatuses.Completed).Sum(r => r.Count), g.Sum(r => r.Count))));
            }
        }

        if (computation.Wants(CapMetrics.ActionsOnTime))
        {
            var from = new DateTimeOffset(computation.FromMonth.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var to = new DateTimeOffset(computation.ToMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var completed = await actions.Where(a => a.Status == ActionStatuses.Completed && a.CompletedAt >= from && a.CompletedAt < to)
                .Join(db.Remarks.AsNoTracking(), a => a.HomeRemarkId, r => r.Id, (a, r) => new { r.DepartmentUnitId, a.CompletedAt, a.ActualEnd, End = a.RevisedEnd ?? a.PlannedEnd })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(completed.GroupBy(c => (c.DepartmentUnitId, Month: MetricComputation.MonthOf(c.CompletedAt!.Value)))
                .Where(g => computation.InRange(g.Key.Month))
                .Select(g => new MetricFact(CapMetrics.ActionsOnTime, g.Key.Month, g.Key.DepartmentUnitId, ReportingPartitions.Public, Empty,
                    g.Count(c => CapMetrics.OnTime(c.ActualEnd, c.CompletedAt!.Value, c.End)), g.Count())));
        }

        return facts;
    }

    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static string Bool(bool value) => value ? "true" : "false";

    private async Task<IEnumerable<MetricFact>> RemarkFactsAsync(IQueryable<Remark> remarks, DateOnly month, DateOnly today, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await remarks.Join(db.Sources.AsNoTracking(), r => r.SourceId, s => s.Id, (r, s) => new { r, s.SourceTypeCode })
            .GroupBy(x => new
            {
                x.r.DepartmentUnitId,
                x.r.Status,
                x.r.Priority,
                x.r.TypeCode,
                x.SourceTypeCode,
                x.r.AssignedOn,
                Overdue = !FinalStatuses.Contains(x.r.Status) && x.r.Status != CapRemarkStatuses.Verification && x.r.DueAt < now,
            })
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows
            .Select(r => (r.Key, Open: !CapRemarkStatuses.IsFinal(r.Key.Status), r.Count))
            .GroupBy(r => (r.Key.DepartmentUnitId, r.Key.Status, r.Key.Priority, r.Key.TypeCode, r.Key.SourceTypeCode, r.Key.Overdue, r.Open,
                Age: CapMetrics.AgeOf(r.Open, r.Key.AssignedOn, today)))
            .Select(g => new MetricFact(CapMetrics.Remarks, month, g.Key.DepartmentUnitId, ReportingPartitions.Public, new Dictionary<string, string>
            {
                [ReportingDimensions.Status] = g.Key.Status,
                [ReportingDimensions.Source] = g.Key.SourceTypeCode,
                [ReportingDimensions.Priority] = g.Key.Priority,
                [ReportingDimensions.Overdue] = Bool(g.Key.Overdue),
                [ReportingDimensions.Type] = g.Key.TypeCode,
                [ReportingDimensions.AgeBucket] = g.Key.Age,
                [CapMetrics.Open] = Bool(g.Key.Open),
            }, g.Sum(r => r.Count)));
    }
}

/// <summary>AR / EN labels of fixed dimension values (status codes are not lookups).</summary>
internal static class CapReportingLabels
{
    public static readonly IReadOnlyDictionary<string, DimensionLabel> YesNo = new Dictionary<string, DimensionLabel>
    {
        ["true"] = new("نعم", "Yes"),
        ["false"] = new("لا", "No"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Ages = new Dictionary<string, DimensionLabel>
    {
        ["0-30"] = new("حتى 30 يوماً", "Up to 30 days"),
        ["31-90"] = new("31 - 90 يوماً", "31-90 days"),
        ["91-180"] = new("91 - 180 يوماً", "91-180 days"),
        ["181-365"] = new("181 - 365 يوماً", "181-365 days"),
        ["365+"] = new("أكثر من سنة", "Over a year"),
        ["closed"] = new("مغلقة", "Closed"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> RemarkStatusLabels = new Dictionary<string, DimensionLabel>
    {
        [CapRemarkStatuses.Assigned] = new("مسندة", "Assigned"),
        [CapRemarkStatuses.RejectionPending] = new("رفض بانتظار التأكيد", "Rejection pending"),
        [CapRemarkStatuses.Rejected] = new("مرفوضة", "Rejected"),
        [CapRemarkStatuses.Planning] = new("تخطيط الإجراءات", "Planning actions"),
        [CapRemarkStatuses.Validation] = new("قيد الاعتماد", "In validation"),
        [CapRemarkStatuses.Correction] = new("معادة للتصحيح", "Returned for correction"),
        [CapRemarkStatuses.Implementation] = new("قيد التنفيذ", "In implementation"),
        [CapRemarkStatuses.Verification] = new("التحقق", "Verification"),
        [CapRemarkStatuses.Completed] = new("منجزة", "Completed"),
        [CapRemarkStatuses.Cancelled] = new("ملغاة", "Cancelled"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> ActionStatusLabels = new Dictionary<string, DimensionLabel>
    {
        ["not-started"] = new("لم يبدأ", "Not started"),
        ["in-progress"] = new("قيد التنفيذ", "In progress"),
        ["late"] = new("متأخر", "Late"),
        ["completed"] = new("منجز", "Completed"),
    };
}
