using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Outbox payload of one e-mail. Sensitive values and links are encrypted (Data Protection) while queued.</summary>
internal sealed record EmailPayload(
    string TemplateKey, string To, string? Name, string Language, Dictionary<string, string>? Values, string? Link, string? Protected, Guid? UserId,
    List<Guid>? Attachments = null, string? IdempotencyKey = null);

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
    IDataProtectionProvider protection,
    IClock clock) : INotificationService
{
    public const string EmailType = "notifications.email";
    public const string InAppType = "notifications.in-app";
    public const string ProtectorPurpose = "Strategy.Notifications.Outbox";

    public async Task<Result> SendAsync(NotificationRequest request, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdempotencyKey is { Length: 0 or > Infrastructure.SentRequestConfiguration.KeyMaxLength })
        {
            throw new ArgumentException("The idempotency key must have 1 to 200 characters.", nameof(request));
        }

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

        if (request.IdempotencyKey is { } key && messages.Count > 0)
        {
            return await EnqueueOnceAsync(key, request.TemplateKey, messages, transactionContext as DbContext, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Records the idempotency key and queues the messages in one transaction (the caller's, or one of its own): a key seen before queues
    /// nothing (ADR 0162).
    /// </summary>
    private async Task<Result> EnqueueOnceAsync(string key, string templateKey, List<OutboxEnvelope> messages, DbContext? caller, CancellationToken ct)
    {
        if (caller?.Database.CurrentTransaction is { } open)
        {
            if (await ClaimAsync(caller, open, key, templateKey, ct).ConfigureAwait(false))
            {
                foreach (var message in messages)
                {
                    await outbox.EnqueueAsync(caller, message, ct).ConfigureAwait(false);
                }
            }

            return Result.Success();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (!await ClaimAsync(db, transaction, key, templateKey, ct).ConfigureAwait(false))
        {
            return Result.Success();
        }

        foreach (var message in messages)
        {
            await outbox.EnqueueAsync(db, message, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<bool> ClaimAsync(DbContext context, IDbContextTransaction transaction, string key, string templateKey, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"insert into {NotifyDbContext.SchemaName}.sent_request (key, template_key, created_at) values (@key, @template, @now) on conflict (key) do nothing",
            (NpgsqlConnection)context.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = key });
        command.Parameters.Add(new NpgsqlParameter("template", NpgsqlDbType.Text) { Value = templateKey });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = clock.UtcNow });
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
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
            payload = new EmailPayload(template.Key, to, name, language, null, null, secret, userId, request.AttachmentFileIds?.ToList(), DeliveryKey(request, to));
        }
        else
        {
            payload = new EmailPayload(template.Key, to, name, language, values, request.Link, null, userId, request.AttachmentFileIds?.ToList(),
                DeliveryKey(request, to));
        }

        return new OutboxEnvelope(EmailType, payload, ExpiresAt: request.ExpiresAt);
    }

    private static string? DeliveryKey(NotificationRequest request, string to) =>
        request.IdempotencyKey is { } key ? $"{key}:{to.Trim().ToLowerInvariant()}" : null;
}
