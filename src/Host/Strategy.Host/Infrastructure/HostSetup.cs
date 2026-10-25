using System.Text.Json.Serialization;
using Serilog;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Context;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Infrastructure;

/// <summary>Composition root: services and the middleware pipeline, in one readable place.</summary>
internal static partial class HostSetup
{
    public const string TestingEnvironment = "Testing";

    public static WebApplicationBuilder AddStrategyHost(
        this WebApplicationBuilder builder, IReadOnlyList<IPlatformComponent> components, IReadOnlyList<IModule> modules)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddSerilog((sp, logger) => logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(sp)
            .Enrich.FromLogContext());
        builder.AddStrategyObservability();

        services.AddPlatformKernel();
        services.AddPlatformPersistence(configuration);
        services.AddPlatformAccess(allowModulesForDevelopment: IsDevelopmentLike(builder.Environment));
        services.AddStrategyDataProtection(configuration);

        // Numbers are numbers: no quoted numbers on input, so the OpenAPI schema stays exact.
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
        services.AddStrategyProblemDetails();
        services.AddStrategyOpenApi();
        services.AddStrategyRateLimiting(configuration);
        services.AddStrategyAntiforgery();
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });
        services.AddHealthChecks();

        foreach (var component in components)
        {
            component.AddServices(services, configuration);
        }

        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        if (IsGeneratingOpenApiDocument)
        {
            // Build-time OpenAPI generation (ADR 0004) starts the host without a database: skip the background work.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hosted);
            }
        }

        return builder;
    }

    public static WebApplication UseStrategyPipeline(
        this WebApplication app, IReadOnlyList<IPlatformComponent> components, IReadOnlyList<IModule> modules)
    {
        WarnIfModuleAccessIsOpen(app);

        app.UseMiddleware<RequestContextMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (!IsDevelopmentLike(app.Environment))
        {
            app.UseHsts();
        }

        app.UseSpaStaticFiles();
        app.UseRouting();
        app.UseRateLimiter(); // before authentication: anonymous and refused calls count too
        app.UseAuthentication();
        app.UseMiddleware<AuthenticationRequirementMiddleware>();
        foreach (var component in components)
        {
            component.UseMiddleware(app);
        }

        app.UseMiddleware<AntiforgeryValidationMiddleware>();

        app.MapStrategyHealthChecks();
        app.MapStrategyOpenApi();
        app.MapAntiforgeryEndpoint();
        foreach (var component in components)
        {
            component.MapEndpoints(app);
        }

        foreach (var module in modules)
        {
            module.MapEndpoints(app);
        }

        app.MapSpaFallback();
        return app;
    }

    /// <summary>True when the build-time OpenAPI tool (<c>GetDocument.Insider</c>) is running the host.</summary>
    public static bool IsGeneratingOpenApiDocument =>
        string.Equals(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name, "GetDocument.Insider", StringComparison.Ordinal);

    public static bool IsDevelopmentLike(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);

    private static void WarnIfModuleAccessIsOpen(WebApplication app)
    {
        if (app.Services.GetRequiredService<InterimAccessService>().AllowsModulesForDevelopment)
        {
            LogInterimAccess(app.Logger, app.Environment.EnvironmentName);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Interim access lets every signed-in user use all modules in the {Environment} environment (ADR 0009). Never use this in production.")]
    private static partial void LogInterimAccess(Microsoft.Extensions.Logging.ILogger logger, string environment);
}
