using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Kernel.Validation;

namespace Strategy.Platform.Kernel;

public static class KernelServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, the default pipeline (logging → validation), clock and request context.</summary>
    public static IServiceCollection AddPlatformKernel(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<RequestContext>();
        services.TryAddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());
        services.TryAddScoped<IDispatcher, Dispatcher>();

        // Order matters: first registered = outermost.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    /// <summary>Registers every request handler and FluentValidation validator found in a module's assembly.</summary>
    public static IServiceCollection AddRequestHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>) || definition == typeof(IValidator<>))
                {
                    services.AddScoped(contract, type);
                }
            }
        }

        return services;
    }
}
