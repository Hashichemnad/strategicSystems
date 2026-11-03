using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Platform.Scheduling;

/// <summary>A subscription declared with <see cref="IntegrationEventRegistration.AddIntegrationEventHandler{TEvent, THandler}"/>.</summary>
public sealed record IntegrationEventSubscription(Type EventType, Type HandlerType)
{
    public string MessageType => "integration:" + HandlerType.FullName;
}

public static class IntegrationEventRegistration
{
    /// <summary>Subscribes <typeparamref name="THandler"/> to <typeparamref name="TEvent"/> (ADR 0043).</summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddSingleton(new IntegrationEventSubscription(typeof(TEvent), typeof(THandler)));
        services.AddScoped<THandler>();
        services.AddScoped<IOutboxHandler, IntegrationEventOutboxHandler<TEvent, THandler>>();
        services.TryAddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();
        return services;
    }
}

/// <summary>One outbox message per subscriber, so each retries alone.</summary>
internal sealed class IntegrationEventPublisher(IOutbox outbox, IEnumerable<IntegrationEventSubscription> subscriptions) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, object? transactionContext = null, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        foreach (var subscription in subscriptions.Where(s => s.EventType == typeof(TEvent)))
        {
            var envelope = new OutboxEnvelope(subscription.MessageType, integrationEvent);
            if (transactionContext is DbContext db)
            {
                await outbox.EnqueueAsync(db, envelope, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await outbox.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

internal sealed class IntegrationEventOutboxHandler<TEvent, THandler>(THandler handler) : IOutboxHandler
    where TEvent : class, IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
{
    public string Type { get; } = new IntegrationEventSubscription(typeof(TEvent), typeof(THandler)).MessageType;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, OutboxJson.Options)
            ?? throw new InvalidOperationException($"Empty integration event payload for {typeof(TEvent).Name}.");
        return handler.HandleAsync(integrationEvent, cancellationToken);
    }
}
