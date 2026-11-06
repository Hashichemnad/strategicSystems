using Strategy.Platform.Notifications;

namespace Strategy.Platform.Reporting;

/// <summary>E-mail templates of the reporting layer (AR / EN side by side, editable in Settings → Notifications).</summary>
public static class ReportingNotifications
{
    public static readonly NotificationTemplateDefinition Scheduled = new(
        "reporting.scheduled", ReportingPermissions.SystemCode, "إرسال مجدول للوحة أو تقرير", "Scheduled dashboard or report",
        "{{titleAr}} — {{period}}", "{{titleEn}} — {{period}}",
        "مرحباً {{name}}،\nمرفق «{{titleAr}}» للفترة {{period}} ({{schedule}})، بحسب صلاحياتك عند الإرسال.\n{{button}}",
        "Hello {{name}},\nAttached is “{{titleEn}}” for {{period}} ({{schedule}}), built with your access at the time of sending.\n{{button}}",
        "فتح في النظام", "Open in the system", ["name", "titleAr", "titleEn", "period", "schedule", "rows"]);

    public static readonly NotificationTemplateDefinition Shared = new(
        "reporting.shared", ReportingPermissions.SystemCode, "مشاركة لوحة أو تقرير", "Dashboard or report shared",
        "تمت مشاركة «{{titleAr}}» معك", "“{{titleEn}}” was shared with you",
        "مرحباً {{name}}،\nشارك {{by}} معك «{{titleAr}}». ترى فيه البيانات المسموح لك بها فقط.\n{{button}}",
        "Hello {{name}},\n{{by}} shared “{{titleEn}}” with you. You see only the data you are allowed to see.\n{{button}}",
        "فتح", "Open", ["name", "titleAr", "titleEn", "by"]);

    public static readonly IReadOnlyList<NotificationTemplateDefinition> All = [Scheduled, Shared];
}
