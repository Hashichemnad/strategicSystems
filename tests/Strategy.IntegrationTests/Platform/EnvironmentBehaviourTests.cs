using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Platform;

/// <summary>Behaviour that depends on configuration or environment; each test gets its own host.</summary>
public class EnvironmentBehaviourTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Production_module_access_fails_closed_for_signed_in_users_and_anonymous_calls_get_401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, environment: "Production");
        await (await (await factory.CreateSpaClientAsync()).GetAsync("/api/v1/sample/items"))
            .ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.required", "errors.unauthorized");
        var client = await factory.CreateSignedInClientAsync();

        await (await client.GetAsync("/api/v1/sample/items"))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden", "errors.forbidden");
        await (await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Denied" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Production_sends_hsts_over_https()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, environment: "Production");
        // ASP.NET Core never sends HSTS for localhost, so use a real-looking host name.
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://strategy.example.org") });

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldContain("max-age=31536000");
    }

    [Fact]
    public async Task Disabled_module_answers_404_module_disabled()
    {
        await using var factory = await ApiFactory.CreateAsync(
            postgres, settings: new Dictionary<string, string?> { ["Modules:Sample:Enabled"] = "false" });

        var client = await factory.CreateSignedInClientAsync();
        var response = await client.GetAsync("/api/v1/sample/items");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "module.disabled", "errors.moduleDisabled");
    }

    [Fact]
    public async Task Rate_limit_returns_429_problem_with_retry_after()
    {
        await using var factory = await ApiFactory.CreateAsync(
            postgres,
            settings: new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "3", ["RateLimiting:WindowSeconds"] = "60" });
        var client = factory.CreateClient();

        // Anonymous calls are refused (401) but still count against the limit.
        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/api/v1/sample/items")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await client.GetAsync("/api/v1/sample/items");
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "request.rateLimited", "errors.tooManyRequests");
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
