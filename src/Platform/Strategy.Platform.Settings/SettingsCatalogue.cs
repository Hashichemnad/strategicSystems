using Strategy.Platform.Access;

namespace Strategy.Platform.Settings;

public static class SettingsPermissions
{
    public const string GeneralManage = "settings.general.manage";
    public const string LookupManage = "settings.lookup.manage";
    public const string NumberingManage = "settings.numbering.manage";
    public const string FeatureManage = "settings.feature.manage";
    public const string TranslationManage = "settings.translation.manage";
    public const string NotificationManage = "settings.notification.manage";
    public const string FileManage = "settings.file.manage";
    public const string JobView = "settings.job.view";
    public const string JobManage = "settings.job.manage";

    /// <summary>Administrator Health page (ADR 0151).</summary>
    public const string HealthView = "settings.health.view";
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
        new(SettingsPermissions.NotificationManage, "إدارة قوالب الإشعارات والبريد", "Manage notification templates and e-mail delivery", Scopable: false),
        new(SettingsPermissions.FileManage, "إعدادات الملفات والمرفقات", "File and attachment settings", Scopable: false),
        new(SettingsPermissions.JobView, "عرض المهام المجدولة", "View scheduled jobs", Scopable: false),
        new(SettingsPermissions.JobManage, "تشغيل المهام المجدولة وإيقافها", "Run and pause scheduled jobs", Scopable: false),
        new(SettingsPermissions.HealthView, "عرض صحة النظام", "View system health", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول الإعدادات", "Settings administrator",
            [new(SettingsPermissions.GeneralManage), new(SettingsPermissions.LookupManage), new(SettingsPermissions.NumberingManage),
             new(SettingsPermissions.FeatureManage), new(SettingsPermissions.TranslationManage), new(SettingsPermissions.NotificationManage),
             new(SettingsPermissions.FileManage), new(SettingsPermissions.JobView), new(SettingsPermissions.JobManage),
             new(SettingsPermissions.HealthView)],
            RequiresMfa: true),
    ];
}
