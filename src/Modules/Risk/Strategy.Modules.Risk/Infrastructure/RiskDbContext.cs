using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Risk.Infrastructure;

/// <summary>Schema <c>risk</c> (docs/design/risk.md §3).</summary>
public sealed class RiskDbContext(DbContextOptions<RiskDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "risk";

    public override string Schema => SchemaName;

    public DbSet<FrameworkVersion> Frameworks => Set<FrameworkVersion>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<RiskRecord> Risks => Set<RiskRecord>();

    public DbSet<Control> Controls => Set<Control>();

    public DbSet<TreatmentAction> Actions => Set<TreatmentAction>();

    public DbSet<ActionUpdate> ActionUpdates => Set<ActionUpdate>();

    public DbSet<Kri> Kris => Set<Kri>();

    public DbSet<Assessment> Assessments => Set<Assessment>();

    public DbSet<RiskEvent> Events => Set<RiskEvent>();

    public DbSet<EventRisk> EventRisks => Set<EventRisk>();

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<CommitteeDecision> Decisions => Set<CommitteeDecision>();
}

internal static class RiskColumns
{
    public static PropertyBuilder<decimal> Score(this PropertyBuilder<decimal> p) => p.HasPrecision(9, 2);

    public static PropertyBuilder<decimal?> Score(this PropertyBuilder<decimal?> p) => p.HasPrecision(9, 2);

    public static void Codes<T>(this EntityTypeBuilder<T> builder, params string[] names)
        where T : class
    {
        foreach (var name in names)
        {
            builder.Property(name).HasMaxLength(100);
        }
    }
}

internal sealed class FrameworkVersionConfiguration : IEntityTypeConfiguration<FrameworkVersion>
{
    public void Configure(EntityTypeBuilder<FrameworkVersion> builder)
    {
        builder.ToTable("framework_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Content).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(2000);
        builder.Property(x => x.Recalculation).HasMaxLength(20);
        builder.HasIndex(x => x.VersionNo).IsUnique();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("category");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Appetite).Score();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RiskConfiguration : IEntityTypeConfiguration<RiskRecord>
{
    public void Configure(EntityTypeBuilder<RiskRecord> builder)
    {
        builder.ToTable("risk");
        builder.HasQuickSearch("number", "title_ar", "title_en", "event_text"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.CategoryCodes);
        builder.Ignore(x => x.ObjectiveCodes);
        builder.Ignore(x => x.EffectiveControl);
        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.Title);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(RiskRecord.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(RiskRecord.TitleMaxLength);
        builder.Property(x => x.Cause).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.EventText).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.Consequence).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.Processes).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.ResidualStatusJustification).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.ReturnComment).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.ClosureNote).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.RejectionReason).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.CategoryCodesJson).HasColumnName("category_codes").HasColumnType("jsonb");
        builder.Property(x => x.ObjectiveCodesJson).HasColumnName("objective_codes").HasColumnType("jsonb");
        builder.Codes(nameof(RiskRecord.DeterminedByCode), nameof(RiskRecord.RiskType), nameof(RiskRecord.Status), nameof(RiskRecord.InherentImpactArea),
            nameof(RiskRecord.InherentBand), nameof(RiskRecord.ControlEffectivenessCode), nameof(RiskRecord.ControlEffectivenessOverride), nameof(RiskRecord.ResidualBand),
            nameof(RiskRecord.ResidualStatus), nameof(RiskRecord.ResidualStatusOverride), nameof(RiskRecord.TargetBand), nameof(RiskRecord.ResponseCode),
            nameof(RiskRecord.EscalationReason), nameof(RiskRecord.ReassessmentReason), nameof(RiskRecord.PendingProcess), nameof(RiskRecord.ClosureReasonCode));
        builder.Property(x => x.InherentScore).Score();
        builder.Property(x => x.ResidualScore).Score();
        builder.Property(x => x.TargetScore).Score();
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.OwnerEmployeeId);
        builder.HasIndex(x => x.CoordinatorEmployeeId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.NextReviewOn);
    }
}

internal sealed class ControlConfiguration : IEntityTypeConfiguration<Control>
{
    public void Configure(EntityTypeBuilder<Control> builder)
    {
        builder.ToTable("control");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(RiskRecord.TitleMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(RiskRecord.TitleMaxLength);
        builder.Property(x => x.Description).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.Evidence).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Codes(nameof(Control.TypeCode), nameof(Control.NatureCode), nameof(Control.FrequencyCode), nameof(Control.EffectivenessCode), nameof(Control.Status));
        builder.HasIndex(x => x.RiskId);
        builder.HasIndex(x => x.OwnerEmployeeId);
    }
}

