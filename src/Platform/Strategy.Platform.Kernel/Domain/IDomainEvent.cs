namespace Strategy.Platform.Kernel.Domain;

/// <summary>Something that happened inside a module. Cross-module reactions use integration events via the outbox.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent;
