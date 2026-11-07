using Strategy.Platform.Notifications;

namespace Strategy.Platform.Identity.Email;

/// <summary>
/// Security e-mails to the account holder (ADR 0162, SPEC-ADDENDUM A3): mandatory (never suppressed by preferences or the digest), e-mail only,
/// editable in Settings → Notifications. They never carry a password, token value, code or link: only what happened, when, from where, and the
/// "if this wasn't you" guidance (<c>guidanceAr</c> / <c>guidanceEn</c>, Administration → Security).
/// </summary>
public static class SecurityEmails
{
    private static readonly string[] Common = ["name", "time", "ip", "device", "guidanceAr", "guidanceEn"];

    private const string WhenAr = "الوقت: {{time}} · عنوان IP: {{ip}} · الجهاز: {{device}}";
    private const string WhenEn = "Time: {{time}} · IP address: {{ip}} · Device: {{device}}";

    /// <summary>Default "if this wasn't you" text (used while the security settings leave it empty).</summary>
    public const string DefaultGuidanceAr = "إذا لم تكن أنت من قام بذلك، فتواصل فوراً مع مسؤول النظام لديك.";

    public const string DefaultGuidanceEn = "If this wasn't you, contact your system administrator straight away.";

    private static NotificationTemplateDefinition Template(
        string code, string nameAr, string nameEn, string subjectAr, string subjectEn, string lineAr, string lineEn, params string[] extra) =>
        new("identity.security." + code, "identity", nameAr, nameEn, subjectAr, subjectEn,
            $"مرحباً {{{{name}}}}،\n{lineAr}\n{WhenAr}\n{{{{guidanceAr}}}}",
            $"Hello {{{{name}}}},\n{lineEn}\n{WhenEn}\n{{{{guidanceEn}}}}",
            Placeholders: [.. Common, .. extra], Mandatory: true);

    public static readonly NotificationTemplateDefinition PasswordChanged = Template(
        "passwordChanged", "أمان: تغيير كلمة المرور", "Security: password changed", "تم تغيير كلمة المرور", "Your password was changed",
        "تم تغيير كلمة مرور حسابك، وانتهت جلساتك الأخرى.", "The password of your account was changed, and your other sessions were signed out.");

    public static readonly NotificationTemplateDefinition PasswordReset = Template(
        "passwordReset", "أمان: إعادة تعيين كلمة المرور", "Security: password reset", "تمت إعادة تعيين كلمة المرور", "Your password was reset",
        "تمت إعادة تعيين كلمة مرور حسابك عبر رابط إعادة التعيين، وانتهت جميع جلساتك.",
        "The password of your account was reset with a reset link, and all your sessions were signed out.");

    public static readonly NotificationTemplateDefinition MfaEnrolled = Template(
        "mfaEnrolled", "أمان: تفعيل التحقق الثنائي", "Security: two-step verification added", "أُضيفت طريقة تحقق ثنائي",
        "A two-step verification method was added",
        "أُضيفت إلى حسابك طريقة التحقق الثنائي: {{methodAr}}. أُنشئت معها رموز استرداد جديدة.",
        "A two-step verification method was added to your account: {{methodEn}}. New recovery codes were created with it.", "methodAr", "methodEn");

    public static readonly NotificationTemplateDefinition MfaRemoved = Template(
        "mfaRemoved", "أمان: إزالة التحقق الثنائي", "Security: two-step verification removed", "أُزيلت طريقة تحقق ثنائي",
        "A two-step verification method was removed",
        "أُزيلت من حسابك طريقة التحقق الثنائي: {{methodAr}}.", "A two-step verification method was removed from your account: {{methodEn}}.",
        "methodAr", "methodEn");

    public static readonly NotificationTemplateDefinition RecoveryCodes = Template(
        "recoveryCodes", "أمان: رموز استرداد جديدة", "Security: new recovery codes", "أُنشئت رموز استرداد جديدة", "New recovery codes were created",
        "أُنشئت رموز استرداد جديدة لحسابك، ولم تعد الرموز السابقة صالحة.",
        "New recovery codes were created for your account; the previous codes no longer work.");

    public static readonly NotificationTemplateDefinition LockedOut = Template(
        "lockedOut", "أمان: قفل الحساب", "Security: account locked", "تم قفل حسابك مؤقتاً", "Your account is temporarily locked",
        "تم قفل حسابك بعد عدة محاولات دخول فاشلة، حتى {{until}}. يمكن لمسؤول النظام إلغاء القفل.",
        "Your account was locked after several failed sign-in attempts, until {{until}}. An administrator can unlock it.", "until");

    public static readonly NotificationTemplateDefinition TokenCreated = Template(
        "tokenCreated", "أمان: رمز وصول شخصي جديد", "Security: new personal access token", "أُنشئ رمز وصول شخصي",
        "A personal access token was created",
        "أُنشئ لحسابك رمز وصول شخصي باسم «{{token}}» تنتهي صلاحيته في {{expires}}.",
        "A personal access token named “{{token}}” was created for your account; it expires on {{expires}}.", "token", "expires");

    public static readonly NotificationTemplateDefinition NewDevice = Template(
        "newDevice", "أمان: دخول من جهاز جديد", "Security: sign-in from a new device", "دخول جديد إلى حسابك",
        "New sign-in to your account",
        "تم تسجيل الدخول إلى حسابك من جهاز أو شبكة لم تُستخدم من قبل.", "Your account was signed in to from a device or network not used before.");

    public static IEnumerable<NotificationTemplateDefinition> All =>
        [PasswordChanged, PasswordReset, MfaEnrolled, MfaRemoved, RecoveryCodes, LockedOut, TokenCreated, NewDevice];
}
