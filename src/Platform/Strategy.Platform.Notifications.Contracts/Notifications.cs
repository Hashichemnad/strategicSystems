using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Notifications;

/// <summary>
/// A notification template declared in code by its owning component or module (event key → AR / EN subject and body).
/// The default text is seeded into <c>notify.template</c> and stays editable in Settings → Notifications.
/// Body: one paragraph per line; <c>{{name}}</c> placeholders; a line <c>{{button}}</c> places the link button; <c>{{code}}</c> alone on a
/// line is shown large (one-time codes).
/// </summary>
public sealed record NotificationTemplateDefinition(
    string Key,
    string SystemCode,
    string NameAr,
    string NameEn,
    string SubjectAr,
    string SubjectEn,
    string BodyAr,
    string BodyEn,
    string? ButtonAr = null,
    string? ButtonEn = null,
    IReadOnlyList<string>? Placeholders = null,
    bool Mandatory = false);

[Flags]
public enum NotificationChannels
{
    None = 0,
    Email = 1,
    InApp = 2,
    All = Email | InApp,
}

/// <summary>A recipient: a user (address and language looked up) or a bare e-mail address.</summary>
public sealed record NotificationRecipient(Guid? UserId, string? Email = null, string? Name = null, string? Language = null)
{
    public static NotificationRecipient User(Guid userId) => new(userId);

    public static NotificationRecipient Address(string email, string? name = null, string? language = null) => new(null, email, name, language);
}

/// <summary>What to send. <paramref name="Sensitive"/> values (codes, reset links) are encrypted while queued and never logged.</summary>
public sealed record NotificationRequest(
    string TemplateKey,
    IReadOnlyList<NotificationRecipient> Recipients,
    IReadOnlyDictionary<string, string>? Values = null,
    string? Link = null,
    NotificationChannels Channels = NotificationChannels.All,
    DateTimeOffset? ExpiresAt = null,
    bool Sensitive = false);

/// <summary>Queues notifications in the outbox. With a DbContext in a transaction the messages commit with the business change.</summary>
public interface INotificationService
{
    /// <summary>Fails (<c>identity.errors.smtpNotConfigured</c>) only when e-mail is the only channel and e-mail is not set up.</summary>
    Task<Result> SendAsync(NotificationRequest request, object? transactionContext = null, CancellationToken cancellationToken = default);
}

/// <summary>Address, language and name of users (implemented by Platform.Identity).</summary>
public interface INotificationRecipientDirectory
{
    Task<IReadOnlyList<ResolvedRecipient>> ResolveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}

public sealed record ResolvedRecipient(Guid UserId, string? Email, string? Name, string Language, bool Active);

/// <summary>SMTP connection settings (stored by Platform.Identity, Settings → Email); the password is decrypted only for sending.</summary>
public sealed record SmtpConnection(
    bool Enabled, string? Host, int Port, string Security, string? UserName, string? Password, string? FromAddress, string? FromNameAr, string? FromNameEn,
    string? PublicBaseUrl = null)
{
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public interface ISmtpSettingsSource
{
    Task<SmtpConnection> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>A rendered e-mail plus what it was made from (template key, values, link) for delivery logs and tests.</summary>
public sealed record EmailMessage(
    string To, string Subject, string Html, string Text, string TemplateKey, IReadOnlyDictionary<string, string> Values, string? Link);

/// <summary>Delivers a rendered e-mail (MailKit SMTP in production; replaced in tests).</summary>
public interface IEmailTransport
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="EmailDeliveryException"/> when the server is not configured or refuses the message.</summary>
    Task SendAsync(EmailMessage message, SmtpConnection? settings = null, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryException(string messageKey, Exception? inner = null) : Exception(messageKey, inner)
{
    public string MessageKey { get; } = messageKey;
}

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationTemplate(this IServiceCollection services, NotificationTemplateDefinition definition)
    {
        services.AddSingleton(definition);
        return services;
    }
}

/// <summary>Sends the bilingual test message straight away with the given (possibly unsaved) settings (Settings → Email → Test).</summary>
public interface ITestEmailSender
{
    Task<Result> SendTestAsync(string to, SmtpConnection settings, CancellationToken cancellationToken = default);
}
