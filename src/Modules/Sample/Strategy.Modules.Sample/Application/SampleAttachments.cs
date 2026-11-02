using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;

namespace Strategy.Modules.Sample.Application;

/// <summary>Documents tab of a sample item: read with the item's read access, write with its edit access (the worked example for modules).</summary>
internal sealed class SampleAttachments(SampleDbContext db, IAccessService access) : IFileOwnerAccess
{
    public static readonly FilePurpose Purpose = new("sample.attachment", SamplePermissions.ResourceType, "sample", FileKinds.Documents, 20 * 1024 * 1024);

    public string OwnerType => SamplePermissions.ResourceType;

    public string AttachmentPurpose => Purpose.Key;

    public Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        CanAsync(user, ownerId, SamplePermissions.Read, cancellationToken);

    public Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        CanAsync(user, ownerId, SamplePermissions.Update, cancellationToken);

    private async Task<bool> CanAsync(ClaimsPrincipal user, string ownerId, string permission, CancellationToken ct)
    {
        if (!Guid.TryParse(ownerId, out var id))
        {
            return false;
        }

        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return item is not null && await access.CanAsync(user, permission, SampleScope.Map.RefOf(item), ct).ConfigureAwait(false);
    }
}
