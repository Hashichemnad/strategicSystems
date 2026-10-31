using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings.Api;
using Strategy.Platform.Settings.Application;
using Strategy.Platform.Settings.Infrastructure;

namespace Strategy.Platform.Settings;

/// <summary>Settings centre backbone (<c>/api/v1/settings</c>, schema <c>settings</c>). docs/design/settings.md.</summary>
public sealed class SettingsComponent : IPlatformComponent
{
    public string Name => "settings";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SettingsDbContext>(SettingsDbContext.SchemaName);
        services.AddMemoryCache();
        services.AddSingleton<SettingsCache>();
        services.AddScoped<ISettingsStore, SettingsStore>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IFeatureFlags, FeatureFlags>();
        services.AddScoped<NumberingService>();
        services.AddScoped<INumberingService>(sp => sp.GetRequiredService<NumberingService>());
        services.AddScoped<SettingsAdministration>();
        services.AddScoped<IDatabaseSeeder, SettingsSeeder>();
        services.AddAccessCatalogue(new SettingsCatalogue());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SettingsEndpoints.Map(endpoints.MapPlatformGroup(this));
}
