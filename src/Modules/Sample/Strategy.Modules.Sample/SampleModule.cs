using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Sample.Api;
using Strategy.Modules.Sample.Contracts;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Sample;

/// <summary>Template module (docs/design/sample.md). Copy this shape for a new system.</summary>
public sealed class SampleModule : IModule
{
    public string Name => "sample";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SampleDbContext>(SampleDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(SampleModule).Assembly);
        services.AddScoped<ISampleItemDirectory, SampleItemDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SampleEndpoints.Map(endpoints.MapModuleGroup(this));
}
