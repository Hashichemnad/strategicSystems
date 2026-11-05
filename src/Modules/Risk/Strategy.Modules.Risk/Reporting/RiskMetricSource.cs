using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Risk.Reporting;

/// <summary>
/// Risk metrics (docs/design/reporting.md §2.5) on the risks the Risk dashboard counts: enterprise risks only — project risks belong to Portfolio
/// (their scope is the project's, not <c>risk.risk.read</c>), so a fact here never counts a record the viewer could not open in the register.
/// Unit = the risk's register unit (<see cref="RiskScope.Risk"/>). Live path: the same <see cref="RiskReader"/> as the register and dashboard.
/// </summary>
internal sealed class RiskMetricSource(RiskDbContext db, RiskReader reader, FrameworkStore frameworks, IClock clock) : IReportingMetricSource
{
    public const string Risks = "risk.risks";
    public const string Heat = "risk.heat";
    public const string Movement = "risk.movement";
    public const string Actions = "risk.actions";
    public const string ReviewsOverdue = "risk.reviews-overdue";
    public const string KriBreaches = "risk.kri-breaches";

    public const string Appetite = "appetite";
    public const string Kind = "kind";
    public const string Likelihood = "likelihood";
    public const string Impact = "impact";
    public const string Direction = "direction";

    public const string Inside = "inside";
    public const string Outside = "outside";
    public const string NoAppetite = "none";
    public const string NoCategory = "none";

    private static readonly IReadOnlyDictionary<string, DimensionLabel> StatusLabels = RiskFacts.Labels(
        (RiskStatuses.Draft, "مسودة", "Draft"), (RiskStatuses.UnderReview, "قيد المراجعة", "Under review"), (RiskStatuses.Active, "نشط", "Active"));

    private static readonly IReadOnlyDictionary<string, DimensionLabel> ActionStatusLabels = RiskFacts.Labels(
        (ActionStatuses.NotStarted, "لم يبدأ", "Not started"), (ActionStatuses.InProgress, "قيد التنفيذ", "In progress"),
        (ActionStatuses.Completed, "مكتمل", "Completed"), (ActionStatuses.Cancelled, "ملغى", "Cancelled"));

