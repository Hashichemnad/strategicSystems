using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Modules.Requests.Contracts;

public static class RequestTypeRegistrationExtensions
{
    /// <summary>Declares a built-in request type (seeded once into <c>requests.request_type</c>, ADR 0080).</summary>
    public static IServiceCollection AddRequestType(this IServiceCollection services, RequestTypeRegistration registration) =>
        services.AddSingleton(registration);

    public static IServiceCollection AddRequestTypeHandler<THandler>(this IServiceCollection services)
        where THandler : class, IRequestTypeHandler =>
        services.AddScoped<IRequestTypeHandler, THandler>();
}
