using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Standards enhancement switches on the server (ADR 0092): a switched-off feature's endpoints answer 404 <c>portfolio.errors.featureOff</c>.</summary>
public static class FeatureGate
{
    public static Error Off(string code) =>
        new("portfolio.featureOff", "portfolio.errors.featureOff", ErrorKind.NotFound, new Dictionary<string, object?> { ["feature"] = code });

    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string code)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var reader = context.HttpContext.RequestServices.GetRequiredService<RequestReader>();
            var settings = await reader.SettingsAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
            return settings.Features.IsOn(code) ? await next(context).ConfigureAwait(false) : Off(code).ToProblem();
        });
        return builder;
    }
}

/// <summary>
/// "View costs / budget" (SPEC §1.9, ADR 0098): permission <c>portfolio.project.costs</c>, plus the team-member record role when Settings
/// <c>teamMembersSeeCosts</c> is on. Every read path that shows money asks here.
/// </summary>
internal sealed class CostVisibility(PortfolioDbContext db, IAccessService access, RequestReader reader)
{
    public async Task<bool> CanSeeAsync(ClaimsPrincipal user, Project project, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (await access.CanAsync(user, PortfolioPermissions.ProjectCosts, ProjectScope.Map.RefOf(project), ct).ConfigureAwait(false))
        {
            return true;
        }

        return (await reader.SettingsAsync(ct).ConfigureAwait(false)).TeamMembersSeeCosts
            && await access.CanAsync(user, PortfolioPermissions.ProjectContribute, ProjectScope.Map.RefOf(project), ct).ConfigureAwait(false);
    }

    /// <summary>The projects among <paramref name="projectIds"/> whose costs the caller may see (in the database).</summary>
    public async Task<HashSet<Guid>> VisibleSetAsync(ClaimsPrincipal user, IReadOnlyCollection<Guid> projectIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var source = db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id));
        var costs = await access.ApplyScopeAsync(source, user, PortfolioPermissions.ProjectCosts, ProjectScope.Map, ct).ConfigureAwait(false);
        var ids = await costs.Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        if ((await reader.SettingsAsync(ct).ConfigureAwait(false)).TeamMembersSeeCosts)
        {
            var members = await access.ApplyScopeAsync(source, user, PortfolioPermissions.ProjectContribute, ProjectScope.Map, ct).ConfigureAwait(false);
            ids.AddRange(await members.Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false));
        }

        return [.. ids];
    }
}
