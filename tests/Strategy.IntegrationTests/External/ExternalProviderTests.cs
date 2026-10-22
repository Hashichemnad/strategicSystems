using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.External;

/// <summary>
/// End-to-end sign-in against real providers (acceptance check: "OIDC and SAML providers can be configured and tested
/// against a test IdP; LDAP bind tested against a container"). Providers are configured only through the admin API,
/// exactly as an administrator does it in the UI.
/// </summary>
[Trait("Category", "ExternalIdP")]
public partial class ExternalProviderTests(IdentityProviderContainers idp) : IClassFixture<IdentityProviderContainers>
{
    private const string Providers = "/api/v1/identity/admin/providers";

    [Fact]
    public async Task Openid_connect_provider_is_configured_tested_enabled_and_signs_in_by_email_link()
    {
        var admin = await AdminAsync();
        await EnsureEmployeeAsync(admin, "kc.user@example.test", "KC-0001");
        var provider = await CreateProviderAsync(admin, new
        {
            code = "kc",
            type = "oidc",
            displayNameAr = "كيكلوك",
            displayNameEn = "Keycloak",
            sort = 1,
            autoLink = true,
            jitCreateUser = true,
            secret = "oidc-client-secret",
            settings = new { authority = idp.RealmUrl, clientId = "strategy-oidc", allowInsecureHttp = true },
        });

        var test = await admin.PostAsJsonAsync($"{Providers}/{provider}/test", new { });
        var result = await test.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("ok").GetBoolean().ShouldBeTrue(result.ToString());
        result.GetProperty("messageKey").GetString().ShouldBe("admin.providers.test.discoveryOk");

        // Disabled by default: not offered, challenge refused.
        var anonymous = await idp.Factory.CreateSpaClientAsync();
        (await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/v1/identity/auth/providers"))!.ShouldNotContain(p => p.GetProperty("code").GetString() == "kc");
        (await anonymous.GetAsync("/api/v1/identity/external/kc/challenge")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await admin.PostAsync($"{Providers}/{provider}/enable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetFromJsonAsync<List<JsonElement>>("/api/v1/identity/auth/providers"))!.ShouldContain(p => p.GetProperty("code").GetString() == "kc");

        var browser = Browser();
        var landing = await RedirectSignInAsync(browser, "kc", "kc.user");
        landing.ShouldBe("/org/units");
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/v1/identity/me");
        me.GetProperty("email").GetString().ShouldBe("kc.user@example.test");
        me.GetProperty("authMethod").GetString().ShouldBe("ext");

        // Second time: the stored link is used.
        (await RedirectSignInAsync(Browser(), "kc", "kc.user")).ShouldBe("/org/units");

        // Nobody matches this person: no account is created, the login page shows why.
        (await RedirectSignInAsync(Browser(), "kc", "kc.visitor")).ShouldBe("/login?error=auth.errors.externalNoMatch");
    }

    [Fact]
    public async Task Uae_pass_flow_uses_userinfo_claims_and_refuses_disallowed_user_types()
    {
        var admin = await AdminAsync();
        await EnsureEmployeeAsync(admin, "kc.user@example.test", "KC-0001");
        await EnsureEmployeeAsync(admin, "kc.visitor@example.test", "KC-0002");
        var endpoints = $"{idp.RealmUrl}/protocol/openid-connect";
        var provider = await CreateProviderAsync(admin, new
        {
            code = "uaepass-test",
            type = "uaepass",
            displayNameAr = "الهوية الرقمية",
            displayNameEn = "UAE Pass",
            sort = 2,
            autoLink = true,
            jitCreateUser = true,
            secret = "uaepass-client-secret",
            settings = new
            {
                environment = "staging",
                clientId = "strategy-uaepass",
                scopes = "openid email profile",
                authorizationEndpoint = $"{endpoints}/auth",
                tokenEndpoint = $"{endpoints}/token",
                userInfoEndpoint = $"{endpoints}/userinfo",
                allowInsecureHttp = true,
            },
        });
        (await admin.PostAsync($"{Providers}/{provider}/test", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"{Providers}/{provider}/enable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var browser = Browser();
        (await RedirectSignInAsync(browser, "uaepass-test", "kc.user")).ShouldBe("/org/units");
        (await browser.GetFromJsonAsync<JsonElement>("/api/v1/identity/me")).GetProperty("email").GetString().ShouldBe("kc.user@example.test");

        // SOP1 (visitor) is not in the allowed user types (SOP2, SOP3).
        (await RedirectSignInAsync(Browser(), "uaepass-test", "kc.visitor")).ShouldBe("/login?error=auth.errors.externalUserType");
    }

    [Fact]
    public async Task Saml_provider_reads_metadata_and_signs_in_with_a_signed_assertion()
    {
        var admin = await AdminAsync();
        await EnsureEmployeeAsync(admin, "kc.user@example.test", "KC-0001");
        var provider = await CreateProviderAsync(admin, new
        {
            code = "kc-saml",
            type = "saml2",
            displayNameAr = "سامل",
            displayNameEn = "Corporate SAML",
            sort = 3,
            autoLink = true,
            jitCreateUser = true,
            matchField = "employee_no",
            settings = new
            {
                idpEntityId = idp.RealmUrl,
                metadataUrl = $"{idp.RealmUrl}/protocol/saml/descriptor",
                allowInsecureHttp = true,
                claimEmail = "email",
                claimEmployeeNo = "employeeNumber",
            },
        });
        var test = await (await admin.PostAsync($"{Providers}/{provider}/test", null)).Content.ReadFromJsonAsync<JsonElement>();
        test.GetProperty("messageKey").GetString().ShouldBe("admin.providers.test.metadataOk");
        (await admin.PostAsync($"{Providers}/{provider}/enable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var details = await admin.GetFromJsonAsync<JsonElement>($"{Providers}/{provider}");
        details.GetProperty("callbackPath").GetString().ShouldBe("/saml2/kc-saml/Acs");
        var metadata = await Browser().GetStringAsync("/saml2/kc-saml");
        metadata.ShouldContain("urn:strategy:sp:kc-saml");

        var browser = Browser();
        (await RedirectSignInAsync(browser, "kc-saml", "kc.user", saml: true)).ShouldBe("/org/units");
        (await browser.GetFromJsonAsync<JsonElement>("/api/v1/identity/me")).GetProperty("email").GetString().ShouldBe("kc.user@example.test");
    }

    [Fact]
    public async Task Ldap_directory_signs_in_with_the_login_form_and_counts_wrong_passwords()
    {
        var admin = await AdminAsync();
        await EnsureEmployeeAsync(admin, "ldap.user@example.test", "LDAP-0001");
        var provider = await CreateProviderAsync(admin, new
        {
            code = "corp-ldap",
            type = "ldap",
            displayNameAr = "الدليل",
            displayNameEn = "Directory",
            sort = 4,
            autoLink = true,
            jitCreateUser = true,
            matchField = "employee_no",
            secret = IdentityProviderContainers.LdapAdminPassword,
            settings = new
            {
                host = "localhost",
                port = idp.LdapPort,
                security = "none",
                bindDn = "cn=admin,dc=example,dc=test",
                baseDn = "ou=people,dc=example,dc=test",
                userFilter = "(uid={0})",
            },
        });

        var test = await (await admin.PostAsJsonAsync($"{Providers}/{provider}/test", new { testUserName = "ldap.user" })).Content.ReadFromJsonAsync<JsonElement>();
        test.GetProperty("ok").GetBoolean().ShouldBeTrue(test.ToString());
        test.GetProperty("details").GetProperty("employeeNo").GetString().ShouldBe("LDAP-0001");

        var client = await idp.Factory.CreateSpaClientAsync();
        (await client.LoginAsync("ldap.user", IdentityProviderContainers.LdapPassword)).ShouldBeNull(); // still disabled

        (await admin.PostAsync($"{Providers}/{provider}/enable", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.LoginAsync("ldap.user", "wrong-password")).ShouldBeNull();
        (await client.LoginAsync("ldap.user", string.Empty)).ShouldBeNull();
        (await client.LoginAsync("*)(uid=*", IdentityProviderContainers.LdapPassword)).ShouldBeNull(); // filter injection finds nobody
        (await client.LoginAsync("ldap.stranger", IdentityProviderContainers.LdapPassword)).ShouldBeNull(); // no matching employee

        (await client.LoginAsync("ldap.user", IdentityProviderContainers.LdapPassword)).ShouldBe("signedIn");

        // Once linked, wrong directory passwords count against the account (lockout).
        var other = await idp.Factory.CreateSpaClientAsync();
        (await other.LoginAsync("ldap.user", "wrong-again")).ShouldBeNull();
        await client.RefreshAntiforgeryAsync();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me");
        me.GetProperty("email").GetString().ShouldBe("ldap.user@example.test");
        me.GetProperty("authMethod").GetString().ShouldBe("ldap");
        me.GetProperty("hasPassword").GetBoolean().ShouldBeFalse();

        var users = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/admin/users?search=ldap.user");
        var user = users.GetProperty("items")[0];
        user.GetProperty("externalLogins").EnumerateArray().Select(l => l.GetString()).ShouldBe(["corp-ldap"]);
        (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/identity/admin/auth-events?userId={user.GetProperty("id").GetGuid()}&type=login.failed"))
            .GetProperty("totalCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Provider_secrets_are_never_returned()
    {
        var admin = await AdminAsync();
        var provider = await CreateProviderAsync(admin, new
        {
            code = "secret-check",
            type = "oidc",
            displayNameEn = "Secret check",
            secret = "do-not-return-me",
            settings = new { authority = "https://login.example.test", clientId = "x" },
        });

        var body = await admin.GetStringAsync($"{Providers}/{provider}");
        body.ShouldNotContain("do-not-return-me");
        JsonDocument.Parse(body).RootElement.GetProperty("hasSecret").GetBoolean().ShouldBeTrue();
        JsonDocument.Parse(body).RootElement.GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private HttpClient? _admin;

    private async Task<HttpClient> AdminAsync() => _admin ??= await idp.Factory.CreateSignedInClientAsync(admin: true);

    private HttpClient Browser() =>
        idp.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<Guid> CreateProviderAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync(Providers, body);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var code = JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty("code").GetString();
            var existing = await admin.GetFromJsonAsync<List<JsonElement>>(Providers);
            return existing!.Single(p => p.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task EnsureEmployeeAsync(HttpClient admin, string email, string employeeNo)
    {
        var existing = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/org/employees?search={Uri.EscapeDataString(employeeNo)}");
        if (existing.GetProperty("totalCount").GetInt32() > 0)
        {
            return;
        }

        var unit = await admin.PostAsJsonAsync("/api/v1/org/units", new
        {
            code = "IDP-" + Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant(),
            nameEn = "IdP unit",
            levelCode = "organisation",
        });
        var unitId = (await unit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var created = await admin.PostAsJsonAsync("/api/v1/org/employees", new { employeeNo, nameEn = email, email, unitId });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Plays the browser: challenge → Keycloak login form → submit credentials → back to the application (OIDC redirect,
    /// or the SAML auto-post form) → external callback. Returns where the application finally sends the browser.
    /// </summary>
    private async Task<string> RedirectSignInAsync(HttpClient browser, string providerCode, string userName, bool saml = false)
    {
        var challenge = await browser.GetAsync($"/api/v1/identity/external/{providerCode}/challenge?returnUrl=%2Forg%2Funits");
        challenge.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.SeeOther);
        var keycloakUrl = challenge.Headers.Location!;
        keycloakUrl.ToString().ShouldStartWith(idp.KeycloakBase);

        // Keycloak marks its cookies Secure even on plain HTTP, which CookieContainer would not send back: keep them by hand.
        using var keycloak = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);
        var page = await SendWithCookiesAsync(keycloak, jar, new HttpRequestMessage(HttpMethod.Get, keycloakUrl));
        var loginPage = await page.Content.ReadAsStringAsync();
        var action = WebUtility.HtmlDecode(FormAction().Match(loginPage).Groups["action"].Value);
        action.ShouldNotBeNullOrEmpty(loginPage);
        var submitted = await SendWithCookiesAsync(keycloak, jar, new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = userName,
                ["password"] = IdentityProviderContainers.KeycloakPassword,
                ["credentialId"] = string.Empty,
            }),
        });

        // Keycloak may redirect within its own pages before answering (e.g. the SAML auto-post form).
        while (submitted.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther
            && submitted.Headers.Location!.IsAbsoluteUri && submitted.Headers.Location.ToString().StartsWith(idp.KeycloakBase, StringComparison.Ordinal))
        {
            submitted = await SendWithCookiesAsync(keycloak, jar, new HttpRequestMessage(HttpMethod.Get, submitted.Headers.Location));
        }

        HttpResponseMessage back;
        if (saml)
        {
            submitted.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = await submitted.Content.ReadAsStringAsync();
            var acs = new Uri(WebUtility.HtmlDecode(FormAction().Match(html).Groups["action"].Value));
            var fields = HiddenInput().Matches(html).ToDictionary(m => m.Groups["name"].Value, m => WebUtility.HtmlDecode(m.Groups["value"].Value));
            back = await browser.PostAsync(acs.PathAndQuery, new FormUrlEncodedContent(fields));
        }
        else
        {
            submitted.StatusCode.ShouldBe(HttpStatusCode.Found, await submitted.Content.ReadAsStringAsync());
            back = await browser.GetAsync(submitted.Headers.Location!.PathAndQuery);
        }

        // Follow the application's own redirects (callback) until it leaves the API.
        var location = back.Headers.Location?.OriginalString ?? throw new InvalidOperationException(await back.Content.ReadAsStringAsync());
        while (location.StartsWith("/api/", StringComparison.Ordinal) || location.StartsWith("http://localhost/api/", StringComparison.Ordinal))
        {
            var next = await browser.GetAsync(location.Replace("http://localhost", string.Empty, StringComparison.Ordinal));
            location = next.Headers.Location!.OriginalString;
        }

        await browser.RefreshAntiforgeryAsync();
        return Uri.UnescapeDataString(location);
    }

    private static async Task<HttpResponseMessage> SendWithCookiesAsync(HttpClient client, Dictionary<string, string> jar, HttpRequestMessage request)
    {
        if (jar.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}")));
        }

        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var pair in cookies.Select(c => c.Split(';')[0]).Where(c => c.Contains('=', StringComparison.Ordinal)))
            {
                jar[pair[..pair.IndexOf('=', StringComparison.Ordinal)]] = pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..];
            }
        }

        return response;
    }

    [GeneratedRegex("<form[^>]*action=\"(?<action>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex FormAction();

    [GeneratedRegex("<input[^>]*type=\"hidden\"[^>]*name=\"(?<name>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInput();
}
