using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Access;

public static class AccessServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    /// <param name="allowModulesForDevelopment">True only for the Development and Testing environments (ADR 0009).</param>
    public static IServiceCollection AddPlatformAccess(this IServiceCollection services, bool allowModulesForDevelopment)
    {
        services.AddSingleton(new InterimAccessService(allowModulesForDevelopment));
        services.AddSingleton<IAccessService>(sp => sp.GetRequiredService<InterimAccessService>());
        return services;
    }
}
