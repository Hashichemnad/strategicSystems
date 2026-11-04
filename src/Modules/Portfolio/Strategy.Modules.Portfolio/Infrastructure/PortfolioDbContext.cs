using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Portfolio.Infrastructure;

/// <summary>Schema <c>portfolio</c> (docs/design/portfolio.md §3).</summary>
public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "portfolio";

    public override string Schema => SchemaName;

    public DbSet<ProgramRequest> Requests => Set<ProgramRequest>();

    public DbSet<ProposalAssessment> Assessments => Set<ProposalAssessment>();

    public DbSet<Project> Projects => Set<Project>();
}

internal sealed class ProgramRequestConfiguration : IEntityTypeConfiguration<ProgramRequest>
{
    public void Configure(EntityTypeBuilder<ProgramRequest> builder)
    {
        builder.ToTable("request");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        foreach (var name in new[]
                 {
                     nameof(ProgramRequest.Status), nameof(ProgramRequest.ApprovalStatus), nameof(ProgramRequest.Source), nameof(ProgramRequest.Classification),
                     nameof(ProgramRequest.ClassificationOverride), nameof(ProgramRequest.ProgramNature), nameof(ProgramRequest.CostPath),
                     nameof(ProgramRequest.ProcurementStatus), nameof(ProgramRequest.PriorityBand),
                 })
        {
            builder.Property(name).HasMaxLength(40);
        }

        foreach (var name in new[]
                 {
                     nameof(ProgramRequest.ApprovalStepCode), nameof(ProgramRequest.StrategicObjectiveCode), nameof(ProgramRequest.SuggestionSourceCode),
                     nameof(ProgramRequest.ProgramTypeCode), nameof(ProgramRequest.MainOptionCode), nameof(ProgramRequest.AltOptionCode),
                 })
        {
            builder.Property(name).HasMaxLength(100);
        }

        foreach (var name in new[]
                 {
                     nameof(ProgramRequest.ApprovalStepNameAr), nameof(ProgramRequest.ApprovalStepNameEn), nameof(ProgramRequest.NameAr),
                     nameof(ProgramRequest.NameEn), nameof(ProgramRequest.PreviousProgramName), nameof(ProgramRequest.SuggestionSourceOther),
                     nameof(ProgramRequest.MainOptionOther), nameof(ProgramRequest.AltOptionOther), nameof(ProgramRequest.PartnerName),
                 })
        {
            builder.Property(name).HasMaxLength(RequestRules.NameMaxLength);
        }

        foreach (var name in new[] { nameof(ProgramRequest.Description), nameof(ProgramRequest.Justifications), nameof(ProgramRequest.ScopeOfWork) })
        {
            builder.Property(name).HasMaxLength(RequestRules.LongTextMaxLength);
        }

        foreach (var name in new[]
                 {
                     nameof(ProgramRequest.MainAdvantages), nameof(ProgramRequest.MainDisadvantages), nameof(ProgramRequest.AltAdvantages),
                     nameof(ProgramRequest.AltDisadvantages), nameof(ProgramRequest.PracticeOperation), nameof(ProgramRequest.PracticeService),
                     nameof(ProgramRequest.PracticeProject), nameof(ProgramRequest.PracticeInitiative), nameof(ProgramRequest.CancelReason),
                     nameof(ProgramRequest.OverrideReason),
                 })
        {
            builder.Property(name).HasMaxLength(RequestRules.TextMaxLength);
        }

        foreach (var name in new[]
                 {
                     nameof(ProgramRequest.EstimatedBudget), nameof(ProgramRequest.Year1Cost), nameof(ProgramRequest.Year2Cost), nameof(ProgramRequest.Year3Cost),
                     nameof(ProgramRequest.Year4Cost), nameof(ProgramRequest.SharedCostAmount), nameof(ProgramRequest.AnnualOperationCost),
                     nameof(ProgramRequest.AnnualLicenceCost), nameof(ProgramRequest.AnnualMaintenanceCost), nameof(ProgramRequest.AnnualOtherCost),
                     nameof(ProgramRequest.ReturnValue), nameof(ProgramRequest.ReductionValue), nameof(ProgramRequest.ApprovedBudget),
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        builder.Property(x => x.PartnerShare).HasPrecision(9, 4);
        builder.Property(x => x.EvaluationScore).HasPrecision(9, 4);
        builder.Property(x => x.PriorityScores).HasColumnType("jsonb");
        builder.Ignore(x => x.EffectiveClassification);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.RequesterEmployeeId);
        builder.HasIndex(x => x.RequesterUserId);
    }
}

internal sealed class ProposalAssessmentConfiguration : IEntityTypeConfiguration<ProposalAssessment>
{
    public void Configure(EntityTypeBuilder<ProposalAssessment> builder)
    {
        builder.ToTable("proposal_assessment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Content).HasColumnType("jsonb");
        builder.Property(x => x.Documents).HasColumnType("jsonb");
        builder.Property(x => x.Language).HasMaxLength(10);
        builder.HasIndex(x => new { x.RequestId, x.VersionNo }).IsUnique();
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.Classification).HasMaxLength(40);
        builder.Property(x => x.Stage).HasMaxLength(40);
        builder.Property(x => x.Status).HasMaxLength(40);
        builder.Property(x => x.NameAr).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.Description).HasMaxLength(RequestRules.LongTextMaxLength);
        builder.Property(x => x.ScopeOfWork).HasMaxLength(RequestRules.LongTextMaxLength);
        builder.Property(x => x.StrategicObjectiveCode).HasMaxLength(100);
        builder.Property(x => x.ProgramTypeCode).HasMaxLength(100);
        foreach (var name in new[]
                 {
                     nameof(Project.Budget), nameof(Project.Year1PlannedCost), nameof(Project.Year2PlannedCost), nameof(Project.Year3PlannedCost),
                     nameof(Project.Year4PlannedCost),
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => x.UnitId);
    }
}
