using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Links;

namespace Strategy.Modules.Sample.Application;

/// <summary>
/// Phase 6 worked example of the links registry (ADR 0034): sample items are task-able; their editors see every task linked to them
/// (<see cref="LinkableTypeDefinition.FollowLinkedPermission"/>).
/// </summary>
public static class SampleLinks
{
    public static readonly LinkableTypeDefinition Definition = new(
        SamplePermissions.ResourceType, "sample", "بند تجريبي", "Sample item", [LinkCapabilities.TaskAble, LinkCapabilities.TeamAble], SamplePermissions.Read, "/sample/items/{id}",
        FollowLinkedPermission: SamplePermissions.Update);
}

/// <summary>Badges of sample items the user may read (the same 404 rule as the item's own endpoints).</summary>
internal sealed class SampleLinkTargets(SampleDbContext db, IAccessService access) : ILinkTargetSource
{
    public string ResourceType => SamplePermissions.ResourceType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var scoped = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        var items = await scoped.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.ToDictionary(x => x.Id, x => new LinkTarget(
            x.Id, x.Number, x.NameAr, x.NameEn, x.ApprovalStatus, ApprovalLabel(x.ApprovalStatus, ar: true), ApprovalLabel(x.ApprovalStatus, ar: false),
            x.Status == SampleItemStatus.Inactive ? "neutral" : x.ApprovalStatus switch
            {
                SampleApprovalStatus.Approved => "success",
                SampleApprovalStatus.Rejected => "danger",
                SampleApprovalStatus.Returned => "warning",
                SampleApprovalStatus.Pending => "info",
                _ => "neutral",
            },
            x.Status == SampleItemStatus.Inactive));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        return item is not null && await access.CanAsync(user, permission, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Status labels for the badge (the module's own wording; the SPA shows them as given).</summary>
    private static string ApprovalLabel(string code, bool ar) => code switch
    {
        SampleApprovalStatus.Draft => ar ? "مسودة" : "Draft",
        SampleApprovalStatus.Pending => ar ? "بانتظار الموافقة" : "Pending approval",
        SampleApprovalStatus.Approved => ar ? "معتمد" : "Approved",
        SampleApprovalStatus.Rejected => ar ? "مرفوض" : "Rejected",
        SampleApprovalStatus.Returned => ar ? "معاد للتصحيح" : "Returned",
        _ => code,
    };
}
