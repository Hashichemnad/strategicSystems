using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Strategy.Platform.Notifications;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces SMTP in integration tests (the Mailpit test and the E2E suite use real SMTP). Messages still go through the templates and the
/// outbox, so a test waits for delivery with <see cref="LastAsync"/>.
/// </summary>
public sealed partial class CapturingEmailTransport : IEmailTransport
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>When set, sending throws this many times before succeeding (retry tests).</summary>
    public int FailNext { get; set; }

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task SendAsync(EmailMessage message, SmtpConnection? settings = null, CancellationToken cancellationToken = default)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new EmailDeliveryException("identity.errors.smtpFailed");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The newest message to <paramref name="to"/> from <paramref name="template"/> (short identity names accepted), waiting up to 15 s.</summary>
    public async Task<EmailMessage> LastAsync(string to, string template, int? afterCount = null)
    {
        var key = template.Contains('.', StringComparison.Ordinal) ? template : "identity." + template;
        for (var i = 0; i < 150; i++)
        {
            var match = Sent.Skip(afterCount ?? 0).LastOrDefault(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase) && e.TemplateKey == key);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(100);
        }

        throw new ShouldAssertException($"No '{key}' e-mail to {to} was delivered.");
    }

    public int CountTo(string to) => Sent.Count(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase));

    /// <summary>The user id and token from a reset / invitation link.</summary>
    public static (Guid UserId, string Token) ParseLink(string link)
    {
        var match = LinkPattern().Match(link);
        match.Success.ShouldBeTrue(link);
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex("user=(?<user>[0-9a-f-]{36})&token=(?<token>[^&]+)")]
    private static partial Regex LinkPattern();
}
