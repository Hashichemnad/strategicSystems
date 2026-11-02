using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Outbox payload of one e-mail. Sensitive values and links are encrypted (Data Protection) while queued.</summary>
internal sealed record EmailPayload(
    string TemplateKey, string To, string? Name, string Language, Dictionary<string, string>? Values, string? Link, string? Protected, Guid? UserId);

/// <summary>Outbox payload of one in-app item.</summary>
internal sealed record InAppPayload(Guid UserId, string TemplateKey, Dictionary<string, string> Values, string? Link, bool AwaitingDigest);

internal sealed record ProtectedPart(Dictionary<string, string>? Values, string? Link);

/// <summary>
/// Resolves recipients and their preferences, then writes one outbox message per recipient and channel (CLAUDE.md §14). Mandatory
/// templates (sign-in codes, password links) ignore preferences and the digest.
/// </summary>
internal sealed class NotificationService(
    NotifyDbContext db,
    IOutbox outbox,
    IEmailTransport transport,
    INotificationRecipientDirectory directory,
    IDataProtectionProvider protection) : INotificationService
{
    public const string EmailType = "notifications.email";
    public const string InAppType = "notifications.in-app";
    public const string ProtectorPurpose = "Strategy.Notifications.Outbox";

    public async Task<Result> SendAsync(NotificationRequest request, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == request.TemplateKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Unknown notification template '{request.TemplateKey}'.");
        var wantsEmail = request.Channels.HasFlag(NotificationChannels.Email);
        var emailReady = wantsEmail && await transport.IsConfiguredAsync(cancellationToken).ConfigureAwait(false);
        if (wantsEmail && !emailReady && request.Channels == NotificationChannels.Email)
        {
            return Error.Rule("notifications.email.notConfigured", "identity.errors.smtpNotConfigured");
        }

        var values = new Dictionary<string, string>(request.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        var userIds = request.Recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var resolved = userIds.Count == 0 ? [] : await directory.ResolveAsync(userIds, cancellationToken).ConfigureAwait(false);
        var preferences = userIds.Count == 0 ? [] : await db.Preferences.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && (p.EventKey == request.TemplateKey || p.EventKey == NotificationPreference.AllEvents))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var messages = new List<OutboxEnvelope>();
        foreach (var recipient in request.Recipients)
        {
            if (recipient.UserId is not { } userId)
            {
                if (emailReady && !string.IsNullOrWhiteSpace(recipient.Email))
                {
                    messages.Add(Email(request, template, values, recipient.Email, recipient.Name, recipient.Language ?? "ar", null));
                }

                continue;
            }

            var user = resolved.FirstOrDefault(u => u.UserId == userId);
            if (user is null || !user.Active)
            {
                continue;
            }

            var specific = preferences.Find(p => p.UserId == userId && p.EventKey == request.TemplateKey);
            var general = preferences.Find(p => p.UserId == userId && p.EventKey == NotificationPreference.AllEvents);
            var email = template.Mandatory || (specific?.Email ?? general?.Email ?? true);
            var inApp = specific?.InApp ?? general?.InApp ?? true;
            var digest = !template.Mandatory && (general?.Digest ?? false);

            var sendEmail = emailReady && email && !digest && !string.IsNullOrWhiteSpace(user.Email);
            if (sendEmail)
            {
                messages.Add(Email(request, template, values, user.Email!, user.Name, user.Language, userId));
            }

            var awaitingDigest = wantsEmail && email && digest;
            if ((request.Channels.HasFlag(NotificationChannels.InApp) && inApp) || awaitingDigest)
            {
                messages.Add(new OutboxEnvelope(InAppType, new InAppPayload(userId, template.Key, request.Sensitive ? [] : values, request.Link, awaitingDigest)));
            }
        }

        foreach (var message in messages)
        {
            if (transactionContext is DbContext context)
            {
                await outbox.EnqueueAsync(context, message, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await outbox.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success();
    }

    private OutboxEnvelope Email(
        NotificationRequest request, NotificationTemplate template, Dictionary<string, string> values, string to, string? name, string language, Guid? userId)
    {
        values = new Dictionary<string, string>(values, StringComparer.Ordinal);
        values.TryAdd("name", name ?? to);
        EmailPayload payload;
        if (request.Sensitive)
        {
            var secret = protection.CreateProtector(ProtectorPurpose).Protect(JsonSerializer.Serialize(new ProtectedPart(values, request.Link)));
            payload = new EmailPayload(template.Key, to, name, language, null, null, secret, userId);
        }
        else
        {
            payload = new EmailPayload(template.Key, to, name, language, values, request.Link, null, userId);
        }

        return new OutboxEnvelope(EmailType, payload, ExpiresAt: request.ExpiresAt);
    }
}
