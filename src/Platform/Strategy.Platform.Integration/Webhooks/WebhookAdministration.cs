using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Net;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Integration.Webhooks;

public sealed record WebhookEventDto(string Code, string SystemCode, string LabelAr, string LabelEn, string ResourceType, JsonElement SamplePayload);

public sealed record WebhookSubscriptionDto(
    Guid Id, Guid ClientId, string Name, string TargetUrl, IReadOnlyList<string> Events, bool Active, DateTimeOffset CreatedAt, int Pending, int Dead, uint Version);

/// <summary>A subscription with its signing secret, shown once (create / regenerate).</summary>
public sealed record WebhookSubscriptionSecretDto(WebhookSubscriptionDto Subscription, string Secret);

public sealed record WebhookSubscriptionInput(string? Name, string? TargetUrl, IReadOnlyList<string>? Events, bool? Active, uint Version = 0);

public sealed record WebhookDeliveryDto(
    Guid Id, Guid SubscriptionId, string? SubscriptionName, Guid ClientId, string? ClientName, string EventCode, string? ResourceType, Guid? ResourceId,
    string Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? NextAttemptAt, DateTimeOffset? DeliveredAt, int? LastStatusCode, string? LastError);

public sealed record WebhookAttemptDto(int Number, DateTimeOffset At, int? StatusCode, int DurationMs, string? Error);

public sealed record WebhookDeliveryDetailDto(WebhookDeliveryDto Delivery, string Payload, IReadOnlyList<WebhookAttemptDto> Attempts);

public sealed record WebhookReplayInput(IReadOnlyList<Guid>? DeliveryIds);

