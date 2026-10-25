using Strategy.Host;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Persistence;

// `--health-check`: container healthcheck without curl in the runtime image. Exit 0 when /health/live answers.
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/health/live");
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.AddStrategyHost(PlatformCatalog.All, ModuleCatalog.All);

var app = builder.Build();

// `--migrate` applies every module's pending migrations and exits (install / upgrade step).
// Database:MigrateOnStartup does the same at startup and keeps running (development, tests).
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await app.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
    return 0;
}

// `--create-admin --email <e> --password-file <f>` creates the first system administrator and exits (install step).
if (args.Contains("--create-admin", StringComparer.Ordinal))
{
    return await Bootstrap.CreateAdminAsync(app.Services, args, Console.Out);
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await app.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Seed:Demo", defaultValue: false))
{
    await Bootstrap.SeedDevelopmentAsync(app.Services, app.Configuration, app.Logger);
}

app.UseStrategyPipeline(PlatformCatalog.All, ModuleCatalog.All);
await app.RunAsync();
return 0;

/// <summary>Entry point; public for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
