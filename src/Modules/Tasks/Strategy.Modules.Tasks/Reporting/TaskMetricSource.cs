using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>
/// Task metrics (docs/design/reporting.md §2.5) — the figures of the Task dashboard (<see cref="GetTaskDashboardHandler"/>). Unit = the assignee's
/// unit (<see cref="TaskScope.Map"/>). Confidential tasks go to <see cref="ReportingPartitions.Confidential"/>, which no viewer reads from the
/// cache (ADR 0039, 0141): participants see them only on the live path, through <see cref="TaskVisibility"/>.
/// </summary>
internal sealed class TaskMetricSource(TasksDbContext db, TaskVisibility visibility, IClock clock) : IReportingMetricSource
{
    public const string Tasks = "tasks.tasks";
    public const string Completed = "tasks.completed";
    public const string OnTime = "tasks.on-time";
    public const string HoursToAccept = "tasks.hours-to-accept";
    public const string HoursToComplete = "tasks.hours-to-complete";

    public const string None = "none";

    private static readonly IReadOnlyDictionary<string, DimensionLabel> StatusLabels = TaskFacts.Labels(
        (TaskStatuses.Assigned, "مسندة", "Assigned"), (TaskStatuses.InProgress, "قيد التنفيذ", "In progress"), (TaskStatuses.OnHold, "معلقة", "On hold"),
        (TaskStatuses.Submitted, "مقدمة للاعتماد", "Submitted"), (TaskStatuses.Rejected, "مرفوضة", "Rejected"), (TaskStatuses.ToDo, "للتنفيذ", "To do"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> PriorityLabels = TaskFacts.Labels(
        (TaskPriorities.High, "عالية", "High"), (TaskPriorities.Medium, "متوسطة", "Medium"), (TaskPriorities.Low, "منخفضة", "Low"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> OnTimeLabels = TaskFacts.Labels(
        ("true", "في الموعد", "On time"), ("false", "متأخرة", "Late"), (None, "بدون موعد", "No due date"));

    public string SystemCode => TaskPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(Tasks, TaskPermissions.SystemCode, "المهام المفتوحة", "Open tasks", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, TaskPermissions.Read,
            [ReportingDimensions.Status, ReportingDimensions.Priority, ReportingDimensions.Source, ReportingDimensions.Overdue], DrillLink: "/tasks/dashboard",
            DimensionLookups: new Dictionary<string, string> { [ReportingDimensions.Source] = TaskPermissions.SourceLookup },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = StatusLabels,
                [ReportingDimensions.Priority] = PriorityLabels,
                [ReportingDimensions.Overdue] = TaskFacts.YesNo,
            }),
        new(Completed, TaskPermissions.SystemCode, "المهام المنجزة", "Completed tasks", MetricKind.Flow, MetricAggregation.Sum, MetricValueKind.Count, TaskPermissions.Read,
            [ReportingDimensions.OnTime, ReportingDimensions.Priority, ReportingDimensions.Source], HigherIsBetter: true, DrillLink: "/tasks/dashboard",
            DimensionLookups: new Dictionary<string, string> { [ReportingDimensions.Source] = TaskPermissions.SourceLookup },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.OnTime] = OnTimeLabels,
                [ReportingDimensions.Priority] = PriorityLabels,
            }),
        new(OnTime, TaskPermissions.SystemCode, "نسبة الإنجاز في الموعد", "On-time completion rate", MetricKind.Flow, MetricAggregation.Ratio, MetricValueKind.Ratio,
            TaskPermissions.Read, [], HigherIsBetter: true, DrillLink: "/tasks/dashboard"),
        new(HoursToAccept, TaskPermissions.SystemCode, "متوسط ساعات قبول المهمة", "Average hours to accept", MetricKind.Flow, MetricAggregation.Ratio,
            MetricValueKind.Hours, TaskPermissions.Read, [], HigherIsBetter: false, DrillLink: "/tasks/dashboard"),
        new(HoursToComplete, TaskPermissions.SystemCode, "متوسط ساعات إنجاز المهمة", "Average hours to complete", MetricKind.Flow, MetricAggregation.Ratio,
            MetricValueKind.Hours, TaskPermissions.Read, [], HigherIsBetter: false, DrillLink: "/tasks/dashboard"),
    ];

