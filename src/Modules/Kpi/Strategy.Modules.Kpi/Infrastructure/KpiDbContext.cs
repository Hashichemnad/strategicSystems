using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Kpi.Infrastructure;

/// <summary>Schema <c>kpi</c> (docs/design/kpi.md §2).</summary>
public sealed class KpiDbContext(DbContextOptions<KpiDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "kpi";

    public override string Schema => SchemaName;

    public DbSet<StrategyElement> Elements => Set<StrategyElement>();

    public DbSet<KpiCard> Cards => Set<KpiCard>();

    public DbSet<CardTarget> CardTargets => Set<CardTarget>();

    public DbSet<CardVersion> CardVersions => Set<CardVersion>();

    public DbSet<DefinitionChange> Changes => Set<DefinitionChange>();

    public DbSet<AnnualPlan> Plans => Set<AnnualPlan>();

    public DbSet<PeriodWindow> Windows => Set<PeriodWindow>();

    public DbSet<KpiResult> Results => Set<KpiResult>();
}

internal static class KpiColumns
{
    public static PropertyBuilder<decimal?> Value(this PropertyBuilder<decimal?> p) => p.HasPrecision(18, 4);

    public static PropertyBuilder<decimal?> Ratio(this PropertyBuilder<decimal?> p) => p.HasPrecision(12, 4);
}

internal sealed class StrategyElementConfiguration : IEntityTypeConfiguration<StrategyElement>
{
    public void Configure(EntityTypeBuilder<StrategyElement> builder)
    {
        builder.ToTable("strategy_element");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(KpiCard.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(KpiCard.NameMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(KpiCard.TextMaxLength);
        builder.Property(x => x.DescriptionEn).HasMaxLength(KpiCard.TextMaxLength);
        builder.Property(x => x.Weight).Ratio();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.ParentId);
    }
}

internal sealed class KpiCardConfiguration : IEntityTypeConfiguration<KpiCard>
{
    public void Configure(EntityTypeBuilder<KpiCard> builder)
    {
        builder.ToTable("kpi_card");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Sources);
        builder.Ignore(x => x.GemCriteria);
        builder.Ignore(x => x.Elements);
        builder.Ignore(x => x.ScaleLevels);
        builder.Ignore(x => x.Controlled);
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(KpiCard.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(KpiCard.NameMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(KpiCard.TextMaxLength);
        builder.Property(x => x.DescriptionEn).HasMaxLength(KpiCard.TextMaxLength);
        builder.Property(x => x.MeasuringEquation).HasMaxLength(2000);
        builder.Property(x => x.ProcessName).HasMaxLength(400);
        builder.Property(x => x.Status).HasMaxLength(20);
        foreach (var code in new[] { nameof(KpiCard.TypeCode), nameof(KpiCard.IndexClassificationCode), nameof(KpiCard.EffectivenessCode), nameof(KpiCard.MeasuringMethodCode),
                     nameof(KpiCard.NationalGoalCode), nameof(KpiCard.VisionLinkCode), nameof(KpiCard.UnitCode) })
        {
            builder.Property(code).HasMaxLength(100);
        }

        builder.Property(x => x.Nature).HasMaxLength(20);
        builder.Property(x => x.MeasuringPeriod).HasMaxLength(20);
        builder.Property(x => x.Direction).HasMaxLength(20);
        builder.Property(x => x.TargetType).HasMaxLength(20);
        builder.Property(x => x.Level).HasMaxLength(20);
        builder.Property(x => x.TargetSettingMechanism).HasMaxLength(2000);
        builder.Property(x => x.SectorGoal).HasMaxLength(1000);
        builder.Property(x => x.CriteriaText).HasMaxLength(2000);
        builder.Property(x => x.ExcellenceLink).HasMaxLength(1000);
        builder.Property(x => x.BenchmarkDescription).HasMaxLength(2000);
        builder.Property(x => x.Formula).HasMaxLength(2000);
        builder.Property(x => x.SourceCodes).HasColumnType("jsonb");
        builder.Property(x => x.GemCriteriaCodes).HasColumnType("jsonb");
        builder.Property(x => x.DataElements).HasColumnType("jsonb");
        builder.Property(x => x.Scale).HasColumnType("jsonb");
        builder.Property(x => x.BenchmarkResult).Ratio();
        builder.Property(x => x.Weight).Ratio();
        builder.Property(x => x.GreenFrom).Ratio();
        builder.Property(x => x.AmberFrom).Ratio();
        builder.Property(x => x.MaxAchieved).Ratio();
        builder.Property(x => x.BaselineValue).Value();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.ResponsibleUnitId);
        builder.HasIndex(x => x.ResponsibleEmployeeId);
        builder.HasIndex(x => x.ObjectiveId);
        builder.HasIndex(x => x.ParentCardId);
    }
}

internal sealed class CardTargetConfiguration : IEntityTypeConfiguration<CardTarget>
{
    public void Configure(EntityTypeBuilder<CardTarget> builder)
    {
        builder.ToTable("card_target");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TargetType).HasMaxLength(20);
        builder.Property(x => x.Target).Value();
        builder.Property(x => x.RangeMin).Value();
        builder.Property(x => x.RangeMax).Value();
        builder.HasIndex(x => new { x.CardId, x.Year }).IsUnique();
    }
}

