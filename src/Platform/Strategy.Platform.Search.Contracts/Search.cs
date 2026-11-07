using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Strategy.Platform.Search;

/// <summary>
/// A search request as contributors receive it. <c>Text</c> is the trimmed term as typed; <c>Normalised</c> is
/// <see cref="Kernel.Text.SearchText.Normalise"/> of it (match this against normalised columns); <c>Limit</c> is the most hits wanted per type.
/// </summary>
public sealed record SearchQuery(string Text, string Normalised, int Limit);

/// <summary>
/// A record found by a contributor. <c>ResourceType</c> is a linkable type (ADR 0034): the search service resolves it through the Links
/// registry, which decides what (if anything) the user sees. <c>MatchedOn</c>: <c>number</c>, <c>title</c> or a key-field code;
/// <c>MatchedText</c>: a snippet of the key field that matched (null for number and title).
/// </summary>
public sealed record SearchHit(string ResourceType, Guid Id, double Score, string? MatchedOn, string? MatchedText);

/// <summary>
/// Implemented by each module over its own record types (ADR 0160). Apply the user's data scope <b>in the database query</b> so the
/// top-N is made of records the user may read; the search service still keeps only hits the Links registry shows as visible.
/// </summary>
public interface ISearchContributor
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>A registered contributor. <c>SystemCode</c> null: cross-system (e.g. external references), always queried.</summary>
public sealed record SearchContributorRegistration(Type ContributorType, string? SystemCode);

public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers a contributor. With <paramref name="systemCode"/>, the contributor is not called when the user cannot open that system
    /// (or it is switched off). Without it, the contributor is cross-system and always called.
    /// </summary>
    public static IServiceCollection AddSearchContributor<TContributor>(this IServiceCollection services, string? systemCode = null)
        where TContributor : class, ISearchContributor
    {
        services.TryAddScoped<TContributor>();
        services.AddSingleton(new SearchContributorRegistration(typeof(TContributor), systemCode));
        return services;
    }
}
