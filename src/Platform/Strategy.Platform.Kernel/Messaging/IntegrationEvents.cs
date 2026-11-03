namespace Strategy.Platform.Kernel.Messaging;

/// <summary>
/// A fact one module publishes for others (CLAUDE.md §5, ADR 0043). A JSON-serialisable record declared in the publisher's (or a
/// platform) contracts project. Delivered through the outbox, at least once: handlers must be idempotent.
/// </summary>
public interface IIntegrationEvent;

/// <summary>Publishes integration events: one outbox message per subscriber, in the caller's transaction when given.</summary>
public interface IIntegrationEventPublisher
{
    /// <param name="integrationEvent">The event.</param>
    /// <param name="transactionContext">The publisher's DbContext with an open transaction (the event exists exactly when the change commits), or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task PublishAsync<TEvent>(TEvent integrationEvent, object? transactionContext = null, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}

/// <summary>Reacts to an integration event in another module. Runs as the system principal (channel <c>job</c>); throwing means "retry later".</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class, IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
