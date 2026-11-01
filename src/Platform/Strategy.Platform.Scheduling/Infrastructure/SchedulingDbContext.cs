using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling.Domain;

namespace Strategy.Platform.Scheduling.Infrastructure;

/// <summary>Schema <c>scheduling</c>: outbox, job runs, and the Quartz.NET job store tables (<c>qrtz_*</c>, created by migration).</summary>
public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "scheduling";

    public override string Schema => SchemaName;

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<JobRun> JobRuns => Set<JobRun>();
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(100);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.HasIndex(x => x.AvailableAt).HasFilter("processed_at is null and dead_at is null").HasDatabaseName("ix_outbox_message_pending");
        builder.HasIndex(x => x.DeadAt);
        builder.HasIndex(x => x.ProcessedAt);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("job_run");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.JobKey).HasMaxLength(100);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Message).HasMaxLength(2000);
        builder.Property(x => x.Instance).HasMaxLength(200);
        builder.HasIndex(x => new { x.JobKey, x.FiredAt });
    }
}
