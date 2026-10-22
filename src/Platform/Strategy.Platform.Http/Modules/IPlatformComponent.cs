using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Http.Modules;

/// <summary>
/// A platform service with its own tables and endpoints (Organisation, Identity, …). Unlike an <see cref="IModule"/>
/// it is always on: no enable switch (ADR 0010). Endpoints live under <c>/api/v1/&lt;Name&gt;</c>.
/// </summary>
public interface IPlatformComponent
{
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Middleware the component needs right after authentication (optional).</summary>
    void UseMiddleware(IApplicationBuilder app)
    {
    }

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public static class PlatformEndpointExtensions
{
    /// <summary>The component's route group <c>/api/v1/&lt;name&gt;</c>, tagged with its name.</summary>
    public static RouteGroupBuilder MapPlatformGroup(this IEndpointRouteBuilder endpoints, IPlatformComponent component) =>
        endpoints.MapGroup($"/api/v1/{component.Name}").WithTags(component.Name);
}
