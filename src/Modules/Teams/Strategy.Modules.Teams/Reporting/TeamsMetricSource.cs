using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Teams.Reporting;

/// <summary>Teams metric, dimension and value codes (docs/design/reporting.md §2.5; stable — seeded dashboards use them).</summary>
public static class TeamsMetricCodes
{
    public const string Teams = "teams.teams";
    public const string Evaluations = "teams.evaluations";
    public const string Score = "teams.score";

    public const string Committee = "committee";
    public const string Operating = "operating";

    /// <summary>The Teams dashboard's rule for "committee" types (type codes containing this word, SPEC §4.6).</summary>
    public const string CommitteeMarker = "committee";

    /// <summary>Team scores are points out of 100 (the dashboard's score bands); facts store them as ratios (1.0 = 100 %).</summary>
    public const decimal ScoreScale = 100m;

    public static bool IsCommittee(string typeCode) => typeCode.Contains(CommitteeMarker, StringComparison.Ordinal);
}

internal static class TeamsLabels
{
    public static IReadOnlyDictionary<string, DimensionLabel> Of(params (string Code, string Ar, string En)[] values) =>
        values.ToDictionary(v => v.Code, v => new DimensionLabel(v.Ar, v.En), StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, DimensionLabel> Bool = Of(("true", "نعم", "Yes"), ("false", "لا", "No"));

    public static readonly IReadOnlyDictionary<string, DimensionLabel> TeamStatusLabels = Of(
        (TeamStatuses.PendingApproval, "بانتظار الاعتماد", "Pending approval"), (TeamStatuses.Active, "نشط", "Active"), (TeamStatuses.Inactive, "غير نشط", "Inactive"),
        (TeamStatuses.PendingEvaluation, "بانتظار التقييم", "Pending evaluation"), (TeamStatuses.EvaluationWaitingApproval, "التقييم بانتظار الاعتماد", "Evaluation awaiting approval"),
        (TeamStatuses.Closed, "مغلق", "Closed"), (TeamStatuses.Replaced, "مستبدل", "Replaced"), (TeamStatuses.Draft, "مسودة", "Draft"));

    public static readonly IReadOnlyDictionary<string, DimensionLabel> EvaluationStatusLabels = Of(
        (EvaluationStatuses.Open, "مفتوح", "Open"), (EvaluationStatuses.Submitted, "مقدّم", "Submitted"), (EvaluationStatuses.Returned, "معاد", "Returned"),
        (EvaluationStatuses.Approved, "معتمد", "Approved"));
}

/// <summary>
/// Teams facts (docs/design/reporting.md §2.5) on the module's DbContext, over the same set as the Teams dashboard: the teams the user reads
/// (<see cref="TeamReader"/>, cancelled excluded); scores only over teams where the user holds the results permission (ADR 0042). Unit = the
/// team's owning unit (<see cref="TeamScope"/>). Team types are the module's configuration table (not a lookup): the type dimension carries
/// the type code.
/// </summary>
internal sealed class TeamsMetricSource(TeamsDbContext db, TeamReader reader, IAccessService access) : IReportingMetricSource
{
    public static readonly IReadOnlyList<MetricDefinition> Definitions =
    [
        new(TeamsMetricCodes.Teams, TeamPermissions.SystemCode, "الفرق واللجان", "Teams and committees", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count,
            TeamPermissions.Read, [ReportingDimensions.Type, ReportingDimensions.Status, TeamsMetricCodes.Committee, TeamsMetricCodes.Operating],
            DrillLink: "/teams?type={dim:type}&status={dim:status}",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = TeamsLabels.TeamStatusLabels,
                [TeamsMetricCodes.Committee] = TeamsLabels.Of(("true", "لجنة", "Committee"), ("false", "فريق", "Team")),
                [TeamsMetricCodes.Operating] = TeamsLabels.Bool,
            }),
        new(TeamsMetricCodes.Evaluations, TeamPermissions.SystemCode, "تقييمات الفرق", "Team evaluations", MetricKind.Stock, MetricAggregation.Sum,
            MetricValueKind.Count, TeamPermissions.Read, [ReportingDimensions.Status], DrillLink: "/teams?view=pending-evaluation",
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>> { [ReportingDimensions.Status] = TeamsLabels.EvaluationStatusLabels }),
        new(TeamsMetricCodes.Score, TeamPermissions.SystemCode, "متوسط درجة تقييم الفرق", "Average team evaluation score", MetricKind.Stock, MetricAggregation.Ratio,
            MetricValueKind.Score, TeamPermissions.Results, [ReportingDimensions.Type], HigherIsBetter: true, DrillLink: "/teams/dashboard"),
    ];

    public string SystemCode => TeamPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics => Definitions;

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        if (!computation.CoversCurrentMonth)
        {
            return [];
        }

        var month = computation.CurrentMonth;
        var facts = new List<MetricFact>();
        var user = computation.User;
        var teams = (user is null ? db.Teams.AsNoTracking() : await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(t => t.Status != TeamStatuses.Cancelled);
        if (computation.Wants(TeamsMetricCodes.Teams))
        {
            var rows = await teams.GroupBy(t => new { t.OwningUnitId, t.TypeCode, t.Status }).Select(g => new { g.Key.OwningUnitId, g.Key.TypeCode, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(TeamsMetricCodes.Teams, month, r.OwningUnitId, ReportingPartitions.Public,
                Dims((ReportingDimensions.Type, r.TypeCode), (ReportingDimensions.Status, r.Status),
                    (TeamsMetricCodes.Committee, Bool(TeamsMetricCodes.IsCommittee(r.TypeCode))), (TeamsMetricCodes.Operating, Bool(TeamStatuses.IsOperating(r.Status)))),
                r.Count)));
        }

        if (computation.Wants(TeamsMetricCodes.Evaluations))
        {
            var rows = await (from e in db.Evaluations.AsNoTracking()
                              join t in teams on e.TeamId equals t.Id
                              where e.Status != EvaluationStatuses.Cancelled
                              group e by new { t.OwningUnitId, e.Status } into g
                              select new { g.Key.OwningUnitId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(TeamsMetricCodes.Evaluations, month, r.OwningUnitId, ReportingPartitions.Public,
                Dims((ReportingDimensions.Status, r.Status)), r.Count)));
        }

        if (computation.Wants(TeamsMetricCodes.Score))
        {
            // The results permission on the team (its scope or a record role such as evaluator / results viewer), as WithResultsAsync checks it.
            var withResults = user is null
                ? teams
                : await access.ApplyScopeAsync(teams, user, TeamPermissions.Results, TeamScope.Map, cancellationToken).ConfigureAwait(false);
            var rows = await (from e in db.Evaluations.AsNoTracking()
                              join t in withResults on e.TeamId equals t.Id
                              where e.Status == EvaluationStatuses.Approved && e.Score != null
                              group e by new { t.OwningUnitId, t.TypeCode } into g
                              select new { g.Key.OwningUnitId, g.Key.TypeCode, Sum = g.Sum(e => e.Score!.Value), Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            facts.AddRange(rows.Select(r => new MetricFact(TeamsMetricCodes.Score, month, r.OwningUnitId, ReportingPartitions.Public,
                Dims((ReportingDimensions.Type, r.TypeCode)), r.Sum / TeamsMetricCodes.ScoreScale, r.Count)));
        }

        return facts;
    }

    /// <summary>No partition beyond the data scope: scores use their own scope permission (<c>teams.evaluation.results</c>).</summary>
    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    private static string Bool(bool value) => value ? "true" : "false";

    private static Dictionary<string, string> Dims(params (string Key, string Value)[] dimensions) =>
        dimensions.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
}
