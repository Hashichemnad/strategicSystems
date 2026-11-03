using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Calendar.Infrastructure;

/// <summary>Schema <c>calendar</c>: business hours, holidays, unit business hours (ADR 0027).</summary>
public sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "calendar";

    public override string Schema => SchemaName;

    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<UnitBusinessHours> UnitBusinessHours => Set<UnitBusinessHours>();
}

internal sealed class BusinessHoursConfiguration : IEntityTypeConfiguration<BusinessHours>
{
    public void Configure(EntityTypeBuilder<BusinessHours> builder)
    {
        builder.ToTable("business_hours");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.TimeZone).HasMaxLength(100);
        builder.Property(x => x.Days).HasColumnType("jsonb");
        // Exactly one default record.
        builder.HasIndex(x => x.IsDefault, "ix_business_hours_one_default").IsUnique().HasFilter("is_default");
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holiday");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.HasIndex(x => new { x.StartDate, x.EndDate });
    }
}

internal sealed class UnitBusinessHoursConfiguration : IEntityTypeConfiguration<UnitBusinessHours>
{
    public void Configure(EntityTypeBuilder<UnitBusinessHours> builder)
    {
        builder.ToTable("unit_business_hours");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.UnitId).IsUnique();
    }
}
