using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Links.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Links.Infrastructure;

/// <summary>Schema <c>links</c>: stored links between records of different systems (ADR 0034).</summary>
public sealed class LinksDbContext(DbContextOptions<LinksDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "links";

    public override string Schema => SchemaName;

    public DbSet<RecordLink> Links => Set<RecordLink>();
}

internal sealed class RecordLinkConfiguration : IEntityTypeConfiguration<RecordLink>
{
    public void Configure(EntityTypeBuilder<RecordLink> builder)
    {
        builder.ToTable("record_link");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(50);
        builder.Property(x => x.FromType).HasMaxLength(100);
        builder.Property(x => x.ToType).HasMaxLength(100);
        builder.Property(x => x.RemovedReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.Kind, x.FromType, x.FromId, x.ToType, x.ToId }, "ux_record_link_active").IsUnique().HasFilter("active");
        builder.HasIndex(x => new { x.ToType, x.ToId });
        builder.HasIndex(x => new { x.FromType, x.FromId });
    }
}
