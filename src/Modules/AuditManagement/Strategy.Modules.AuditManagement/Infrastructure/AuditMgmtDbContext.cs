using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.AuditManagement.Infrastructure;

/// <summary>Schema <c>audit_mgmt</c> (docs/design/audit.md §3).</summary>
public sealed class AuditMgmtDbContext(DbContextOptions<AuditMgmtDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "audit_mgmt";

    public override string Schema => SchemaName;

    public DbSet<AuditReport> Reports => Set<AuditReport>();

    public DbSet<Finding> Findings => Set<Finding>();

    public DbSet<FindingRisk> FindingRisks => Set<FindingRisk>();

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    public DbSet<FollowUp> FollowUps => Set<FollowUp>();

    public DbSet<RecommendationLog> Logs => Set<RecommendationLog>();

    public DbSet<AuditableEntityRecord> Universe => Set<AuditableEntityRecord>();

    public DbSet<AuditPlan> Plans => Set<AuditPlan>();

    public DbSet<Engagement> Engagements => Set<Engagement>();

    public DbSet<WorkPaper> WorkPapers => Set<WorkPaper>();
}

internal static class AuditColumns
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
            builder.Property(name).HasMaxLength(AuditReport.TextMaxLength);
        }
    }
}

internal sealed class AuditReportConfiguration : IEntityTypeConfiguration<AuditReport>
{
    public void Configure(EntityTypeBuilder<AuditReport> builder)
    {
        builder.ToTable("report");
        builder.HasQuickSearch("number", "title_ar", "title_en", "auditor_body"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.AuditedUnitIds);
        builder.Ignore(x => x.DistributionEmployeeIds);
        builder.Ignore(x => x.IsOpen);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.AuditorBody).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Texts(nameof(AuditReport.DistributionNote), nameof(AuditReport.Summary), nameof(AuditReport.CancelReason));
        builder.Property(x => x.AuditedUnitIdsJson).HasColumnName("audited_unit_ids").HasColumnType("jsonb");
        builder.Property(x => x.DistributionJson).HasColumnName("distribution_employee_ids").HasColumnType("jsonb");
        builder.Codes(nameof(AuditReport.TypeCode), nameof(AuditReport.Status));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.EngagementId);
    }
}

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("finding");
        builder.HasQuickSearch("number", "title_ar", "title_en"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.IsOpen);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Texts(nameof(Finding.Condition), nameof(Finding.Criteria), nameof(Finding.Cause), nameof(Finding.Effect), nameof(Finding.CancelReason));
        builder.Property(x => x.CapRemarkNumber).HasMaxLength(60);
        builder.Property(x => x.Completion).HasPrecision(9, 4);
        builder.Codes(nameof(Finding.TypeCode), nameof(Finding.Priority), nameof(Finding.Status));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.ReportId);
        builder.HasIndex(x => x.ResponsibleUnitId);
        builder.HasIndex(x => x.DepartmentUnitId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Year);
    }
}

internal sealed class FindingRiskConfiguration : IEntityTypeConfiguration<FindingRisk>
{
    public void Configure(EntityTypeBuilder<FindingRisk> builder)
    {
        builder.ToTable("finding_risk");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RiskNumber).HasMaxLength(60);
        builder.Property(x => x.ControlLoweredTo).HasMaxLength(100);
        builder.HasIndex(x => x.FindingId);
        builder.HasIndex(x => new { x.FindingId, x.RiskId, x.ControlId }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> builder)
    {
        builder.ToTable("recommendation");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.EffectiveDue);
        builder.Ignore(x => x.IsOpen);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Texts(nameof(Recommendation.ActionAr), nameof(Recommendation.ActionEn), nameof(Recommendation.ManagementComment), nameof(Recommendation.ImplementationPlan),
            nameof(Recommendation.RevisedReason), nameof(Recommendation.LastComment), nameof(Recommendation.RiskAcceptanceJustification), nameof(Recommendation.Notes),
            nameof(Recommendation.CancelReason));
        builder.Property(x => x.RiskAcceptanceReference).HasMaxLength(400);
        builder.Property(x => x.LatestPct).HasPrecision(9, 4);
        builder.Codes(nameof(Recommendation.ManagementResponseCode), nameof(Recommendation.Status));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.FindingId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.NextFollowUpOn);
    }
}

internal sealed class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> builder)
    {
        builder.ToTable("follow_up");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.Pct).HasPrecision(9, 4);
        builder.Texts(nameof(FollowUp.ActionsImplemented), nameof(FollowUp.EvidenceText), nameof(FollowUp.Comment), nameof(FollowUp.ReviewNote));
        builder.Codes(nameof(FollowUp.ReviewStatus));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RecommendationId);
    }
}

internal sealed class RecommendationLogConfiguration : IEntityTypeConfiguration<RecommendationLog>
{
    public void Configure(EntityTypeBuilder<RecommendationLog> builder)
    {
        builder.ToTable("recommendation_log");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Texts(nameof(RecommendationLog.Justification));
        builder.Property(x => x.Reference).HasMaxLength(400);
        builder.Codes(nameof(RecommendationLog.Kind));
        builder.HasIndex(x => x.RecommendationId);
    }
}

internal sealed class AuditableEntityConfiguration : IEntityTypeConfiguration<AuditableEntityRecord>
{
    public void Configure(EntityTypeBuilder<AuditableEntityRecord> builder)
    {
        builder.ToTable("auditable_entity");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NameAr).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Codes(nameof(AuditableEntityRecord.KindCode), nameof(AuditableEntityRecord.InherentRating), nameof(AuditableEntityRecord.Origin));
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.RiskId);
    }
}

internal sealed class AuditPlanConfiguration : IEntityTypeConfiguration<AuditPlan>
{
    public void Configure(EntityTypeBuilder<AuditPlan> builder)
    {
        builder.ToTable("plan");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Editable);
        builder.Property(x => x.TitleAr).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Texts(nameof(AuditPlan.Note));
        builder.Codes(nameof(AuditPlan.Status));
        builder.HasIndex(x => x.Year).IsUnique();
    }
}

internal sealed class EngagementConfiguration : IEntityTypeConfiguration<Engagement>
{
    public void Configure(EntityTypeBuilder<Engagement> builder)
    {
        builder.ToTable("engagement");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TitleAr).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Texts(nameof(Engagement.Objectives), nameof(Engagement.Scope), nameof(Engagement.SatisfactionComment), nameof(Engagement.CancelReason));
        builder.Property(x => x.RiskScore).HasPrecision(9, 2);
        builder.Codes(nameof(Engagement.Stage));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.PlanId);
    }
}

internal sealed class WorkPaperConfiguration : IEntityTypeConfiguration<WorkPaper>
{
    public void Configure(EntityTypeBuilder<WorkPaper> builder)
    {
        builder.ToTable("work_paper");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reference).HasMaxLength(60);
        builder.Property(x => x.Title).HasMaxLength(AuditReport.TitleMaxLength);
        builder.Texts(nameof(WorkPaper.TestStep), nameof(WorkPaper.Sample), nameof(WorkPaper.Result), nameof(WorkPaper.Conclusion));
        builder.Codes(nameof(WorkPaper.Status));
        builder.HasIndex(x => x.EngagementId);
    }
}
