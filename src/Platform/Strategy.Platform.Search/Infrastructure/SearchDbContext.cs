using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Search.Infrastructure;

/// <summary>
/// A record the user opened (recent items, ADR 0160). Personal navigation history, not a business record: not audited, the oldest rows
/// are deleted when a user has more than <see cref="RecentItem.MaxPerUser"/>.
/// </summary>
public sealed class RecentItem : Entity
{
    public const int MaxPerUser = 20;

    private RecentItem()
    {
    }

    public Guid UserId { get; private set; }

    public string ResourceType { get; private set; } = string.Empty;

    public Guid ResourceId { get; private set; }

    public DateTimeOffset VisitedAt { get; private set; }

    public static RecentItem Create(Guid userId, string resourceType, Guid resourceId, DateTimeOffset at) =>
        new() { UserId = userId, ResourceType = resourceType, ResourceId = resourceId, VisitedAt = at };

    public void Visit(DateTimeOffset at) => VisitedAt = at;
}

/// <summary>Schema <c>search</c>: the users' recent items.</summary>
public sealed class SearchDbContext(DbContextOptions<SearchDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "search";

    public override string Schema => SchemaName;

    public DbSet<RecentItem> RecentItems => Set<RecentItem>();
}

internal sealed class RecentItemConfiguration : IEntityTypeConfiguration<RecentItem>
{
    public void Configure(EntityTypeBuilder<RecentItem> builder)
    {
        builder.ToTable("recent_item");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ResourceType).HasMaxLength(100);
        builder.HasIndex(x => new { x.UserId, x.ResourceType, x.ResourceId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.VisitedAt });
    }
}
