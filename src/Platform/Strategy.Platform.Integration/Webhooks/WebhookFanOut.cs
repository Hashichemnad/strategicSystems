using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Strategy.Platform.Access;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary>
/// Turns an integration event into one delivery per matching subscription (ADR 0194): active client, event subscribed, system / record type
/// allowed, and the client may read the record. Idempotent: (subscription, event key) is unique, so outbox retries never duplicate.
/// </summary>
internal sealed class WebhookFanOut<TEvent>(
    IntegrationDbContext db, IEnumerable<WebhookEventDefinition> definitions, IntegrationClientAuthority authority, ILinkRegistry links, IAccessService access,
    IClock clock) : IIntegrationEventHandler<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        var definition = definitions.First(d => d.EventType == typeof(TEvent));
        var now = clock.UtcNow;
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.Active && s.Events.Contains(definition.Code))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var clientIds = subscriptions.Select(s => s.ClientRef).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var resourceId = definition.ResourceId(integrationEvent);
        var data = JsonSerializer.SerializeToElement(integrationEvent, WebhookJson.Options);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition.Code + "|" + data.GetRawText())));
        var visible = new Dictionary<Guid, bool>();
        foreach (var subscription in subscriptions)
        {
            if (!clients.TryGetValue(subscription.ClientRef, out var client) || !client.IsUsable(now) || !client.AllowedSystems.Contains(definition.SystemCode)
                || (client.AllowedRecordTypes.Count > 0 && !client.AllowedRecordTypes.Contains(definition.ResourceType)))
            {
                continue;
            }

            if (!visible.TryGetValue(client.Id, out var canRead))
            {
                visible[client.Id] = canRead = await CanReadAsync(client, definition, resourceId, cancellationToken).ConfigureAwait(false);
            }

            if (!canRead)
            {
                continue;
            }

            var deliveryId = Guid.CreateVersion7();
            var payload = JsonSerializer.Serialize(new
            {
                id = deliveryId,
                type = definition.Code,
                occurredAt = now,
                system = definition.SystemCode,
                resource = new { type = definition.ResourceType, id = resourceId },
                data,
            }, WebhookJson.Options);
            db.Deliveries.Add(new WebhookDelivery
            {
                Id = deliveryId,
                SubscriptionRef = subscription.Id,
                ClientRef = client.Id,
                EventCode = definition.Code,
                EventKey = key,
                ResourceType = definition.ResourceType,
                ResourceId = resourceId,
                Payload = payload,
                CreatedAt = now,
                NextAttemptAt = now,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear(); // already delivered once for this subscription (outbox retry)
            }
        }
    }

    private async Task<bool> CanReadAsync(IntegrationClient client, WebhookEventDefinition definition, Guid resourceId, CancellationToken ct)
    {
        var principal = authority.PrincipalFor(client);
        if (links.Find(definition.ResourceType) is not null)
        {
            var views = await links.ResolveAsync([new LinkRef(definition.ResourceType, resourceId)], principal, ct).ConfigureAwait(false);
            return views.Count == 1 && views[0].Visibility == LinkVisibility.Visible;
        }

        return await access.CanAsync(principal, definition.Permission, cancellationToken: ct).ConfigureAwait(false);
    }
}

internal static class WebhookJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
