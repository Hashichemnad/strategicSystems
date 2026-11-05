using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.IpRegistry.Reporting;

/// <summary>
/// IP register metric (docs/design/reporting.md §2.5): every IP record, as the IP dashboard counts them. Unit = the owner unit
/// (<see cref="IpScope.Map"/>). Live path: <see cref="IpReader.VisibleAsync"/> (scope, requester, contributors).
/// </summary>
internal sealed class IpMetricSource(IpDbContext db, IpReader reader) : IReportingMetricSource
{
    public const string Records = "ip.records";

    public const string Protection = "protection";
    public const string Expiring = "expiring";
    public const string Year = "year";
    public const string None = "none";

    public string SystemCode => IpPermissions.SystemCode;

    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new(Records, IpPermissions.SystemCode, "سجلات الملكية الفكرية", "IP records", MetricKind.Stock, MetricAggregation.Sum, MetricValueKind.Count, IpPermissions.Read,
            [ReportingDimensions.Type, Protection, ReportingDimensions.Status, Expiring, Year], DrillLink: "/ip/dashboard",
            DimensionLookups: new Dictionary<string, string>
            {
                [ReportingDimensions.Type] = IpPermissions.PropertyTypeLookup,
                [Protection] = IpPermissions.ProtectionTypeLookup,
            },
            DimensionLabels: new Dictionary<string, IReadOnlyDictionary<string, DimensionLabel>>
            {
                [ReportingDimensions.Status] = IpFacts.Labels(
                    (IpStatuses.Submitted, "مقدم", "Submitted"), (IpStatuses.UnderReview, "قيد المراجعة", "Under review"), (IpStatuses.Returned, "معاد للتصحيح", "Returned"),
                    (IpStatuses.Rejected, "مرفوض", "Rejected"), (IpStatuses.ApprovedToFile, "معتمد للإيداع", "Approved to file"), (IpStatuses.Filed, "مودع", "Filed"),
                    (IpStatuses.Registered, "مسجل", "Registered"), (IpStatuses.RejectedByMoet, "مرفوض من الوزارة", "Rejected by the ministry"),
                    (IpStatuses.Expired, "منتهي", "Expired"), (IpStatuses.Renewed, "مجدد", "Renewed"), (IpStatuses.Withdrawn, "مسحوب", "Withdrawn")),
                [Expiring] = IpFacts.YesNo,
            }),
    ];

    public Task<IReadOnlyCollection<string>> VisiblePartitionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public async Task<IReadOnlyList<MetricFact>> ComputeAsync(MetricComputation computation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(computation);
        if (!computation.CoversCurrentMonth || !computation.Wants(Records))
        {
            return [];
        }

        var records = computation.User is { } user ? await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false) : db.Records.AsNoTracking();
        var soon = computation.Today.AddDays(IpService.SoonDays);
        var rows = await records.Select(r => new
        {
            r.OwnerUnitId,
            r.PropertyType,
            r.ProtectionType,
            r.Status,
            Expiring = (r.Status == IpStatuses.Registered || r.Status == IpStatuses.Renewed) && r.ExpiryDate != null && r.ExpiryDate <= soon,
            r.CreatedAt,
        })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var facts = new IpFacts();
        foreach (var r in rows)
        {
            facts.Add(Records, computation.CurrentMonth, r.OwnerUnitId, IpFacts.Dims(
                (ReportingDimensions.Type, r.PropertyType ?? None), (Protection, r.ProtectionType ?? None), (ReportingDimensions.Status, r.Status),
                (Expiring, IpFacts.Bool(r.Expiring)), (Year, r.CreatedAt.Year.ToString(CultureInfo.InvariantCulture))), 1);
        }

        return facts.ToList();
    }
}
