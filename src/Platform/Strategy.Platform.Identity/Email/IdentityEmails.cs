using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Notifications;

namespace Strategy.Platform.Identity.Email;

/// <summary>
/// The identity e-mails as notification templates (ADR 0014 closed in Phase 3): editable in Settings → Notifications, sent through the
/// outbox with retry. All are mandatory (never suppressed by preferences or the digest) and sensitive (codes and links are encrypted while queued).
/// </summary>
public static class IdentityEmails
{
    public static readonly NotificationTemplateDefinition PasswordReset = new(
        "identity.passwordReset", "identity", "إعادة تعيين كلمة المرور", "Password reset",
        "إعادة تعيين كلمة المرور", "Reset your password",
        "مرحباً {{name}}،\nتلقّينا طلباً لإعادة تعيين كلمة المرور. الرابط أدناه صالح لمدة 60 دقيقة.\n{{button}}\nإذا لم تطلب ذلك فتجاهل هذه الرسالة، ولن تتغير كلمة المرور.",
        "Hello {{name}},\nWe received a request to reset your password. The link below is valid for 60 minutes.\n{{button}}\nIf you did not ask for this, ignore this email; your password stays the same.",
        "إعادة تعيين كلمة المرور", "Reset password", ["name"], Mandatory: true);

    public static readonly NotificationTemplateDefinition Invitation = new(
        "identity.invitation", "identity", "دعوة حساب جديد", "Account invitation",
        "حسابك جاهز", "Your account is ready",
        "مرحباً {{name}}،\nتم إنشاء حساب لك. اسم المستخدم: {{userName}}. عيّن كلمة المرور من الرابط أدناه (صالح لمدة 3 أيام).\n{{button}}",
        "Hello {{name}},\nAn account has been created for you. Your user name is {{userName}}. Set your password with the link below (valid for 3 days).\n{{button}}",
        "تعيين كلمة المرور", "Set password", ["name", "userName"], Mandatory: true);

    public static readonly NotificationTemplateDefinition EmailCode = new(
        "identity.emailCode", "identity", "رمز الدخول بالبريد", "E-mail sign-in code",
        "رمز تسجيل الدخول", "Your sign-in code",
        "مرحباً {{name}}،\nرمز الدخول لمرة واحدة هو:\n{{code}}\nتنتهي صلاحيته خلال دقائق. لا تشاركه مع أي شخص.",
        "Hello {{name}},\nYour one-time sign-in code is:\n{{code}}\nIt expires in a few minutes. Never share it with anyone.",
        Placeholders: ["name", "code"], Mandatory: true);

    /// <summary>Wasel vendor portal invitation (ADR 0110): the link opens <c>/wasel/set-password</c>.</summary>
    public static readonly NotificationTemplateDefinition WaselInvitation = new(
        "identity.waselInvitation", "identity", "دعوة إلى بوابة واصل", "Wasel vendor portal invitation",
        "دعوة للانضمام إلى بوابة واصل للموردين", "You are invited to the Wasel vendor portal",
        "مرحباً {{name}}،\nدعاك {{inviter}} للمشاركة في المشروع {{project}} عبر بوابة واصل. عيّن كلمة المرور من الرابط أدناه (صالح لمدة 3 أيام)، ثم فعّل التحقق الثنائي عند أول دخول.\n{{button}}",
        "Hello {{name}},\n{{inviter}} has invited you to take part in the project {{project}} through the Wasel vendor portal. Set your password with the link below (valid for 3 days); you will set up two-step verification at your first sign-in.\n{{button}}",
        "تعيين كلمة المرور", "Set password", ["name", "userName", "inviter", "project"], Mandatory: true);

    /// <summary>Wasel password reset (the link opens <c>/wasel/set-password</c>).</summary>
    public static readonly NotificationTemplateDefinition WaselPasswordReset = new(
        "identity.waselPasswordReset", "identity", "إعادة تعيين كلمة مرور واصل", "Wasel password reset",
        "إعادة تعيين كلمة المرور — واصل", "Reset your Wasel password",
        "مرحباً {{name}}،\nتلقّينا طلباً لإعادة تعيين كلمة مرور بوابة واصل. الرابط أدناه صالح لمدة 60 دقيقة.\n{{button}}\nإذا لم تطلب ذلك فتجاهل هذه الرسالة.",
        "Hello {{name}},\nWe received a request to reset your Wasel portal password. The link below is valid for 60 minutes.\n{{button}}\nIf you did not ask for this, ignore this email.",
        "إعادة تعيين كلمة المرور", "Reset password", ["name"], Mandatory: true);

    public static IEnumerable<NotificationTemplateDefinition> All => [PasswordReset, Invitation, EmailCode, WaselInvitation, WaselPasswordReset];
}

/// <summary>The Settings → Email connection for Platform.Notifications; the password is decrypted only here.</summary>
internal sealed class SmtpSettingsSource(IdentitySettingsCache settings, ISecretProtector protector) : ISmtpSettingsSource
{
    public Task<SmtpConnection> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(From(settings.Smtp, protector, password: null));

    public static SmtpConnection From(SmtpSettings smtp, ISecretProtector protector, string? password) => new(
        smtp.Enabled, smtp.Host, smtp.Port, smtp.Security, smtp.UserName,
        password ?? protector.Unprotect(SecretPurposes.SmtpPassword, smtp.PasswordProtected),
        smtp.FromAddress, smtp.FromNameAr, smtp.FromNameEn, smtp.PublicBaseUrl);
}
