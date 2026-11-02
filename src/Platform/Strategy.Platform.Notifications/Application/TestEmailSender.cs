using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Settings → Email → Test: renders the test template and sends it now (not queued), so the administrator sees the result.</summary>
internal sealed class TestEmailSender(NotifyDbContext db, ISettingsStore settings, IEmailTransport transport) : ITestEmailSender
{
    public async Task<Result> SendTestAsync(string to, SmtpConnection connection, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>();
        var (subject, html, text, _) = await EmailOutboxHandler.RenderAsync(db, settings, NotificationTemplates.Test.Key, values, null, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await transport.SendAsync(new EmailMessage(to, subject, html, text, NotificationTemplates.Test.Key, values, null), connection, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (EmailDeliveryException exception)
        {
            return Error.Rule("identity.smtp.testFailed", exception.MessageKey);
        }
    }
}
