using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary>
/// A webhook event (ADR 0194): an integration event offered to integration clients. <paramref name="ResourceType"/> + <paramref name="ResourceId"/>:
/// the record the event is about — a client receives the event only if it may read it (Links registry, else <paramref name="Permission"/>).
/// <paramref name="Sample"/>: an example event for the developer portal.
/// </summary>
public sealed record WebhookEventDefinition(
    string Code,
    string SystemCode,
    string LabelAr,
    string LabelEn,
    Type EventType,
    string ResourceType,
    string Permission,
    Func<object, Guid> ResourceId,
    object Sample);

public static class WebhookRegistration
{
    /// <summary>Offers <typeparamref name="TEvent"/> to webhook subscriptions: registers the definition and the fan-out handler (outbox, ADR 0043).</summary>
    public static IServiceCollection AddWebhookEvent<TEvent>(
        this IServiceCollection services, string code, string systemCode, string labelAr, string labelEn, string resourceType, string permission,
        Func<TEvent, Guid> resourceId, TEvent sample)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        services.AddSingleton(new WebhookEventDefinition(code, systemCode, labelAr, labelEn, typeof(TEvent), resourceType, permission, e => resourceId((TEvent)e), sample));
        services.AddIntegrationEventHandler<TEvent, WebhookFanOut<TEvent>>();
        return services;
    }
}
