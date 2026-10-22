using System.Security.Claims;
using System.Text.Json.Nodes;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.External;

namespace Strategy.UnitTests.Identity;

public class AccessTokenFormatTests
{
    [Theory]
    [InlineData("sst_abcdefgh_abcdefghijklmnopqrstuvwxyz234567", true)]
    [InlineData("sst_abcdefgh_abcdefghijklmnopqrstuvwxyz23456", false)] // short secret
    [InlineData("sst_ABCDEFGH_abcdefghijklmnopqrstuvwxyz234567", false)] // upper case is not in the alphabet
    [InlineData("sst_abcdefg1_abcdefghijklmnopqrstuvwxyz234567", false)] // '1' is not base32
    [InlineData("pat_abcdefgh_abcdefghijklmnopqrstuvwxyz234567", false)]
    [InlineData("sst_abcdefgh", false)]
    public void Only_well_formed_tokens_parse(string value, bool valid)
    {
        AccessTokenService.TryParseToken(value, out var prefix, out var secret).ShouldBe(valid);
        if (valid)
        {
            prefix.ShouldBe("abcdefgh");
            secret.Length.ShouldBe(32);
        }
    }

    [Theory]
    [InlineData("api.read", "GET", "/api/v1/org/units/tree", true)]
    [InlineData("api.read", "HEAD", "/api/v1/org/units/tree", true)]
    [InlineData("api.read", "POST", "/api/v1/org/units", false)]
    [InlineData("api.write", "DELETE", "/api/v1/x", true)]
    [InlineData("api.read api.write", "PUT", "/api/v1/x", true)]
    [InlineData("api.write", "POST", "/mcp", false)]
    [InlineData("mcp", "POST", "/mcp", true)]
    [InlineData("mcp", "GET", "/api/v1/x", false)]
    public void Token_scopes_limit_what_a_token_may_call(string scopes, string method, string path, bool allowed)
    {
        var identity = new ClaimsIdentity(
            [new Claim(StrategyClaims.AuthMethod, AuthMethods.Token), new Claim(StrategyClaims.TokenScopes, scopes)], AuthSchemes.PersonalAccessToken);

        TokenScopeRules.Allows(new ClaimsPrincipal(identity), method, path).ShouldBe(allowed);
    }

    [Fact]
    public void Session_principals_are_never_limited_by_token_scopes()
    {
        var cookie = new ClaimsPrincipal(new ClaimsIdentity([new Claim(StrategyClaims.AuthMethod, AuthMethods.Token)], "Identity.Application"));

        cookie.IsToken().ShouldBeFalse();
        TokenScopeRules.Allows(cookie, "POST", "/api/v1/x").ShouldBeTrue();
    }
}

public class ProviderSchemaTests
{
    [Fact]
    public void Defaults_are_filled_and_unknown_keys_dropped()
    {
        var settings = ProviderSchema.Normalise(ProviderTypes.Oidc, new JsonObject { ["authority"] = "https://login.example.org", ["evil"] = "x" });

        settings["scopes"]!.GetValue<string>().ShouldBe("openid profile email");
        settings["claimSubject"]!.GetValue<string>().ShouldBe("sub");
        settings.ContainsKey("evil").ShouldBeFalse();
    }

    [Fact]
    public void Oidc_requires_https_unless_insecure_http_is_explicitly_allowed()
    {
        var http = ProviderSchema.Normalise(ProviderTypes.Oidc, new JsonObject { ["authority"] = "http://idp.local", ["clientId"] = "app" });
        ProviderSchema.Validate(ProviderTypes.Oidc, http, hasSecret: false).Select(e => e.Field).ShouldBe(["settings.authority"]);

        http["allowInsecureHttp"] = true;
        ProviderSchema.Validate(ProviderTypes.Oidc, http, hasSecret: false).ShouldBeEmpty();
    }

    [Fact]
    public void Uae_pass_requires_the_client_secret()
    {
        var settings = ProviderSchema.Normalise(ProviderTypes.UaePass, new JsonObject { ["clientId"] = "sandbox_stage" });

        ProviderSchema.Validate(ProviderTypes.UaePass, settings, hasSecret: false).Select(e => e.Field).ShouldBe(["secret"]);
        ProviderSchema.Validate(ProviderTypes.UaePass, settings, hasSecret: true).ShouldBeEmpty();
        settings["scopes"]!.GetValue<string>().ShouldBe("urn:uae:digitalid:profile:general");
    }

    [Fact]
    public void Saml_needs_metadata_or_sso_url_with_certificate()
    {
        var settings = ProviderSchema.Normalise(ProviderTypes.Saml2, new JsonObject { ["idpEntityId"] = "https://idp" });
        ProviderSchema.Validate(ProviderTypes.Saml2, settings, false).Select(e => e.Field).ShouldContain("settings.metadataUrl");

        settings["ssoUrl"] = "https://idp/sso";
        settings["idpCertificate"] = "not a certificate";
        ProviderSchema.Validate(ProviderTypes.Saml2, settings, false).Select(e => e.Field).ShouldBe(["settings.idpCertificate"]);
    }

