using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Persistence;
using Strategy.Platform.Reporting.Domain;

namespace Strategy.Platform.Reporting.Infrastructure;

/// <summary>Schema <c>reporting</c>: cached aggregates, dashboards, saved reports, schedules (docs/design/reporting.md).</summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "reporting";

    public override string Schema => SchemaName;

    public DbSet<MetricFactRow> Facts => Set<MetricFactRow>();

    public DbSet<RefreshLog> RefreshLogs => Set<RefreshLog>();

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    public DbSet<DashboardRole> DashboardRoles => Set<DashboardRole>();

    public DbSet<Share> Shares => Set<Share>();

    public DbSet<UserPin> Pins => Set<UserPin>();

    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>();

    public DbSet<MyWorkLayout> MyWorkLayouts => Set<MyWorkLayout>();

    public DbSet<SavedReport> Reports => Set<SavedReport>();

    public DbSet<Schedule> Schedules => Set<Schedule>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<ExecutiveBrief> Briefs => Set<ExecutiveBrief>();

    public DbSet<ReportType> ReportTypes => Set<ReportType>();

    public DbSet<ReportFolder> Folders => Set<ReportFolder>();

    public DbSet<DossierTemplate> DossierTemplates => Set<DossierTemplate>();
}

internal sealed class FactConfiguration : IEntityTypeConfiguration<MetricFactRow>
{
    public void Configure(EntityTypeBuilder<MetricFactRow> builder)
    {
        builder.ToTable("fact");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.Metric).HasMaxLength(100);
        builder.Property(x => x.Partition).HasMaxLength(100);
        builder.Property(x => x.DimsJson).HasColumnType("jsonb");
        builder.Property(x => x.Value).HasColumnType("numeric(20,6)");
        builder.Property(x => x.Weight).HasColumnType("numeric(20,6)");
        builder.HasIndex(x => new { x.Metric, x.Month });
    }
}

internal sealed class RefreshLogConfiguration : IEntityTypeConfiguration<RefreshLog>
{
    public void Configure(EntityTypeBuilder<RefreshLog> builder)
    {
        builder.ToTable("refresh_log");
        builder.Property(x => x.Id).UseIdentityAlwaysColumn();
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Error).HasMaxLength(2000);
        builder.HasIndex(x => new { x.SystemCode, x.At });
    }
}

internal sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.ToTable("dashboard");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.TitleAr).HasMaxLength(200);
        builder.Property(x => x.TitleEn).HasMaxLength(200);
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(1000);
        builder.Property(x => x.Level).HasMaxLength(20);
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        builder.Property(x => x.DraftJson).HasColumnType("jsonb");
        builder.Property(x => x.SeedHash).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class DashboardRoleConfiguration : IEntityTypeConfiguration<DashboardRole>
{
    public void Configure(EntityTypeBuilder<DashboardRole> builder)
    {
        builder.ToTable("dashboard_role");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RoleKey).HasMaxLength(120);
        builder.HasIndex(x => new { x.DashboardId, x.RoleKey }).IsUnique();
        builder.HasIndex(x => x.RoleKey);
    }
}

internal sealed class ShareConfiguration : IEntityTypeConfiguration<Share>
{
    public void Configure(EntityTypeBuilder<Share> builder)
    {
        builder.ToTable("share");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TargetKind).HasMaxLength(20);
        builder.Property(x => x.PrincipalKind).HasMaxLength(20);
        builder.Property(x => x.PrincipalRef).HasMaxLength(120);
        builder.HasIndex(x => new { x.TargetKind, x.TargetId, x.PrincipalKind, x.PrincipalRef }).IsUnique();
    }
}

internal sealed class UserPinConfiguration : IEntityTypeConfiguration<UserPin>
{
    public void Configure(EntityTypeBuilder<UserPin> builder)
    {
        builder.ToTable("user_pin");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.WidgetId).HasMaxLength(50);
        builder.HasIndex(x => x.UserId);
    }
}

internal sealed class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.ToTable("saved_filter");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.FiltersJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.UserId, x.DashboardId });
    }
}

internal sealed class MyWorkLayoutConfiguration : IEntityTypeConfiguration<MyWorkLayout>
{
    public void Configure(EntityTypeBuilder<MyWorkLayout> builder)
    {
        builder.ToTable("my_work_layout");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CardsJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}

internal sealed class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> builder)
    {
        builder.ToTable("report");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Dataset).HasMaxLength(150);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.FolderId);
    }
}

internal sealed class ScheduleConfiguration : IEntityTypeConfiguration<Schedule>
{
    public void Configure(EntityTypeBuilder<Schedule> builder)
    {
        builder.ToTable("schedule");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TargetKind).HasMaxLength(20);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Frequency).HasMaxLength(20);
        builder.Property(x => x.PeriodRule).HasMaxLength(30);
        builder.Property(x => x.Format).HasMaxLength(10);
        builder.Property(x => x.RecipientsJson).HasColumnType("jsonb");
        builder.Property(x => x.OptionsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.Active, x.NextRunAt });
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("delivery");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PeriodKey).HasMaxLength(50);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Format).HasMaxLength(10);
        builder.Property(x => x.Detail).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ScheduleId, x.UserId, x.PeriodKey }).IsUnique();
    }
}

internal sealed class ExecutiveBriefConfiguration : IEntityTypeConfiguration<ExecutiveBrief>
{
    public void Configure(EntityTypeBuilder<ExecutiveBrief> builder)
    {
        builder.ToTable("brief");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PeriodKey).HasMaxLength(50);
        builder.Property(x => x.ScopeDescription).HasMaxLength(500);
        builder.Property(x => x.ContentJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.CreatedBy);
    }
}

internal sealed class ReportTypeConfiguration : IEntityTypeConfiguration<ReportType>
{
    public void Configure(EntityTypeBuilder<ReportType> builder)
    {
        builder.ToTable("report_type");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.LabelAr).HasMaxLength(200);
        builder.Property(x => x.LabelEn).HasMaxLength(200);
        builder.Property(x => x.DescriptionAr).HasMaxLength(1000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(1000);
        builder.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.SeedHash).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class ReportFolderConfiguration : IEntityTypeConfiguration<ReportFolder>
{
    public void Configure(EntityTypeBuilder<ReportFolder> builder)
    {
        builder.ToTable("report_folder");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.HasIndex(x => x.OwnerUserId);
    }
}

internal sealed class DossierTemplateConfiguration : IEntityTypeConfiguration<DossierTemplate>
{
    public void Configure(EntityTypeBuilder<DossierTemplate> builder)
    {
        builder.ToTable("dossier_template");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Dataset).HasMaxLength(100);
        builder.Property(x => x.LabelAr).HasMaxLength(200);
        builder.Property(x => x.LabelEn).HasMaxLength(200);
        builder.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.SeedHash).HasMaxLength(64);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
