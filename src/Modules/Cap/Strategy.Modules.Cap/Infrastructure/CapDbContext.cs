using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Cap.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Cap.Infrastructure;

/// <summary>Schema <c>cap</c> (docs/design/cap.md §3).</summary>
public sealed class CapDbContext(DbContextOptions<CapDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "cap";

    public override string Schema => SchemaName;

    public DbSet<CapSource> Sources => Set<CapSource>();

    public DbSet<Remark> Remarks => Set<Remark>();

    public DbSet<CapAction> Actions => Set<CapAction>();

    public DbSet<RemarkAction> RemarkActions => Set<RemarkAction>();

    public DbSet<AchievementUpdate> Updates => Set<AchievementUpdate>();

    public DbSet<ExtensionRequest> Extensions => Set<ExtensionRequest>();

    public DbSet<EffectivenessCheck> Checks => Set<EffectivenessCheck>();

    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
}

internal static class CapColumns
{
    public static void Codes<T>(this EntityTypeBuilder<T> builder, params string[] names)
        where T : class
    {
        foreach (var name in names)
        {
            builder.Property(name).HasMaxLength(100);
        }
    }
}

internal sealed class CapSourceConfiguration : IEntityTypeConfiguration<CapSource>
{
    public void Configure(EntityTypeBuilder<CapSource> builder)
    {
        builder.ToTable("source");
        builder.HasQuickSearch("number", "title_ar", "title_en"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.ViewerEmployeeIds);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(CapSource.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(CapSource.TitleMaxLength);
        builder.Property(x => x.Description).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.ViewerEmployeeIdsJson).HasColumnName("viewer_employee_ids").HasColumnType("jsonb");
        builder.Codes(nameof(CapSource.SourceTypeCode), nameof(CapSource.Origin), nameof(CapSource.LinkedType), nameof(CapSource.Status));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.LinkedType, x.LinkedId }).IsUnique().HasFilter("linked_id IS NOT NULL");
    }
}

internal sealed class RemarkConfiguration : IEntityTypeConfiguration<Remark>
{
    public void Configure(EntityTypeBuilder<Remark> builder)
    {
        builder.ToTable("remark");
        builder.HasQuickSearch("number", "text_ar", "text_en"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Stage);
        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.HasRootCause);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TextAr).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.TextEn).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.RejectionReason).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.PmoDecisionNote).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.RootCauseText).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.CancelReason).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.ExternalKey).HasMaxLength(200);
        builder.Property(x => x.RootCauseJson).HasColumnName("root_cause").HasColumnType("jsonb");
        builder.Codes(nameof(Remark.CriterionCode), nameof(Remark.TypeCode), nameof(Remark.Priority), nameof(Remark.Status), nameof(Remark.PmoDecision),
            nameof(Remark.RootCauseMethod), nameof(Remark.LinkedType));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.SourceId);
        builder.HasIndex(x => new { x.SourceId, x.ExternalKey }).IsUnique().HasFilter("external_key IS NOT NULL");
        builder.HasIndex(x => x.OwnerEmployeeId);
        builder.HasIndex(x => x.DepartmentUnitId);
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class CapActionConfiguration : IEntityTypeConfiguration<CapAction>
{
    public void Configure(EntityTypeBuilder<CapAction> builder)
    {
        builder.ToTable("action");
        builder.HasQuickSearch("number", "details_ar", "details_en"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.EffectiveEnd);
        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.IsEditable);
        builder.Property(x => x.Number).HasMaxLength(80);
        builder.Property(x => x.DetailsAr).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.DetailsEn).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.RelatedProcess).HasMaxLength(400);
        builder.Property(x => x.ValidationNote).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.CompletionNote).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.CancelReason).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.ProgramRequestNumber).HasMaxLength(60);
        builder.Property(x => x.ExternalKey).HasMaxLength(200);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.MonthlyRemindedFor).HasMaxLength(7);
        builder.Property(x => x.EstimatedCost).HasPrecision(18, 2);
        builder.Property(x => x.AchievedPct).HasPrecision(9, 4);
        builder.Codes(nameof(CapAction.TypeCode), nameof(CapAction.ValidationStatus), nameof(CapAction.Status), nameof(CapAction.PendingProcess));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.HomeRemarkId);
        builder.HasIndex(x => x.AssignedEmployeeId);
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class RemarkActionConfiguration : IEntityTypeConfiguration<RemarkAction>
{
    public void Configure(EntityTypeBuilder<RemarkAction> builder)
    {
        builder.ToTable("remark_action");
        builder.HasKey(x => new { x.RemarkId, x.ActionId });
        builder.HasIndex(x => x.ActionId);
    }
}

internal sealed class AchievementUpdateConfiguration : IEntityTypeConfiguration<AchievementUpdate>
{
    public void Configure(EntityTypeBuilder<AchievementUpdate> builder)
    {
        builder.ToTable("achievement_update");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Note).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.Pct).HasPrecision(9, 4);
        builder.HasIndex(x => x.ActionId);
    }
}

internal sealed class ExtensionRequestConfiguration : IEntityTypeConfiguration<ExtensionRequest>
{
    public void Configure(EntityTypeBuilder<ExtensionRequest> builder)
    {
        builder.ToTable("extension_request");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Justification).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.DecisionNote).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.HasIndex(x => x.ActionId);
    }
}

internal sealed class EffectivenessCheckConfiguration : IEntityTypeConfiguration<EffectivenessCheck>
{
    public void Configure(EntityTypeBuilder<EffectivenessCheck> builder)
    {
        builder.ToTable("effectiveness_check");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Comment).HasMaxLength(CapSource.TextMaxLength);
        builder.HasIndex(x => x.RemarkId);
    }
}

internal sealed class SuggestionConfiguration : IEntityTypeConfiguration<Suggestion>
{
    public void Configure(EntityTypeBuilder<Suggestion> builder)
    {
        builder.ToTable("suggestion");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TitleAr).HasMaxLength(CapSource.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(CapSource.TitleMaxLength);
        builder.Property(x => x.ParentNumber).HasMaxLength(100);
        builder.Property(x => x.DismissReason).HasMaxLength(CapSource.TextMaxLength);
        builder.Property(x => x.DetailsJson).HasColumnName("details").HasColumnType("jsonb");
        builder.Codes(nameof(Suggestion.Kind), nameof(Suggestion.ResourceType), nameof(Suggestion.ParentType), nameof(Suggestion.Priority), nameof(Suggestion.Status));
        builder.HasIndex(x => new { x.Kind, x.ResourceId }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}