internal sealed class CardVersionConfiguration : IEntityTypeConfiguration<CardVersion>
{
    public void Configure(EntityTypeBuilder<CardVersion> builder)
    {
        builder.ToTable("card_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Snapshot).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.HasIndex(x => new { x.CardId, x.VersionNo }).IsUnique();
    }
}

internal sealed class DefinitionChangeConfiguration : IEntityTypeConfiguration<DefinitionChange>
{
    public void Configure(EntityTypeBuilder<DefinitionChange> builder)
    {
        builder.ToTable("definition_change");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Change);
        builder.Property(x => x.Proposed).HasColumnType("jsonb");
        builder.Property(x => x.Current).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.DecisionComment).HasMaxLength(2000);
        builder.HasIndex(x => x.CardId);
    }
}

internal sealed class AnnualPlanConfiguration : IEntityTypeConfiguration<AnnualPlan>
{
    public void Configure(EntityTypeBuilder<AnnualPlan> builder)
    {
        builder.ToTable("annual_plan");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MeasuringPeriod).HasMaxLength(20);
        builder.Property(x => x.UnitCode).HasMaxLength(100);
        builder.Property(x => x.Direction).HasMaxLength(20);
        builder.Property(x => x.TargetType).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Rag).HasMaxLength(20);
        builder.Property(x => x.Target).Value();
        builder.Property(x => x.RangeMin).Value();
        builder.Property(x => x.RangeMax).Value();
        builder.Property(x => x.AchievedTotal).Value();
        builder.Property(x => x.LastAchieved).Value();
        builder.Property(x => x.ActualPerformance).Ratio();
        builder.Property(x => x.CappedPerformance).Ratio();
        builder.HasIndex(x => new { x.CardId, x.Year }).IsUnique();
        builder.HasIndex(x => x.Year);
    }
}

internal sealed class PeriodWindowConfiguration : IEntityTypeConfiguration<PeriodWindow>
{
    public void Configure(EntityTypeBuilder<PeriodWindow> builder)
    {
        builder.ToTable("period_window");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PeriodCode).HasMaxLength(10);
        builder.HasIndex(x => new { x.Year, x.PeriodCode }).IsUnique();
    }
}

internal sealed class KpiResultConfiguration : IEntityTypeConfiguration<KpiResult>
{
    public void Configure(EntityTypeBuilder<KpiResult> builder)
    {
        builder.ToTable("result");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Elements);
        builder.Ignore(x => x.ChecklistAnswers);
        builder.Property(x => x.PeriodCode).HasMaxLength(10);
        builder.Property(x => x.TargetType).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(30);
        builder.Property(x => x.Target).Value();
        builder.Property(x => x.RangeMin).Value();
        builder.Property(x => x.RangeMax).Value();
        builder.Property(x => x.Achieve).Value();
        builder.Property(x => x.ScaleValue).Value();
        builder.Property(x => x.ActualPerformance).Ratio();
        builder.Property(x => x.CappedPerformance).Ratio();
        builder.Property(x => x.ElementValues).HasColumnType("jsonb");
        builder.Property(x => x.Checklist).HasColumnType("jsonb");
        builder.Property(x => x.Causality).HasMaxLength(KpiResult.TextMaxLength);
        builder.Property(x => x.CorrectiveActions).HasMaxLength(KpiResult.TextMaxLength);
        builder.Property(x => x.Notes).HasMaxLength(KpiResult.TextMaxLength);
        builder.Property(x => x.NaReason).HasMaxLength(2000);
        builder.Property(x => x.StatusReason).HasMaxLength(2000);
        builder.HasIndex(x => new { x.PlanId, x.PeriodCode }).IsUnique();
        builder.HasIndex(x => new { x.Year, x.Status });
        builder.HasIndex(x => x.CardId);
        builder.HasIndex(x => x.OwnerEmployeeId);
        builder.HasIndex(x => x.OwnerUnitId);
    }
}
