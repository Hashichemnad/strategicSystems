using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Host.Infrastructure;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.IntegrationTests.Platform;

/// <summary>Phase 18 hardening (docs/security/asvs-l2.md): cookies, sessions, outbound URL policy (ADR 0155).</summary>
public class SecurityHardeningTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Session_and_antiforgery_cookies_are_http_only_same_site_and_secure_over_https()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var user = await factory.CreateUserAsync();
        var cookies = await SignInCookiesAsync(factory, new Uri("https://strategy.example.org"), user);

        var session = cookies.Single(c => c.StartsWith("strategy.session=", StringComparison.Ordinal));
        session.ShouldContain("httponly", Case.Insensitive);
        session.ShouldContain("samesite=lax", Case.Insensitive);
        session.ShouldContain("secure", Case.Insensitive);
        var antiforgery = cookies.First(c => c.StartsWith(AntiforgerySetup.CookieName + "=", StringComparison.Ordinal));
        antiforgery.ShouldContain("httponly", Case.Insensitive);
        antiforgery.ShouldContain("samesite=strict", Case.Insensitive);
        antiforgery.ShouldContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task With_secure_cookies_on_the_session_cookie_is_secure_even_on_a_plain_http_hop()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Security:SecureCookies"] = "true" });
        var user = await factory.CreateUserAsync();
        var cookies = await SignInCookiesAsync(factory, new Uri("http://localhost"), user);

        cookies.Single(c => c.StartsWith("strategy.session=", StringComparison.Ordinal)).ShouldContain("secure", Case.Insensitive);
    }

    [Fact]
    public async Task Sessions_end_after_the_idle_and_the_absolute_limit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var idleUser = await factory.CreateUserAsync();
        var idle = await factory.SignInAsync(idleUser);
        var absoluteUser = await factory.CreateUserAsync();
        var absolute = await factory.SignInAsync(absoluteUser);
        (await idle.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await absolute.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
            var now = DateTimeOffset.UtcNow;
            // Defaults (Settings → Security): 30 minutes idle, 12 hours absolute.
            await db.Sessions.Where(s => s.UserId == idleUser.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now.AddMinutes(-31)));
            await db.Sessions.Where(s => s.UserId == absoluteUser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, now.AddHours(-13)).SetProperty(x => x.LastSeenAt, now));
        }

        (await idle.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await absolute.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1:5432/")]
    [InlineData("http://localhost:8080/v1")]
    public async Task Ai_provider_endpoints_on_the_host_itself_or_the_metadata_service_are_refused(string endpoint)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Outbound:AllowLoopback"] = "false" });
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var response = await admin.PostAsJsonAsync("/api/v1/ai/providers", new
        {
            name = "Internal",
            type = "openai-compatible",
            endpoint,
            defaultModel = "m",
            models = new[] { "m" },
            maxOutputTokens = 1000,
            timeoutSeconds = 10,
            active = true,
        });
        await response.ShouldBeValidationProblemAsync("endpoint", "validation.outboundUrlRefused");
    }

    [Fact]
    public async Task Identity_provider_urls_and_ldap_hosts_follow_the_outbound_policy()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Outbound:AllowLoopback"] = "false" });
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var oidc = await admin.PostAsJsonAsync("/api/v1/identity/admin/providers", new
        {
            code = "meta",
            type = "oidc",
            displayNameEn = "Metadata",
            settings = new { authority = "https://169.254.169.254/realms/x", clientId = "c", allowInsecureHttp = false },
            secret = "s",
            matchField = "email",
        });
        await oidc.ShouldBeValidationProblemAsync("settings.authority", "validation.outboundUrlRefused");

        var ldap = await admin.PostAsJsonAsync("/api/v1/identity/admin/providers", new
        {
            code = "ldaploop",
            type = "ldap",
            displayNameEn = "Directory",
            settings = new { host = "127.0.0.1", port = 389, baseDn = "dc=x", userFilter = "(uid={0})" },
            matchField = "email",
        });
        await ldap.ShouldBeValidationProblemAsync("settings.host", "validation.outboundUrlRefused");
    }

    private static async Task<List<string>> SignInCookiesAsync(ApiFactory factory, Uri baseAddress, TestUser user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = baseAddress, HandleCookies = false });
        var token = await client.GetAsync("/api/v1/antiforgery/token");
        var cookies = token.Headers.GetValues("Set-Cookie").ToList();
        var body = (await token.Content.ReadFromJsonAsync<AntiforgeryToken>())!;
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/auth/login") { Content = JsonContent.Create(new { login = user.UserName, password = user.Password }) };
        login.Headers.Add(body.HeaderName, body.Token);
        login.Headers.Add("X-Client", "spa");
        login.Headers.Add("Cookie", string.Join("; ", cookies.Select(c => c.Split(';')[0])));
        var response = await client.SendAsync(login);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        cookies.AddRange(response.Headers.GetValues("Set-Cookie"));
        return cookies;
    }
}
