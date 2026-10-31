using System.Net;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Email;

/// <summary>A bilingual email: template key plus values. Subject and body come from the embedded templates (AR and EN side by side).</summary>
public sealed record IdentityEmail(string To, string Template, IReadOnlyDictionary<string, string> Values, string? Link = null);

/// <summary>Sends identity emails (password reset, invitation, sign-in code, test). Replaced by Notifications in Phase 3 (ADR 0014).</summary>
public interface IIdentityEmailSender
{
    /// <summary>Throws <see cref="EmailDeliveryException"/> when the server is not configured or refuses the message.</summary>
    Task SendAsync(IdentityEmail email, CancellationToken cancellationToken);
}

public sealed class EmailDeliveryException(string messageKey, Exception? inner = null) : Exception(messageKey, inner)
{
    public string MessageKey { get; } = messageKey;
}

/// <summary>Renders the side-by-side bilingual template (CLAUDE.md §10: Arabic right, English left).</summary>
public static class IdentityEmailRenderer
{
    private static readonly Lazy<string> Layout = new(() => ReadResource("layout.html"));
    private static readonly Lazy<JsonElement> Messages = new(() => JsonDocument.Parse(ReadResource("messages.json")).RootElement);

    public static (string Subject, string Html, string Text) Render(IdentityEmail email, BrandingSettings? branding = null)
    {
        ArgumentNullException.ThrowIfNull(email);
        var template = Messages.Value.GetProperty(email.Template);
        var organisationAr = Blank(branding?.OrganisationNameAr);
        var organisationEn = Blank(branding?.OrganisationNameEn);

        // Branding from Settings (SPEC §0.2): an optional header line above the body and a footer replacing the default one.
        string Part(string lang)
        {
            var header = Blank(lang == "ar" ? branding?.EmailHeaderAr : branding?.EmailHeaderEn);
            var body = Body(template, lang, email);
            return header is null ? body : $"<p style=\"margin:0 0 12px;line-height:1.6;font-weight:600\">{Encode(header)}</p>{body}";
        }

        string FooterText(string lang) =>
            Blank(lang == "ar" ? branding?.EmailFooterAr : branding?.EmailFooterEn) ?? Messages.Value.GetProperty("footer").GetProperty(lang).GetString()!;
        var subjectEn = template.GetProperty("subject").GetProperty("en").GetString()!;
        var subjectAr = template.GetProperty("subject").GetProperty("ar").GetString()!;
        var org = Messages.Value.GetProperty("orgName");
        var organisation = $"{organisationEn ?? org.GetProperty("en").GetString()} · {organisationAr ?? org.GetProperty("ar").GetString()}";

        var html = Layout.Value
            .Replace("{{subject_en}}", Encode(subjectEn), StringComparison.Ordinal)
            .Replace("{{subject_ar}}", Encode(subjectAr), StringComparison.Ordinal)
            .Replace("{{body_en}}", Part("en"), StringComparison.Ordinal)
            .Replace("{{body_ar}}", Part("ar"), StringComparison.Ordinal)
            .Replace("{{footer_en}}", Encode(FooterText("en")), StringComparison.Ordinal)
            .Replace("{{footer_ar}}", Encode(FooterText("ar")), StringComparison.Ordinal)
            .Replace("{{org}}", Encode(organisation), StringComparison.Ordinal);

        var text = new StringBuilder();
        foreach (var lang in new[] { "ar", "en" })
        {
            foreach (var line in template.GetProperty("body").GetProperty(lang).EnumerateArray())
            {
                text.AppendLine(Fill(line.GetString()!, email, buttonText: email.Link ?? string.Empty, encode: false));
            }

            text.AppendLine();
        }

        return ($"{subjectAr} | {subjectEn}", html, text.ToString());
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Body(JsonElement template, string lang, IdentityEmail email)
    {
        var button = string.Empty;
        if (email.Link is not null && template.TryGetProperty("button", out var label))
        {
            button = $"<a href=\"{WebUtility.HtmlEncode(email.Link)}\" style=\"display:inline-block;background:#1f3a68;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none\">{Encode(label.GetProperty(lang).GetString()!)}</a>";
        }

        var builder = new StringBuilder();
        foreach (var line in template.GetProperty("body").GetProperty(lang).EnumerateArray())
        {
            var raw = line.GetString()!;
            var style = raw == "{{code}}" ? "font-size:28px;letter-spacing:6px;font-weight:700;margin:12px 0" : "margin:0 0 12px;line-height:1.6";
            builder.Append("<p style=\"").Append(style).Append("\">").Append(Fill(raw, email, button, encode: true)).Append("</p>");
        }

        return builder.ToString();
    }

    private static string Fill(string line, IdentityEmail email, string buttonText, bool encode)
    {
        var result = encode ? Encode(line).Replace("{{button}}", buttonText, StringComparison.Ordinal) : line.Replace("{{button}}", buttonText, StringComparison.Ordinal);
        foreach (var (key, value) in email.Values)
        {
            result = result.Replace("{{" + key + "}}", encode ? Encode(value) : value, StringComparison.Ordinal);
        }

        return result;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string ReadResource(string name)
    {
        var assembly = typeof(IdentityEmailRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Templates." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>MailKit SMTP sender using the Email server settings (password decrypted only for the connection).</summary>
internal sealed class SmtpIdentityEmailSender(IdentitySettingsCache settings, ISecretProtector protector, ISettingsStore store) : IIdentityEmailSender
{
    public async Task SendAsync(IdentityEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var smtp = settings.Smtp;
        var branding = await store.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        await SendWithAsync(smtp, protector.Unprotect(SecretPurposes.SmtpPassword, smtp.PasswordProtected), email, cancellationToken, branding)
            .ConfigureAwait(false);
    }

    public static async Task SendWithAsync(
        SmtpSettings smtp, string? password, IdentityEmail email, CancellationToken cancellationToken, BrandingSettings? branding = null)
    {
        if (!smtp.Enabled || string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            throw new EmailDeliveryException("identity.errors.smtpNotConfigured");
        }

        var (subject, html, text) = IdentityEmailRenderer.Render(email, branding);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.Join(" | ", new[] { smtp.FromNameAr, smtp.FromNameEn }.Where(n => !string.IsNullOrWhiteSpace(n))), smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 15_000 };
        try
        {
            var security = smtp.Security switch
            {
                SmtpSecurityModes.Tls => SecureSocketOptions.SslOnConnect,
                SmtpSecurityModes.StartTls => SecureSocketOptions.StartTls,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SmtpCommandException or SmtpProtocolException
            or AuthenticationException or SslHandshakeException or System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new EmailDeliveryException("identity.errors.smtpFailed", exception);
        }
    }
}
