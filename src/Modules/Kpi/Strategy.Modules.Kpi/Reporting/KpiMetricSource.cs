using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Kpi.Reporting;

/// <summary>KPI metric, dimension and value codes (docs/design/reporting.md §2.5; stable — seeded dashboards use them).</summary>
public static class KpiMetricCodes
{
    public const string Score = "kpi.score";
    public const string Results = "kpi.results";
    public const string SubmissionsOnTime = "kpi.submissions-on-time";
    public const string Cards = "kpi.cards";
    public const string Pending = "kpi.pending";

    public const string Rag = "rag";
    public const string Year = "year";

    /// <summary>A result without an approved, scored value (open, returned, pending, not applicable, locked).</summary>
    public const string NotReported = "not-reported";

    /// <summary>A card without a type / objective, a result without a perspective.</summary>
    public const string None = "none";
}

/// <summary>
/// The weight a KPI card carries in additive score facts: the scorecard's card weight (<see cref="ScorecardCalculator.WeightedAverage"/>), and 1 for
/// a card without a weight (the scorecard's equal weighting when no card of the set has one). A weight of zero or less excludes the card.
/// </summary>
public static class KpiScoreWeights
{
    public static decimal Of(decimal? weight) => weight switch
    {
        null => 1m,
        > 0 => weight.Value,
        _ => 0m,
    };

    /// <summary>The scorecard caps every KPI's capped performance at 100 % before weighting (SPEC K5, §2.7).</summary>
    public static decimal Capped(decimal capped) => Math.Min(1m, capped);
}

