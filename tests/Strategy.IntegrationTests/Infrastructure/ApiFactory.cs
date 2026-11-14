using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Notifications;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// The real host on a fresh database. Environment <c>Testing</c> by default (modules open to signed-in users, ADR 0009);
/// pass <c>environment: "Production"</c> to test fail-closed behaviour. Email goes to <see cref="Emails"/>.
/// </summary>
public sealed class ApiFactory(
    string connectionString, string environment = "Testing", IDictionary<string, string?>? settings = null, bool backgroundServices = true,
    bool realEmail = false, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public static async Task<ApiFactory> CreateAsync(
        PostgresFixture postgres, string environment = "Testing", IDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), environment, settings, configureServices: configureServices);
        _ = factory.Server; // start now: runs the migrations
        return factory;
    }

    public string ConnectionString => connectionString;

    // The started host's scheduler factory, taken while the host is alive: base.DisposeAsync disposes the host and then calls Dispose(true).
    private Quartz.ISchedulerFactory? _schedulers;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        _schedulers = host.Services.GetService<Quartz.ISchedulerFactory>();
        return host;
    }

    /// <summary>
    /// Stops the host as usual (its hosted service shuts Quartz down, waiting for running jobs), then shuts down any scheduler still running —
    /// e.g. after a stop that timed out — so no scheduler thread outlives the host or keeps the test process alive.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await StopSchedulersAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                StopSchedulersAsync().GetAwaiter().GetResult();
            }
        }
    }

    private async Task StopSchedulersAsync()
    {
        if (Interlocked.Exchange(ref _schedulers, null) is { } schedulers)
        {
            await TestSchedulers.ShutdownAsync(schedulers);
        }
    }

    /// <summary>This host's file store (local provider) in a temporary folder.</summary>
    public string FilesPath { get; } = Path.Combine(Path.GetTempPath(), "strategy-files-" + Guid.CreateVersion7().ToString("N"));

    public CapturingEmailTransport Emails { get; } = new();

    /// <summary>An ordinary (not mandatory) event, so tests can exercise preferences and the digest.</summary>
    public static readonly NotificationTemplateDefinition TestEventTemplate = new(
        "tests.event", "sample", "حدث تجريبي", "Test event", "حدث: {{subject}}", "Event: {{subject}}", "تفاصيل {{subject}}\n{{button}}",
        "Details of {{subject}}\n{{button}}", "فتح", "Open", ["subject"]);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Strategy", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Modules:Sample:Enabled", "true");
        builder.UseSetting("Modules:Portfolio:Enabled", "true");
        builder.UseSetting("Modules:Wasel:Enabled", "true");
        builder.UseSetting("Modules:Kpi:Enabled", "true");
        builder.UseSetting("Modules:Risk:Enabled", "true");
        builder.UseSetting("Modules:Cap:Enabled", "true");
        builder.UseSetting("Modules:Bench:Enabled", "true");
        builder.UseSetting("Modules:Requests:Enabled", "true");
        builder.UseSetting("Modules:DocReg:Enabled", "true");
        builder.UseSetting("Modules:Ip:Enabled", "true");
        builder.UseSetting("Mcp:RequireHttps", "false");
        builder.UseSetting("Spa:RootPath", "does-not-exist");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("RateLimiting:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        builder.UseSetting("Files:LocalPath", FilesPath);
        builder.UseSetting("Scheduling:SchedulerName", "test-" + Guid.CreateVersion7().ToString("N")); // Quartz names are per process
        builder.UseSetting("Scheduling:RunSchedules", "false"); // tests run jobs explicitly; cron firings would race them
        builder.UseSetting("Outbound:AllowLoopback", "true"); // identity-provider and fake provider containers listen on localhost (ADR 0155)
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddNotificationTemplate(TestEventTemplate);
            configureServices?.Invoke(services);
            if (!realEmail)
            {
                services.RemoveAll<IEmailTransport>();
                services.AddSingleton<IEmailTransport>(Emails);
            }
            if (!backgroundServices)
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                    && d.ImplementationType?.Assembly.GetName().Name is { } name
                    && (name.StartsWith("Strategy.", StringComparison.Ordinal) || name.StartsWith("Quartz", StringComparison.Ordinal))).ToList())
                {
                    services.Remove(hosted);
                }
            }
        });
    }

    /// <summary>A client that behaves like the SPA: holds the antiforgery cookie and sends the token header.</summary>
    public async Task<HttpClient> CreateSpaClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await client.GetFromJsonAsync<AntiforgeryToken>("/api/v1/antiforgery/token");
        client.DefaultRequestHeaders.Add(token!.HeaderName, token.Token);
        client.DefaultRequestHeaders.Add("X-Client", "spa");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
