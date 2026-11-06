using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.IpRegistry.Reporting;

/// <summary>Registered / renewed IP records whose protection ends within the dashboard's window (soonest first), within <see cref="IpReader.VisibleAsync"/>.</summary>
internal sealed class IpListSource(IpReader reader) : IReportingListSource
{
    public const string ExpiringList = "ip.expiring";

    public string SystemCode => IpPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(ExpiringList, IpPermissions.SystemCode, "حمايات ملكية فكرية تقترب من الانتهاء", "Upcoming IP expiries", IpPermissions.Read),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (code != ExpiringList)
        {
            return [];
        }

        var soon = query.Today.AddDays(IpService.SoonDays);
        var records = (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => (r.Status == IpStatuses.Registered || r.Status == IpStatuses.Renewed) && r.ExpiryDate != null && r.ExpiryDate <= soon);
        if (query.UnitIds is { Count: > 0 } units)
        {
            var ids = units.ToList();
            records = records.Where(r => r.OwnerUnitId != null && ids.Contains(r.OwnerUnitId.Value));
        }

        var rows = await records.OrderBy(r => r.ExpiryDate).ThenBy(r => r.Number).Take(Math.Clamp(query.Take, 1, 100)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r =>
        {
            var days = r.ExpiryDate!.Value.DayNumber - query.Today.DayNumber;
            return new ReportingListItem(IpRecord.EntityType, r.Id, r.Number, r.NameAr, r.NameEn, r.Status, days < 0 ? "danger" : days <= 90 ? "warning" : "info", days,
                r.ExpiryDate, r.OwnerUnitId, $"/ip/records/{r.Id:D}");
        }).ToList();
    }
}
