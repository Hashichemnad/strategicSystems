using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Files.Domain;

public static class FileStatuses
{
    public const string Active = "active";
    public const string Deleted = "deleted";
}

public static class ScanStatuses
{
    public const string NotScanned = "not_scanned";
    public const string Clean = "clean";
    public const string Pending = "pending";
    public const string Infected = "infected";
}

/// <summary>A stored file of one owner record; its content lives in versions. Never hard-deleted (soft delete with reason).</summary>
[Audited("files.file")]
public sealed class StoredFile : AuditableEntity
{
    private StoredFile()
    {
    }

    public string Purpose { get; private set; } = string.Empty;

    public string OwnerType { get; private set; } = string.Empty;

    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public Guid CurrentVersionId { get; private set; }

    public int VersionCount { get; private set; }

    public string Status { get; private set; } = FileStatuses.Active;

    public string? DeleteReason { get; private set; }

    public static StoredFile Create(Guid id, string purpose, string ownerType, string ownerId, string name) =>
        new() { Id = id, Purpose = purpose, OwnerType = ownerType, OwnerId = ownerId, Name = name };

    public void AddVersion(FileVersion version, string name)
    {
        CurrentVersionId = version.Id;
        VersionCount = version.VersionNo;
        Name = name;
    }

    public void Delete(string? reason)
    {
        Status = FileStatuses.Deleted;
        DeleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

/// <summary>One immutable version: where the bytes are, what they are (sniffed), their hash and scan result.</summary>
[Audited("files.file_version")]
[AuditParent("files.file", nameof(FileId))]
public sealed class FileVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid FileId { get; set; }

    public int VersionNo { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the content.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string ScanStatus { get; set; } = ScanStatuses.NotScanned;

    public DateTimeOffset UploadedAt { get; set; }

    public Guid? UploadedBy { get; set; }

    public string? Comment { get; set; }
}

/// <summary>Staging for files carried over from interim bytea tables (employee photos, branding logos); drained by the files seeder.</summary>
public sealed class PendingImport
{
    public Guid Id { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string OwnerType { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
