using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Strategy.Platform.Notifications.Email;

/// <summary>MailKit SMTP delivery with the Settings → Email connection (password decrypted only for the connection).</summary>
internal sealed class SmtpEmailTransport(ISmtpSettingsSource settings) : IEmailTransport
{
    public const string SmtpNotConfigured = "identity.errors.smtpNotConfigured";
    public const string SmtpFailed = "identity.errors.smtpFailed";

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        (await settings.GetAsync(cancellationToken).ConfigureAwait(false)).IsUsable;

    public async Task SendAsync(EmailMessage message, SmtpConnection? connection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = connection ?? await settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!smtp.IsUsable)
        {
            throw new EmailDeliveryException(SmtpNotConfigured);
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(string.Join(" | ", new[] { smtp.FromNameAr, smtp.FromNameEn }.Where(n => !string.IsNullOrWhiteSpace(n))), smtp.FromAddress!));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text };
        foreach (var attachment in message.Attachments ?? [])
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, MimeKit.ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15_000 };
        try
        {
            var security = smtp.Security switch
            {
                "tls" => SecureSocketOptions.SslOnConnect,
                "starttls" => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(smtp.Host!, smtp.Port, security, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SmtpCommandException or SmtpProtocolException
            or AuthenticationException or SslHandshakeException or System.Net.Sockets.SocketException or TimeoutException or ParseException)
        {
            throw new EmailDeliveryException(SmtpFailed, exception);
        }
    }
}
