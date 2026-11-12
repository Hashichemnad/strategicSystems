using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.Application;

public sealed record ReportFolderDto(Guid Id, string Name, string Kind, Guid OwnerUserId, bool CanEdit, uint Version);

public sealed record ReportFolderInput(string Name, string Kind);

/// <summary>
/// Report folders (ADR 0183): every user keeps private folders ("My reports"); shared folders — created, shared and published into only with
/// <c>reporting.report.share</c> (Report author) — are visible to the users / roles / units they are shared with.
/// </summary>
public sealed class FolderService(ReportingDbContext db, IAccessService access, DashboardService dashboards)
{
    private static ReportFolderDto ToDto(ReportFolder f, Guid? userId) => new(f.Id, f.Name, f.Kind, f.OwnerUserId, f.OwnerUserId == userId, f.Version);

    private Task<bool> CanShareAsync(ClaimsPrincipal user, CancellationToken ct) => access.CanAsync(user, ReportingPermissions.ReportShare, cancellationToken: ct);

    public async Task<IReadOnlyList<ReportFolderDto>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var folders = await db.Folders.AsNoTracking().Where(f => f.Status == DefinitionStatuses.Active && (f.OwnerUserId == userId || f.Kind == FolderKinds.Shared))
            .OrderBy(f => f.Name).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<ReportFolderDto>();
        foreach (var folder in folders)
        {
            if (folder.OwnerUserId == userId
                || await dashboards.MatchesAnyAsync(await db.Shares.AsNoTracking().Where(s => s.TargetKind == "folder" && s.TargetId == folder.Id).ToListAsync(ct).ConfigureAwait(false), user, ct)
                    .ConfigureAwait(false))
            {
                result.Add(ToDto(folder, userId));
            }
        }

        return result;
    }

    public async Task<Result<ReportFolderDto>> SaveAsync(Guid? id, ReportFolderInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Kind is not (FolderKinds.Private or FolderKinds.Shared))
        {
            return Error.Validation([new FieldError("name", "reporting.errors.folder", "reporting.errors.folder")]);
        }

        if (input.Kind == FolderKinds.Shared && !await CanShareAsync(user, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        ReportFolder folder;
        if (id is { } existingId)
        {
            folder = (await db.Folders.FirstOrDefaultAsync(f => f.Id == existingId && f.OwnerUserId == userId && f.Status == DefinitionStatuses.Active, ct).ConfigureAwait(false))!;
            if (folder is null)
            {
                return Error.NotFound();
            }

            if (folder.Kind != input.Kind)
            {
                return Error.Validation([new FieldError("kind", "reporting.errors.folder", "reporting.errors.folder")]);
            }

            folder.Rename(input.Name.Trim());
        }
        else
        {
            folder = new ReportFolder(userId, input.Name.Trim(), input.Kind);
            db.Folders.Add(folder);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(folder, userId);
    }

    /// <summary>Archives an own folder; its reports move back to "My reports" (they stay the owners' reports).</summary>
    public async Task<Result> ArchiveAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerUserId == userId && f.Status == DefinitionStatuses.Active, ct).ConfigureAwait(false);
        if (folder is null)
        {
            return Error.NotFound();
        }

        folder.Archive();
        foreach (var report in await db.Reports.Where(r => r.FolderId == id).ToListAsync(ct).ConfigureAwait(false))
        {
            report.MoveTo(null);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<bool> CanShareFolderAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        return await db.Folders.AnyAsync(f => f.Id == id && f.OwnerUserId == userId && f.Kind == FolderKinds.Shared && f.Status == DefinitionStatuses.Active, ct).ConfigureAwait(false)
            && await CanShareAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>Moves an own report into an own folder (or back to My reports); a shared folder needs Report author (publishing).</summary>
    public async Task<Result> MoveAsync(Guid reportId, Guid? folderId, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && r.OwnerUserId == userId && r.Status == "active", ct).ConfigureAwait(false);
        if (report is null)
        {
            return Error.NotFound();
        }

        if (folderId is { } target)
        {
            var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == target && f.OwnerUserId == userId && f.Status == DefinitionStatuses.Active, ct)
                .ConfigureAwait(false);
            if (folder is null)
            {
                return Error.NotFound();
            }

            if (folder.Kind == FolderKinds.Shared && !await CanShareAsync(user, ct).ConfigureAwait(false))
            {
                return Error.Forbidden();
            }
        }

        report.MoveTo(folderId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
