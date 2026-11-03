using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.AI.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.AI.Infrastructure;

/// <summary>Schema <c>ai</c>: providers and the usage log (docs/design/ai.md).</summary>
public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "ai";

    public override string Schema => SchemaName;

    public DbSet<AiProvider> Providers => Set<AiProvider>();

    public DbSet<AiUsage> Usage => Set<AiUsage>();
}

internal sealed class AiProviderConfiguration : IEntityTypeConfiguration<AiProvider>
{
    public void Configure(EntityTypeBuilder<AiProvider> builder)
    {
        builder.ToTable("provider");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Type).HasMaxLength(40);
        builder.Property(x => x.Endpoint).HasMaxLength(500);
        builder.Property(x => x.DefaultModel).HasMaxLength(200);
        builder.Property(x => x.Models).HasColumnType("jsonb");
        builder.Property(x => x.Temperature).HasPrecision(4, 2);
    }
}

internal sealed class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("usage_log");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.Feature).HasMaxLength(60);
        builder.Property(x => x.ProviderType).HasMaxLength(40);
        builder.Property(x => x.Model).HasMaxLength(200);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ResourceType).HasMaxLength(100);
        builder.Property(x => x.ResourceId).HasMaxLength(100);
        builder.Property(x => x.Channel).HasMaxLength(20);
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.UserId, x.OccurredAt });
    }
}