    [Theory]
    [InlineData("(uid=*)", false)]
    [InlineData("uid={0}", false)]
    [InlineData("(uid={0})", true)]
    public void Ldap_filter_must_be_a_filter_with_the_placeholder(string filter, bool valid)
    {
        var settings = ProviderSchema.Normalise(ProviderTypes.Ldap, new JsonObject { ["host"] = "ldap", ["baseDn"] = "dc=x", ["userFilter"] = filter });

        ProviderSchema.Validate(ProviderTypes.Ldap, settings, false).Any(e => e.Field == "settings.userFilter").ShouldBe(!valid);
    }

    [Theory]
    [InlineData("entra", true)]
    [InlineData("uae-pass", true)]
    [InlineData("Entra", false)]
    [InlineData("a", false)]
    [InlineData("pat", false)]
    [InlineData("1abc", false)]
    public void Provider_codes_are_lower_case_and_not_reserved(string code, bool valid) => ProviderSchema.IsValidCode(code).ShouldBe(valid);

    [Fact]
    public void Ldap_filter_values_are_escaped()
    {
        LdapDirectory.EscapeFilter("*)(uid=admin)(|(uid=*").ShouldBe(@"\2a\29\28uid=admin\29\28|\28uid=\2a");
        LdapDirectory.EscapeFilter(@"a\b").ShouldBe(@"a\5cb");
    }
}

public class ExternalSignInTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("/org/units?x=1", "/org/units?x=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    public void Return_urls_are_local_only(string? input, string expected) => ExternalEndpoints.LocalUrl(input).ShouldBe(expected);

    [Fact]
    public void Claims_are_mapped_with_the_provider_settings()
    {
        var provider = new IdentityProvider
        {
            Type = ProviderTypes.Oidc,
            Settings = ProviderSchema.Normalise(ProviderTypes.Oidc, new JsonObject { ["claimEmployeeNo"] = "employee_id" }),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "abc-123"), new Claim("email", "a@b.c"), new Claim("employee_id", "E7"), new Claim("preferred_username", "ab"),
        ]));

        var identity = ExternalEndpoints.MapIdentity(provider, principal)!;

        identity.ShouldBe(new ExternalIdentity("abc-123", "a@b.c", "E7", "ab"));
        ExternalEndpoints.MapIdentity(provider, new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeNull();
    }
}

public class IdentityEmailRendererTests
{
    [Fact]
    public void Renders_arabic_right_and_english_left_and_encodes_values()
    {
        var (subject, html, text) = IdentityEmailRenderer.Render(new IdentityEmail(
            "a@b.c", "passwordReset", new Dictionary<string, string> { ["name"] = "<b>Sara</b>" }, "https://app.example/reset?x=1&y=2"));

        subject.ShouldBe("إعادة تعيين كلمة المرور | Reset your password");
        html.ShouldContain("dir=\"rtl\" lang=\"ar\"");
        html.ShouldContain("dir=\"ltr\" lang=\"en\"");
        html.ShouldContain("&lt;b&gt;Sara&lt;/b&gt;");
        html.ShouldNotContain("<b>Sara</b>");
        html.ShouldContain("href=\"https://app.example/reset?x=1&amp;y=2\"");
        text.ShouldContain("https://app.example/reset?x=1&y=2");
    }

    [Fact]
    public void Email_code_template_shows_the_code()
    {
        var (_, html, _) = IdentityEmailRenderer.Render(new IdentityEmail("a@b.c", "emailCode",
            new Dictionary<string, string> { ["name"] = "Sara", ["code"] = "123456" }));

        html.ShouldContain(">123456</p>");
    }
}

public class SecuritySettingsTests
{
    [Fact]
    public void Settings_become_identity_options()
    {
        var security = new SecuritySettings { PasswordMinLength = 14, PasswordRequireSymbol = false, LockoutThreshold = 3, LockoutMinutes = 30 };

        var options = StrategyUserManager.WithSettings(new Microsoft.AspNetCore.Identity.IdentityOptions(), security);

        options.Password.RequiredLength.ShouldBe(14);
        options.Password.RequireNonAlphanumeric.ShouldBeFalse();
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(3);
        options.Lockout.DefaultLockoutTimeSpan.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Defaults_match_the_design_note()
    {
        var defaults = new SecuritySettings();

        defaults.PasswordMinLength.ShouldBe(12);
        defaults.MfaPolicy.ShouldBe(MfaPolicies.Admins);
        defaults.LockoutThreshold.ShouldBe(5);
        defaults.SessionIdleMinutes.ShouldBe(30);
        defaults.SessionAbsoluteHours.ShouldBe(12);
        defaults.TokenMaxDays.ShouldBe(365);
    }

    [Fact]
    public void Sessions_end_after_idle_or_absolute_limits_or_revocation()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession { CreatedAt = now.AddHours(-2), LastSeenAt = now.AddMinutes(-10) };

        session.IsActive(now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12)).ShouldBeTrue();
        session.IsActive(now, TimeSpan.FromMinutes(5), TimeSpan.FromHours(12)).ShouldBeFalse();
        session.IsActive(now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1)).ShouldBeFalse();
        session.RevokedAt = now;
        session.IsActive(now, TimeSpan.FromMinutes(30), TimeSpan.FromHours(12)).ShouldBeFalse();
    }
}
