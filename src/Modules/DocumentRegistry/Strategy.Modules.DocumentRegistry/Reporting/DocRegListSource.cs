using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.DocumentRegistry.Reporting;

/// <summary>Published documents expiring within the dashboard's window (soonest first), among the documents the viewer may see (<see cref="DocumentVisibility"/>).</summary>
internal sealed class DocRegListSource(DocumentVisibility visibility) : IReportingListSource
{
    public const string ExpiringList = "docreg.expiring";

    public string SystemCode => DocRegPermissions.SystemCode;

    public IReadOnlyList<ReportingListDefinition> Lists { get; } =
    [
        new(ExpiringList, DocRegPermissions.SystemCode, "وثائق تقترب من انتهاء صلاحيتها", "Documents expiring soon", DocRegPermissions.Read),
    ];

    public async Task<IReadOnlyList<ReportingListItem>> GetAsync(string code, ReportingListQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (code != ExpiringList)
        {
            return [];
        }

        var soon = query.Today.AddDays(DocumentService.SoonDays);
        var documents = (await visibility.VisibleAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(d => d.Status == DocumentStatuses.Published && d.ExpiryDate != null && d.ExpiryDate <= soon);
        if (query.UnitIds is { Count: > 0 } units)
        {
            var ids = units.ToList();
            documents = documents.Where(d => d.OwnerUnitId != null && ids.Contains(d.OwnerUnitId.Value));
        }

        var rows = await documents.OrderBy(d => d.ExpiryDate).ThenBy(d => d.DocumentNo).Take(Math.Clamp(query.Take, 1, 100)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(d =>
        {
            var days = d.ExpiryDate!.Value.DayNumber - query.Today.DayNumber;
            return new ReportingListItem(Document.EntityType, d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Status, days < 0 ? "danger" : days <= 30 ? "warning" : "info", days,
                d.ExpiryDate, d.OwnerUnitId, $"/documents/{d.Id:D}");
        }).ToList();
    }
}
