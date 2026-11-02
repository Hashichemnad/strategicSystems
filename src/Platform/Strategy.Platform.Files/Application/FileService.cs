using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit;
using Strategy.Platform.Files.Domain;
using Strategy.Platform.Files.Infrastructure;
using Strategy.Platform.Files.Storage;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Files.Application;

internal sealed class FileService(
    FilesDbContext db,
    IFileStore store,
    IVirusScanner scanner,
    ISettingsStore settings,
    IEnumerable<FilePurpose> purposes,
    IAuditLog audit,
    IRequestContext context,
    IClock clock) : IFileService
{
    private const int SniffBytes = 8192;

    public static Error UnknownPurpose => Error.Rule("files.unknownPurpose", "files.errors.unknownPurpose");

    public static Error NotAvailable => Error.Rule("files.notAvailable", "files.errors.notAvailable");

    public async Task<Result<FileDto>> UploadAsync(FileUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var purpose = purposes.FirstOrDefault(p => p.Key == upload.Purpose);
        if (purpose is null)
        {
            return UnknownPurpose;
        }

        var options = await settings.GetAsync<FilesSettings>(cancellationToken).ConfigureAwait(false);
        var limit = Math.Min(purpose.MaxBytes, options.MaxUploadMegabytes * 1024L * 1024L);
        var name = ContentInspector.SafeName(upload.FileName);

        using var buffer = new MemoryStream();
        var tooLarge = !await CopyLimitedAsync(upload.Content, buffer, limit, cancellationToken).ConfigureAwait(false);
        if (tooLarge || buffer.Length == 0)
        {
            var error = tooLarge
                ? Field("files.errors.tooLarge", new Dictionary<string, object?> { ["maxMb"] = Math.Max(1, limit / (1024 * 1024)) })
                : Field("files.errors.empty");
            await RefusedAsync(purpose, upload, name, tooLarge ? "tooLarge" : "empty", cancellationToken).ConfigureAwait(false);
            return error;
        }

        var bytes = buffer.ToArray();
        var kind = ContentInspector.Identify(name, bytes.AsSpan(0, Math.Min(bytes.Length, SniffBytes)), () => new MemoryStream(bytes, writable: false));
        if (kind is null || !purpose.AllowedKinds.Contains(kind))
        {
            await RefusedAsync(purpose, upload, name, "typeNotAllowed", cancellationToken).ConfigureAwait(false);
            return Field("files.errors.typeNotAllowed", new Dictionary<string, object?> { ["allowed"] = string.Join(", ", purpose.AllowedKinds) });
        }

        var scan = ScanStatuses.NotScanned;
        if (options.ScanEnabled)
        {
            var outcome = await scanner.ScanAsync(new MemoryStream(bytes, writable: false), options.ClamAvHost, options.ClamAvPort, cancellationToken)
                .ConfigureAwait(false);
            if (outcome == ScanOutcome.Infected)
            {
                await RefusedAsync(purpose, upload, name, "infected", cancellationToken).ConfigureAwait(false);
                return Field("files.errors.infected");
            }

            scan = outcome == ScanOutcome.Clean ? ScanStatuses.Clean : options.ScanRequired ? ScanStatuses.Pending : ScanStatuses.NotScanned;
        }

        StoredFile? file = null;
        if (upload.ReplacesFileId is { } replaces)
        {
            file = await db.Files.FirstOrDefaultAsync(f => f.Id == replaces && f.Status == FileStatuses.Active, cancellationToken).ConfigureAwait(false);
            if (file is null || file.Purpose != purpose.Key || file.OwnerId != upload.OwnerId)
            {
                return Error.NotFound();
            }
        }
        else if (purpose.SingleFile)
        {
            file = await db.Files.FirstOrDefaultAsync(
                f => f.Purpose == purpose.Key && f.OwnerId == upload.OwnerId && f.Status == FileStatuses.Active, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        file ??= db.Files.Add(StoredFile.Create(Guid.CreateVersion7(), purpose.Key, purpose.OwnerType, upload.OwnerId, name)).Entity;
        var version = new FileVersion
        {
            FileId = file.Id,
            VersionNo = file.VersionCount + 1,
            SizeBytes = bytes.Length,
            Kind = kind,
            ContentType = ContentInspector.ContentTypeOf(kind),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ScanStatus = scan,
            UploadedAt = now,
            UploadedBy = context.UserId,
            Comment = string.IsNullOrWhiteSpace(upload.Comment) ? null : upload.Comment.Trim()[..Math.Min(upload.Comment.Trim().Length, 500)],
        };
        version.StorageKey = $"{now:yyyy}/{now:MM}/{version.Id:N}";
        await store.WriteAsync(version.StorageKey, new MemoryStream(bytes, writable: false), cancellationToken).ConfigureAwait(false);
        db.Versions.Add(version);
        file.AddVersion(version, name);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent(AuditActions.FileUpload, true, purpose.SystemCode, purpose.OwnerType, upload.OwnerId,
            new { fileId = file.Id, versionId = version.Id, version.VersionNo, name, kind, size = bytes.Length, purpose = purpose.Key, scan }), cancellationToken)
            .ConfigureAwait(false);
        return ToDto(file, version);
    }

    public async Task<IReadOnlyList<FileDto>> ListAsync(string ownerType, string ownerId, string? purpose = null, CancellationToken cancellationToken = default)
    {
        var query = db.Files.AsNoTracking().Where(f => f.OwnerType == ownerType && f.OwnerId == ownerId && f.Status == FileStatuses.Active);
        if (purpose is not null)
        {
            query = query.Where(f => f.Purpose == purpose);
        }

        var rows = await query.Join(db.Versions.AsNoTracking(), f => f.CurrentVersionId, v => v.Id, (f, v) => new { f, v })
            .OrderByDescending(x => x.f.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(x => ToDto(x.f, x.v)).ToList();
    }

    public async Task<FileDto?> FindAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var row = await db.Files.AsNoTracking().Where(f => f.Id == fileId && f.Status == FileStatuses.Active)
            .Join(db.Versions.AsNoTracking(), f => f.CurrentVersionId, v => v.Id, (f, v) => new { f, v }).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDto(row.f, row.v);
    }

    public async Task<FileDto?> FindSingleAsync(string purpose, string ownerId, CancellationToken cancellationToken = default)
    {
        var row = await db.Files.AsNoTracking().Where(f => f.Purpose == purpose && f.OwnerId == ownerId && f.Status == FileStatuses.Active)
            .Join(db.Versions.AsNoTracking(), f => f.CurrentVersionId, v => v.Id, (f, v) => new { f, v }).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToDto(row.f, row.v);
    }

    public async Task<IReadOnlySet<string>> OwnersWithFilesAsync(string purpose, IReadOnlyCollection<string> ownerIds, CancellationToken cancellationToken = default) =>
        (await db.Files.AsNoTracking().Where(f => f.Purpose == purpose && f.Status == FileStatuses.Active && ownerIds.Contains(f.OwnerId))
            .Select(f => f.OwnerId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlyList<FileVersionDto>> VersionsAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        await db.Versions.AsNoTracking().Where(v => v.FileId == fileId).OrderByDescending(v => v.VersionNo)
            .Select(v => new FileVersionDto(v.Id, v.VersionNo, v.ContentType, v.SizeBytes, v.ScanStatus, v.UploadedAt, v.UploadedBy, v.Comment))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result<FileContent>> OpenAsync(Guid fileId, Guid? versionId = null, bool auditDownload = true, CancellationToken cancellationToken = default)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.Status == FileStatuses.Active, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            return Error.NotFound();
        }

        var id = versionId ?? file.CurrentVersionId;
        var version = await db.Versions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id && v.FileId == fileId, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return Error.NotFound();
        }

        if (version.ScanStatus is ScanStatuses.Pending or ScanStatuses.Infected)
        {
            return NotAvailable;
        }

        var stream = await store.OpenReadAsync(version.StorageKey, cancellationToken).ConfigureAwait(false);
        if (auditDownload)
        {
            var purpose = purposes.FirstOrDefault(p => p.Key == file.Purpose);
            await audit.WriteAsync(new AuditEvent(AuditActions.FileDownload, true, purpose?.SystemCode, file.OwnerType, file.OwnerId,
                new { fileId, versionId = version.Id, version.VersionNo, name = file.Name }), cancellationToken).ConfigureAwait(false);
        }

        return new FileContent(stream, version.ContentType, file.Name, version.SizeBytes, version.UploadedAt, file.Id, version.Id);
    }

    public async Task<Result> DeleteAsync(Guid fileId, string? reason, CancellationToken cancellationToken = default)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.Status == FileStatuses.Active, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return Error.NotFound();
        }

        file.Delete(reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var purpose = purposes.FirstOrDefault(p => p.Key == file.Purpose);
        await audit.WriteAsync(new AuditEvent(AuditActions.FileDelete, true, purpose?.SystemCode, file.OwnerType, file.OwnerId,
            new { fileId, name = file.Name }, file.DeleteReason), cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private Task RefusedAsync(FilePurpose purpose, FileUpload upload, string name, string why, CancellationToken ct) =>
        audit.WriteAsync(new AuditEvent(AuditActions.FileUpload, false, purpose.SystemCode, purpose.OwnerType, upload.OwnerId,
            new { name, purpose = purpose.Key, refused = why }), ct);

    private static Error Field(string key, Dictionary<string, object?>? parameters = null) =>
        Error.Validation([new FieldError("file", key, key, parameters)]);

    /// <returns>False when the content is longer than <paramref name="limit"/>.</returns>
    private static async Task<bool> CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return true;
    }

    internal static FileDto ToDto(StoredFile f, FileVersion v) => new(
        f.Id, f.Purpose, f.OwnerType, f.OwnerId, f.Name, v.ContentType, v.SizeBytes, v.VersionNo, v.Id, v.ScanStatus, f.UpdatedAt, f.UpdatedBy, f.Version);
}
