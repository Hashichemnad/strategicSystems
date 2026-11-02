using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Audit;
using Strategy.Platform.Files.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Platform.Files.Api;

/// <summary>
/// Generic attachments API. Record access is decided by the owner type's <see cref="IFileOwnerAccess"/>: outside the caller's scope the
/// owner and its files look missing (404), and a refused download is written to the audit trail (<c>file.download.denied</c>).
/// </summary>
internal static class FileEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (ISettingsEditor editor, CancellationToken ct) => TypedResults.Ok(await editor.GetDocumentAsync<FilesSettings>(ct)))
            .WithName("GetFilesSettings").RequirePermission(SettingsPermissions.FileManage);
        group.MapPut("/settings", async Task<Results<Ok<SettingsDocument<FilesSettings>>, ProblemHttpResult>> (
                    FilesSettingsInput body, ISettingsEditor editor, CancellationToken ct) =>
                (await editor.SaveAsync(body.Value, body.Version, FilesSettings.Validate, ct)).ToOk())
            .WithName("UpdateFilesSettings").RequirePermission(SettingsPermissions.FileManage);

        var owners = group.MapGroup("/owners/{ownerType}/{ownerId}");
        owners.MapGet("/", ListAsync).WithName("ListAttachments").RequireSignedIn();
        owners.MapPost("/", UploadAsync).WithName("UploadAttachment").RequireSignedIn().DisableAntiforgery();

        var files = group.MapGroup("/{fileId:guid}");
        files.MapGet("/versions", VersionsAsync).WithName("ListFileVersions").RequireSignedIn();
        files.MapPost("/versions", NewVersionAsync).WithName("UploadFileVersion").RequireSignedIn().DisableAntiforgery();
        files.MapGet("/content", ContentAsync).WithName("GetFileContent").RequireSignedIn()
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");
        files.MapPost("/delete", DeleteAsync).WithName("DeleteFile").RequireSignedIn();
    }

    private static async Task<Results<Ok<IReadOnlyList<FileDto>>, ProblemHttpResult>> ListAsync(
        string ownerType, string ownerId, HttpContext context, IFileService files, CancellationToken ct)
    {
        var access = Owner(context, ownerType);
        if (access is null || !await access.CanReadAsync(context.User, ownerId, ct))
        {
            return Error.NotFound().ToProblem();
        }

        return TypedResults.Ok(await files.ListAsync(ownerType, ownerId, access.AttachmentPurpose, ct));
    }

    private static async Task<Results<Ok<FileDto>, ProblemHttpResult>> UploadAsync(
        string ownerType, string ownerId, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? comment, HttpContext context, IFileService files,
        CancellationToken ct)
    {
        var access = Owner(context, ownerType);
        if (access is null || !await access.CanReadAsync(context.User, ownerId, ct))
        {
            return Error.NotFound().ToProblem();
        }

        if (!await access.CanWriteAsync(context.User, ownerId, ct))
        {
            return Error.Forbidden().ToProblem();
        }

        await using var stream = file.OpenReadStream();
        return (await files.UploadAsync(new FileUpload(access.AttachmentPurpose, ownerId, file.FileName, stream, comment), ct)).ToOk();
    }

    private static async Task<Results<Ok<FileDto>, ProblemHttpResult>> NewVersionAsync(
        Guid fileId, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? comment, HttpContext context, IFileService files, CancellationToken ct)
    {
        var (existing, access) = await LoadAsync(fileId, context, files, ct);
        if (existing is null || access is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (!await access.CanWriteAsync(context.User, existing.OwnerId, ct))
        {
            return Error.Forbidden().ToProblem();
        }

        await using var stream = file.OpenReadStream();
        return (await files.UploadAsync(new FileUpload(existing.Purpose, existing.OwnerId, file.FileName, stream, comment, fileId), ct)).ToOk();
    }

    private static async Task<Results<Ok<IReadOnlyList<FileVersionDto>>, ProblemHttpResult>> VersionsAsync(
        Guid fileId, HttpContext context, IFileService files, CancellationToken ct)
    {
        var (existing, _) = await LoadAsync(fileId, context, files, ct);
        return existing is null ? Error.NotFound().ToProblem() : TypedResults.Ok(await files.VersionsAsync(fileId, ct));
    }

    private static async Task<IResult> ContentAsync(
        Guid fileId, Guid? versionId, bool? download, HttpContext context, IFileService files, IAuditLog audit, CancellationToken ct)
    {
        var (existing, _) = await LoadAsync(fileId, context, files, ct);
        if (existing is null)
        {
            // Refused (or missing): audited without revealing which.
            await audit.WriteAsync(new AuditEvent(AuditActions.FileDownloadDenied, false, null, "files.file", fileId.ToString("D"), new { versionId }), ct);
            return Error.NotFound().ToProblem();
        }

        var opened = await files.OpenAsync(fileId, versionId, auditDownload: true, ct);
        if (opened.IsFailure)
        {
            return opened.Error!.ToProblem();
        }

        var content = opened.Value;
        var inline = download != true && (content.ContentType.StartsWith("image/", StringComparison.Ordinal) || content.ContentType == "application/pdf");
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        context.Response.Headers.CacheControl = "private, no-store";
        return inline
            ? TypedResults.Stream(content.Stream, content.ContentType, null, content.LastModified)
            : TypedResults.Stream(content.Stream, content.ContentType, content.FileName, content.LastModified);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid fileId, DeleteInput body, HttpContext context, IFileService files, CancellationToken ct)
    {
        var (existing, access) = await LoadAsync(fileId, context, files, ct);
        if (existing is null || access is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (!await access.CanWriteAsync(context.User, existing.OwnerId, ct))
        {
            return Error.Forbidden().ToProblem();
        }

        var result = await files.DeleteAsync(fileId, body.Reason, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
    }

    /// <summary>The file and its owner's access hook when the caller may read the owner record; otherwise (null, null).</summary>
    private static async Task<(FileDto? File, IFileOwnerAccess? Access)> LoadAsync(Guid fileId, HttpContext context, IFileService files, CancellationToken ct)
    {
        var file = await files.FindAsync(fileId, ct);
        var access = file is null ? null : Owner(context, file.OwnerType);
        return file is not null && access is not null && await access.CanReadAsync(context.User, file.OwnerId, ct) ? (file, access) : (null, null);
    }

    private static IFileOwnerAccess? Owner(HttpContext context, string ownerType) =>
        context.RequestServices.GetServices<IFileOwnerAccess>().FirstOrDefault(a => a.OwnerType == ownerType);
}

internal sealed record DeleteInput(string? Reason);

internal sealed record FilesSettingsInput(FilesSettings Value, uint Version);