/// <summary>Webhook subscriptions, delivery log, dead letters and replay (ADR 0194), Settings → Integrations.</summary>
public sealed class WebhookAdministration(
    IntegrationDbContext db, IEnumerable<WebhookEventDefinition> definitions, ISecretProtector secrets, OutboundUrlPolicy outbound, IConfiguration configuration,
    IAuditLog audit, IClock clock)
{
    public const string PingEvent = "integration.ping";

    public IReadOnlyList<WebhookEventDto> Catalogue() =>
        definitions.OrderBy(d => d.SystemCode, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal)
            .Select(d => new WebhookEventDto(d.Code, d.SystemCode, d.LabelAr, d.LabelEn, d.ResourceType, SamplePayload(d)))
            .ToList();

    public static JsonElement SamplePayload(WebhookEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.SerializeToElement(new
        {
            id = Guid.Parse("0190a4f0-0000-7000-8000-000000000001"),
            type = definition.Code,
            occurredAt = new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero),
            system = definition.SystemCode,
            resource = new { type = definition.ResourceType, id = definition.ResourceId(definition.Sample) },
            data = JsonSerializer.SerializeToElement(definition.Sample, definition.EventType, WebhookJson.Options),
        }, WebhookJson.Options);
    }

    public async Task<Result<IReadOnlyList<WebhookSubscriptionDto>>> ListAsync(Guid clientId, CancellationToken ct)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == clientId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.Subscriptions.AsNoTracking().Where(s => s.ClientRef == clientId).OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<WebhookSubscriptionDto>>.Success(await ToDtosAsync(rows, ct).ConfigureAwait(false));
    }

    public async Task<Result<WebhookSubscriptionSecretDto>> CreateAsync(Guid clientId, WebhookSubscriptionInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct).ConfigureAwait(false);
        if (client is null)
        {
            return Error.NotFound();
        }

        if (client.Status == ClientStatuses.Revoked)
        {
            return Error.Rule("integration.client.revoked", "integration.errors.clientRevoked");
        }

        var errors = Validate(input, client);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var secret = "whsec_" + ClientSecrets.Random(32);
        var subscription = WebhookSubscription.Create(clientId, input.Name!, input.TargetUrl!, input.Events!, secrets.Protect(WebhookDispatcher.SecretPurpose, secret));
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WebhookSubscriptionSecretDto((await ToDtosAsync([subscription], ct).ConfigureAwait(false))[0], secret);
    }

    public async Task<Result<WebhookSubscriptionDto>> UpdateAsync(Guid clientId, Guid id, WebhookSubscriptionInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.ClientRef == clientId, ct).ConfigureAwait(false);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct).ConfigureAwait(false);
        if (subscription is null || client is null)
        {
            return Error.NotFound();
        }

        var errors = Validate(input, client);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(subscription).Property(x => x.Version).OriginalValue = input.Version;
        subscription.Update(input.Name!, input.TargetUrl!, input.Events!, input.Active ?? subscription.Active);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return (await ToDtosAsync([subscription], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<WebhookSubscriptionSecretDto>> RegenerateSecretAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.ClientRef == clientId, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return Error.NotFound();
        }

        var secret = "whsec_" + ClientSecrets.Random(32);
        subscription.SetSecret(secrets.Protect(WebhookDispatcher.SecretPurpose, secret));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WebhookSubscriptionSecretDto((await ToDtosAsync([subscription], ct).ConfigureAwait(false))[0], secret);
    }

    /// <summary>Queues a <c>integration.ping</c> delivery for the subscription (sent by the next dispatcher run).</summary>
    public async Task<Result<WebhookDeliveryDto>> SendTestAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.ClientRef == clientId, ct).ConfigureAwait(false);
        if (subscription is null)
        {
            return Error.NotFound();
        }

        var now = clock.UtcNow;
        var deliveryId = Guid.CreateVersion7();
        var delivery = new WebhookDelivery
        {
            Id = deliveryId,
            SubscriptionRef = id,
            ClientRef = clientId,
            EventCode = PingEvent,
            EventKey = "ping-" + deliveryId.ToString("N"),
            Payload = JsonSerializer.Serialize(new { id = deliveryId, type = PingEvent, occurredAt = now, data = new { subscription = id } }, WebhookJson.Options),
            CreatedAt = now,
            NextAttemptAt = now,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await DeliveryDtosAsync([delivery], ct).ConfigureAwait(false))[0];
    }

    /// <summary>The delivery log (one client, or every client with <paramref name="clientId"/> null), newest first; <paramref name="status"/> filters (e.g. <c>dead</c>).</summary>
    public async Task<PagedResult<WebhookDeliveryDto>> DeliveriesAsync(Guid? clientId, string? status, PageRequest paging, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paging);
        var query = db.Deliveries.AsNoTracking();
        if (clientId is { } client)
        {
            query = query.Where(d => d.ClientRef == client);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(d => d.Status == status);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(d => d.CreatedAt).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<WebhookDeliveryDto>(await DeliveryDtosAsync(rows, ct).ConfigureAwait(false), paging.Page, paging.PageSize, total);
    }

    public async Task<Result<WebhookDeliveryDetailDto>> DeliveryAsync(Guid id, CancellationToken ct)
    {
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        if (delivery is null)
        {
            return Error.NotFound();
        }

        var attempts = await db.Attempts.AsNoTracking().Where(a => a.DeliveryRef == id).OrderBy(a => a.Number)
            .Select(a => new WebhookAttemptDto(a.Number, a.At, a.StatusCode, a.DurationMs, a.Error)).ToListAsync(ct).ConfigureAwait(false);
        return new WebhookDeliveryDetailDto((await DeliveryDtosAsync([delivery], ct).ConfigureAwait(false))[0], delivery.Payload, attempts);
    }

    /// <summary>Puts deliveries (dead, failed or delivered) back in the queue with a fresh attempt count. Audited.</summary>
    public async Task<Result<int>> ReplayAsync(WebhookReplayInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ids = (input.DeliveryIds ?? []).Distinct().Take(500).ToList();
        if (ids.Count == 0)
        {
            return Error.Validation([new FieldError("deliveryIds", "validation.notEmpty", "validation.notEmpty")]);
        }

        var deliveries = await db.Deliveries.Where(d => ids.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        foreach (var delivery in deliveries)
        {
            delivery.Status = DeliveryStatuses.Pending;
            delivery.Attempts = 0;
            delivery.NextAttemptAt = now;
            delivery.DeliveredAt = null;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.webhook.replay", true, IntegrationPermissions.SystemCode, null, null,
            new { deliveries = deliveries.Select(d => d.Id).ToList() }), ct).ConfigureAwait(false);
        return deliveries.Count;
    }

    private List<FieldError> Validate(WebhookSubscriptionInput input, IntegrationClient client)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
        {
            errors.Add(new FieldError("name", "validation.notEmpty", "validation.notEmpty"));
        }

        var url = input.TargetUrl?.Trim();
        var allowHttp = configuration.GetValue("Integration:AllowHttpWebhooks", false);
        if (string.IsNullOrEmpty(url) || url.Length > WebhookSubscription.UrlMaxLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
        {
            errors.Add(new FieldError("targetUrl", "integration.errors.webhookUrl", "integration.errors.webhookUrl"));
        }
        else if (outbound.Check(url) is var refusal and not OutboundRefusal.None)
        {
            // ADR 0155: never the host itself, link-local / cloud metadata or (when configured) private networks.
            errors.Add(new FieldError("targetUrl", OutboundUrlPolicy.MessageKey, OutboundUrlPolicy.MessageKey, new Dictionary<string, object?> { ["reason"] = refusal.ToString() }));
        }

        var known = definitions.Where(d => client.AllowedSystems.Contains(d.SystemCode)).Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
        if (input.Events is not { Count: > 0 } events)
        {
            errors.Add(new FieldError("events", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (events.Any(e => !known.Contains(e)))
        {
            errors.Add(new FieldError("events", "integration.errors.webhookEvent", "integration.errors.webhookEvent"));
        }

        return errors;
    }

    private async Task<IReadOnlyList<WebhookSubscriptionDto>> ToDtosAsync(IReadOnlyList<WebhookSubscription> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Deliveries.AsNoTracking().Where(d => ids.Contains(d.SubscriptionRef) && d.Status != DeliveryStatuses.Delivered)
            .GroupBy(d => new { d.SubscriptionRef, d.Status }).Select(g => new { g.Key.SubscriptionRef, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new WebhookSubscriptionDto(r.Id, r.ClientRef, r.Name, r.TargetUrl, r.Events, r.Active, r.CreatedAt,
            counts.Where(c => c.SubscriptionRef == r.Id && c.Status != DeliveryStatuses.Dead).Sum(c => c.Count),
            counts.Where(c => c.SubscriptionRef == r.Id && c.Status == DeliveryStatuses.Dead).Sum(c => c.Count), r.Version)).ToList();
    }

    private async Task<IReadOnlyList<WebhookDeliveryDto>> DeliveryDtosAsync(IReadOnlyList<WebhookDelivery> rows, CancellationToken ct)
    {
        var subscriptionIds = rows.Select(r => r.SubscriptionRef).Distinct().ToList();
        var names = await db.Subscriptions.AsNoTracking().Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);
        var clientIds = rows.Select(r => r.ClientRef).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameEn ?? c.NameAr, ct).ConfigureAwait(false);
        return rows.Select(d => new WebhookDeliveryDto(d.Id, d.SubscriptionRef, names.GetValueOrDefault(d.SubscriptionRef), d.ClientRef, clients.GetValueOrDefault(d.ClientRef),
            d.EventCode, d.ResourceType, d.ResourceId, d.Status, d.Attempts, d.CreatedAt,
            d.Status is DeliveryStatuses.Pending or DeliveryStatuses.Retrying ? d.NextAttemptAt : null, d.DeliveredAt, d.LastStatusCode, d.LastError)).ToList();
    }
}
