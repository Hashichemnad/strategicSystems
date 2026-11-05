using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.AuditManagement.Reporting;

/// <summary>Metric codes of Audit Management (docs/design/reporting.md §2.5) and their extra dimensions.</summary>
public static class AuditMetrics
{
    public const string Findings = "auditmgmt.findings";
    public const string Recommendations = "auditmgmt.recommendations";
    public const string Closure = "auditmgmt.closure";

    public const string ReportType = "reportType";
    public const string Year = "year";
    public const string Repeat = "repeat";

    /// <summary>Findings of reports still in draft are seen by the Audit Office only (ADR 0141).</summary>
    public static string PartitionOf(bool draftReport) => draftReport ? ReportingPartitions.Office : ReportingPartitions.Public;
}

/// <summary>
/// Audit Management metrics on its own data. Live path: the findings the user reads through the Audit reader (non-office readers never see
/// findings of draft reports, ADR 0132) and their recommendations; unit = the finding's responsible unit (the unit column of
/// <c>auditmgmt.finding.read</c>); findings of draft reports go to the office partition.
/// </summary>
internal sealed class AuditMetricSource(AuditMgmtDbContext db, AuditReader reader) : IReportingMetricSource
{
    private static readonly string[] FinalFindingStatuses = [.. AuditFindingStatuses.Final];
    private static readonly string[] FinalRecommendationStatuses = [.. RecommendationStatuses.Final];

