using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Files;

/// <summary>Content kinds the inspector recognises by their bytes (magic number), not by the name the client sent.</summary>
public static class FileKinds
{
    public const string Pdf = "pdf";
    public const string Png = "png";
    public const string Jpeg = "jpeg";
    public const string Gif = "gif";
    public const string Webp = "webp";
    public const string Docx = "docx";
    public const string Xlsx = "xlsx";
    public const string Pptx = "pptx";
    public const string Doc = "doc";
    public const string Xls = "xls";
    public const string Ppt = "ppt";
    public const string Text = "txt";
    public const string Csv = "csv";

    public static readonly IReadOnlyList<string> Images = [Png, Jpeg, Webp];

    public static readonly IReadOnlyList<string> Documents = [Pdf, Png, Jpeg, Gif, Webp, Docx, Xlsx, Pptx, Doc, Xls, Ppt, Text, Csv];
}

/// <summary>
/// What a file is for, declared in code by its owner: allowed kinds, size limit, whether versions are kept and whether an owner has a
/// single file of this purpose (photo, logo) or a list (attachments).
/// </summary>
public sealed record FilePurpose(
    string Key,
    string OwnerType,
    string SystemCode,
    IReadOnlyList<string> AllowedKinds,
    long MaxBytes,
    bool SingleFile = false);

/// <summary>Record-level access to the files of one owner type (e.g. <c>sample.item</c>), implemented by the owning module.</summary>
public interface IFileOwnerAccess
{
    string OwnerType { get; }

    /// <summary>Attachments purpose used by the generic attachments API for this owner type.</summary>
    string AttachmentPurpose { get; }

    Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken);

    Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken);
}

public sealed record FileVersionDto(
    Guid Id, int VersionNo, string ContentType, long SizeBytes, string ScanStatus, DateTimeOffset UploadedAt, Guid? UploadedBy, string? Comment);

public sealed record FileDto(
    Guid Id,
    string Purpose,
    string OwnerType,
    string OwnerId,
    string Name,
    string ContentType,
    long SizeBytes,
    int VersionNo,
    Guid CurrentVersionId,
    string ScanStatus,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    uint Version);

/// <summary>An upload. <paramref name="ReplacesFileId"/> adds a version to an existing file; single-file purposes version automatically.</summary>
public sealed record FileUpload(
    string Purpose, string OwnerId, string FileName, Stream Content, string? Comment = null, Guid? ReplacesFileId = null);

/// <summary>A stream to send to the client; the caller disposes it.</summary>
public sealed record FileContent(Stream Stream, string ContentType, string FileName, long Length, DateTimeOffset LastModified, Guid FileId, Guid VersionId);

/// <summary>
/// Stores and reads files through the configured store with the purpose's checks (allow-list, magic number, size, optional virus scan).
/// It does not check record access: callers do that first (generic attachments API, or the owner's own endpoint).
/// </summary>
public interface IFileService
{
    Task<Result<FileDto>> UploadAsync(FileUpload upload, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileDto>> ListAsync(string ownerType, string ownerId, string? purpose = null, CancellationToken cancellationToken = default);

    Task<FileDto?> FindAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>The current file of a single-file purpose (photo, logo).</summary>
    Task<FileDto?> FindSingleAsync(string purpose, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Owners (of <paramref name="ownerIds"/>) that have a current file of the purpose.</summary>
    Task<IReadOnlySet<string>> OwnersWithFilesAsync(string purpose, IReadOnlyCollection<string> ownerIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileVersionDto>> VersionsAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Opens a version (default: current). Refused while a required scan is pending or the version is infected. Audited.</summary>
    Task<Result<FileContent>> OpenAsync(Guid fileId, Guid? versionId = null, bool auditDownload = true, CancellationToken cancellationToken = default);

    /// <summary>Soft delete with reason (audited); the content stays in the store for the audit trail.</summary>
    Task<Result> DeleteAsync(Guid fileId, string? reason, CancellationToken cancellationToken = default);
}

public static class FilesServiceCollectionExtensions
{
    public static IServiceCollection AddFilePurpose(this IServiceCollection services, FilePurpose purpose)
    {
        services.AddSingleton(purpose);
        return services;
    }

    public static IServiceCollection AddFileOwnerAccess<TAccess>(this IServiceCollection services)
        where TAccess : class, IFileOwnerAccess
    {
        services.AddScoped<IFileOwnerAccess, TAccess>();
        return services;
    }
}
