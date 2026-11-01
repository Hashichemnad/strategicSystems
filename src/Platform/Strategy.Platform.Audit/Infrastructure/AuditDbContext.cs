using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Audit.Infrastructure;

/// <summary>Schema <c>audit</c>: the chain (read side), verification runs, the checkpoint and the legacy import staging table.</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "audit";

    public override string Schema => SchemaName;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    public DbSet<VerificationRun> VerificationRuns => Set<VerificationRun>();

    public DbSet<ChainCheckpoint> Checkpoints => Set<ChainCheckpoint>();

    public DbSet<LegacyImport> LegacyImports => Set<LegacyImport>();
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("entry");
        builder.HasKey(x => x.Seq);
        builder.Property(x => x.Seq).ValueGeneratedNever();
        builder.HasIndex(x => x.Id).IsUnique();
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Action).HasMaxLength(100);
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.EntityType).HasMaxLength(150);
        builder.Property(x => x.RecordId).HasMaxLength(200);
        builder.Property(x => x.ParentType).HasMaxLength(150);
        builder.Property(x => x.ParentId).HasMaxLength(200);
        builder.Property(x => x.Field).HasMaxLength(150);
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.OnBehalfOfName).HasMaxLength(256);
        builder.Property(x => x.Channel).HasMaxLength(20);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.EntityType, x.RecordId, x.Seq });
        builder.HasIndex(x => new { x.ParentType, x.ParentId, x.Seq });
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.UserId, x.OccurredAt });
        builder.HasIndex(x => new { x.SystemCode, x.OccurredAt });
        builder.HasIndex(x => new { x.Action, x.OccurredAt });
    }
}

internal sealed class VerificationRunConfiguration : IEntityTypeConfiguration<VerificationRun>
{
    public void Configure(EntityTypeBuilder<VerificationRun> builder)
    {
        builder.ToTable("verification_run");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Mode).HasMaxLength(20);
        builder.Property(x => x.MessageKey).HasMaxLength(100);
        builder.Property(x => x.Channel).HasMaxLength(20);
        builder.HasIndex(x => x.StartedAt);
    }
}

internal sealed class ChainCheckpointConfiguration : IEntityTypeConfiguration<ChainCheckpoint>
{
    public void Configure(EntityTypeBuilder<ChainCheckpoint> builder)
    {
        builder.ToTable("checkpoint");
        builder.Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class LegacyImportConfiguration : IEntityTypeConfiguration<LegacyImport>
{
    public void Configure(EntityTypeBuilder<LegacyImport> builder)
    {
        builder.ToTable("legacy_import");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(100);
        builder.Property(x => x.Channel).HasMaxLength(20);
        builder.HasIndex(x => x.OccurredAt);
    }
}
