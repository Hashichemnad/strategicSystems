using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.IntegrationTests.Identity;

public class AccessTokenAndSettingsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Personal_access_token_is_created_used_without_antiforgery_and_revoked()
    {
        var user = await api.Factory.CreateUserAsync();
        var session = await api.Factory.SignInAsync(user);

        var created = await session.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Reporting script", scopes = new[] { "api.read", "api.write" }, days = 30 });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var tokenId = body.GetProperty("details").GetProperty("id").GetGuid();
        token.ShouldStartWith("sst_");

        var bearer = api.Factory.CreateTokenClient(token);
        var me = await bearer.GetFromJsonAsync<JsonElement>("/api/v1/identity/me");
        me.GetProperty("userName").GetString().ShouldBe(user.UserName);
        me.GetProperty("authMethod").GetString().ShouldBe("pat");

        // State-changing call with a token needs no antiforgery token (no ambient cookie to forge).
        (await bearer.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "From a script" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        // A token can never mint another token or change the password.
        await (await bearer.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "x", scopes = new[] { "api.read" } }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.interactiveOnly");

        var listed = await session.GetFromJsonAsync<List<JsonElement>>("/api/v1/identity/me/tokens");
        listed!.Single().GetProperty("lastUsedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        (await session.PostAsync($"/api/v1/identity/me/tokens/{tokenId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bearer.GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Read_only_token_cannot_write_and_wrong_tokens_are_refused()
    {
        var session = await api.Factory.CreateSignedInClientAsync();
        var created = await session.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Read", scopes = new[] { "api.read" } });
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var bearer = api.Factory.CreateTokenClient(token);

        (await bearer.GetAsync("/api/v1/org/units/tree")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await bearer.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "x" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.tokenScope");

        var forged = token[..^4] + "aaaa";
        (await api.Factory.CreateTokenClient(forged).GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await api.Factory.CreateTokenClient("sst_garbage").GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_lifetime_is_capped_and_expired_tokens_stop_working()
    {
        var user = await api.Factory.CreateUserAsync();
        var session = await api.Factory.SignInAsync(user);

        await (await session.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Long", scopes = new[] { "api.read" }, days = 5000 }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var created = await session.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Soon expired", scopes = new[] { "api.read" }, days = 1 });
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        await using (var scope = api.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
            var stored = await db.Tokens.SingleAsync(t => t.UserId == user.Id);
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        (await api.Factory.CreateTokenClient(token).GetAsync("/api/v1/identity/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Administrator_sees_and_revokes_tokens_of_a_user_and_strangers_get_404()
    {
        var user = await api.Factory.CreateUserAsync();
        var session = await api.Factory.SignInAsync(user);
        var created = await session.PostAsJsonAsync("/api/v1/identity/me/tokens", new { name = "Mine", scopes = new[] { "api.read" } });
        var tokenId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details").GetProperty("id").GetGuid();

        var stranger = await api.UserClientAsync();
        await (await stranger.PostAsync($"/api/v1/identity/me/tokens/{tokenId}/revoke", null)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");

        var admin = await api.AdminClientAsync();
        (await admin.GetFromJsonAsync<List<JsonElement>>($"/api/v1/identity/admin/users/{user.Id}/tokens"))!.Count.ShouldBe(1);
        var revoked = await admin.PostAsync($"/api/v1/identity/admin/users/{user.Id}/tokens/{tokenId}/revoke", null);
        (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString().ShouldBe("revoked");
    }

    [Fact]
    public async Task Security_settings_change_the_password_policy_at_runtime()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var user = await factory.CreateUserAsync(); // created under the default policy
        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/admin/security-settings");
        settings.GetProperty("passwordMinLength").GetInt32().ShouldBe(12);

        var changed = JsonSerializer.Deserialize<Dictionary<string, object?>>(settings.GetRawText())!;
        changed["passwordMinLength"] = 20;
        (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", changed)).StatusCode.ShouldBe(HttpStatusCode.OK);
        changed["lockoutMinutes"] = 20; // a different change with the old version
        await (await admin.PutAsJsonAsync("/api/v1/identity/admin/security-settings", changed))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict");

        var policy = await (await factory.CreateSpaClientAsync()).GetFromJsonAsync<JsonElement>("/api/v1/identity/auth/password-policy");
        policy.GetProperty("minLength").GetInt32().ShouldBe(20);

        var client = await factory.SignInAsync(user);
        var weak = await client.PostAsJsonAsync("/api/v1/identity/me/password", new { currentPassword = user.Password, newPassword = "Only#Fifteen-15" });
        (await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed"))
            .GetProperty("errors")[0].GetProperty("params").GetProperty("length").GetInt32().ShouldBe(20);
    }

    [Fact]
    public async Task Smtp_password_is_stored_encrypted_and_never_returned()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/admin/smtp");
        const string secret = "Smtp-Secret-Value-123";

        var saved = await admin.PutAsJsonAsync("/api/v1/identity/admin/smtp", new
        {
            enabled = true,
            host = "mail.example.test",
            port = 587,
            security = "starttls",
            userName = "mailer",
            password = secret,
            fromAddress = "noreply@example.test",
            fromNameEn = "Strategy",
            publicBaseUrl = "https://strategy.example.test",
            version = current.GetProperty("version").GetUInt32(),
        });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var text = await saved.Content.ReadAsStringAsync();
        text.ShouldNotContain(secret);
        (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasPassword").GetBoolean().ShouldBeTrue();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT password_protected FROM identity.smtp_settings", connection);
        var stored = (string)(await command.ExecuteScalarAsync())!;
        stored.ShouldNotContain(secret);
        stored.Length.ShouldBeGreaterThan(secret.Length);
    }

    [Fact]
    public async Task Data_protection_keys_survive_a_restart_so_sessions_and_secrets_stay_valid()
    {
        var database = await api.Postgres.CreateDatabaseAsync();
        string cookieHeader;
        TestUser user;
        await using (var first = new ApiFactory(database))
        {
            user = await first.CreateUserAsync();
            var client = first.CreateClient(new() { HandleCookies = false });
            var token = await client.GetAsync("/api/v1/antiforgery/token");
            var cookies = token.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();
            var body = await token.Content.ReadFromJsonAsync<JsonElement>();
            var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/auth/login")
            {
                Content = JsonContent.Create(new { login = user.UserName, password = user.Password }),
            };
            login.Headers.Add("Cookie", string.Join("; ", cookies));
            login.Headers.Add("X-XSRF-TOKEN", body.GetProperty("token").GetString());
            var response = await client.SendAsync(login);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            cookieHeader = response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith("strategy.session=", StringComparison.Ordinal));
        }

        await using var second = new ApiFactory(database);
        var again = second.CreateClient(new() { HandleCookies = false });
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/me");
        me.Headers.Add("Cookie", cookieHeader);
        var result = await again.SendAsync(me);
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userName").GetString().ShouldBe(user.UserName);
    }
}
