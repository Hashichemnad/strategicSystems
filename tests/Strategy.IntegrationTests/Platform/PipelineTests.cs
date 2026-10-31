using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Platform;

public class PipelineTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/sample/items")]
    [InlineData("/api/v1/does-not-exist")]
    public async Task Security_headers_are_on_every_response(string path)
    {
        var response = await api.Factory.CreateClient().GetAsync(path);

        var headers = response.Headers;
        headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
        headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'self'");
        headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        headers.Contains("Permissions-Policy").ShouldBeTrue();
        headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var client = api.Factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        var checks = body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        checks.ShouldContain("db:sample");
        checks.ShouldContain("db:core");
        checks.ShouldContain("db:identity");
    }

    [Fact]
    public async Task OpenApi_document_is_published_with_module_paths_and_problem_schema()
    {
        var client = api.Factory.CreateClient(new() { AllowAutoRedirect = false });

        var redirect = await client.GetAsync("/openapi");
        redirect.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        redirect.Headers.Location!.ToString().ShouldBe("/openapi/v1.json");

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
        document.GetProperty("paths").TryGetProperty("/api/v1/sample/items", out _).ShouldBeTrue();
        document.GetProperty("components").GetProperty("schemas").TryGetProperty("Problem", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Correlation_id_is_echoed_when_safe_and_generated_otherwise()
    {
        var client = api.Factory.CreateClient();

        var echoed = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        echoed.Headers.Add("X-Correlation-Id", "client-supplied-1234");
        (await client.SendAsync(echoed)).Headers.GetValues("X-Correlation-Id").Single().ShouldBe("client-supplied-1234");

        var unsafeId = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        unsafeId.Headers.Add("X-Correlation-Id", "<script>");
        var generated = (await client.SendAsync(unsafeId)).Headers.GetValues("X-Correlation-Id").Single();
        generated.ShouldNotBe("<script>");
        generated.Length.ShouldBe(32);
    }

    [Fact]
    public async Task State_changing_call_without_antiforgery_token_is_refused()
    {
        var client = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        var response = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "No token" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "antiforgery.invalid", "errors.antiforgery");
    }

    [Fact]
    public async Task Antiforgery_token_from_another_session_is_refused()
    {
        var first = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var token = first.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single();
        var attacker = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        attacker.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        attacker.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

        var response = await attacker.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Forged" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public async Task Public_login_endpoint_also_requires_the_antiforgery_token()
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/v1/identity/auth/login", new { login = "x", password = "y" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "antiforgery.invalid");
    }

    [Fact]
    public async Task Unknown_api_route_is_a_404_problem_not_the_spa()
    {
        var response = await api.Factory.CreateClient().GetAsync("/api/v1/nothing-here");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "route.notFound", "errors.notFound");
    }
}
