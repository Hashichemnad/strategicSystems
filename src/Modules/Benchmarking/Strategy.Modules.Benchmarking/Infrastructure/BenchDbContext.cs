using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Benchmarking.Infrastructure;

/// <summary>Schema <c>bench</c> (docs/design/benchmarking.md §3).</summary>
public sealed class BenchDbContext(DbContextOptions<BenchDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "bench";

    public override string Schema => SchemaName;

    public DbSet<Study> Studies => Set<Study>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<Comparator> Comparators => Set<Comparator>();

    public DbSet<StudyEntity> StudyEntities => Set<StudyEntity>();

    public DbSet<Outreach> Outreach => Set<Outreach>();

    public DbSet<Visit> Visits => Set<Visit>();

    public DbSet<PracticeRow> Practices => Set<PracticeRow>();

    public DbSet<IndicatorRow> Indicators => Set<IndicatorRow>();

    public DbSet<PlanItem> PlanItems => Set<PlanItem>();

    public DbSet<BestPractice> BestPractices => Set<BestPractice>();

    public DbSet<ImpactReview> ImpactReviews => Set<ImpactReview>();

    public DbSet<PlanCirculation> Circulations => Set<PlanCirculation>();
}

internal static class BenchColumns
{
    public static void Codes<T>(this EntityTypeBuilder<T> builder, params string[] names)
        where T : class
    {
        foreach (var name in names)
        {
            builder.Property(name).HasMaxLength(100);
        }
    }

    public static void Texts<T>(this EntityTypeBuilder<T> builder, params string[] names)
        where T : class
    {
        foreach (var name in names)
        {
            builder.Property(name).HasMaxLength(Study.TextMaxLength);
        }
    }
}

internal sealed class StudyConfiguration : IEntityTypeConfiguration<Study>
{
    public void Configure(EntityTypeBuilder<Study> builder)
    {
        builder.ToTable("study");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.AnalysisApproved);
        builder.Ignore(x => x.IsActive);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.RequestNumber).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(Study.TitleMaxLength);
        builder.Texts(nameof(Study.Scope), nameof(Study.ReturnComment), nameof(Study.CancelReason));
        builder.Codes(nameof(Study.ComparisonTypeCode), nameof(Study.PurposeCode), nameof(Study.Stage), nameof(Study.Status), nameof(Study.PendingProcess));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => x.ResponsibleUnitId);
        builder.HasIndex(x => new { x.TargetYear, x.TargetQuarter });
    }
}

internal sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.ToTable("topic");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(Study.TitleMaxLength);
        builder.Texts(nameof(Topic.Description));
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class ComparatorConfiguration : IEntityTypeConfiguration<Comparator>
{
    public void Configure(EntityTypeBuilder<Comparator> builder)
    {
        builder.ToTable("comparator");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.Website).HasMaxLength(500);
        builder.Property(x => x.ContactName).HasMaxLength(200);
        builder.Property(x => x.ContactEmail).HasMaxLength(200);
        builder.Property(x => x.ContactPhone).HasMaxLength(60);
        builder.Texts(nameof(Comparator.Notes));
        builder.Codes(nameof(Comparator.SectorCode), nameof(Comparator.Scope));
    }
}

internal sealed class StudyEntityConfiguration : IEntityTypeConfiguration<StudyEntity>
{
    public void Configure(EntityTypeBuilder<StudyEntity> builder)
    {
        builder.ToTable("study_entity");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Texts(nameof(StudyEntity.Reason), nameof(StudyEntity.AvailableInfo), nameof(StudyEntity.Contact), nameof(StudyEntity.RatingNote));
        builder.HasIndex(x => new { x.StudyId, x.ComparatorId }).IsUnique();
        builder.HasIndex(x => x.ComparatorId);
    }
}

internal sealed class OutreachConfiguration : IEntityTypeConfiguration<Outreach>
{
    public void Configure(EntityTypeBuilder<Outreach> builder)
    {
        builder.ToTable("outreach");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Texts(nameof(Outreach.Summary));
        builder.Codes(nameof(Outreach.Channel), nameof(Outreach.Response));
        builder.HasIndex(x => x.StudyEntityId);
    }
}

internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visit");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.ParticipantIds);
        builder.Property(x => x.ParticipantIdsJson).HasColumnName("participant_ids").HasColumnType("jsonb");
        builder.Property(x => x.Questionnaire).HasMaxLength(20000);
        builder.Property(x => x.Notes).HasMaxLength(20000);
        builder.Codes(nameof(Visit.Method));
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class PracticeRowConfiguration : IEntityTypeConfiguration<PracticeRow>
{
    public void Configure(EntityTypeBuilder<PracticeRow> builder)
    {
        builder.ToTable("practice_row");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Subject).HasMaxLength(Study.TitleMaxLength);
        builder.Texts(nameof(PracticeRow.OurPractice), nameof(PracticeRow.TheirPractice));
        builder.Codes(nameof(PracticeRow.Category));
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class IndicatorRowConfiguration : IEntityTypeConfiguration<IndicatorRow>
{
    public void Configure(EntityTypeBuilder<IndicatorRow> builder)
    {
        builder.ToTable("indicator_row");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Indicator).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.KpiCardCode).HasMaxLength(100);
        builder.Property(x => x.Period).HasMaxLength(100);
        builder.Property(x => x.SourceText).HasMaxLength(1000);
        builder.Property(x => x.OurValue).HasPrecision(18, 4);
        builder.Property(x => x.TheirValue).HasPrecision(18, 4);
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class PlanItemConfiguration : IEntityTypeConfiguration<PlanItem>
{
    public void Configure(EntityTypeBuilder<PlanItem> builder)
    {
        builder.ToTable("plan_item");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TitleAr).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(Study.TitleMaxLength);
        builder.Texts(nameof(PlanItem.Description));
        builder.Codes(nameof(PlanItem.Priority), nameof(PlanItem.ActionTypeCode));
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class BestPracticeConfiguration : IEntityTypeConfiguration<BestPractice>
{
    public void Configure(EntityTypeBuilder<BestPractice> builder)
    {
        builder.ToTable("best_practice");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Tags);
        builder.Property(x => x.TitleAr).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(Study.TitleMaxLength);
        builder.Property(x => x.Topic).HasMaxLength(Study.TitleMaxLength);
        builder.Texts(nameof(BestPractice.Description), nameof(BestPractice.Applicability));
        builder.Property(x => x.TagsJson).HasColumnName("tags").HasMaxLength(2000); // text: searched with ILIKE together with the titles
        builder.Codes(nameof(BestPractice.SectorCode), nameof(BestPractice.Origin));
        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.PracticeRowId).IsUnique().HasFilter("practice_row_id IS NOT NULL");
    }
}

internal sealed class ImpactReviewConfiguration : IEntityTypeConfiguration<ImpactReview>
{
    public void Configure(EntityTypeBuilder<ImpactReview> builder)
    {
        builder.ToTable("impact_review");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Texts(nameof(ImpactReview.Notes));
        builder.HasIndex(x => x.StudyId);
    }
}

internal sealed class PlanCirculationConfiguration : IEntityTypeConfiguration<PlanCirculation>
{
    public void Configure(EntityTypeBuilder<PlanCirculation> builder)
    {
        builder.ToTable("plan_circulation");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => x.Year);
    }
}
