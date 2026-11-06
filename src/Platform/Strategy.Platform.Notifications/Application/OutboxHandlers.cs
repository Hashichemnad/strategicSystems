using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Email;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Loads the current template text and branding, renders and sends one e-mail; logs the delivery. Throwing schedules a retry.</summary>
internal sealed class EmailOutboxHandler(
    NotifyDbContext db, IEmailTransport transport, ISmtpSettingsSource smtp, ISettingsStore settings, IDataProtectionProvider protection,
    IClock clock, IRequestContext context, IFileService files) : IOutboxHandler
{
    public string Type => NotificationService.EmailType;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<EmailPayload>(payload, OutboxJson.Options)!;
        var values = message.Values;
        var link = message.Link;
        if (message.Protected is not null)
        {
            var part = JsonSerializer.Deserialize<ProtectedPart>(protection.CreateProtector(NotificationService.ProtectorPurpose).Unprotect(message.Protected))!;
            values = part.Values;
            link = part.Link;
        }

        var rendered = await RenderAsync(db, settings, message.TemplateKey, values ?? [], await AbsoluteAsync(link, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        var attachments = new List<EmailAttachment>();
        foreach (var fileId in message.Attachments ?? [])
        {
            var opened = await files.OpenAsync(fileId, auditDownload: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                continue;
            }

            await using var stream = opened.Value.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            attachments.Add(new EmailAttachment(opened.Value.FileName, opened.Value.ContentType, buffer.ToArray()));
        }

        var email = new EmailMessage(message.To, rendered.Subject, rendered.Html, rendered.Text, message.TemplateKey, values ?? [], rendered.Link,
            attachments.Count == 0 ? null : attachments);
        var delivery = new EmailDelivery { To = message.To, TemplateKey = message.TemplateKey, At = clock.UtcNow, CorrelationId = context.CorrelationId };
        try
        {
            await transport.SendAsync(email, null, cancellationToken).ConfigureAwait(false);
        }
        catch (EmailDeliveryException exception)
        {
            delivery.Status = DeliveryStatuses.Failed;
            delivery.Error = exception.InnerException?.Message is { } inner ? $"{exception.MessageKey}: {Cut(inner)}" : exception.MessageKey;
            db.Deliveries.Add(delivery);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders a template with the current text, layout and branding.</summary>
    public static async Task<(string Subject, string Html, string Text, string? Link)> RenderAsync(
        NotifyDbContext db, ISettingsStore settings, string templateKey, IReadOnlyDictionary<string, string> values, string? link, CancellationToken ct)
    {
        var templates = await db.Templates.AsNoTracking().Where(t => t.Key == templateKey || t.Key == NotificationTemplates.Layout.Key).ToListAsync(ct)
            .ConfigureAwait(false);
        var template = templates.Find(t => t.Key == templateKey) ?? throw new InvalidOperationException($"Unknown notification template '{templateKey}'.");
        var layout = templates.Find(t => t.Key == NotificationTemplates.Layout.Key);
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var (subject, html, text) = EmailRenderer.Render(TemplateText.From(template),
            layout is null ? NotificationTemplates.LayoutText : TemplateText.From(layout), values, link, branding);
        return (subject, html, text, link);
    }

    private async Task<string?> AbsoluteAsync(string? link, CancellationToken ct)
    {
        if (link is null || Uri.TryCreate(link, UriKind.Absolute, out _))
        {
            return link;
        }

        var baseUrl = (await smtp.GetAsync(ct).ConfigureAwait(false)).PublicBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/') + "/" + link.TrimStart('/');
    }

    private static string Cut(string value) => value.Length > 500 ? value[..500] : value;
}

/// <summary>Adds the item to the user's notification centre and pushes the new unread count over SignalR.</summary>
internal sealed class InAppOutboxHandler(NotifyDbContext db, IHubContext<NotificationHub> hub, IClock clock) : IOutboxHandler
{
    public string Type => NotificationService.InAppType;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<InAppPayload>(payload, OutboxJson.Options)!;
        var template = await db.Templates.AsNoTracking().FirstAsync(t => t.Key == message.TemplateKey, cancellationToken).ConfigureAwait(false);
        var item = new InAppNotification
        {
            UserId = message.UserId,
            TemplateKey = message.TemplateKey,
            TitleAr = Cut(EmailRenderer.Subject(template.SubjectAr, message.Values)),
            TitleEn = Cut(EmailRenderer.Subject(template.SubjectEn, message.Values)),
            Values = JsonSerializer.Serialize(message.Values),
            Link = message.Link is { } link && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) ? link : null,
            CreatedAt = clock.UtcNow,
            AwaitingDigest = message.AwaitingDigest,
        };
        db.Notifications.Add(item);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var unread = await db.Notifications.CountAsync(n => n.UserId == message.UserId && n.ReadAt == null, cancellationToken).ConfigureAwait(false);
        await hub.Clients.Group(NotificationHub.GroupOf(message.UserId)).SendAsync("notification", new { item.Id, item.TitleAr, item.TitleEn, item.Link, unread },
            cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string value) => value.Length > 300 ? value[..300] : value;
}