    public string SystemCode => AuditPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(AuditMetrics.Findings, AuditPermissions.SystemCode, "ملاحظات التدقيق", "Audit findings", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            AuditPermissions.Read,
            [ReportingDimensions.Status, ReportingDimensions.Priority, ReportingDimensions.AgeBucket, AuditMetrics.ReportType, AuditMetrics.Year, AuditMetrics.Repeat,
                ReportingDimensions.Overdue],
            HigherIsBetter: null,
            DrillLink: "/audit-management/findings?unitId={unit}&status={dim:status}&priority={dim:priority}&reportType={dim:reportType}&year={dim:year}",
            DimensionLookups: new Dictionary<string, string>
            {
                [ReportingDimensions.Priority] = AuditPermissions.PriorityLookup,
                [AuditMetrics.ReportType] = AuditPermissions.ReportTypeLookup,
            },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = AuditReportingLabels.FindingStatusLabels,
                [ReportingDimensions.AgeBucket] = AuditReportingLabels.Ages,
                [AuditMetrics.Repeat] = AuditReportingLabels.YesNo,
                [ReportingDimensions.Overdue] = AuditReportingLabels.YesNo,
            }),
        new(AuditMetrics.Recommendations, AuditPermissions.SystemCode, "توصيات التدقيق", "Audit recommendations", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, AuditPermissions.Read, [ReportingDimensions.Status, ReportingDimensions.Overdue, ReportingDimensions.Priority], HigherIsBetter: null,
            DrillLink: "/audit-management/findings?view=late&unitId={unit}",
            DimensionLookups: new Dictionary<string, string> { [ReportingDimensions.Priority] = AuditPermissions.PriorityLookup },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = AuditReportingLabels.RecommendationStatusLabels,
                [ReportingDimensions.Overdue] = AuditReportingLabels.YesNo,
            }),
        new(AuditMetrics.Closure, AuditPermissions.SystemCode, "نسبة إغلاق الملاحظات", "Finding closure rate", MetricKind.Stock, MetricAggregation.Ratio,
            MetricValueKind.Ratio, AuditPermissions.Read, [], HigherIsBetter: true, DrillLink: "/audit-management"),
    ];

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        var facts = new List<MetricFact>();
        if (!computation.CoversCurrentMonth)
        {
            return facts;
        }

        var findings = computation.User is { } user ? await reader.FindingsAsync(user, cancellationToken).ConfigureAwait(false) : db.Findings.AsNoTracking();
        var month = computation.CurrentMonth;
        var today = computation.Today;
        if (computation.Wants(AuditMetrics.Findings) || computation.Wants(AuditMetrics.Closure))
        {
            var d30 = today.AddDays(-30);
            var d90 = today.AddDays(-90);
            var d180 = today.AddDays(-180);
            var d365 = today.AddDays(-365);
            var rows = await findings.Join(db.Reports.AsNoTracking(), f => f.ReportId, r => r.Id, (f, r) => new { f, r.TypeCode, Draft = r.Status == ReportStatuses.Draft })
                .GroupBy(x => new
                {
                    x.f.ResponsibleUnitId,
                    x.Draft,
                    x.f.Status,
                    x.f.Priority,
                    x.f.Year,
                    x.TypeCode,
                    x.f.Repeat,
                    Age = FinalFindingStatuses.Contains(x.f.Status) ? "closed"
                        : x.f.FindingDate >= d30 ? "0-30"
                        : x.f.FindingDate >= d90 ? "31-90"
                        : x.f.FindingDate >= d180 ? "91-180"
                        : x.f.FindingDate >= d365 ? "181-365"
                        : "365+",
                })
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (computation.Wants(AuditMetrics.Findings))
            {
                facts.AddRange(rows.Select(r => new MetricFact(AuditMetrics.Findings, month, r.Key.ResponsibleUnitId, AuditMetrics.PartitionOf(r.Key.Draft),
                    new Dictionary<string, string>
                    {
                        [ReportingDimensions.Status] = r.Key.Status,
                        [ReportingDimensions.Priority] = r.Key.Priority,
                        [ReportingDimensions.AgeBucket] = r.Key.Age,
                        [AuditMetrics.ReportType] = r.Key.TypeCode,
                        [AuditMetrics.Year] = r.Key.Year.ToString(CultureInfo.InvariantCulture),
                        [AuditMetrics.Repeat] = Bool(r.Key.Repeat),
                        [ReportingDimensions.Overdue] = Bool(r.Key.Status == AuditFindingStatuses.Late),
                    }, r.Count)));
            }

            if (computation.Wants(AuditMetrics.Closure))
            {
                facts.AddRange(rows.GroupBy(r => (r.Key.ResponsibleUnitId, r.Key.Draft))
                    .Select(g => new MetricFact(AuditMetrics.Closure, month, g.Key.ResponsibleUnitId, AuditMetrics.PartitionOf(g.Key.Draft), Empty,
                        g.Where(r => r.Key.Status is AuditFindingStatuses.Closed or AuditFindingStatuses.RiskAccepted).Sum(r => r.Count),
                        g.Where(r => r.Key.Status != AuditFindingStatuses.Cancelled).Sum(r => r.Count))));
            }
        }

        if (computation.Wants(AuditMetrics.Recommendations))
        {
            var rows = await db.Recommendations.AsNoTracking()
                .Join(findings, r => r.FindingId, f => f.Id, (r, f) => new { r, f })
                .Join(db.Reports.AsNoTracking(), x => x.f.ReportId, p => p.Id, (x, p) => new { x.r, x.f, Draft = p.Status == ReportStatuses.Draft })
                .GroupBy(x => new
                {
                    x.f.ResponsibleUnitId,
                    x.Draft,
                    x.r.Status,
                    x.f.Priority,
                    Overdue = x.r.Status == RecommendationStatuses.Late || (!FinalRecommendationStatuses.Contains(x.r.Status) && (x.r.RevisedDueOn ?? x.r.DueOn) < today),
                })
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(AuditMetrics.Recommendations, month, r.Key.ResponsibleUnitId, AuditMetrics.PartitionOf(r.Key.Draft),
                new Dictionary<string, string>
                {
                    [ReportingDimensions.Status] = r.Key.Status,
                    [ReportingDimensions.Overdue] = Bool(r.Key.Overdue),
                    [ReportingDimensions.Priority] = r.Key.Priority,
                }, r.Count)));
        }

        return facts;
    }

    /// <summary>The Audit Office (the readers the Audit reader lets see draft reports) reads the office partition.</summary>
    public async Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.IsOfficeAsync(user, cancellationToken).ConfigureAwait(false) ? [ReportingPartitions.Office] : [];

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>AR / EN labels of fixed dimension values.</summary>
internal static class AuditReportingLabels
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

    public static readonly IReadOnlyDictionary<string, DimensionLabel> FindingStatusLabels = new Dictionary<string, DimensionLabel>
    {
        [AuditFindingStatuses.Open] = new("مفتوحة", "Open"),
        [AuditFindingStatuses.Late] = new("متأخرة", "Late"),
        [AuditFindingStatuses.Closed] = new("مغلقة", "Closed"),
        [AuditFindingStatuses.RiskAccepted] = new("قبول المخاطر", "Risk accepted"),
        [AuditFindingStatuses.Cancelled] = new("ملغاة", "Cancelled"),
    };

    public static readonly IReadOnlyDictionary<string, DimensionLabel> RecommendationStatusLabels = new Dictionary<string, DimensionLabel>
    {
        [RecommendationStatuses.NotStarted] = new("لم يبدأ", "Not started"),
        [RecommendationStatuses.Continuing] = new("مستمر", "Continuing"),
        [RecommendationStatuses.Late] = new("متأخر", "Late"),
        [RecommendationStatuses.Completed] = new("مكتمل", "Completed"),
        [RecommendationStatuses.RiskAccepted] = new("قبول المخاطر", "Risk accepted"),
        [RecommendationStatuses.Cancelled] = new("ملغى", "Cancelled"),
    };
}
