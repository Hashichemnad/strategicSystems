using System.Text.RegularExpressions;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Settings;

/// <summary>
/// Settings → Appearance (SPEC-ADDENDUM §C1, docs/design/themes.md §2, ADR 0171): enabled themes, the installation's default theme and mode,
/// whether users may choose, the landing app per role and per-app overrides. One JSON document like Branding.
/// </summary>
public sealed record AppearanceSettings(
    IReadOnlyList<string>? EnabledThemes = null,
    string DefaultTheme = AppearanceCatalogue.Classic,
    string DefaultMode = AppearanceCatalogue.System,
    bool AllowUserTheme = false,
    bool AllowUserMode = true,
    IReadOnlyList<LandingRule>? LandingRules = null,
    IReadOnlyDictionary<string, AppOverride>? Apps = null) : ISettingsArea
{
    public static string Key => "appearance";

    /// <summary>Enabled themes; every theme when never saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> Themes => EnabledThemes is { Count: > 0 } themes ? themes : AppearanceCatalogue.Themes;
}

/// <summary>"Users holding <paramref name="RoleCode"/> land on <paramref name="App"/>" — the first matching rule wins (ADR 0173).</summary>
public sealed record LandingRule(string RoleCode, string App);

/// <summary>
/// Administrator overrides of one application workspace (SPEC-ADDENDUM §C2): names, icon, accent colour, tab order / hidden tabs and the
/// home dashboard. Never grants access: tabs and apps stay filtered by the user's permissions.
/// </summary>
public sealed record AppOverride(
    string? NameAr = null,
    string? NameEn = null,
    string? Icon = null,
    string? Accent = null,
    IReadOnlyList<string>? TabOrder = null,
    IReadOnlyList<string>? HiddenTabs = null,
    string? Dashboard = null);

/// <summary>The effective appearance of one user.</summary>
public sealed record EffectiveAppearance(string Theme, string Mode);

/// <summary>Theme, mode and application codes known to the product (the client's app registry uses the same codes).</summary>
public static partial class AppearanceCatalogue
{
    public const string Classic = "classic";
    public const string Enterprise = "enterprise";
    public const string Modern = "modern";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string System = "system";

    public static readonly IReadOnlyList<string> Themes = [Classic, Enterprise, Modern];

    public static readonly IReadOnlyList<string> Modes = [Light, Dark, System];

    /// <summary>Application workspaces (web/src/layout/apps.ts): the eleven systems, the template module and the shared apps.</summary>
    public static readonly IReadOnlyList<string> Apps =
    [
        "mywork", "dashboards", "portfolio", "kpi", "risk", "teams", "tasks", "requests", "docreg", "ip", "bench", "cap", "auditmgmt",
        "org", "settings", "sample",
    ];

    public const int MaxRules = 50;
    public const int MaxNameLength = 60;
    public const int MaxFavourites = 20;

    /// <summary>Lucide icon name (kebab case), tab key or dashboard code.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,59}$")]
    public static partial Regex Code();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    public static partial Regex Colour();

    public static bool IsApp(string? code) => code is not null && Apps.Contains(code);
}

/// <summary>Theme and mode of a user (docs/design/themes.md §2). Pure: the only place this rule lives.</summary>
public static class AppearanceResolver
{
    /// <param name="settings">The installation's appearance settings.</param>
    /// <param name="userTheme">The user's stored theme choice (null: none).</param>
    /// <param name="userMode">The user's stored mode choice (light / dark / system; null: none).</param>
    public static EffectiveAppearance Resolve(AppearanceSettings settings, string? userTheme, string? userMode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var themes = settings.Themes;
        var fallback = themes.Contains(settings.DefaultTheme) ? settings.DefaultTheme : themes[0];
        var theme = settings.AllowUserTheme && userTheme is not null && themes.Contains(userTheme) ? userTheme : fallback;
        var defaultMode = AppearanceCatalogue.Modes.Contains(settings.DefaultMode) ? settings.DefaultMode : AppearanceCatalogue.System;
        var mode = settings.AllowUserMode && userMode is not null && AppearanceCatalogue.Modes.Contains(userMode) ? userMode : defaultMode;
        return new EffectiveAppearance(theme, mode);
    }

