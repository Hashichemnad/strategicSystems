namespace Strategy.Platform.Kernel.Domain;

/// <summary>Base for every entity: a Guid v7 identity and a buffer of domain events raised by the entity.</summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; protected init; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
