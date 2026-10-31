using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Access.Infrastructure;

/// <summary>Systems, permission catalogue, roles, assignments and record roles, schema <c>access</c> (docs/design/access.md).</summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "access";

    public override string Schema => SchemaName;

    public DbSet<AccessSystem> Systems => Set<AccessSystem>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> Assignments => Set<RoleAssignment>();

    public DbSet<SystemAccessGrant> SystemAccess => Set<SystemAccessGrant>();

    public DbSet<RecordRoleType> RecordRoleTypes => Set<RecordRoleType>();

    public DbSet<RecordRolePermission> RecordRolePermissions => Set<RecordRolePermission>();

    public DbSet<RecordRoleGrant> RecordRoles => Set<RecordRoleGrant>();

    public DbSet<PendingAdministratorGrant> PendingAdministratorGrants => Set<PendingAdministratorGrant>();

    public DbSet<RoleTransferLog> Transfers => Set<RoleTransferLog>();
}

internal sealed class AccessSystemConfiguration : IEntityTypeConfiguration<AccessSystem>
{
    public void Configure(EntityTypeBuilder<AccessSystem> builder)
    {
        builder.ToTable("system");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(64);
        builder.Property(x => x.Kind).HasMaxLength(16);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Ignore(x => x.IsPlatform);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permission");
        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasMaxLength(128);
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.ObjectCode).HasMaxLength(64).HasColumnName("object");
        builder.Property(x => x.Action).HasMaxLength(64);
        builder.Property(x => x.LabelAr).HasMaxLength(200);
        builder.Property(x => x.LabelEn).HasMaxLength(200);
        builder.HasIndex(x => x.SystemCode);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.Code).HasMaxLength(Role.CodeMaxLength);
        builder.Property(x => x.NameAr).HasMaxLength(Role.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(Role.NameMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(1000);
        builder.Ignore(x => x.Key);
        builder.HasIndex(x => new { x.SystemCode, x.Code }).IsUnique();
        builder.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permission");
        builder.HasKey(x => new { x.RoleId, x.PermissionCode });
        builder.Property(x => x.PermissionCode).HasMaxLength(128);
        builder.Property(x => x.Scope).HasMaxLength(32);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionCode).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("role_assignment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PrincipalKind).HasMaxLength(16);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PrincipalKind, x.PrincipalId });
        builder.HasIndex(x => x.RoleId);
    }
}

internal sealed class SystemAccessGrantConfiguration : IEntityTypeConfiguration<SystemAccessGrant>
{
    public void Configure(EntityTypeBuilder<SystemAccessGrant> builder)
    {
        builder.ToTable("system_access");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.PrincipalKind).HasMaxLength(16);
        builder.HasIndex(x => new { x.SystemCode, x.PrincipalKind, x.PrincipalId }).IsUnique().HasFilter("active");
    }
}

internal sealed class RecordRoleTypeConfiguration : IEntityTypeConfiguration<RecordRoleType>
{
    public void Configure(EntityTypeBuilder<RecordRoleType> builder)
    {
        builder.ToTable("record_role_definition");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.ResourceType).HasMaxLength(128);
        builder.Property(x => x.RoleCode).HasMaxLength(64);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.HasIndex(x => new { x.ResourceType, x.RoleCode }).IsUnique();
        builder.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RecordRolePermissionConfiguration : IEntityTypeConfiguration<RecordRolePermission>
{
    public void Configure(EntityTypeBuilder<RecordRolePermission> builder)
    {
        builder.ToTable("record_role_permission");
        builder.HasKey(x => new { x.TypeId, x.PermissionCode });
        builder.Property(x => x.TypeId).HasColumnName("definition_id");
        builder.Property(x => x.PermissionCode).HasMaxLength(128);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionCode).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RecordRoleGrantConfiguration : IEntityTypeConfiguration<RecordRoleGrant>
{
    public void Configure(EntityTypeBuilder<RecordRoleGrant> builder)
    {
        builder.ToTable("record_role");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ResourceType).HasMaxLength(128);
        builder.Property(x => x.PrincipalKind).HasMaxLength(16);
        builder.Property(x => x.RoleCode).HasMaxLength(64);
        builder.HasIndex(x => new { x.PrincipalKind, x.PrincipalId, x.ResourceType });
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
    }
}

internal sealed class PendingAdministratorGrantConfiguration : IEntityTypeConfiguration<PendingAdministratorGrant>
{
    public void Configure(EntityTypeBuilder<PendingAdministratorGrant> builder)
    {
        builder.ToTable("pending_admin_grant");
        builder.HasKey(x => x.UserId);
    }
}

internal sealed class RoleTransferLogConfiguration : IEntityTypeConfiguration<RoleTransferLog>
{
    public void Configure(EntityTypeBuilder<RoleTransferLog> builder)
    {
        builder.ToTable("role_transfer");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RoleKeys).HasMaxLength(4000);
        builder.Property(x => x.FromDescription).HasMaxLength(4000);
        builder.Property(x => x.ToDescription).HasMaxLength(4000);
        builder.Property(x => x.Reason).HasMaxLength(1000);
    }
}
