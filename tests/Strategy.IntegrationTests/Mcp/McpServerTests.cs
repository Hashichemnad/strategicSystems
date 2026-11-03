using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Mcp.Application;

namespace Strategy.IntegrationTests.Mcp;

/// <summary>
/// The MCP server end to end (docs/design/mcp.md §6): off by default, tokens only, scope <c>mcp</c>, vendors refused, tools listed by switches
/// and permissions, calls with the token owner's permissions (404 semantics), approve tools off by default, rate limits, audit channel
/// <c>mcp</c>, and the OAuth 2.1 flow (registration → authorization with PKCE and consent → token → refresh).
/// </summary>
public sealed class McpServerTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Items = "/api/v1/sample/items";

    [Fact]
    public async Task Server_is_off_by_default_and_accepts_only_tokens_with_the_mcp_scope()
    {
        await SetSettingsAsync(s => s with { Enabled = false });
        var user = await api.Factory.CreateUserAsync();
        var token = await TokenAsync(user.Id, TokenScopes.Mcp);
        (await Post(api.Factory.CreateTokenClient(token))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetSettingsAsync(s => s with { Enabled = true });
        var anonymous = await Post(api.Factory.CreateClient());
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.ToString().ShouldContain("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"");

        // A browser session is not enough: tokens only.
        var session = await api.Factory.SignInAsync(user);
        (await Post(session)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A personal access token without the mcp scope is refused; with it the server answers.
        var apiOnly = await TokenAsync(user.Id, TokenScopes.ApiRead);
        await (await Post(api.Factory.CreateTokenClient(apiOnly))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.tokenScope");
        await using var client = await McpAsync(token);
        (await client.ListToolsAsync()).ShouldNotBeEmpty();

        var metadata = await api.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");
        metadata.GetProperty("resource").GetString().ShouldBe("http://localhost/mcp");
    }

    [Fact]
    public async Task Vendor_principals_are_refused()
    {
        await SetSettingsAsync(s => s with { Enabled = true });
        var vendor = await api.Factory.CreateUserAsync(withEmployee: false);
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
            var entity = (await users.FindByIdAsync(vendor.Id.ToString()))!;
            entity.PrincipalType = StrategyClaims.VendorPrincipal;
            (await users.UpdateAsync(entity)).Succeeded.ShouldBeTrue();
        }

        var token = await TokenAsync(vendor.Id, TokenScopes.Mcp, TokenScopes.ApiWrite);
        await (await Post(api.Factory.CreateTokenClient(token))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mcp.vendorRefused");
        var ai = await api.Factory.CreateTokenClient(token).PostAsJsonAsync("/api/v1/ai/translate", new { text = "x", from = "en", to = "ar" });
        ai.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tools_run_with_the_token_owners_permissions_and_every_call_is_audited()
    {
        await SetSettingsAsync(s => s with { Enabled = true });
        var marker = Guid.CreateVersion7().ToString("N")[^6..];
        var ownRole = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.Own), (SamplePermissions.Create, DataScope.All));
        var alice = await api.Factory.CreateUserAsync(roles: ownRole);
        var bob = await api.Factory.CreateUserAsync(roles: ownRole);
        foreach (var u in new[] { alice, bob })
        {
            await TestAccess.GrantSystemAccessAsync(api.Factory, "sample", "user", u.Id);
        }

        var mine = await CreateItemAsync(await api.Factory.SignInAsync(alice), $"MCP plan {marker}");
        var theirs = await CreateItemAsync(await api.Factory.SignInAsync(bob), $"MCP plan {marker} of Bob");
        await using var client = await McpAsync(await TokenAsync(alice.Id, TokenScopes.Mcp));

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        tools.ShouldContain("whoami");
        tools.ShouldContain("search_records");
        tools.ShouldContain("record_status");
        tools.ShouldContain("get_record");
        tools.ShouldContain("my_pending_approvals");
        tools.ShouldContain("my_notifications");
        tools.ShouldNotContain("approval_decide"); // approve tools are never enabled by default

        var whoami = await CallAsync(client, "whoami");
        whoami.GetProperty("userId").GetGuid().ShouldBe(alice.Id);

        var search = await CallAsync(client, "search_records", new() { ["query"] = marker });
        search.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ShouldBe([mine]);

        (await CallAsync(client, "get_record", new() { ["resourceType"] = "sample.item", ["id"] = mine.ToString() }))
            .GetProperty("id").GetGuid().ShouldBe(mine);
        var hidden = await client.CallToolAsync("get_record", new Dictionary<string, object?> { ["resourceType"] = "sample.item", ["id"] = theirs.ToString() });
        hidden.IsError.ShouldBe(true);
        Text(hidden).ShouldContain("not_found");
        var status = await client.CallToolAsync("record_status", new Dictionary<string, object?> { ["resourceType"] = "sample.item", ["id"] = theirs.ToString() });
        status.IsError.ShouldBe(true);
        Text(status).ShouldContain("not_found");
        (await CallAsync(client, "record_status", new() { ["resourceType"] = "sample.item", ["id"] = mine.ToString() }))
            .GetProperty("messageKey").GetString().ShouldNotBeNullOrEmpty();
        (await CallAsync(client, "my_pending_approvals")).GetProperty("approvals").GetArrayLength().ShouldBe(0);
        (await CallAsync(client, "my_notifications")).TryGetProperty("items", out _).ShouldBeTrue();

        // Every call is in the audit trail with channel mcp, the user and the client (token prefix).
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var audit = (await scope.ServiceProvider.GetRequiredService<IAuditQuery>()
            .SearchAsync(new AuditFilter(UserId: alice.Id, Action: "mcp.tool.call", Channel: Channels.Mcp, ExactAction: true), new PageRequest(1, 100))).Items;
        audit.Count.ShouldBeGreaterThanOrEqualTo(8);
        audit.ShouldContain(e => e.RecordId == "search_records" && e.Data!.Contains(marker, StringComparison.Ordinal) && e.Data.Contains("pat:", StringComparison.Ordinal));
        audit.ShouldContain(e => e.RecordId == "get_record" && e.Success == false);
    }

    [Fact]
    public async Task Administrators_switch_tools_and_limits_and_approve_tools_still_check_the_approver()
    {
        await SetSettingsAsync(s => s with { Enabled = true }); // independent of the order the class's tests run in
        var user = await api.Factory.CreateUserAsync();
        await using var client = await McpAsync(await TokenAsync(user.Id, TokenScopes.Mcp));
        try
        {
            await SetSettingsAsync(s => s with
            {
                Enabled = true,
                Tools = new Dictionary<string, McpToolSettings>
                {
                    ["approval_decide"] = new(Enabled: true),
                    ["search_records"] = new(Enabled: false),
                    ["whoami"] = new(RateLimitPerMinute: 2),
                },
            });
            var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
            tools.ShouldContain("approval_decide");
            tools.ShouldNotContain("search_records");
            var disabled = await client.CallToolAsync("search_records", new Dictionary<string, object?> { ["query"] = "x" });
            disabled.IsError.ShouldBe(true);
            Text(disabled).ShouldContain("disabled");

            // Enabled by an administrator, the approve tool still acts only on the caller's own tasks.
            var decide = await client.CallToolAsync("approval_decide", new Dictionary<string, object?> { ["taskId"] = Guid.CreateVersion7().ToString(), ["action"] = "approve" });
            decide.IsError.ShouldBe(true);
            Text(decide).ShouldContain("not_found");

            (await client.CallToolAsync("whoami")).IsError.ShouldNotBe(true);
            (await client.CallToolAsync("whoami")).IsError.ShouldNotBe(true);
            var limited = await client.CallToolAsync("whoami");
            limited.IsError.ShouldBe(true);
            Text(limited).ShouldContain("rate_limited");
        }
        finally
        {
            await SetSettingsAsync(s => s with { Enabled = true, Tools = null });
        }
    }

    [Fact]
    public async Task Oauth_registration_authorization_with_pkce_and_consent_gives_a_token_for_mcp_only()
    {
        await SetSettingsAsync(s => s with { Enabled = true, AllowDynamicRegistration = true });
        var anonymous = api.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var discovery = await anonymous.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");
        discovery.GetProperty("registration_endpoint").GetString().ShouldBe("http://localhost/connect/register");
        discovery.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).ShouldContain("S256");

        // Dynamic registration: redirect hosts are checked against the allowed list.
        (await anonymous.PostAsJsonAsync("/connect/register", new { client_name = "Evil", redirect_uris = new[] { "https://evil.example/cb" } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var registered = await anonymous.PostAsJsonAsync("/connect/register",
            new { client_name = "Claude Code", redirect_uris = new[] { "http://127.0.0.1:33418/callback" }, token_endpoint_auth_method = "none" });
        registered.StatusCode.ShouldBe(HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
        var clientId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("client_id").GetString()!;

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string redirect = "http://127.0.0.1:33418/callback";
        var authorize = $"/connect/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirect)}&response_type=code&scope=mcp" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=xyz&resource={Uri.EscapeDataString("http://localhost/mcp")}";

        // Not signed in: to the login page, coming back afterwards.
        var toLogin = await anonymous.GetAsync(authorize);
        toLogin.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        toLogin.Headers.Location!.ToString().ShouldStartWith("/login?redirect=");

        // Signed in: the consent page; consent gives a ticket bound to the request; the code follows.
        var user = await api.Factory.CreateUserAsync();
        var browser = await SignInWithoutRedirectsAsync(user);
        var toConsent = await browser.GetAsync(authorize);
        toConsent.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        toConsent.Headers.Location!.ToString().ShouldStartWith("/oauth/consent?");
        var info = await browser.GetFromJsonAsync<McpConsentInfoDto>($"/api/v1/mcp/consent?clientId={clientId}&redirectUri={Uri.EscapeDataString(redirect)}");
        info!.ClientName.ShouldBe("Claude Code");
        info.RedirectHost.ShouldBe("127.0.0.1");
        var ticket = (await (await browser.PostAsJsonAsync("/api/v1/mcp/consent", new { clientId, redirectUri = redirect, codeChallenge = challenge }))
            .Content.ReadFromJsonAsync<McpConsentTicketDto>())!.Ticket;

        // A ticket for another PKCE challenge does not authorize this request.
        var other = authorize.Replace(challenge, Base64Url(SHA256.HashData("other"u8.ToArray())), StringComparison.Ordinal);
        (await browser.GetAsync(other + "&consent_ticket=" + Uri.EscapeDataString(ticket))).Headers.Location!.ToString().ShouldStartWith("/oauth/consent?");

        var granted = await browser.GetAsync(authorize + "&consent_ticket=" + Uri.EscapeDataString(ticket));
        granted.StatusCode.ShouldBe(HttpStatusCode.Redirect, await granted.Content.ReadAsStringAsync());
        var callback = granted.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).ShouldBe(redirect);
        var query = System.Web.HttpUtility.ParseQueryString(callback.Query);
        query["state"].ShouldBe("xyz");
        var code = query["code"]!;

        // Token exchange needs the verifier.
        var badExchange = await anonymous.PostAsync("/connect/token", Form(("grant_type", "authorization_code"), ("code", code), ("redirect_uri", redirect),
            ("client_id", clientId), ("code_verifier", Base64Url(RandomNumberGenerator.GetBytes(32)))));
        badExchange.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var granted2 = await browser.GetAsync(authorize + "&consent_ticket=" + Uri.EscapeDataString(ticket)); // remembered consent: straight to the code
        code = System.Web.HttpUtility.ParseQueryString(granted2.Headers.Location!.Query)["code"]!;
        var exchange = await anonymous.PostAsync("/connect/token", Form(("grant_type", "authorization_code"), ("code", code), ("redirect_uri", redirect),
            ("client_id", clientId), ("code_verifier", verifier)));
        exchange.StatusCode.ShouldBe(HttpStatusCode.OK, await exchange.Content.ReadAsStringAsync());
        var tokens = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        var refreshToken = tokens.GetProperty("refresh_token").GetString()!;

        // The OAuth token reaches /mcp as the user, and never the REST API.
        await using (var mcp = await McpAsync(accessToken))
        {
            (await CallAsync(mcp, "whoami")).GetProperty("userId").GetGuid().ShouldBe(user.Id);
        }

        await (await api.Factory.CreateTokenClient(accessToken).GetAsync("/api/v1/identity/me")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "auth.tokenScope");

        var refreshed = await anonymous.PostAsync("/connect/token", Form(("grant_type", "refresh_token"), ("refresh_token", refreshToken), ("client_id", clientId)));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());

        // An administrator revokes the client: its tokens stop working.
        var admin = await api.AdminClientAsync();
        (await admin.GetFromJsonAsync<List<McpClientDto>>("/api/v1/mcp/clients"))!.ShouldContain(c => c.ClientId == clientId);
        (await admin.PostAsync($"/api/v1/mcp/clients/{clientId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Post(api.Factory.CreateTokenClient(accessToken))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivated_users_lose_their_oauth_and_token_access_at_once()
    {
        await SetSettingsAsync(s => s with { Enabled = true });
        var user = await api.Factory.CreateUserAsync();
        var token = await TokenAsync(user.Id, TokenScopes.Mcp);
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var users = scope.ServiceProvider.GetRequiredService<StrategyUserManager>();
            var entity = (await users.FindByIdAsync(user.Id.ToString()))!;
            entity.Active = false;
            await users.UpdateAsync(entity);
        }

        (await Post(api.Factory.CreateTokenClient(token))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task SetSettingsAsync(Func<McpSettings, McpSettings> change)
    {
        var admin = await api.AdminClientAsync();
        var current = (await admin.GetFromJsonAsync<McpSettingsDto>("/api/v1/mcp/settings"))!;
        var saved = await admin.PutAsJsonAsync("/api/v1/mcp/settings", new McpSettingsInput(change(current.Settings), current.Version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private async Task<string> TokenAsync(Guid userId, params string[] scopes)
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        var created = await scope.ServiceProvider.GetRequiredService<AccessTokenService>().CreateAsync(userId, "mcp test", scopes, 1, CancellationToken.None);
        return created.Value.Token;
    }

    private async Task<McpClient> McpAsync(string token)
    {
        var http = api.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?>? args = null)
    {
        var result = await client.CallToolAsync(tool, args ?? []);
        result.IsError.ShouldNotBe(true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement.Clone();
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static Task<HttpResponseMessage> Post(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return client.SendAsync(request);
    }

    private static async Task<Guid> CreateItemAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(Items, new { nameEn = name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!.Id;
    }

    private async Task<HttpClient> SignInWithoutRedirectsAsync(TestUser user)
    {
        var client = api.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        client.DefaultRequestHeaders.Add(token.GetProperty("headerName").GetString()!, token.GetProperty("token").GetString());
        client.DefaultRequestHeaders.Add("X-Client", "spa");
        (await client.LoginAsync(user.UserName, user.Password)).ShouldBe(SignInStatuses.SignedIn);
        await client.RefreshAntiforgeryAsync();
        return client;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
