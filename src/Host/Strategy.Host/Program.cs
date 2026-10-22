using Strategy.Host;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
builder.AddStrategyHost(ModuleCatalog.All);

var app = builder.Build();

// `--migrate` applies every module's pending migrations and exits (install / upgrade step).
// Database:MigrateOnStartup does the same at startup and keeps running (development, tests).
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await app.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await app.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
}

app.UseStrategyPipeline(ModuleCatalog.All);
await app.RunAsync();

/// <summary>Entry point; public for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;
