using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Host.Infrastructure;
using Strategy.Platform.Identity.Email;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// The real host on a fresh database. Environment <c>Testing</c> by default (modules open to signed-in users, ADR 0009);
/// pass <c>environment: "Production"</c> to test fail-closed behaviour. Email goes to <see cref="Emails"/>.
/// </summary>
public sealed class ApiFactory(string connectionString, string environment = "Testing", IDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    public static async Task<ApiFactory> CreateAsync(
        PostgresFixture postgres, string environment = "Testing", IDictionary<string, string?>? settings = null)
    {
        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), environment, settings);
        _ = factory.Server; // start now: runs the migrations
        return factory;
    }

    public string ConnectionString => connectionString;

    public CapturingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Strategy", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Modules:Sample:Enabled", "true");
        builder.UseSetting("Spa:RootPath", "does-not-exist");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityEmailSender>();
            services.AddSingleton<IIdentityEmailSender>(Emails);
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
