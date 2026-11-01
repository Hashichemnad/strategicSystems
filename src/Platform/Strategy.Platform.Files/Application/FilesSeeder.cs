using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Files.Domain;
using Strategy.Platform.Files.Infrastructure;
using Strategy.Platform.Files.Storage;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Files.Application;

/// <summary>
/// Carries files staged by migrations (Phase 1–2 employee photos and branding logos) into the file store, keeping their ids, owners and
/// dates. Idempotent: each staged row is removed in the same transaction that records the file.
/// </summary>
internal sealed class FilesSeeder : IDatabaseSeeder
{
    public int Order => 5;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<FilesDbContext>();
        var store = services.GetRequiredService<IFileStore>();
        var pending = await db.PendingImports.OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in pending)
        {
            var kind = ContentInspector.Sniff(item.Content) ?? FileKinds.Png;
            var file = StoredFile.Create(item.Id, item.Purpose, item.OwnerType, item.OwnerId, item.FileName);
            var version = new FileVersion
            {
                FileId = file.Id,
                VersionNo = 1,
                SizeBytes = item.Content.Length,
                Kind = kind,
                ContentType = ContentInspector.ContentTypeOf(kind),
                Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(item.Content)),
                UploadedAt = item.CreatedAt,
                UploadedBy = item.CreatedBy,
                Comment = "carried over (Phase 3 upgrade)",
            };
            version.StorageKey = $"{item.CreatedAt:yyyy}/{item.CreatedAt:MM}/{version.Id:N}";
            await store.WriteAsync(version.StorageKey, new MemoryStream(item.Content, writable: false), cancellationToken).ConfigureAwait(false);
            file.AddVersion(version, item.FileName);
            db.Files.Add(file);
            db.Versions.Add(version);
            db.PendingImports.Remove(item);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
