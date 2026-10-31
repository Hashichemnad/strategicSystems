using Strategy.Platform.Access;

namespace Strategy.Platform.Settings;

public static class SettingsPermissions
{
    public const string GeneralManage = "settings.general.manage";
    public const string LookupManage = "settings.lookup.manage";
    public const string NumberingManage = "settings.numbering.manage";
    public const string FeatureManage = "settings.feature.manage";
    public const string TranslationManage = "settings.translation.manage";
}

internal sealed class SettingsCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new("settings", SystemKind.Platform, "الإعدادات", "Settings", 40);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(SettingsPermissions.GeneralManage, "الإعدادات الإقليمية والهوية المؤسسية", "Regional settings and branding", Scopable: false),
        new(SettingsPermissions.LookupManage, "إدارة القوائم", "Manage lists", Scopable: false),
        new(SettingsPermissions.NumberingManage, "إدارة صيغ الترقيم", "Manage numbering formats", Scopable: false),
        new(SettingsPermissions.FeatureManage, "تفعيل الخصائص", "Switch features", Scopable: false),
        new(SettingsPermissions.TranslationManage, "تعديل الترجمات", "Edit translations", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول الإعدادات", "Settings administrator",
            [new(SettingsPermissions.GeneralManage), new(SettingsPermissions.LookupManage), new(SettingsPermissions.NumberingManage),
             new(SettingsPermissions.FeatureManage), new(SettingsPermissions.TranslationManage)],
            RequiresMfa: true),
    ];
}