internal sealed class TreatmentActionConfiguration : IEntityTypeConfiguration<TreatmentAction>
{
    public void Configure(EntityTypeBuilder<TreatmentAction> builder)
    {
        builder.ToTable("treatment_action");
        builder.HasQuickSearch("number", "title_ar", "title_en"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.ResponsibleUnitIds);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.ResponsibleUnitIdsJson).HasColumnName("responsible_unit_ids").HasColumnType("jsonb");
        builder.Property(x => x.CompletionPct).HasPrecision(9, 4);
        builder.Property(x => x.Cost).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RiskId);
        builder.HasIndex(x => x.OwnerEmployeeId);
    }
}

internal sealed class ActionUpdateConfiguration : IEntityTypeConfiguration<ActionUpdate>
{
    public void Configure(EntityTypeBuilder<ActionUpdate> builder)
    {
        builder.ToTable("action_update");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Note).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.CompletionPct).HasPrecision(9, 4);
        builder.HasIndex(x => x.ActionId);
    }
}

internal sealed class KriConfiguration : IEntityTypeConfiguration<Kri>
{
    public void Configure(EntityTypeBuilder<Kri> builder)
    {
        builder.ToTable("kri");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CardCode).HasMaxLength(100);
        builder.Property(x => x.CardNameAr).HasMaxLength(400);
        builder.Property(x => x.CardNameEn).HasMaxLength(400);
        builder.Codes(nameof(Kri.Basis), nameof(Kri.Direction), nameof(Kri.Status), nameof(Kri.LastLevel), nameof(Kri.LastPeriod));
        builder.Property(x => x.WarningThreshold).HasPrecision(18, 4);
        builder.Property(x => x.BreachThreshold).HasPrecision(18, 4);
        builder.Property(x => x.LastValue).HasPrecision(18, 4);
        builder.HasIndex(x => x.RiskId);
        builder.HasIndex(x => x.KpiCardId);
    }
}

internal sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> builder)
    {
        builder.ToTable("assessment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Codes(nameof(Assessment.Kind), nameof(Assessment.InherentBand), nameof(Assessment.ResidualBand), nameof(Assessment.ResidualStatus),
            nameof(Assessment.TargetBand), nameof(Assessment.ControlEffectiveness), nameof(Assessment.Status));
        builder.Property(x => x.InherentScore).Score();
        builder.Property(x => x.ResidualScore).Score();
        builder.Property(x => x.TargetScore).Score();
        builder.Property(x => x.Comment).HasMaxLength(RiskRecord.TextMaxLength);
        builder.HasIndex(x => new { x.RiskId, x.At });
    }
}

internal sealed class RiskEventConfiguration : IEntityTypeConfiguration<RiskEvent>
{
    public void Configure(EntityTypeBuilder<RiskEvent> builder)
    {
        builder.ToTable("risk_event");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.Description).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.OtherImpact).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.RootCause).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.Lessons).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.FinancialImpact).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.HasIndex(x => x.Number).IsUnique();
    }
}

internal sealed class EventRiskConfiguration : IEntityTypeConfiguration<EventRisk>
{
    public void Configure(EntityTypeBuilder<EventRisk> builder)
    {
        builder.ToTable("event_risk");
        builder.HasKey(x => new { x.EventId, x.RiskId });
        builder.HasIndex(x => x.RiskId);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("review");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.ControlRatings);
        builder.Property(x => x.ControlRatingsJson).HasColumnName("control_ratings").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.ActionNotes).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.Commentary).HasMaxLength(RiskRecord.TextMaxLength);
        builder.Property(x => x.DecisionComment).HasMaxLength(RiskRecord.TextMaxLength);
        builder.HasIndex(x => x.RiskId);
    }
}

internal sealed class CommitteeDecisionConfiguration : IEntityTypeConfiguration<CommitteeDecision>
{
    public void Configure(EntityTypeBuilder<CommitteeDecision> builder)
    {
        builder.ToTable("committee_decision");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DecisionCode).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(RiskRecord.TextMaxLength);
        builder.HasIndex(x => x.RiskId);
    }
}
