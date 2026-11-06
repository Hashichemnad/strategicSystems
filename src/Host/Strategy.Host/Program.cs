using Strategy.Host;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Persistence;

// `--health-check`: container healthcheck without curl in the runtime image. Exit 0 when /health/live answers.
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/health/live");
}

// `--create-admin` on a two-role install (ConnectionStrings:StrategyMigrations set to another role, e.g. inside the `migrate` service):
// pending migrations are applied first as the owner, in a separate host, exactly like `--migrate`. The administrator itself is created
// below as the runtime role. Without an owner connection nothing is migrated here: `--create-admin` then requires `--migrate` first.
if (args.Contains("--create-admin", StringComparer.Ordinal))
{
    var probe = WebApplication.CreateBuilder(args);
    if (Bootstrap.HasSeparateOwnerConnection(probe.Configuration))
    {
        probe.Configuration["Database:UseMigrationsConnection"] = "true";
        probe.AddStrategyHost(PlatformCatalog.All, ModuleCatalog.All);
        await using var migrations = probe.Build();
        await migrations.Services.GetRequiredService<ModuleMigrator>().MigrateAllAsync();
    }
}

var builder = WebApplication.CreateBuilder(args);
if (args.Contains("--migrate", StringComparer.Ordinal) || args.Contains("--setup-db-roles", StringComparer.Ordinal))
{
    // Migrations run as the owner role when ConnectionStrings:StrategyMigrations is set (ADR 0018).
    builder.Configuration["Database:UseMigrationsConnection"] = "true";
}

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

// `--setup-db-roles` (upgrade step, ADR 0018): creates / updates the runtime role of ConnectionStrings:Strategy as the owner of
// ConnectionStrings:StrategyMigrations, then migrates and grants it read / write on module schemas and only SELECT + INSERT on audit.entry.
if (args.Contains("--setup-db-roles", StringComparer.Ordinal))
{
    return await Bootstrap.SetupDatabaseRolesAsync(app.Services, Console.Out);
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
