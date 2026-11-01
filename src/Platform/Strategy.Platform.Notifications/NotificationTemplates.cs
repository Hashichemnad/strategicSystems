using Strategy.Platform.Notifications.Email;

namespace Strategy.Platform.Notifications;

/// <summary>Templates owned by the notifications service itself.</summary>
public static class NotificationTemplates
{
    /// <summary>Not sent: its subject is the default organisation name and its body the default footer of every e-mail.</summary>
    public static readonly NotificationTemplateDefinition Layout = new(
        "notifications.layout", "notifications", "إطار الرسائل (الاسم والتذييل الافتراضيان)", "E-mail frame (default name and footer)",
        "نظام الاستراتيجية المتكامل", "Integrated Strategy System",
        "هذه رسالة آلية، يرجى عدم الرد عليها.", "This is an automated message. Please do not reply.");

    public static readonly NotificationTemplateDefinition Digest = new(
        "notifications.digest", "notifications", "الملخص اليومي للإشعارات", "Daily notification digest",
        "ملخص إشعاراتك: {{count}} جديدة", "Your notification digest: {{count}} new",
        "مرحباً {{name}}،\nلديك {{count}} إشعارات جديدة منذ آخر ملخص:\n{{items}}\n{{button}}",
        "Hello {{name}},\nYou have {{count}} new notifications since the last digest:\n{{items}}\n{{button}}",
        "فتح مركز الإشعارات", "Open the notification centre", ["name", "count", "items"], Mandatory: true);

    public static readonly NotificationTemplateDefinition Test = new(
        "notifications.test", "notifications", "رسالة تجريبية", "Test e-mail", "رسالة تجريبية", "Test email",
        "هذه رسالة تجريبية من نظام الاستراتيجية المتكامل. إرسال البريد يعمل.",
        "This is a test email from the Integrated Strategy System. Email delivery works.", Mandatory: true);

    internal static readonly TemplateText LayoutText = new(Layout.SubjectAr, Layout.SubjectEn, Layout.BodyAr, Layout.BodyEn, null, null);
}
