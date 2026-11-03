using System.Security.Claims;

namespace Strategy.Modules.Portfolio.Contracts;

/// <summary>A program request or project as another module may show it (badge), for a caller who may read it.</summary>
public sealed record PortfolioRecordBadge(string ResourceType, Guid Id, string? Number, string? NameAr, string? NameEn, string Status, string? Classification);

/// <summary>Read access to Portfolio records for other modules (docs/design/portfolio.md §1). Null when missing or outside the caller's scope.</summary>
public interface IPortfolioDirectory
{
    Task<PortfolioRecordBadge?> GetRequestAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    Task<PortfolioRecordBadge?> GetProjectAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
