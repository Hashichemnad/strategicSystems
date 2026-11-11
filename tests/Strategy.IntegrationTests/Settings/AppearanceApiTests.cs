using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Settings;

/// <summary>
/// Settings → Appearance and the user's theme / mode resolution (SPEC-ADDENDUM §C1, docs/design/themes.md §2, ADR 0171): the admin default
/// applies to everyone; a user's choice only while choosing is allowed and the theme is enabled.
/// </summary>
public class AppearanceApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Appearance = "/api/v1/settings/appearance";
    private const string Preferences = "/api/v1/identity/me/preferences";

    private static async Task<JsonElement> MeAppearanceAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/identity/me")).GetProperty("appearance");

    private static async Task<uint> SaveAsync(HttpClient admin, object value, uint version)
    {
        var response = await admin.PutAsJsonAsync(Appearance, new { value, version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32();
    }

    [Fact]
    public async Task Defaults_are_classic_system_mode_and_users_may_not_choose_a_theme()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var user = await factory.CreateSignedInClientAsync();

        var settings = await user.GetFromJsonAsync<JsonElement>(Appearance);
        settings.GetProperty("value").GetProperty("allowUserTheme").GetBoolean().ShouldBeFalse();
        settings.GetProperty("value").GetProperty("defaultTheme").GetString().ShouldBe("classic");

        var me = await MeAppearanceAsync(user);
        me.GetProperty("theme").GetString().ShouldBe("classic");
        me.GetProperty("mode").GetString().ShouldBe("system");
        me.GetProperty("allowUserTheme").GetBoolean().ShouldBeFalse();
        me.GetProperty("enabledThemes").GetArrayLength().ShouldBe(3);

        // Choosing is off by default: refused, nothing stored.
        var refused = await user.PutAsJsonAsync(Preferences, new { uiTheme = "modern" });
        var problem = await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors")[0].GetProperty("messageKey").GetString().ShouldBe("appearance.errors.themeChoiceOff");
        (await MeAppearanceAsync(user)).GetProperty("themeChoice").ValueKind.ShouldBe(JsonValueKind.Null);

        var anonymous = await factory.CreateSpaClientAsync();
        var publicSettings = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/settings/public");
        publicSettings.GetProperty("defaultTheme").GetString().ShouldBe("classic");
        publicSettings.GetProperty("defaultMode").GetString().ShouldBe("system");
    }

    [Fact]
    public async Task Admin_default_applies_to_everyone_and_a_user_choice_only_while_allowed_and_enabled()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var user = await factory.CreateSignedInClientAsync();

        var version = await SaveAsync(admin, new { defaultTheme = "enterprise", defaultMode = "dark", allowUserTheme = false }, 0);
        var me = await MeAppearanceAsync(user);
        me.GetProperty("theme").GetString().ShouldBe("enterprise");
        // The user had chosen no mode: the admin default mode applies.
        me.GetProperty("mode").GetString().ShouldBe("dark");
        var anonymous = await factory.CreateSpaClientAsync();
        (await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/settings/public")).GetProperty("defaultTheme").GetString().ShouldBe("enterprise");

        // Allowed: the user's choice wins, but only among enabled themes.
        version = await SaveAsync(admin, new
        {
            enabledThemes = new[] { "classic", "enterprise", "modern" },
            defaultTheme = "enterprise",
            defaultMode = "dark",
            allowUserTheme = true,
        }, version);
        (await user.PutAsJsonAsync(Preferences, new { uiTheme = "modern", theme = "light" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me = await MeAppearanceAsync(user);
        me.GetProperty("theme").GetString().ShouldBe("modern");
        me.GetProperty("mode").GetString().ShouldBe("light");
        me.GetProperty("themeChoice").GetString().ShouldBe("modern");
        await (await user.PutAsJsonAsync(Preferences, new { uiTheme = "neon" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        // The admin disables Modern: the stored choice is ignored (default applies) and can no longer be saved.
        version = await SaveAsync(admin, new
        {
            enabledThemes = new[] { "classic", "enterprise" },
            defaultTheme = "enterprise",
            defaultMode = "dark",
            allowUserTheme = true,
        }, version);
        (await MeAppearanceAsync(user)).GetProperty("theme").GetString().ShouldBe("enterprise");
        var disabled = await user.PutAsJsonAsync(Preferences, new { uiTheme = "modern" });
        (await disabled.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed"))
            .GetProperty("errors")[0].GetProperty("messageKey").GetString().ShouldBe("validation.unknownCode");
        (await user.PutAsJsonAsync(Preferences, new { uiTheme = "classic" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAppearanceAsync(user)).GetProperty("theme").GetString().ShouldBe("classic");

        // Choosing turned off again: the choice is kept but the default applies; mode choice off forces the default mode.
        await SaveAsync(admin, new
        {
            enabledThemes = new[] { "classic", "enterprise" },
            defaultTheme = "enterprise",
            defaultMode = "dark",
            allowUserTheme = false,
            allowUserMode = false,
        }, version);
        me = await MeAppearanceAsync(user);
        me.GetProperty("theme").GetString().ShouldBe("enterprise");
        me.GetProperty("themeChoice").GetString().ShouldBe("classic");
        me.GetProperty("mode").GetString().ShouldBe("dark");
        var mode = await user.PutAsJsonAsync(Preferences, new { theme = "light" });
        (await mode.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed"))
            .GetProperty("errors")[0].GetProperty("messageKey").GetString().ShouldBe("appearance.errors.modeChoiceOff");
        // Clearing a stored choice is always allowed.
        (await user.PutAsJsonAsync(Preferences, new { uiTheme = "" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAppearanceAsync(user)).GetProperty("themeChoice").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Only_settings_managers_change_appearance_and_the_document_is_validated_versioned_and_audited()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        var user = await factory.CreateSignedInClientAsync();

        await (await user.PutAsJsonAsync(Appearance, new { value = new { defaultTheme = "modern" }, version = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        (await (await factory.CreateSpaClientAsync()).GetAsync(Appearance)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Default theme not enabled, unknown app, bad colour, unknown mode.
        var invalid = await admin.PutAsJsonAsync(Appearance, new
        {
            value = new
            {
                enabledThemes = new[] { "classic" },
                defaultTheme = "modern",
                defaultMode = "sepia",
                landingRules = new[] { new { roleCode = "pmo", app = "nowhere" } },
                apps = new Dictionary<string, object> { ["kpi"] = new { accent = "blue" }, ["unknown"] = new { } },
            },
            version = 0,
        });
        var problem = await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var fields = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        fields.ShouldContain("defaultTheme");
        fields.ShouldContain("defaultMode");
        fields.ShouldContain("landingRules[0].app");
        fields.ShouldContain("apps.kpi.accent");
        fields.ShouldContain("apps.unknown");

        var valid = new
        {
            defaultTheme = "modern",
            allowUserTheme = true,
            landingRules = new[] { new { roleCode = "kpi.administrator", app = "kpi" } },
            apps = new Dictionary<string, object>
            {
                ["kpi"] = new { nameEn = "Performance", nameAr = "الأداء", icon = "gauge", accent = "#7c3aed", tabOrder = new[] { "cards", "home" }, hiddenTabs = new[] { "reports" } },
            },
        };
        var version = await SaveAsync(admin, valid, 0);
        await (await admin.PutAsJsonAsync(Appearance, new { value = valid, version = 0 })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict");

        var read = (await user.GetFromJsonAsync<JsonElement>(Appearance)).GetProperty("value");
        read.GetProperty("landingRules")[0].GetProperty("app").GetString().ShouldBe("kpi");
        read.GetProperty("apps").GetProperty("kpi").GetProperty("accent").GetString().ShouldBe("#7c3aed");
        version.ShouldBeGreaterThan(0u);

        var audit = await admin.GetStringAsync("/api/v1/audit/entries?entityType=settings.setting&pageSize=200");
        audit.ShouldContain("#7c3aed");
    }

    [Fact]
    public async Task Default_app_and_favourites_are_validated_and_saved_on_the_profile()
    {
        await using var factory = await ApiFactory.CreateAsync(api.Postgres);
        var user = await factory.CreateSignedInClientAsync();

        await (await user.PutAsJsonAsync(Preferences, new { landingApp = "nowhere" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        await (await user.PutAsJsonAsync(Preferences, new { favouriteApps = new[] { "kpi", "x" } })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        (await user.PutAsJsonAsync(Preferences, new { landingApp = "risk", favouriteApps = new[] { "kpi", "risk", "kpi" } })).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        var me = await MeAppearanceAsync(user);
        me.GetProperty("landingApp").GetString().ShouldBe("risk");
        me.GetProperty("favouriteApps").EnumerateArray().Select(a => a.GetString()).ShouldBe(["kpi", "risk"]);

        // Language-only updates keep the other preferences.
        (await user.PutAsJsonAsync(Preferences, new { language = "ar", theme = (string?)null })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAppearanceAsync(user)).GetProperty("landingApp").GetString().ShouldBe("risk");
        (await user.PutAsJsonAsync(Preferences, new { landingApp = "", favouriteApps = Array.Empty<string>() })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me = await MeAppearanceAsync(user);
        me.GetProperty("landingApp").ValueKind.ShouldBe(JsonValueKind.Null);
        me.GetProperty("favouriteApps").GetArrayLength().ShouldBe(0);
    }
}
