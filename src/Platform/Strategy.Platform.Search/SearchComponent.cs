using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search.Api;
using Strategy.Platform.Search.Application;
using Strategy.Platform.Search.Infrastructure;

namespace Strategy.Platform.Search;

/// <summary>Global search (<c>/api/v1/search</c>, schema <c>search</c>, ADR 0160, docs/design/search.md).</summary>
public sealed class SearchComponent : IPlatformComponent
{
    public string Name => "search";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SearchDbContext>(SearchDbContext.SchemaName);
        services.AddScoped<SearchService>();
        services.AddAppTool<SearchTool>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SearchEndpoints.Map(endpoints.MapPlatformGroup(this));
}
