namespace Strategy.Platform.Kernel.Domain;

/// <summary>
/// Business entity with the standard audit columns (set by the persistence interceptor, never by handlers)
/// and an optimistic concurrency token mapped to PostgreSQL <c>xmin</c>.
/// </summary>
public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>Row version (PostgreSQL <c>xmin</c>). Clients send it back on update; a mismatch is a 409.</summary>
    public uint Version { get; private set; }

    void IAuditable.MarkCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
        UpdatedAt = at;
        UpdatedBy = by;
    }

    void IAuditable.MarkUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset UpdatedAt { get; }

    Guid? UpdatedBy { get; }

    void MarkCreated(DateTimeOffset at, Guid? by);

    void MarkUpdated(DateTimeOffset at, Guid? by);
}
