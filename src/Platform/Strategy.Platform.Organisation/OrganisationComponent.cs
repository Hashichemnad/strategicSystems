using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Organisation.Api;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Import;
using Strategy.Platform.Organisation.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Organisation;

/// <summary>Organisation units, employees and delegations (<c>/api/v1/org</c>, schema <c>core</c>).</summary>
public sealed class OrganisationComponent : IPlatformComponent
{
    public string Name => "org";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrgDbContext>(OrgDbContext.SchemaName);
        services.AddRequestHandlersFrom(typeof(OrganisationComponent).Assembly);
        services.AddScoped<OrgStructure>();
        services.AddScoped<OrgReader>();
        services.AddScoped<UnitChange>();
        services.AddScoped<EmployeeRegister>();
        services.AddScoped<EmployeeReader>();
        services.AddScoped<EmployeeChange>();
        services.AddScoped<DelegationReader>();
        services.AddScoped<OrgImporter>();
        services.AddScoped<IOrgDirectory, OrgDirectory>();
        services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        services.AddScoped<IDelegationQuery, DelegationQuery>();
        services.AddSingleton<IDirectorySyncSource, NoDirectorySyncSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => OrgEndpoints.Map(endpoints.MapPlatformGroup(this));
}
