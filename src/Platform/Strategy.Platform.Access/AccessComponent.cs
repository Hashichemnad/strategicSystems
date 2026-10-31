using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access.Api;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Access;

/// <summary>Access control (<c>/api/v1/access</c>, schema <c>access</c>). docs/design/access.md.</summary>
public sealed class AccessComponent : IPlatformComponent
{
    public string Name => "access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AccessDbContext>(AccessDbContext.SchemaName);
        services.AddMemoryCache();
        services.AddSingleton<AccessModelCache>();
        services.AddScoped<UserAccessLoader>();
        services.AddScoped<AccessService>();
        services.AddScoped<IAccessService>(sp => sp.GetRequiredService<AccessService>());
        services.AddScoped<ISystemState, SystemState>();
        services.AddScoped<IRecordRoleService, RecordRoleService>();
        services.AddScoped<UserRoleQuery>();
        services.AddScoped<IUserRoleQuery>(sp => sp.GetRequiredService<UserRoleQuery>());
        services.AddScoped<PrincipalNames>();
        services.AddScoped<AccessAdministration>();
        services.AddScoped<AccessReporting>();
        services.AddScoped<IDatabaseSeeder, AccessSeeder>();
        services.AddAccessCatalogue(new AccessCatalogue());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AccessEndpoints.Map(endpoints.MapPlatformGroup(this));
}
