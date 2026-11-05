using Strategy.Platform.Access;

namespace Strategy.Platform.Reporting;

/// <summary>Permissions of the reporting layer (docs/design/reporting.md §10). Module permissions apply on top for every figure.</summary>
public static class ReportingPermissions
{
    public const string SystemCode = "reporting";
    public const string DashboardRead = "reporting.dashboard.read";
    public const string ExecutiveRead = "reporting.executive.read";
    public const string DashboardBuild = "reporting.dashboard.build";
    public const string DashboardManage = "reporting.dashboard.manage";
    public const string ReportRun = "reporting.report.run";
    public const string ReportBuild = "reporting.report.build";
    public const string ReportShare = "reporting.report.share";
    public const string ReportExport = "reporting.report.export";
    public const string ScheduleManage = "reporting.schedule.manage";
    public const string ScheduleAdminister = "reporting.schedule.administer";
    public const string Refresh = "reporting.aggregate.refresh";
    public const string BriefGenerate = "reporting.brief.generate";
    public const string SettingsManage = "reporting.settings.manage";

    public const string ExecutiveViewerRole = "executive-viewer";
    public const string ReportAuthorRole = "report-author";
    public const string AdministratorRole = "administrator";
}

internal sealed class ReportingCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(ReportingPermissions.SystemCode, SystemKind.Platform, "لوحات المعلومات والتقارير", "Dashboards and reports", 5);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(ReportingPermissions.DashboardRead, "عرض لوحات المعلومات وبطاقات أعمالي", "View dashboards and My Work cards", false),
        new(ReportingPermissions.ExecutiveRead, "عرض اللوحة التنفيذية", "View the executive dashboard", false),
        new(ReportingPermissions.DashboardBuild, "إنشاء لوحات معلومات شخصية ومشاركتها", "Build and share personal dashboards", false),
        new(ReportingPermissions.DashboardManage, "إدارة لوحات الأنظمة والأدوار ونشرها", "Manage and publish system and role dashboards", false),
        new(ReportingPermissions.ReportRun, "تشغيل التقارير", "Run reports", false),
        new(ReportingPermissions.ReportBuild, "إنشاء التقارير وحفظها", "Build and save reports", false),
        new(ReportingPermissions.ReportShare, "مشاركة التقارير", "Share reports", false),
        new(ReportingPermissions.ReportExport, "تصدير التقارير ولوحات المعلومات", "Export reports and dashboards", false),
        new(ReportingPermissions.ScheduleManage, "جدولة إرسال لوحاتي وتقاريري", "Schedule my dashboards and reports", false),
        new(ReportingPermissions.ScheduleAdminister, "إدارة جميع الجداول", "Administer every schedule", false),
        new(ReportingPermissions.Refresh, "طلب تحديث البيانات المجمّعة", "Request a refresh of the aggregates", false),
        new(ReportingPermissions.BriefGenerate, "إنشاء الموجز التنفيذي بالذكاء الاصطناعي", "Generate the AI executive brief", false),
        new(ReportingPermissions.SettingsManage, "إعدادات التقارير ولوحة المقارنة", "Reporting settings and the comparison scorecard", false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("user", "مستخدم لوحات المعلومات", "Dashboard user",
            [new(ReportingPermissions.DashboardRead), new(ReportingPermissions.ReportRun), new(ReportingPermissions.ReportExport)], AssignToEveryone: true),
        new(ReportingPermissions.ExecutiveViewerRole, "مستعرض اللوحة التنفيذية", "Executive viewer",
            [new(ReportingPermissions.ExecutiveRead), new(ReportingPermissions.BriefGenerate), new(ReportingPermissions.ScheduleManage)], RequiresMfa: true),
        new(ReportingPermissions.ReportAuthorRole, "معدّ التقارير", "Report author",
            [new(ReportingPermissions.DashboardBuild), new(ReportingPermissions.ReportBuild), new(ReportingPermissions.ReportShare), new(ReportingPermissions.ScheduleManage)]),
        new(ReportingPermissions.AdministratorRole, "مسؤول التقارير ولوحات المعلومات", "Reporting administrator",
        [
            new(ReportingPermissions.DashboardBuild), new(ReportingPermissions.DashboardManage), new(ReportingPermissions.ReportBuild), new(ReportingPermissions.ReportShare),
            new(ReportingPermissions.ScheduleManage), new(ReportingPermissions.ScheduleAdminister), new(ReportingPermissions.Refresh), new(ReportingPermissions.SettingsManage),
        ], RequiresMfa: true),
    ];
}
