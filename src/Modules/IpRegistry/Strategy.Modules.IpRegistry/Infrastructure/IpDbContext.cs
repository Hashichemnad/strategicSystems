using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.IpRegistry.Infrastructure;

/// <summary>Schema <c>ip</c> (docs/design/requests.md §4.2).</summary>
public sealed class IpDbContext(DbContextOptions<IpDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "ip";

    public override string Schema => SchemaName;

    public DbSet<IpRecord> Records => Set<IpRecord>();

    public DbSet<Contributor> Contributors => Set<Contributor>();

    public DbSet<RegistrationPeriod> Periods => Set<RegistrationPeriod>();

    public DbSet<ReminderEvent> Reminders => Set<ReminderEvent>();
}

internal sealed class IpRecordConfiguration : IEntityTypeConfiguration<IpRecord>
{
    public void Configure(EntityTypeBuilder<IpRecord> builder)
    {
        builder.ToTable("ip_record");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.NameAr).HasMaxLength(IpRecord.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(IpRecord.NameMaxLength);
        builder.Property(x => x.Description).HasMaxLength(IpRecord.TextMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(IpRecord.TextMaxLength);
        builder.Property(x => x.DescriptionEn).HasMaxLength(IpRecord.TextMaxLength);
        builder.Property(x => x.PropertyType).HasMaxLength(100);
        builder.Property(x => x.ProtectionType).HasMaxLength(100);
        builder.Property(x => x.Source).HasMaxLength(20);
        builder.Property(x => x.DocumentNo).HasMaxLength(80);
        builder.Property(x => x.Status).HasMaxLength(30);
        builder.Property(x => x.ApplicationNo).HasMaxLength(100);
        builder.Property(x => x.RegistrationNo).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(IpRecord.TextMaxLength);
        builder.Property(x => x.MoetRejectionReason).HasMaxLength(2000);
        builder.Property(x => x.SearchText).HasMaxLength(20000);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => x.DocumentVersionId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ExpiryDate);
    }
}

internal sealed class ContributorConfiguration : IEntityTypeConfiguration<Contributor>
{
    public void Configure(EntityTypeBuilder<Contributor> builder)
    {
        builder.ToTable("contributor");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.EmiratesIdMasked);
        builder.Property(x => x.ExternalName).HasMaxLength(300);
        builder.Property(x => x.ExternalOrganisation).HasMaxLength(300);
        builder.Property(x => x.EmiratesIdEncrypted).HasMaxLength(2000);
        builder.Property(x => x.EmiratesIdLast).HasMaxLength(2);
        builder.Property(x => x.Role).HasMaxLength(100);
        builder.Property(x => x.Share).HasPrecision(9, 4);
        builder.Property(x => x.SignatureStatus).HasMaxLength(20);
        builder.HasIndex(x => x.IpRecordId);
        builder.HasIndex(x => x.EmployeeId);
    }
}

internal sealed class RegistrationPeriodConfiguration : IEntityTypeConfiguration<RegistrationPeriod>
{
    public void Configure(EntityTypeBuilder<RegistrationPeriod> builder)
    {
        builder.ToTable("registration_period");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.RegistrationNo).HasMaxLength(100);
        builder.HasIndex(x => x.IpRecordId);
    }
}

internal sealed class ReminderEventConfiguration : IEntityTypeConfiguration<ReminderEvent>
{
    public void Configure(EntityTypeBuilder<ReminderEvent> builder)
    {
        builder.ToTable("reminder_event");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.HasIndex(x => new { x.IpRecordId, x.Kind });
    }
}
