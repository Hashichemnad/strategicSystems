using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Links.Api;
using Strategy.Platform.Links.Application;
using Strategy.Platform.Links.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Links;

/// <summary>Links registry (<c>/api/v1/links</c>, schema <c>links</c>, ADR 0034, docs/design/links.md).</summary>
public sealed class LinksComponent : IPlatformComponent
{
    public string Name => "links";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<LinksDbContext>(LinksDbContext.SchemaName);
        services.AddScoped<ILinkRegistry, LinkRegistry>();
        services.AddScoped<ILinkStore, LinkStore>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => LinksEndpoints.Map(endpoints.MapPlatformGroup(this));
}
