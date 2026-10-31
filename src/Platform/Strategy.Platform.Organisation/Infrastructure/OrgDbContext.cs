using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Organisation.Infrastructure;

/// <summary>Organisation structure and employees, schema <c>core</c> (docs/design/organisation.md).</summary>
public sealed class OrgDbContext(DbContextOptions<OrgDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "core";

    public override string Schema => SchemaName;

    public DbSet<OrgLevel> Levels => Set<OrgLevel>();

    public DbSet<OrgSettings> Settings => Set<OrgSettings>();

    public DbSet<OrgUnit> Units => Set<OrgUnit>();

    public DbSet<OrgUnitVersion> UnitVersions => Set<OrgUnitVersion>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeAssignment> Assignments => Set<EmployeeAssignment>();

    public DbSet<EmployeePhoto> Photos => Set<EmployeePhoto>();

    public DbSet<Delegation> Delegations => Set<Delegation>();

    /// <summary>Structure, placement and manager changes feed data scopes: tell the access cache (ADR 0015).</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var affectsAccess = ChangeTracker.Entries()
            .Any(e => e.State is EntityState.Added or EntityState.Modified && e.Entity is OrgUnit or Employee);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        if (affectsAccess)
        {
            AccessChangeSignal.Raise();
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");
        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class OrgLevelConfiguration : IEntityTypeConfiguration<OrgLevel>
{
    public void Configure(EntityTypeBuilder<OrgLevel> builder)
    {
        builder.ToTable("org_level");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(OrgLevel.CodeMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(OrgLevel.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(OrgLevel.NameMaxLength);
        builder.HasIndex(x => x.Code).IsUnique();

        var seededAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            Seed("0192e3a0-0000-7000-8000-000000000001", "organisation", "المؤسسة", "Organisation", 10, seededAt),
            Seed("0192e3a0-0000-7000-8000-000000000002", "sector", "القطاع", "Sector", 20, seededAt),
            Seed("0192e3a0-0000-7000-8000-000000000003", "department", "الإدارة", "Department", 30, seededAt),
            Seed("0192e3a0-0000-7000-8000-000000000004", "section", "القسم", "Section", 40, seededAt),
            Seed("0192e3a0-0000-7000-8000-000000000005", "unit", "الوحدة", "Unit", 50, seededAt));
    }

    private static object Seed(string id, string code, string nameAr, string nameEn, int sort, DateTimeOffset at) => new
    {
        Id = Guid.Parse(id),
        Code = code,
        NameAr = nameAr,
        NameEn = nameEn,
        Sort = sort,
        Active = true,
        CreatedAt = at,
        UpdatedAt = at,
    };
}

internal sealed class OrgSettingsConfiguration : IEntityTypeConfiguration<OrgSettings>
{
    public void Configure(EntityTypeBuilder<OrgSettings> builder)
    {
        builder.ToTable("org_settings");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasData(new { Id = OrgSettings.SingletonId, EnforceLevelOrder = true });
    }
}

internal sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> builder)
    {
        builder.ToTable("org_unit");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(OrgUnit.CodeMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(OrgUnit.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(OrgUnit.NameMaxLength);
        builder.Property(x => x.LevelCode).HasMaxLength(OrgLevel.CodeMaxLength);
        builder.Property(x => x.Path).HasMaxLength(4000);
        builder.Property(x => x.CostCentre).HasMaxLength(OrgUnit.CostCentreMaxLength);
        builder.Property(x => x.NameSearch).HasMaxLength(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.Path).HasOperators("text_pattern_ops");
        builder.HasIndex(x => x.NameSearch).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrgUnitVersionConfiguration : IEntityTypeConfiguration<OrgUnitVersion>
{
    public void Configure(EntityTypeBuilder<OrgUnitVersion> builder)
    {
        builder.ToTable("org_unit_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(OrgUnit.CodeMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(OrgUnit.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(OrgUnit.NameMaxLength);
        builder.Property(x => x.LevelCode).HasMaxLength(OrgLevel.CodeMaxLength);
        builder.Property(x => x.Path).HasMaxLength(4000);
        builder.Property(x => x.CostCentre).HasMaxLength(OrgUnit.CostCentreMaxLength);
        builder.HasIndex(x => new { x.UnitId, x.ValidFrom }).IsUnique();
        builder.HasIndex(x => new { x.ValidFrom, x.ValidTo });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employee");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeNo).HasMaxLength(Employee.NumberMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(Employee.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(Employee.NameMaxLength);
        builder.Property(x => x.Email).HasMaxLength(Employee.EmailMaxLength);
        builder.Property(x => x.Mobile).HasMaxLength(Employee.MobileMaxLength);
        builder.Property(x => x.JobTitleAr).HasMaxLength(Employee.NameMaxLength);
        builder.Property(x => x.JobTitleEn).HasMaxLength(Employee.NameMaxLength);
        builder.Property(x => x.Grade).HasMaxLength(Employee.GradeMaxLength);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.NameSearch).HasMaxLength(1200);
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => x.EmployeeNo).IsUnique();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.ManagerEmployeeId);
        builder.HasIndex(x => x.NameSearch).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmployeeAssignmentConfiguration : IEntityTypeConfiguration<EmployeeAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeAssignment> builder)
    {
        builder.ToTable("employee_assignment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.JobTitleAr).HasMaxLength(Employee.NameMaxLength);
        builder.Property(x => x.JobTitleEn).HasMaxLength(Employee.NameMaxLength);
        builder.HasIndex(x => new { x.EmployeeId, x.ValidFrom }).IsUnique();
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EmployeePhotoConfiguration : IEntityTypeConfiguration<EmployeePhoto>
{
    public void Configure(EntityTypeBuilder<EmployeePhoto> builder)
    {
        builder.ToTable("employee_photo");
        builder.HasKey(x => x.EmployeeId);
        builder.Property(x => x.ContentType).HasMaxLength(50);
        builder.HasOne<Employee>().WithOne().HasForeignKey<EmployeePhoto>(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DelegationConfiguration : IEntityTypeConfiguration<Delegation>
{
    public void Configure(EntityTypeBuilder<Delegation> builder)
    {
        builder.ToTable("delegation");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(Delegation.ReasonMaxLength);
        builder.Property(x => x.CancelReason).HasMaxLength(Delegation.ReasonMaxLength);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Ignore(x => x.CoversAll);
        builder.HasIndex(x => new { x.DelegatorEmployeeId, x.Status });
        builder.HasIndex(x => new { x.DelegateEmployeeId, x.Status });
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.DelegatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.DelegateEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