    public string SystemCode => RiskPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(Risks, RiskPermissions.SystemCode, "المخاطر المفتوحة", "Open risks", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, RiskPermissions.Read,
            [ReportingDimensions.Band, ReportingDimensions.Severe, Appetite, ReportingDimensions.Category, ReportingDimensions.Status, ReportingDimensions.Type],
            DrillLink: "/risk/risks?unitId={unit}&band={dim:band}&status={dim:status}&category={dim:category}",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Severe] = RiskFacts.YesNo,
                [Appetite] = RiskFacts.Labels((Inside, "ضمن الرغبة", "Inside appetite"), (Outside, "خارج الرغبة", "Outside appetite"),
                    (NoAppetite, "لا توجد رغبة محددة", "No appetite set")),
                [ReportingDimensions.Status] = StatusLabels,
                [ReportingDimensions.Type] = RiskFacts.Labels((RiskTypes.Internal, "داخلي", "Internal"), (RiskTypes.External, "خارجي", "External")),
                [ReportingDimensions.Category] = RiskFacts.Labels((NoCategory, "بدون فئة", "No category")),
            }),
        new(Heat, RiskPermissions.SystemCode, "خريطة المخاطر الحرارية", "Risk heat map", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, RiskPermissions.Read,
            [Kind, Likelihood, Impact], DrillLink: "/risk",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [Kind] = RiskFacts.Labels(("inherent", "الكامن", "Inherent"), ("residual", "المتبقي", "Residual")),
            }),
        new(Movement, RiskPermissions.SystemCode, "حركة المخاطر", "Risk movement", MetricKind.Flow, MetricAggregation.Sum, MetricValueKind.Count, RiskPermissions.Read,
            [Direction], DrillLink: "/risk",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [Direction] = RiskFacts.Labels((RiskMovement.Improved, "تحسن", "Improved"), (RiskMovement.Worsened, "تدهور", "Worsened"),
                    (RiskMovement.Unchanged, "دون تغيير", "Unchanged")),
            }),
        new(Actions, RiskPermissions.SystemCode, "إجراءات المعالجة", "Treatment actions", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            RiskPermissions.Read, [ReportingDimensions.Status, ReportingDimensions.Overdue], DrillLink: "/risk",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = ActionStatusLabels,
                [ReportingDimensions.Overdue] = RiskFacts.YesNo,
            }),
        new(ReviewsOverdue, RiskPermissions.SystemCode, "مراجعات المخاطر المتأخرة", "Overdue risk reviews", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, RiskPermissions.Read, [ReportingDimensions.Band], HigherIsBetter: false,
            DrillLink: "/risk/risks?view=overdue-review&unitId={unit}"),
        new(KriBreaches, RiskPermissions.SystemCode, "تجاوزات مؤشرات المخاطر الرئيسية", "Key risk indicator breaches", MetricKind.Flow, MetricAggregation.Sum,
            MetricValueKind.Count, RiskPermissions.Read, [], HigherIsBetter: false, DrillLink: "/risk"),
    ];

    /// <summary>Restricted partitions: none — every risk fact is in <see cref="ReportingPartitions.Public"/> (unit scope only).</summary>
    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new RiskFacts();
        var risks = computation.User is { } user ? await reader.RisksAsync(user, cancellationToken).ConfigureAwait(false) : db.Risks.AsNoTracking();
        var open = risks.Where(r => r.Status == RiskStatuses.Draft || r.Status == RiskStatuses.UnderReview || r.Status == RiskStatuses.Active);
        var active = risks.Where(r => r.Status == RiskStatuses.Active);
        var month = computation.CurrentMonth;
        var stock = computation.CoversCurrentMonth;

        if (stock && computation.Wants(Risks))
        {
            var severe = await SevereBandsAsync(cancellationToken).ConfigureAwait(false);
            var groups = await open.GroupBy(r => new { r.UnitId, r.ResidualBand, r.WithinAppetite, r.CategoryCodesJson, r.Status, r.RiskType })
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var g in groups)
            {
                facts.Add(Risks, month, g.Key.UnitId, RiskFacts.Dims(
                    (ReportingDimensions.Band, g.Key.ResidualBand),
                    (ReportingDimensions.Severe, RiskFacts.Bool(severe.Contains(g.Key.ResidualBand))),
                    (Appetite, g.Key.WithinAppetite switch { true => Inside, false => Outside, null => NoAppetite }),
                    (ReportingDimensions.Category, FirstCategory(g.Key.CategoryCodesJson)),
                    (ReportingDimensions.Status, g.Key.Status),
                    (ReportingDimensions.Type, g.Key.RiskType)), g.Count);
            }
        }

        if (stock && computation.Wants(Heat))
        {
            var inherent = await open.GroupBy(r => new { r.UnitId, r.InherentLikelihood, r.InherentImpact })
                .Select(g => new { g.Key.UnitId, L = g.Key.InherentLikelihood, I = g.Key.InherentImpact, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            var residual = await open.GroupBy(r => new { r.UnitId, L = r.ResidualLikelihood ?? r.InherentLikelihood, I = r.ResidualImpact ?? r.InherentImpact })
                .Select(g => new { g.Key.UnitId, g.Key.L, g.Key.I, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (kind, cells) in new[] { ("inherent", inherent.Select(c => (c.UnitId, c.L, c.I, c.Count))), ("residual", residual.Select(c => (c.UnitId, c.L, c.I, c.Count))) })
            {
                foreach (var c in cells)
                {
                    facts.Add(Heat, month, c.UnitId, RiskFacts.Dims((Kind, kind), (Likelihood, Number(c.L)), (Impact, Number(c.I))), c.Count);
                }
            }
        }

        if (stock && computation.Wants(Actions))
        {
            var today = computation.Today;
            var rows = await (from a in db.Actions.AsNoTracking()
                              join r in open on a.RiskId equals r.Id
                              group a by new
                              {
                                  r.UnitId,
                                  a.Status,
                                  Overdue = (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress) && a.PlannedEnd != null && a.PlannedEnd < today,
                              } into g
                              select new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var g in rows)
            {
                facts.Add(Actions, month, g.Key.UnitId, RiskFacts.Dims((ReportingDimensions.Status, g.Key.Status), (ReportingDimensions.Overdue, RiskFacts.Bool(g.Key.Overdue))),
                    g.Count);
            }
        }

        if (stock && computation.Wants(ReviewsOverdue))
        {
            var now = clock.UtcNow;
            var rows = await active.Where(r => r.NextReviewDueAt != null && r.NextReviewDueAt < now).GroupBy(r => new { r.UnitId, r.ResidualBand })
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var g in rows)
            {
                facts.Add(ReviewsOverdue, month, g.Key.UnitId, RiskFacts.Dims((ReportingDimensions.Band, g.Key.ResidualBand)), g.Count);
            }
        }

        var rangeStart = RiskFacts.Start(computation.FromMonth);
        var until = RiskFacts.Start(computation.ToMonth).AddMonths(1);
        if (computation.Wants(KriBreaches))
        {
            var rows = await (from k in db.Kris.AsNoTracking()
                              join r in active on k.RiskId equals r.Id
                              where k.Status == ControlStatuses.Active && k.LastLevel == KriLevels.Breach && k.LastAt != null && k.LastAt >= rangeStart && k.LastAt < until
                              select new { r.UnitId, At = k.LastAt!.Value }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                facts.Add(KriBreaches, MetricComputation.MonthOf(row.At), row.UnitId, RiskFacts.Dims(), 1);
            }
        }

        if (computation.Wants(Movement))
        {
            var history = await (from a in db.Assessments.AsNoTracking()
                                 join r in active on a.RiskId equals r.Id
                                 where a.At < until
                                 select new { a.RiskId, r.UnitId, a.At, a.ResidualScore }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var risk in history.GroupBy(h => (h.RiskId, h.UnitId)))
            {
                foreach (var (m, direction) in RiskMovement.Monthly(risk.Select(h => (h.At, h.ResidualScore)), computation.FromMonth, computation.ToMonth))
                {
                    facts.Add(Movement, m, risk.Key.UnitId, RiskFacts.Dims((Direction, direction)), 1);
                }
            }
        }

        return facts.ToList();
    }

    /// <summary>The framework's two highest rating bands ("severe", design §2.5).</summary>
    private async Task<IReadOnlySet<string>> SevereBandsAsync(CancellationToken ct)
    {
        var scorer = await frameworks.ScorerAsync(ct).ConfigureAwait(false);
        return SevereBands(scorer.Content.Bands);
    }

    public static IReadOnlySet<string> SevereBands(IEnumerable<RatingBand> bands) =>
        bands.OrderByDescending(b => b.Min).Take(2).Select(b => b.Code).ToHashSet(StringComparer.Ordinal);

    /// <summary>The first category code, as the dashboard's "by category" counts it.</summary>
    public static string FirstCategory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NoCategory;
        }

        var codes = JsonSerializer.Deserialize<List<string>>(json);
        return codes is { Count: > 0 } && !string.IsNullOrWhiteSpace(codes[0]) ? codes[0] : NoCategory;
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