    /// <summary>Confidential tasks are never readable "in full" (only record by record, by participants), so no restricted partition is listed.</summary>
    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public static string PartitionOf(bool confidential) => confidential ? ReportingPartitions.Confidential : ReportingPartitions.Public;

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new TaskFacts();
        var tasks = (computation.User is { } user ? await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false) : db.Tasks.AsNoTracking())
            .Where(t => t.Status != TaskStatuses.Draft);
        var now = clock.UtcNow;

        if (computation.CoversCurrentMonth && computation.Wants(Tasks))
        {
            var groups = await tasks.Where(t => t.Status != TaskStatuses.Approved && t.Status != TaskStatuses.Done && t.Status != TaskStatuses.Cancelled)
                .GroupBy(t => new
                {
                    t.UnitId,
                    t.Confidential,
                    t.Status,
                    t.Priority,
                    t.SourceCode,
                    Overdue = t.Status != TaskStatuses.OnHold && t.DueAt != null && t.DueAt < now,
                })
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var g in groups)
            {
                facts.Add(Tasks, computation.CurrentMonth, g.Key.UnitId, TaskFacts.Dims(
                    (ReportingDimensions.Status, g.Key.Status), (ReportingDimensions.Priority, g.Key.Priority), (ReportingDimensions.Source, g.Key.SourceCode),
                    (ReportingDimensions.Overdue, TaskFacts.Bool(g.Key.Overdue))), g.Count, partition: PartitionOf(g.Key.Confidential));
            }
        }

        if (computation.Wants(Completed) || computation.Wants(OnTime) || computation.Wants(HoursToAccept) || computation.Wants(HoursToComplete))
        {
            var start = TaskFacts.Start(computation.FromMonth);
            var until = TaskFacts.Start(computation.ToMonth).AddMonths(1);
            var rows = await tasks.Where(t => (t.Status == TaskStatuses.Approved || t.Status == TaskStatuses.Done) && t.ClosedAt >= start && t.ClosedAt < until)
                .Select(t => new
                {
                    t.UnitId,
                    t.Confidential,
                    ClosedAt = t.ClosedAt!.Value,
                    t.CompletedOnTime,
                    t.Priority,
                    t.SourceCode,
                    t.TimeToAcceptSeconds,
                    t.TimeToCompleteSeconds,
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            // Grouped in memory: the month of a timestamp needs no SQL date arithmetic, and only the closed tasks of the range are read.
            foreach (var g in rows.GroupBy(r => (r.UnitId, r.Confidential, Month: MetricComputation.MonthOf(r.ClosedAt), r.CompletedOnTime, r.Priority, r.SourceCode)))
            {
                var (unit, confidential, month, onTime, priority, source) = g.Key;
                var partition = PartitionOf(confidential);
                if (computation.Wants(Completed))
                {
                    facts.Add(Completed, month, unit, TaskFacts.Dims(
                        (ReportingDimensions.OnTime, onTime is { } o ? TaskFacts.Bool(o) : None), (ReportingDimensions.Priority, priority), (ReportingDimensions.Source, source)),
                        g.Count(), partition: partition);
                }

                if (computation.Wants(OnTime) && onTime is { } flag)
                {
                    facts.Add(OnTime, month, unit, TaskFacts.Dims(), flag ? g.Count() : 0, g.Count(), partition);
                }

                var accept = g.Where(r => r.TimeToAcceptSeconds is not null).ToList();
                if (computation.Wants(HoursToAccept) && accept.Count > 0)
                {
                    facts.Add(HoursToAccept, month, unit, TaskFacts.Dims(), accept.Sum(r => r.TimeToAcceptSeconds!.Value) / 3600m, accept.Count, partition);
                }

                var complete = g.Where(r => r.TimeToCompleteSeconds is not null).ToList();
                if (computation.Wants(HoursToComplete) && complete.Count > 0)
                {
                    facts.Add(HoursToComplete, month, unit, TaskFacts.Dims(), complete.Sum(r => r.TimeToCompleteSeconds!.Value) / 3600m, complete.Count, partition);
                }
            }
        }

        return facts.ToList();
    }
}
