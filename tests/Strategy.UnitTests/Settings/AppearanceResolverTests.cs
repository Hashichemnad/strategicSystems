using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Settings;

/// <summary>Theme and mode resolution table (docs/design/themes.md §2, ADR 0171).</summary>
public class AppearanceResolverTests
{
    public static TheoryData<string[]?, string, string, bool, bool, string?, string?, string, string> Cases => new()
    {
        // enabled, defaultTheme, defaultMode, allowTheme, allowMode, userTheme, userMode → theme, mode
        { null, "classic", "system", false, true, null, null, "classic", "system" },
        { null, "classic", "system", false, true, "modern", "dark", "classic", "dark" },
        { null, "enterprise", "light", true, true, "modern", null, "modern", "light" },
        { null, "enterprise", "light", true, false, "modern", "dark", "modern", "light" },
        { new[] { "classic", "enterprise" }, "enterprise", "dark", true, true, "modern", null, "enterprise", "dark" },
        { new[] { "modern" }, "classic", "system", false, true, null, null, "modern", "system" },
        { null, "classic", "sepia", false, true, null, null, "classic", "system" },
        { null, "modern", "dark", true, true, "neon", "blue", "modern", "dark" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolves_theme_and_mode(
        string[]? enabled, string defaultTheme, string defaultMode, bool allowTheme, bool allowMode, string? userTheme, string? userMode,
        string theme, string mode)
    {
        var settings = new AppearanceSettings(enabled, defaultTheme, defaultMode, allowTheme, allowMode);
        AppearanceResolver.Resolve(settings, userTheme, userMode).ShouldBe(new EffectiveAppearance(theme, mode));
    }

    [Fact]
    public void A_user_theme_is_refused_while_choosing_is_off_or_the_theme_is_not_enabled()
    {
        AppearanceResolver.ValidateUserTheme(new AppearanceSettings(), "modern")!.MessageKey.ShouldBe("appearance.errors.themeChoiceOff");
        AppearanceResolver.ValidateUserTheme(new AppearanceSettings(AllowUserTheme: true), "modern").ShouldBeNull();
        AppearanceResolver.ValidateUserTheme(new AppearanceSettings(["classic"], AllowUserTheme: true), "modern")!.MessageKey.ShouldBe("validation.unknownCode");
        AppearanceResolver.ValidateUserTheme(new AppearanceSettings(), null).ShouldBeNull();
    }

    [Fact]
    public void Defaults_are_valid_and_classic_with_choosing_off()
    {
        var defaults = new AppearanceSettings();
        AppearanceResolver.Validate(defaults).ShouldBeEmpty();
        defaults.DefaultTheme.ShouldBe("classic");
        defaults.AllowUserTheme.ShouldBeFalse();
        defaults.Themes.ShouldBe(["classic", "enterprise", "modern"]);
    }

    [Fact]
    public void Validation_reports_each_problem_by_field()
    {
        var errors = AppearanceResolver.Validate(new AppearanceSettings(
            ["classic", "classic"], "modern", "sepia", LandingRules: [new LandingRule("", "nowhere")],
            Apps: new Dictionary<string, AppOverride>
            {
                ["kpi"] = new(NameEn: new string('x', 61), Icon: "Bad Icon", Accent: "#12345", TabOrder: ["ok", "Not OK"]),
            }));
        errors.Select(e => e.Field).ShouldBe(
            ["enabledThemes", "defaultMode", "landingRules[0].roleCode", "landingRules[0].app", "apps.kpi.name", "apps.kpi.icon", "apps.kpi.accent", "apps.kpi.tabs"],
            ignoreOrder: true);
    }
}
