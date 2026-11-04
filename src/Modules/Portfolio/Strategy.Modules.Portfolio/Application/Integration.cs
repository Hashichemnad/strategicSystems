using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Links;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Program requests are task-able (SPEC §0.0, ADR 0034): tasks can be linked to a request; its editors follow them.</summary>
public static class RequestLinks
{
    public static readonly LinkableTypeDefinition Definition = new(
        PortfolioPermissions.RequestResource, PortfolioPermissions.SystemCode, "طلب برنامج", "Program request", [LinkCapabilities.TaskAble],
        PortfolioPermissions.RequestRead, "/portfolio/requests/{id}", FollowLinkedPermission: PortfolioPermissions.RequestEdit);
}

/// <summary>Badges of requests the user may read (the same 404 rule as the request's own endpoints).</summary>
internal sealed class RequestLinkTargets(RequestReader reader) : ILinkTargetSource
{
    public string ResourceType => PortfolioPermissions.RequestResource;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => new LinkTarget(
            r.Id, r.Number, r.NameAr, r.NameEn, r.Status, StatusLabel(r.Status, ar: true), StatusLabel(r.Status, ar: false), r.Status switch
            {
                RequestStatuses.Approved => "success",
                RequestStatuses.Rejected => "danger",
                RequestStatuses.InApproval => "info",
                RequestStatuses.Cancelled => "neutral",
                _ => r.ApprovalStatus == RequestApprovalResults.Returned ? "warning" : "neutral",
            },
            RequestStatuses.IsFinal(r.Status)));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } request
        && await reader.CanAsync(user, permission, request, cancellationToken).ConfigureAwait(false);

    /// <summary>Status labels for the badge (the module's own wording; the SPA shows them as given).</summary>
    private static string StatusLabel(string code, bool ar) => code switch
    {
        RequestStatuses.Draft => ar ? "مسودة" : "Draft",
        RequestStatuses.InApproval => ar ? "قيد الاعتماد" : "In approval",
        RequestStatuses.Approved => ar ? "معتمد" : "Approved",
        RequestStatuses.Rejected => ar ? "مرفوض" : "Rejected",
        RequestStatuses.Cancelled => ar ? "ملغى" : "Cancelled",
        _ => code,
    };
}

/// <summary><see cref="IPortfolioDirectory"/> for other modules (read scope applied; null outside it).</summary>
internal sealed class PortfolioDirectory(RequestReader reader) : IPortfolioDirectory
{
    public async Task<PortfolioRecordBadge?> GetRequestAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } r
            ? new PortfolioRecordBadge(PortfolioPermissions.RequestResource, r.Id, r.Number, r.NameAr, r.NameEn, r.Status, r.EffectiveClassification)
            : null;

    public async Task<PortfolioRecordBadge?> GetProjectAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await (await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false)).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false) is { } p
            ? new PortfolioRecordBadge(PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, p.Stage, p.Classification)
            : null;

    public async Task<IReadOnlyList<PortfolioRecordBadge>> SearchProjectsAsync(string? text, ClaimsPrincipal user, int take = 20, CancellationToken cancellationToken = default)
    {
        var source = await reader.VisibleProjectsAsync(user, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            source = source.Where(p => EF.Functions.ILike(p.Number, pattern, "\\") || EF.Functions.ILike(p.NameAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(p.NameEn ?? string.Empty, pattern, "\\"));
        }

        var rows = await source.OrderByDescending(p => p.Number).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(p => new PortfolioRecordBadge(PortfolioPermissions.ProjectResource, p.Id, p.Number, p.NameAr, p.NameEn, p.Stage, p.Classification)).ToList();
    }
}
