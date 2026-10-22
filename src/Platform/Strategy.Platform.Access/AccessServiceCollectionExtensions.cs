using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Access;

public static class AccessServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    /// <param name="allowAllForDevelopment">True only for the Development and Testing environments.</param>
    public static IServiceCollection AddPlatformAccess(this IServiceCollection services, bool allowAllForDevelopment)
    {
        services.AddSingleton<IAccessService>(new PlaceholderAccessService(allowAllForDevelopment));
        return services;
    }
}
