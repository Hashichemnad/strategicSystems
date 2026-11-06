using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Portfolio.Reporting;

/// <summary>Portfolio metric, dimension and value codes (docs/design/reporting.md §2.5; stable — seeded dashboards use them).</summary>
public static class PortfolioMetricCodes
{
    public const string Projects = "portfolio.projects";
    public const string Health = "portfolio.health";
    public const string Budget = "portfolio.budget";
    public const string Requests = "portfolio.requests";
    public const string Milestones = "portfolio.milestones";

    public const string Delayed = "delayed";
    public const string Classification = "classification";
    public const string Kind = "kind";

    public const string HealthNone = "none";
    public const string KindPlanned = "planned";
    public const string KindActual = "actual";
    public const string MilestoneCompleted = "completed";
    public const string MilestoneOpen = "open";
}

internal static class Labels
{
    public static IReadOnlyDictionary<string, DimensionLabel> Of(params (string Code, string Ar, string En)[] values) =>
        values.ToDictionary(v => v.Code, v => new DimensionLabel(v.Ar, v.En), StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Bool = Of(("true", "نعم", "Yes"), ("false", "لا", "No"));

    public static string Code(bool value) => value ? "true" : "false";
}

/// <summary>
/// Portfolio facts (docs/design/reporting.md §2.5) computed on the module's own DbContext. With a user: only the projects / requests that user
/// reads (<see cref="RequestReader"/>, the reader of the Portfolio screens and dashboard) and budget figures only of the projects whose costs they
/// may see (<see cref="CostVisibility"/>, ADR 0098). The fact unit is the scope unit of <c>portfolio.project.read</c> / <c>portfolio.request.read</c>
/// (<see cref="ProjectScope"/> / <see cref="RequestScope"/>: the owning unit).
/// </summary>
internal sealed class PortfolioMetricSource(
    PortfolioDbContext db, RequestReader reader, HealthService health, CostVisibility costs, IAccessService access) : IReportingMetricSource
{
    private static readonly IReadOnlyDictionary<string, DimensionLabel> Stages = Labels.Of(
        (ProjectStages.Initiation, "البدء", "Initiation"), (ProjectStages.Planning, "التخطيط", "Planning"), (ProjectStages.Execution, "التنفيذ", "Execution"),
        (ProjectStages.Closure, "الإغلاق", "Closure"), (ProjectStages.Impact, "قياس الأثر", "Impact"), (ProjectStages.Completed, "مكتمل", "Completed"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> ProjectStatusLabels = Labels.Of(
        (ProjectStatuses.Active, "نشط", "Active"), (ProjectStatuses.OnHold, "معلّق", "On hold"), (ProjectStatuses.Cancelled, "ملغى", "Cancelled"),
        (ProjectStatuses.Completed, "مكتمل", "Completed"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> HealthLabels = Labels.Of(
        (Rag.Green, "أخضر", "Green"), (Rag.Amber, "كهرماني", "Amber"), (Rag.Red, "أحمر", "Red"), (PortfolioMetricCodes.HealthNone, "غير مقيّم", "Not rated"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> ClassificationLabels = Labels.Of(
        (Classifications.Project, "مشروع", "Project"), (Classifications.Initiative, "مبادرة", "Initiative"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> RequestStages = Labels.Of(
        (RequestStatuses.InApproval, "قيد الاعتماد", "In approval"), (RequestStatuses.Approved, "معتمد", "Approved"), (RequestStatuses.Rejected, "مرفوض", "Rejected"),
        (RequestStatuses.Cancelled, "ملغى", "Cancelled"));

    public static readonly IReadOnlyList<MetricDefinition> Definitions =
    [
        new(PortfolioMetricCodes.Projects, PortfolioPermissions.SystemCode, "المشاريع والمبادرات", "Projects and initiatives", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, PortfolioPermissions.ProjectRead,
            [ReportingDimensions.Stage, ReportingDimensions.Health, ReportingDimensions.Status, PortfolioMetricCodes.Delayed, PortfolioMetricCodes.Classification],
            DrillLink: "/portfolio/projects?status={dim:status}&classification={dim:classification}",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Stage] = Stages,
                [ReportingDimensions.Health] = HealthLabels,
                [ReportingDimensions.Status] = ProjectStatusLabels,
                [PortfolioMetricCodes.Delayed] = Labels.Of(("true", "متأخر", "Delayed"), ("false", "حسب الخطة", "On track")),
                [PortfolioMetricCodes.Classification] = ClassificationLabels,
            }),
        new(PortfolioMetricCodes.Health, PortfolioPermissions.SystemCode, "نسبة المشاريع بحالة خضراء", "Projects in green health", MetricKind.Stock,
            MetricAggregation.Ratio, MetricValueKind.Ratio, PortfolioPermissions.ProjectRead, [PortfolioMetricCodes.Classification], HigherIsBetter: true,
            DrillLink: "/portfolio/dashboard",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> { [PortfolioMetricCodes.Classification] = ClassificationLabels }),
        new(PortfolioMetricCodes.Budget, PortfolioPermissions.SystemCode, "ميزانية المشاريع والتكلفة الفعلية", "Project budget and actual cost", MetricKind.Stock,
            MetricAggregation.Sum, MetricValueKind.Money, PortfolioPermissions.ProjectRead, [PortfolioMetricCodes.Kind, PortfolioMetricCodes.Classification],
            DrillLink: "/portfolio/dashboard",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [PortfolioMetricCodes.Kind] = Labels.Of((PortfolioMetricCodes.KindPlanned, "الميزانية", "Budget"), (PortfolioMetricCodes.KindActual, "التكلفة الفعلية", "Actual cost")),
                [PortfolioMetricCodes.Classification] = ClassificationLabels,
            }),
        new(PortfolioMetricCodes.Requests, PortfolioPermissions.SystemCode, "طلبات البرامج", "Program requests", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, PortfolioPermissions.RequestRead, [ReportingDimensions.Stage, PortfolioMetricCodes.Classification],
            DrillLink: "/portfolio/requests?status={dim:stage}",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Stage] = RequestStages,
                [PortfolioMetricCodes.Classification] = ClassificationLabels,
            }),
        new(PortfolioMetricCodes.Milestones, PortfolioPermissions.SystemCode, "معالم المشاريع المستحقة", "Project milestones due", MetricKind.Flow,
            MetricAggregation.Sum, MetricValueKind.Count, PortfolioPermissions.ProjectRead, [ReportingDimensions.Status, ReportingDimensions.OnTime],
            DrillLink: "/portfolio/gantt",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = Labels.Of((PortfolioMetricCodes.MilestoneCompleted, "مكتمل", "Completed"), (PortfolioMetricCodes.MilestoneOpen, "مفتوح", "Open")),
                [ReportingDimensions.OnTime] = Labels.Of(("true", "في الموعد", "On time"), ("false", "متأخر", "Late")),
            }),
    ];

    public string SystemCode => PortfolioPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics => Definitions;

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new List<MetricFact>();
        var projects = await ProjectsAsync(computation.User, cancellationToken).ConfigureAwait(false);
        if (computation.CoversCurrentMonth
            && (computation.Wants(PortfolioMetricCodes.Projects) || computation.Wants(PortfolioMetricCodes.Health) || computation.Wants(PortfolioMetricCodes.Budget)))
        {
            await ProjectFactsAsync(computation, projects, facts, cancellationToken).ConfigureAwait(false);
        }

        if (computation.CoversCurrentMonth && computation.Wants(PortfolioMetricCodes.Requests))
        {
            var requests = computation.User is null
                ? db.Requests.AsNoTracking()
                : await reader.VisibleAsync(computation.User, cancellationToken).ConfigureAwait(false);
            var rows = await requests.Where(r => r.Status != RequestStatuses.Draft)
                .GroupBy(r => new { r.UnitId, r.Status, Classification = r.ClassificationOverride ?? r.Classification })
                .Select(g => new { g.Key.UnitId, g.Key.Status, g.Key.Classification, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => Fact(PortfolioMetricCodes.Requests, computation.CurrentMonth, r.UnitId, r.Count,
                (ReportingDimensions.Stage, r.Status), (PortfolioMetricCodes.Classification, r.Classification))));
        }

        if (computation.Wants(PortfolioMetricCodes.Milestones))
        {
            var first = computation.FromMonth;
            var last = computation.ToMonth.AddMonths(1).AddDays(-1);
            var rows = await (from m in db.Milestones.AsNoTracking()
                              join p in projects on m.ProjectId equals p.Id
                              where !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled && m.PlannedEnd != null && m.PlannedEnd >= first && m.PlannedEnd <= last
                              select new { p.UnitId, PlannedEnd = m.PlannedEnd!.Value, m.Status, m.ActualEnd })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var today = computation.Today;
            facts.AddRange(rows
                .Select(r =>
                {
                    var completed = r.Status == MilestoneStatuses.Completed;
                    var onTime = completed ? (r.ActualEnd is { } end && end <= r.PlannedEnd) : r.PlannedEnd >= today;
                    return new
                    {
                        r.UnitId,
                        Month = MetricComputation.MonthOf(r.PlannedEnd),
                        Status = completed ? PortfolioMetricCodes.MilestoneCompleted : PortfolioMetricCodes.MilestoneOpen,
                        OnTime = Labels.Code(onTime),
                    };
                })
                .GroupBy(r => r)
                .Select(g => Fact(PortfolioMetricCodes.Milestones, g.Key.Month, g.Key.UnitId, g.Count(),
                    (ReportingDimensions.Status, g.Key.Status), (ReportingDimensions.OnTime, g.Key.OnTime))));
        }

        return facts;
    }

    /// <summary>
    /// Costs in full only when the user may see the costs of every project they read (their cost scope covers their read scope); otherwise
    /// budget figures are served live, project by project.
    /// </summary>
    public async Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var readable = await (await ProjectsAsync(user, cancellationToken).ConfigureAwait(false)).Select(p => p.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (readable.Count == 0)
        {
            return await access.CanAsync(user, PortfolioPermissions.ProjectCosts, null, cancellationToken).ConfigureAwait(false) ? [ReportingPartitions.Costs] : [];
        }

        var visible = await costs.VisibleSetAsync(user, readable, cancellationToken).ConfigureAwait(false);
        return readable.All(visible.Contains) ? [ReportingPartitions.Costs] : [];
    }

    /// <summary>The projects counted (the dashboard's set: every non-draft project, scoped for a user).</summary>
    private async Task<IQueryable<Project>> ProjectsAsync(ClaimsPrincipal? user, CancellationToken ct)
    {
        var query = user is null ? db.Projects.AsNoTracking() : await reader.VisibleProjectsAsync(user, ct).ConfigureAwait(false);
        return query.Where(p => p.Status != ProjectStatuses.Draft);
    }

    private async Task ProjectFactsAsync(MetricComputation computation, IQueryable<Project> projects, List<MetricFact> facts, CancellationToken ct)
    {
        var month = computation.CurrentMonth;
        var rows = await projects.Select(p => new
        {
            p.Id,
            p.UnitId,
            p.Stage,
            p.Status,
            p.Classification,
            p.ProgressStatus,
            p.Budget,
            Actual = (p.Year1ActualCost ?? 0) + (p.Year2ActualCost ?? 0) + (p.Year3ActualCost ?? 0) + (p.Year4ActualCost ?? 0),
        }).ToListAsync(ct).ConfigureAwait(false);

        // Health as the dashboard computes it (HealthService, ADR 0093) for active projects, when the feature is on.
        var healths = new Dictionary<Guid, string>();
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (settings.Features.Health && (computation.Wants(PortfolioMetricCodes.Projects) || computation.Wants(PortfolioMetricCodes.Health)))
        {
            var active = await projects.Where(p => p.Status == ProjectStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
            foreach (var (id, value) in await health.BulkAsync(active, ct).ConfigureAwait(false))
            {
                healths[id] = value.Overall.Effective;
            }
        }

        string HealthOf(Guid id) => healths.GetValueOrDefault(id) is { } colour && Rag.Colours.Contains(colour) ? colour : PortfolioMetricCodes.HealthNone;

        if (computation.Wants(PortfolioMetricCodes.Projects))
        {
            facts.AddRange(rows
                .GroupBy(r => new
                {
                    r.UnitId,
                    r.Stage,
                    r.Status,
                    r.Classification,
                    Health = HealthOf(r.Id),
                    Delayed = Labels.Code(r.Status == ProjectStatuses.Active && r.ProgressStatus == ProgressCalculator.Delayed),
                })
                .Select(g => Fact(PortfolioMetricCodes.Projects, month, g.Key.UnitId, g.Count(), (ReportingDimensions.Stage, g.Key.Stage),
                    (ReportingDimensions.Health, g.Key.Health), (ReportingDimensions.Status, g.Key.Status), (PortfolioMetricCodes.Delayed, g.Key.Delayed),
                    (PortfolioMetricCodes.Classification, g.Key.Classification))));
        }

        if (computation.Wants(PortfolioMetricCodes.Health))
        {
            facts.AddRange(rows.Where(r => r.Status == ProjectStatuses.Active && HealthOf(r.Id) != PortfolioMetricCodes.HealthNone)
                .GroupBy(r => new { r.UnitId, r.Classification })
                .Select(g => new MetricFact(PortfolioMetricCodes.Health, month, g.Key.UnitId, ReportingPartitions.Public,
                    Dimensions((PortfolioMetricCodes.Classification, g.Key.Classification)), g.Count(r => HealthOf(r.Id) == Rag.Green), g.Count())));
        }

        if (computation.Wants(PortfolioMetricCodes.Budget))
        {
            var budgeted = rows.Where(r => r.Budget is not null).ToList();
            if (computation.User is { } user)
            {
                var visible = await costs.VisibleSetAsync(user, budgeted.Select(r => r.Id).ToList(), ct).ConfigureAwait(false);
                budgeted = budgeted.Where(r => visible.Contains(r.Id)).ToList();
            }

            foreach (var g in budgeted.GroupBy(r => new { r.UnitId, r.Classification }))
            {
                facts.Add(new MetricFact(PortfolioMetricCodes.Budget, month, g.Key.UnitId, ReportingPartitions.Costs,
                    Dimensions((PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindPlanned), (PortfolioMetricCodes.Classification, g.Key.Classification)),
                    g.Sum(r => r.Budget ?? 0)));
                facts.Add(new MetricFact(PortfolioMetricCodes.Budget, month, g.Key.UnitId, ReportingPartitions.Costs,
                    Dimensions((PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindActual), (PortfolioMetricCodes.Classification, g.Key.Classification)),
                    g.Sum(r => r.Actual)));
            }
        }
    }

    private static MetricFact Fact(string metric, DateOnly month, Guid? unit, decimal value, params (string Key, string Value)[] dimensions) =>
        new(metric, month, unit, ReportingPartitions.Public, Dimensions(dimensions), value);

    private static Dictionary<string, string> Dimensions(params (string Key, string Value)[] dimensions) =>
        dimensions.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
}
