using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Strategy.Platform.Identity.Email;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>Replaces SMTP in integration tests; the E2E suite uses Mailpit instead.</summary>
public sealed partial class CapturingEmailSender : IIdentityEmailSender
{
    public ConcurrentQueue<IdentityEmail> Sent { get; } = new();

    public Task SendAsync(IdentityEmail email, CancellationToken cancellationToken)
    {
        Sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public IdentityEmail Last(string to, string template) =>
        Sent.Last(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase) && e.Template == template);

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
