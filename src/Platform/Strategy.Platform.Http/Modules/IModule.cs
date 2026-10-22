using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Http.Modules;

/// <summary>
/// A system plugged into the host. The host lists modules explicitly; each registers its own services
/// (DbContext, handlers, validators) and maps its endpoints under <c>/api/v1/&lt;Name&gt;</c>.
/// </summary>
public interface IModule
{
    /// <summary>Stable module code, used in routes, permissions and configuration (e.g. <c>portfolio</c>).</summary>
    string Name { get; }

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
