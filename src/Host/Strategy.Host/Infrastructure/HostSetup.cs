using System.Text.Json.Serialization;
using Serilog;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Context;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Kernel;
using Strategy.Platform.Persistence;

namespace Strategy.Host.Infrastructure;

/// <summary>Composition root: services and the middleware pipeline, in one readable place.</summary>
internal static partial class HostSetup
{
    public const string TestingEnvironment = "Testing";

    public static WebApplicationBuilder AddStrategyHost(this WebApplicationBuilder builder, IReadOnlyList<IModule> modules)
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
        services.AddPlatformAccess(allowAllForDevelopment: IsDevelopmentLike(builder.Environment));

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

        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        return builder;
    }

    public static WebApplication UseStrategyPipeline(this WebApplication app, IReadOnlyList<IModule> modules)
    {
        WarnIfAccessPlaceholderAllowsAll(app);

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
        app.UseRateLimiter();
        app.UseMiddleware<AntiforgeryValidationMiddleware>();

        app.MapStrategyHealthChecks();
        app.MapStrategyOpenApi();
        app.MapAntiforgeryEndpoint();
        foreach (var module in modules)
        {
            module.MapEndpoints(app);
        }

        app.MapSpaFallback();
        return app;
    }

    public static bool IsDevelopmentLike(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment);

    private static void WarnIfAccessPlaceholderAllowsAll(WebApplication app)
    {
        if (app.Services.GetRequiredService<IAccessService>() is PlaceholderAccessService { AllowsAll: true })
        {
            LogPlaceholderAccess(app.Logger, app.Environment.EnvironmentName);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Access placeholder allows every request in the {Environment} environment (Phase 0, ADR 0005). Never use this in production.")]
    private static partial void LogPlaceholderAccess(Microsoft.Extensions.Logging.ILogger logger, string environment);
}
