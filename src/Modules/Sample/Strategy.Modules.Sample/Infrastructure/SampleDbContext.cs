using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Sample.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Sample.Infrastructure;

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "sample";

    public override string Schema => SchemaName;

    public DbSet<SampleItem> Items => Set<SampleItem>();
}

internal sealed class SampleItemConfiguration : IEntityTypeConfiguration<SampleItem>
{
    public void Configure(EntityTypeBuilder<SampleItem> builder)
    {
        builder.ToTable("sample_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(SampleItem.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(SampleItem.NameMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(SampleItem.DescriptionMaxLength);
        builder.Property(x => x.DescriptionEn).HasMaxLength(SampleItem.DescriptionMaxLength);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
    }
}
