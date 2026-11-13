using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Integration.Domain;

namespace Strategy.Platform.Integration.Sync;

internal sealed class SyncConnectionConfiguration : IEntityTypeConfiguration<SyncConnection>
{
    public void Configure(EntityTypeBuilder<SyncConnection> builder)
    {
        builder.ToTable("sync_connection");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceSystem).HasMaxLength(SourceSystem.CodeMaxLength);
        builder.HasIndex(x => x.SourceSystem).IsUnique();
        builder.Property(x => x.Connector).HasMaxLength(50);
        builder.Property(x => x.Mode).HasMaxLength(20);
        builder.Property(x => x.LoginUrl).HasMaxLength(500);
        builder.Property(x => x.ClientId).HasMaxLength(500);
        builder.Property(x => x.Username).HasMaxLength(320);
        builder.Property(x => x.KeyThumbprint).HasMaxLength(100);
        builder.Property(x => x.ApiVersion).HasMaxLength(10);
        builder.Property(x => x.Schedule).HasColumnType("jsonb");
        builder.Property(x => x.InstanceUrl).HasMaxLength(500);
        builder.Property(x => x.LastLimits).HasColumnType("jsonb");
        builder.Property(x => x.LastTestMessageKey).HasMaxLength(200);
        builder.Property(x => x.LastTestDetail).HasColumnType("jsonb");
        builder.Property(x => x.RunRequestedObjects).HasColumnType("jsonb");
    }
}

internal sealed class SyncMappingPackConfiguration : IEntityTypeConfiguration<SyncMappingPack>
{
    public void Configure(EntityTypeBuilder<SyncMappingPack> builder)
    {
        builder.ToTable("sync_mapping_pack");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Connector).HasMaxLength(50);
    }
}

internal sealed class SyncObjectMappingConfiguration : IEntityTypeConfiguration<SyncObjectMapping>
{
    public void Configure(EntityTypeBuilder<SyncObjectMapping> builder)
    {
        builder.ToTable("sync_object_mapping");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.HasIndex(x => new { x.PackId, x.Key }).IsUnique();
        builder.Property(x => x.SourceObject).HasMaxLength(100);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.TargetType).HasMaxLength(100);
        builder.Property(x => x.Where).HasMaxLength(1000);
        builder.Property(x => x.WatermarkField).HasMaxLength(100);
        builder.Property(x => x.ExternalIdTemplate).HasMaxLength(200);
        builder.Property(x => x.Fields).HasColumnType("jsonb");
        builder.Property(x => x.Defaults).HasColumnType("jsonb");
        builder.Property(x => x.Ignore).HasColumnType("jsonb");
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.NotesAr).HasMaxLength(4000);
        builder.Property(x => x.NotesEn).HasMaxLength(4000);
    }
}

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("sync_run");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Trigger).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Objects).HasColumnType("jsonb");
        builder.Property(x => x.MessageKey).HasMaxLength(200);
        builder.Property(x => x.MessageParams).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.ConnectionId, x.StartedAt });
    }
}

internal sealed class SyncRunLogConfiguration : IEntityTypeConfiguration<SyncRunLog>
{
    public void Configure(EntityTypeBuilder<SyncRunLog> builder)
    {
        builder.ToTable("sync_run_log");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Level).HasMaxLength(10);
        builder.Property(x => x.ObjectKey).HasMaxLength(100);
        builder.Property(x => x.MessageKey).HasMaxLength(200);
        builder.Property(x => x.Params).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.RunId, x.At });
    }
}

internal sealed class SyncExceptionConfiguration : IEntityTypeConfiguration<SyncException>
{
    public void Configure(EntityTypeBuilder<SyncException> builder)
    {
        builder.ToTable("sync_exception");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceObject).HasMaxLength(100);
        builder.Property(x => x.ExternalId).HasMaxLength(255);
        builder.Property(x => x.Reason).HasMaxLength(50);
        builder.Property(x => x.Field).HasMaxLength(100);
        builder.Property(x => x.Value).HasMaxLength(1000);
        builder.Property(x => x.MessageKey).HasMaxLength(200);
        builder.Property(x => x.Params).HasColumnType("jsonb");
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.HasIndex(x => new { x.ConnectionId, x.MappingId, x.ExternalId })
            .IsUnique().HasFilter("status = 'open'").HasDatabaseName("ux_sync_exception_open");
        builder.HasIndex(x => new { x.ConnectionId, x.Status, x.Reason });
    }
}

internal sealed class SyncPersonConfiguration : IEntityTypeConfiguration<SyncPerson>
{
    public void Configure(EntityTypeBuilder<SyncPerson> builder)
    {
        builder.ToTable("sync_person");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceSystem).HasMaxLength(SourceSystem.CodeMaxLength);
        builder.Property(x => x.SourceObject).HasMaxLength(100);
        builder.Property(x => x.ExternalId).HasMaxLength(255);
        builder.Property(x => x.MatchedBy).HasMaxLength(20);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.Name).HasMaxLength(300);
        builder.HasIndex(x => new { x.SourceSystem, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.EmployeeId);
    }
}

internal sealed class SourceHistoryConfiguration : IEntityTypeConfiguration<SourceHistoryEntry>
{
    public void Configure(EntityTypeBuilder<SourceHistoryEntry> builder)
    {
        builder.ToTable("source_history");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SourceSystem).HasMaxLength(SourceSystem.CodeMaxLength);
        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.Property(x => x.RecordExternalId).HasMaxLength(255);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.ExternalId).HasMaxLength(255);
        builder.Property(x => x.ActorName).HasMaxLength(300);
        builder.Property(x => x.ProcessName).HasMaxLength(300);
        builder.Property(x => x.ProcessInstanceId).HasMaxLength(255);
        builder.Property(x => x.ProcessStatus).HasMaxLength(50);
        builder.Property(x => x.StepStatus).HasMaxLength(50);
        builder.Property(x => x.Comments).HasMaxLength(4000);
        builder.Property(x => x.Field).HasMaxLength(200);
        builder.Property(x => x.OldValue).HasMaxLength(4000);
        builder.Property(x => x.NewValue).HasMaxLength(4000);
        builder.HasIndex(x => new { x.SourceSystem, x.Kind, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.EntityType, x.RecordId, x.OccurredAt });
    }
}

internal sealed class SyncOutageConfiguration : IEntityTypeConfiguration<SyncOutage>
{
    public void Configure(EntityTypeBuilder<SyncOutage> builder)
    {
        builder.ToTable("sync_outage");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ConnectionId, x.Status });
    }
}

internal sealed class SyncOutageRecordConfiguration : IEntityTypeConfiguration<SyncOutageRecord>
{
    public void Configure(EntityTypeBuilder<SyncOutageRecord> builder)
    {
        builder.ToTable("sync_outage_record");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EntityTypeCode).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(x => x.ExternalId).HasMaxLength(255);
        builder.Property(x => x.Resolution).HasMaxLength(20);
        builder.Property(x => x.PendingPayload).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.OutageId, x.EntityTypeCode, x.RecordId }).IsUnique();
        builder.HasIndex(x => new { x.EntityTypeCode, x.RecordId });
    }
}