internal static class KpiLabels
{
    public static IReadOnlyDictionary<string, DimensionLabel> Of(params (string Code, string Ar, string En)[] values) =>
        values.ToDictionary(v => v.Code, v => new DimensionLabel(v.Ar, v.En), StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, DimensionLabel> ResultStatuses = Of(
        (Domain.ResultStatuses.Open, "مفتوح للتحديث", "Open"), (Domain.ResultStatuses.PendingApproval, "بانتظار الاعتماد", "Pending approval"),
        (Domain.ResultStatuses.Returned, "معاد للتصحيح", "Returned"), (Domain.ResultStatuses.Approved, "معتمد", "Approved"), (Domain.ResultStatuses.Locked, "مقفل", "Locked"));

    public static readonly IReadOnlyDictionary<string, DimensionLabel> CardStatuses = Of(
        (Domain.CardStatuses.Active, "نشط", "Active"), (Domain.CardStatuses.Inactive, "غير نشط", "Inactive"));

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Rag = Of(
        (RagCodes.Green, "أخضر", "Green"), (RagCodes.Amber, "كهرماني", "Amber"), (RagCodes.Red, "أحمر", "Red"), (KpiMetricCodes.NotReported, "غير مُبلغ", "Not reported"));

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Bool = Of(("true", "نعم", "Yes"), ("false", "لا", "No"));
}

/// <summary>
/// KPI facts (docs/design/reporting.md §2.5) on the module's DbContext. With a user: only the cards / results that user reads (<see cref="KpiReader"/>,
/// the reader of the KPI screens and dashboard). Only approved results count in scores (SPEC §2): <c>kpi.score</c> = Σ weight · capped ÷ Σ weight
/// over approved results by result period, with the capped performance the central PerformanceCalculator stored on each result and the card
/// weights of the scorecard. Units: the result's owner unit (<c>kpi.result.read</c> scope) or the card's responsible unit (<c>kpi.card.read</c>).
/// </summary>
internal sealed class KpiMetricSource(KpiDbContext db, KpiReader reader) : IReportingMetricSource
{
    public static readonly IReadOnlyList<MetricDefinition> Definitions =
    [
        new(KpiMetricCodes.Score, KpiPermissions.SystemCode, "أداء مؤشرات الأداء (المحدود)", "KPI performance (capped)", MetricKind.Flow, MetricAggregation.Ratio,
            MetricValueKind.Score, KpiPermissions.ResultRead, [ReportingDimensions.Objective, ReportingDimensions.Perspective], HigherIsBetter: true,
            DrillLink: "/kpi/scorecard"),
        new(KpiMetricCodes.Results, KpiPermissions.SystemCode, "نتائج مؤشرات الأداء", "KPI results", MetricKind.Flow, MetricAggregation.Sum, MetricValueKind.Count,
            KpiPermissions.ResultRead, [KpiMetricCodes.Rag, ReportingDimensions.Status], DrillLink: "/kpi",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [KpiMetricCodes.Rag] = KpiLabels.Rag,
                [ReportingDimensions.Status] = KpiLabels.ResultStatuses,
            }),
        new(KpiMetricCodes.SubmissionsOnTime, KpiPermissions.SystemCode, "نسبة تقديم النتائج في الموعد", "Results submitted on time", MetricKind.Flow,
            MetricAggregation.Ratio, MetricValueKind.Ratio, KpiPermissions.ResultRead, [], HigherIsBetter: true, DrillLink: "/kpi/reports"),
        new(KpiMetricCodes.Cards, KpiPermissions.SystemCode, "بطاقات مؤشرات الأداء", "KPI cards", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            KpiPermissions.CardRead, [ReportingDimensions.Type, ReportingDimensions.Objective, ReportingDimensions.Status],
            DrillLink: "/kpi/cards?unitId={unit}&type={dim:type}&status={dim:status}",
            DimensionLookups: new Dictionary<string, string> { [ReportingDimensions.Type] = KpiPermissions.TypeLookup },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> { [ReportingDimensions.Status] = KpiLabels.CardStatuses }),
        new(KpiMetricCodes.Pending, KpiPermissions.SystemCode, "نتائج قيد الإنجاز", "KPI results in progress", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, KpiPermissions.ResultRead, [ReportingDimensions.Status, ReportingDimensions.Overdue, KpiMetricCodes.Year], HigherIsBetter: false,
            DrillLink: "/kpi/pending",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = KpiLabels.ResultStatuses,
                [ReportingDimensions.Overdue] = KpiLabels.Bool,
            }),
    ];

    public string SystemCode => KpiPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics => Definitions;

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new List<MetricFact>();
        var user = computation.User;
        var results = user is null ? db.Results.AsNoTracking() : await reader.ResultsAsync(user, cancellationToken).ConfigureAwait(false);
        var first = computation.FromMonth;
        var last = computation.ToMonth.AddMonths(1).AddDays(-1);
        var today = computation.Today;
        var objectives = computation.Wants(KpiMetricCodes.Score) || computation.Wants(KpiMetricCodes.Cards)
            ? await ObjectivesAsync(cancellationToken).ConfigureAwait(false)
            : [];

        if (computation.Wants(KpiMetricCodes.Score))
        {
            var rows = await (from r in results
                              join c in db.Cards.AsNoTracking() on r.CardId equals c.Id
                              where r.Status == ResultStatuses.Approved && r.CappedPerformance != null && r.PeriodEnd >= first && r.PeriodEnd <= last
                              select new { r.OwnerUnitId, r.PeriodEnd, Capped = r.CappedPerformance!.Value, c.Weight, c.ObjectiveId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows
                .GroupBy(r =>
                {
                    var objective = r.ObjectiveId is { } o ? objectives.GetValueOrDefault(o) : default;
                    return new
                    {
                        r.OwnerUnitId,
                        Month = MetricComputation.MonthOf(r.PeriodEnd),
                        Objective = objective.Code ?? KpiMetricCodes.None,
                        Perspective = objective.Perspective ?? KpiMetricCodes.None,
                    };
                })
                .Select(g => new MetricFact(KpiMetricCodes.Score, g.Key.Month, g.Key.OwnerUnitId, ReportingPartitions.Public,
                    Dims((ReportingDimensions.Objective, g.Key.Objective), (ReportingDimensions.Perspective, g.Key.Perspective)),
                    g.Sum(r => KpiScoreWeights.Of(r.Weight) * KpiScoreWeights.Capped(r.Capped)), g.Sum(r => KpiScoreWeights.Of(r.Weight))))
                .Where(f => f.Weight > 0));
        }

        if (computation.Wants(KpiMetricCodes.Results))
        {
            var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
            var rows = await (from r in results
                              join c in db.Cards.AsNoTracking() on r.CardId equals c.Id
                              where r.Status != ResultStatuses.NotOpen && r.PeriodEnd >= first && r.PeriodEnd <= last
                              group r by new
                              {
                                  r.OwnerUnitId,
                                  r.PeriodEnd.Year,
                                  r.PeriodEnd.Month,
                                  r.Status,
                                  r.CappedPerformance,
                                  c.GreenFrom,
                                  c.AmberFrom,
                              }
                              into g
                              select new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows
                .GroupBy(r => new
                {
                    r.Key.OwnerUnitId,
                    Month = new DateOnly(r.Key.Year, r.Key.Month, 1),
                    r.Key.Status,
                    Rag = r.Key.Status == ResultStatuses.Approved && r.Key.CappedPerformance is not null
                        ? RagBands.Band(r.Key.CappedPerformance, r.Key.GreenFrom ?? settings.GreenFrom, r.Key.AmberFrom ?? settings.AmberFrom)
                        : KpiMetricCodes.NotReported,
                })
                .Select(g => new MetricFact(KpiMetricCodes.Results, g.Key.Month, g.Key.OwnerUnitId, ReportingPartitions.Public,
                    Dims((KpiMetricCodes.Rag, g.Key.Rag), (ReportingDimensions.Status, g.Key.Status)), g.Sum(r => r.Count))));
        }

        if (computation.Wants(KpiMetricCodes.SubmissionsOnTime))
        {
            // Results due in the month (by their due date, else the period end) whose deadline has passed or that were submitted (the data-quality rule).
            var rows = await results
                .Where(r => r.Status != ResultStatuses.NotOpen && (r.DueOn ?? r.PeriodEnd) >= first && (r.DueOn ?? r.PeriodEnd) <= last
                    && (r.SubmittedAt != null || (r.DueOn != null && r.DueOn < today)))
                .GroupBy(r => new { r.OwnerUnitId, Due = r.DueOn ?? r.PeriodEnd, OnTime = r.SubmittedAt != null && !r.Late })
                .Select(g => new { g.Key.OwnerUnitId, g.Key.Due, g.Key.OnTime, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.GroupBy(r => new { r.OwnerUnitId, Month = MetricComputation.MonthOf(r.Due) })
                .Select(g => new MetricFact(KpiMetricCodes.SubmissionsOnTime, g.Key.Month, g.Key.OwnerUnitId, ReportingPartitions.Public, Dims(),
                    g.Where(r => r.OnTime).Sum(r => r.Count), g.Sum(r => r.Count))));
        }

        if (computation.CoversCurrentMonth && computation.Wants(KpiMetricCodes.Cards))
        {
            var cards = user is null ? db.Cards.AsNoTracking() : await reader.CardsAsync(user, cancellationToken).ConfigureAwait(false);
            var rows = await cards.GroupBy(c => new { c.ResponsibleUnitId, c.TypeCode, c.ObjectiveId, c.Status })
                .Select(g => new { g.Key.ResponsibleUnitId, g.Key.TypeCode, g.Key.ObjectiveId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows
                .GroupBy(r => new
                {
                    r.ResponsibleUnitId,
                    Type = r.TypeCode ?? KpiMetricCodes.None,
                    Objective = r.ObjectiveId is { } o && objectives.TryGetValue(o, out var objective) ? objective.Code ?? KpiMetricCodes.None : KpiMetricCodes.None,
                    r.Status,
                })
                .Select(g => new MetricFact(KpiMetricCodes.Cards, computation.CurrentMonth, g.Key.ResponsibleUnitId, ReportingPartitions.Public,
                    Dims((ReportingDimensions.Type, g.Key.Type), (ReportingDimensions.Objective, g.Key.Objective), (ReportingDimensions.Status, g.Key.Status)),
                    g.Sum(r => r.Count))));
        }

        if (computation.CoversCurrentMonth && computation.Wants(KpiMetricCodes.Pending))
        {
            var rows = await results
                .Where(r => r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned || r.Status == ResultStatuses.PendingApproval)
                .GroupBy(r => new
                {
                    r.OwnerUnitId,
                    r.Status,
                    r.Year,
                    Overdue = r.Status != ResultStatuses.PendingApproval && r.DueOn != null && r.DueOn < today,
                })
                .Select(g => new { g.Key.OwnerUnitId, g.Key.Status, g.Key.Year, g.Key.Overdue, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(KpiMetricCodes.Pending, computation.CurrentMonth, r.OwnerUnitId, ReportingPartitions.Public,
                Dims((ReportingDimensions.Status, r.Status), (ReportingDimensions.Overdue, r.Overdue ? "true" : "false"),
                    (KpiMetricCodes.Year, r.Year.ToString(CultureInfo.InvariantCulture))),
                r.Count)));
        }

        return facts;
    }

    /// <summary>KPI figures have no partition beyond the data scope.</summary>
    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    /// <summary>Objective id → (code, code of its perspective — the nearest perspective above it in the strategy hierarchy).</summary>
    private async Task<Dictionary<Guid, (string? Code, string? Perspective)>> ObjectivesAsync(CancellationToken ct)
    {
        var elements = await db.Elements.AsNoTracking().Select(e => new { e.Id, e.Kind, e.Code, e.ParentId }).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        var result = new Dictionary<Guid, (string? Code, string? Perspective)>();
        foreach (var objective in elements.Values.Where(e => e.Kind == ElementKinds.Objective))
        {
            string? perspective = null;
            var parent = objective.ParentId;
            for (var depth = 0; parent is { } p && depth < 20 && elements.TryGetValue(p, out var node); depth++)
            {
                if (node.Kind == ElementKinds.Perspective)
                {
                    perspective = node.Code;
                    break;
                }

                parent = node.ParentId;
            }

            result[objective.Id] = (objective.Code, perspective);
        }

        return result;
    }

    private static Dictionary<string, string> Dims(params (string Key, string Value)[] dimensions) =>
        dimensions.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
}
