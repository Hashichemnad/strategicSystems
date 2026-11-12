using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Integration.Infrastructure;

/// <summary>Schema <c>integration</c>: source systems and external references (ADR 0164).</summary>
public sealed class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "integration";

    public override string Schema => SchemaName;

    public DbSet<SourceSystem> SourceSystems => Set<SourceSystem>();

    public DbSet<ExternalRef> References => Set<ExternalRef>();

    // Phase 22 (ADR 0191–0194): integration clients, credentials, usage, webhooks, scheduled exports.
    public DbSet<IntegrationClient> Clients => Set<IntegrationClient>();

    public DbSet<ClientCredential> Credentials => Set<ClientCredential>();

    public DbSet<ClientUsage> Usage => Set<ClientUsage>();

    public DbSet<WebhookSubscription> Subscriptions => Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    public DbSet<WebhookAttempt> Attempts => Set<WebhookAttempt>();

    public DbSet<ExportSchedule> ExportSchedules => Set<ExportSchedule>();

    // Phase 23 (ADR 0200–0208): Salesforce standby sync.
    public DbSet<Sync.SyncConnection> SyncConnections => Set<Sync.SyncConnection>();

    public DbSet<Sync.SyncMappingPack> MappingPacks => Set<Sync.SyncMappingPack>();

    public DbSet<Sync.SyncObjectMapping> ObjectMappings => Set<Sync.SyncObjectMapping>();

    public DbSet<Sync.SyncRun> SyncRuns => Set<Sync.SyncRun>();

    public DbSet<Sync.SyncRunLog> SyncRunLog => Set<Sync.SyncRunLog>();

    public DbSet<Sync.SyncException> SyncExceptions => Set<Sync.SyncException>();

    public DbSet<Sync.SyncPerson> SyncPeople => Set<Sync.SyncPerson>();

    public DbSet<Sync.SourceHistoryEntry> SourceHistory => Set<Sync.SourceHistoryEntry>();

    public DbSet<Sync.SyncOutage> Outages => Set<Sync.SyncOutage>();

    public DbSet<Sync.SyncOutageRecord> OutageRecords => Set<Sync.SyncOutageRecord>();
}

internal sealed class SourceSystemConfiguration : IEntityTypeConfiguration<SourceSystem>
{
    public void Configure(EntityTypeBuilder<SourceSystem> builder)
    {
        builder.ToTable("source_system");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(SourceSystem.CodeMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(SourceSystem.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(SourceSystem.NameMaxLength);
        builder.Property(x => x.TypeCode).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(SourceSystem.DescriptionMaxLength);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class ExternalRefConfiguration : IEntityTypeConfiguration<ExternalRef>
{
    public void Configure(EntityTypeBuilder<ExternalRef> builder)
    {
        builder.ToTable("external_ref");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceSystem).HasMaxLength(SourceSystem.CodeMaxLength);
        builder.Property(x => x.EntityTypeCode).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(x => x.ExternalId).HasMaxLength(ExternalReferenceErrors.ExternalIdMaxLength);
        builder.Property(x => x.SourceHash).HasMaxLength(ExternalReferenceErrors.SourceHashMaxLength);
        builder.Property(x => x.SyncStatus).HasMaxLength(30);
        builder.HasIndex(x => new { x.SourceSystem, x.EntityTypeCode, x.ExternalId }).IsUnique().HasDatabaseName(ExternalRef.ExternalIndex);
        builder.HasIndex(x => new { x.SourceSystem, x.EntityTypeCode, x.RecordId }).IsUnique().HasDatabaseName(ExternalRef.RecordIndex);
        builder.HasIndex(x => new { x.EntityTypeCode, x.RecordId });
        builder.HasIndex(x => x.ExternalId);
    }
}
