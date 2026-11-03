using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Links.Domain;

/// <summary>A link from one record to another (ADR 0034). Never deleted: a removed link is inactive with a reason.</summary>
[Audited(EntityType)]
public sealed class RecordLink : AuditableEntity
{
    public const string EntityType = "links.record_link";

    private RecordLink()
    {
    }

    public string Kind { get; private set; } = string.Empty;

    public string FromType { get; private set; } = string.Empty;

    public Guid FromId { get; private set; }

    public string ToType { get; private set; } = string.Empty;

    public Guid ToId { get; private set; }

    public bool Active { get; private set; } = true;

    public DateTimeOffset? RemovedAt { get; private set; }

    public string? RemovedReason { get; private set; }

    public static RecordLink Create(string kind, LinkRef from, LinkRef to) =>
        new() { Kind = kind, FromType = from.Type, FromId = from.Id, ToType = to.Type, ToId = to.Id };

    public void Remove(DateTimeOffset at, string? reason)
    {
        Active = false;
        RemovedAt = at;
        RemovedReason = reason;
    }
}