    /// <summary>Why a user may not save this theme choice (null: allowed). Null clears the choice and is always allowed.</summary>
    public static FieldError? ValidateUserTheme(AppearanceSettings settings, string? userTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (userTheme is null)
        {
            return null;
        }

        if (!AppearanceCatalogue.Themes.Contains(userTheme) || !settings.Themes.Contains(userTheme))
        {
            return new FieldError("uiTheme", "validation.unknownCode", "validation.unknownCode");
        }

        return settings.AllowUserTheme ? null : new FieldError("uiTheme", "appearance.errors.themeChoiceOff", "appearance.errors.themeChoiceOff");
    }

    /// <summary>Validation of the Appearance document (Settings → Appearance).</summary>
    public static List<FieldError> Validate(AppearanceSettings value)
    {
        var errors = new List<FieldError>();
        if (value is null)
        {
            errors.Add(new FieldError("value", "validation.required", "validation.required"));
            return errors;
        }

        var themes = value.EnabledThemes ?? AppearanceCatalogue.Themes;
        if (themes.Count == 0 || themes.Any(t => !AppearanceCatalogue.Themes.Contains(t)) || themes.Distinct().Count() != themes.Count)
        {
            errors.Add(new FieldError("enabledThemes", "validation.unknownCode", "validation.unknownCode"));
        }
        else if (!themes.Contains(value.DefaultTheme))
        {
            errors.Add(new FieldError("defaultTheme", "appearance.errors.defaultNotEnabled", "appearance.errors.defaultNotEnabled"));
        }

        if (!AppearanceCatalogue.Themes.Contains(value.DefaultTheme))
        {
            errors.Add(new FieldError("defaultTheme", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!AppearanceCatalogue.Modes.Contains(value.DefaultMode))
        {
            errors.Add(new FieldError("defaultMode", "validation.unknownCode", "validation.unknownCode"));
        }

        var rules = value.LandingRules ?? [];
        if (rules.Count > AppearanceCatalogue.MaxRules)
        {
            errors.Add(new FieldError("landingRules", "validation.maximumItems", "validation.maximumItems",
                new Dictionary<string, object?> { ["max"] = AppearanceCatalogue.MaxRules }));
        }

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule is null || string.IsNullOrWhiteSpace(rule.RoleCode) || rule.RoleCode.Length > 100)
            {
                errors.Add(new FieldError($"landingRules[{i}].roleCode", "validation.required", "validation.required"));
            }

            if (rule is null || !AppearanceCatalogue.IsApp(rule.App))
            {
                errors.Add(new FieldError($"landingRules[{i}].app", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        foreach (var (app, o) in value.Apps ?? new Dictionary<string, AppOverride>())
        {
            var prefix = $"apps.{app}";
            if (!AppearanceCatalogue.IsApp(app) || o is null)
            {
                errors.Add(new FieldError(prefix, "validation.unknownCode", "validation.unknownCode"));
                continue;
            }

            if (o.NameAr?.Length > AppearanceCatalogue.MaxNameLength || o.NameEn?.Length > AppearanceCatalogue.MaxNameLength)
            {
                errors.Add(new FieldError($"{prefix}.name", "validation.maximumLength", "validation.maximumLength",
                    new Dictionary<string, object?> { ["maxLength"] = AppearanceCatalogue.MaxNameLength }));
            }

            if (o.Icon is not null && !AppearanceCatalogue.Code().IsMatch(o.Icon))
            {
                errors.Add(new FieldError($"{prefix}.icon", "validation.unknownCode", "validation.unknownCode"));
            }

            if (o.Accent is not null && !AppearanceCatalogue.Colour().IsMatch(o.Accent))
            {
                errors.Add(new FieldError($"{prefix}.accent", "settings.errors.colour", "settings.errors.colour"));
            }

            if (o.Dashboard is not null && !AppearanceCatalogue.Code().IsMatch(o.Dashboard))
            {
                errors.Add(new FieldError($"{prefix}.dashboard", "validation.unknownCode", "validation.unknownCode"));
            }

            foreach (var tab in (o.TabOrder ?? []).Concat(o.HiddenTabs ?? []))
            {
                if (tab is null || !AppearanceCatalogue.Code().IsMatch(tab))
                {
                    errors.Add(new FieldError($"{prefix}.tabs", "validation.unknownCode", "validation.unknownCode"));
                    break;
                }
            }

            if ((o.TabOrder?.Count ?? 0) > 40 || (o.HiddenTabs?.Count ?? 0) > 40)
            {
                errors.Add(new FieldError($"{prefix}.tabs", "validation.maximumItems", "validation.maximumItems",
                    new Dictionary<string, object?> { ["max"] = 40 }));
            }
        }

        return errors;
    }
}
