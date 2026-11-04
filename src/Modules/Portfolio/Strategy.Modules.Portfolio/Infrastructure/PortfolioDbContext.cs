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

    public DbSet<Milestone> Milestones => Set<Milestone>();

    public DbSet<MonthlyUpdate> MonthlyUpdates => Set<MonthlyUpdate>();

    public DbSet<ProjectKpi> Kpis => Set<ProjectKpi>();

    public DbSet<ProjectRisk> Risks => Set<ProjectRisk>();

    public DbSet<Stakeholder> Stakeholders => Set<Stakeholder>();

    public DbSet<ProposedMember> Members => Set<ProposedMember>();

    public DbSet<PlanningItem> PlanningItems => Set<PlanningItem>();

    public DbSet<Challenge> Challenges => Set<Challenge>();

    public DbSet<Lesson> Lessons => Set<Lesson>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
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
        foreach (var name in new[]
                 {
                     nameof(Project.Classification), nameof(Project.Stage), nameof(Project.Status), nameof(Project.SubState), nameof(Project.ApprovalResult),
                     nameof(Project.ProgressStatus),
                 })
        {
            builder.Property(name).HasMaxLength(40);
        }

        builder.Property(x => x.NameAr).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.Description).HasMaxLength(RequestRules.LongTextMaxLength);
        builder.Property(x => x.ScopeOfWork).HasMaxLength(RequestRules.LongTextMaxLength);
        builder.Property(x => x.StrategicObjectiveCode).HasMaxLength(100);
        builder.Property(x => x.ProgramTypeCode).HasMaxLength(100);
        builder.Property(x => x.ApprovalProcess).HasMaxLength(100);
        builder.Property(x => x.ApprovalStepCode).HasMaxLength(100);
        builder.Property(x => x.ApprovalStepNameAr).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.ApprovalStepNameEn).HasMaxLength(RequestRules.NameMaxLength);
        builder.Property(x => x.Template).HasColumnType("jsonb");
        builder.Property(x => x.OperationalThemes).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.SubState).HasDefaultValue(SubStates.Ownership);
        builder.Property(x => x.ProgressStatus).HasDefaultValue(ProgressCalculator.OnTrack);
        foreach (var name in new[]
                 {
                     nameof(Project.StructuralChanges), nameof(Project.ResourcesHuman), nameof(Project.ResourcesFinancial), nameof(Project.ResourcesTechnical),
                     nameof(Project.ResourcesContract), nameof(Project.ResourcesOther), nameof(Project.SummaryOfAchievement), nameof(Project.MainDeliverables),
                     nameof(Project.ImpactNotes), nameof(Project.CancelReason), nameof(Project.HoldReason),
                 })
        {
            builder.Property(name).HasMaxLength(RequestRules.LongTextMaxLength);
        }

        foreach (var name in new[]
                 {
                     nameof(Project.Budget), nameof(Project.Year1PlannedCost), nameof(Project.Year2PlannedCost), nameof(Project.Year3PlannedCost),
                     nameof(Project.Year4PlannedCost), nameof(Project.Year1ContractedCost), nameof(Project.Year2ContractedCost), nameof(Project.Year3ContractedCost),
                     nameof(Project.Year4ContractedCost), nameof(Project.Year1ActualCost), nameof(Project.Year2ActualCost), nameof(Project.Year3ActualCost),
                     nameof(Project.Year4ActualCost), nameof(Project.ContractedInitialCost), nameof(Project.AnnualOperationCost), nameof(Project.AnnualMaintenanceCost),
                     nameof(Project.AnnualLicenceCost), nameof(Project.AnnualOtherCost), nameof(Project.PreviousOperationalCost),
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        builder.Property(x => x.Performance).HasPrecision(9, 4);
        builder.Property(x => x.PlannedProgress).HasPrecision(9, 4);
        builder.Property(x => x.CostPerformance).HasPrecision(9, 4);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.RequestId).IsUnique();
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.SectionUnitId);
        builder.HasIndex(x => x.PmEmployeeId);
        builder.HasIndex(x => new { x.Status, x.SubState });
    }
}

internal static class ChildConfiguration
{
    public static void Child<T>(EntityTypeBuilder<T> builder, string table)
        where T : ProjectChild
    {
        builder.ToTable(table);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.InactiveReason).HasMaxLength(RequestRules.TextMaxLength);
        builder.HasIndex(x => x.ProjectId);
    }

    public static void Texts<T>(EntityTypeBuilder<T> builder, int length, params string[] names)
        where T : class
    {
        foreach (var name in names)
        {
            builder.Property(name).HasMaxLength(length);
        }
    }
}

internal sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.ToTable("milestone");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(80);
        builder.Property(x => x.Status).HasMaxLength(40);
        builder.Property(x => x.UpdateState).HasMaxLength(40);
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(Milestone.TitleAr), nameof(Milestone.TitleEn));
        ChildConfiguration.Texts(builder, MilestoneRules.TextMaxLength, nameof(Milestone.Causality), nameof(Milestone.CorrectiveActions), nameof(Milestone.EvidenceNote),
            nameof(Milestone.MainDeliverable), nameof(Milestone.AcceptanceCriteria), nameof(Milestone.InnovativeReason), nameof(Milestone.CancelReason));
        foreach (var name in new[] { nameof(Milestone.Weight), nameof(Milestone.MaxAchieved), nameof(Milestone.PendingAchieve) })
        {
            builder.Property(name).HasPrecision(9, 4);
        }

        builder.Property(x => x.Cost).HasPrecision(18, 2);
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.HasPendingUpdate);
        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class MonthlyUpdateConfiguration : IEntityTypeConfiguration<MonthlyUpdate>
{
    public void Configure(EntityTypeBuilder<MonthlyUpdate> builder)
    {
        builder.ToTable("monthly_update");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Period).HasMaxLength(7);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.OpenedBy).HasMaxLength(60);
        builder.Property(x => x.SubmitComment).HasMaxLength(RequestRules.TextMaxLength);
        builder.Property(x => x.Items).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.ProjectId, x.Period }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class ProjectKpiConfiguration : IEntityTypeConfiguration<ProjectKpi>
{
    public void Configure(EntityTypeBuilder<ProjectKpi> builder)
    {
        ChildConfiguration.Child(builder, "project_kpi");
        ChildConfiguration.Texts(builder, 40, nameof(ProjectKpi.Nature), nameof(ProjectKpi.Direction), nameof(ProjectKpi.WhenMeasured), nameof(ProjectKpi.CreatedStage));
        ChildConfiguration.Texts(builder, 100, nameof(ProjectKpi.IndexClassCode));
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(ProjectKpi.NameAr), nameof(ProjectKpi.NameEn));
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(ProjectKpi.ProjectGoal), nameof(ProjectKpi.Equation));
        builder.Property(x => x.Target).HasPrecision(18, 4);
        builder.Property(x => x.CurrentResult).HasPrecision(18, 4);
        builder.HasIndex(x => x.KpiCardId);
    }
}

internal sealed class ProjectRiskConfiguration : IEntityTypeConfiguration<ProjectRisk>
{
    public void Configure(EntityTypeBuilder<ProjectRisk> builder)
    {
        ChildConfiguration.Child(builder, "project_risk");
        ChildConfiguration.Texts(builder, 40, nameof(ProjectRisk.InherentBand), nameof(ProjectRisk.ResidualBand), nameof(ProjectRisk.Response),
            nameof(ProjectRisk.CreatedStage));
        ChildConfiguration.Texts(builder, 100, nameof(ProjectRisk.CategoryCode), nameof(ProjectRisk.RmsStatus));
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(ProjectRisk.Title));
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(ProjectRisk.RiskFactor), nameof(ProjectRisk.MitigationActions));
        builder.Property(x => x.ResidualScore).HasPrecision(9, 2);
        builder.Property(x => x.ImplementationPct).HasPrecision(9, 4);
    }
}

internal sealed class StakeholderConfiguration : IEntityTypeConfiguration<Stakeholder>
{
    public void Configure(EntityTypeBuilder<Stakeholder> builder)
    {
        ChildConfiguration.Child(builder, "stakeholder");
        ChildConfiguration.Texts(builder, 40, nameof(Stakeholder.InterestLevel), nameof(Stakeholder.ImpactLevel));
        ChildConfiguration.Texts(builder, 100, nameof(Stakeholder.ClassificationCode), nameof(Stakeholder.Phone));
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(Stakeholder.Name), nameof(Stakeholder.CommunicationMethod),
            nameof(Stakeholder.CommunicationFrequency), nameof(Stakeholder.Email));
    }
}

internal sealed class ProposedMemberConfiguration : IEntityTypeConfiguration<ProposedMember>
{
    public void Configure(EntityTypeBuilder<ProposedMember> builder)
    {
        ChildConfiguration.Child(builder, "team_member");
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(ProposedMember.RoleAr), nameof(ProposedMember.RoleEn));
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(ProposedMember.Responsibilities));
    }
}

internal sealed class PlanningItemConfiguration : IEntityTypeConfiguration<PlanningItem>
{
    public void Configure(EntityTypeBuilder<PlanningItem> builder)
    {
        ChildConfiguration.Child(builder, "planning_item");
        ChildConfiguration.Texts(builder, 40, nameof(PlanningItem.Category), nameof(PlanningItem.CreatedStage));
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(PlanningItem.Description));
    }
}

internal sealed class ChallengeConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> builder)
    {
        ChildConfiguration.Child(builder, "challenge");
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(Challenge.Text), nameof(Challenge.Impact), nameof(Challenge.Recommendation));
        builder.Property(x => x.AchievedPct).HasPrecision(9, 4);
    }
}

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        ChildConfiguration.Child(builder, "lesson");
        ChildConfiguration.Texts(builder, 40, nameof(Lesson.Stage));
        ChildConfiguration.Texts(builder, 100, nameof(Lesson.TypeCode));
        ChildConfiguration.Texts(builder, RequestRules.TextMaxLength, nameof(Lesson.Text), nameof(Lesson.Recommendation));
    }
}

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        ChildConfiguration.Child(builder, "meeting");
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(Meeting.Title));
        ChildConfiguration.Texts(builder, 20000, nameof(Meeting.Notes));
    }
}

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        ChildConfiguration.Child(builder, "contract");
        ChildConfiguration.Texts(builder, 40, nameof(Contract.ContractStatus));
        ChildConfiguration.Texts(builder, 100, nameof(Contract.Number));
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(Contract.Name), nameof(Contract.Supplier));
        builder.Property(x => x.Amount).HasPrecision(18, 2);
    }
}

internal sealed class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.ToTable("change_request");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(80);
        builder.Property(x => x.Type).HasMaxLength(40);
        builder.Property(x => x.Status).HasMaxLength(40);
        builder.Property(x => x.Areas).HasColumnType("jsonb");
        builder.Property(x => x.Applied).HasColumnType("jsonb");
        ChildConfiguration.Texts(builder, RequestRules.NameMaxLength, nameof(ChangeRequest.ProposedNameAr), nameof(ChangeRequest.ProposedNameEn));
        ChildConfiguration.Texts(builder, RequestRules.LongTextMaxLength, nameof(ChangeRequest.Reason), nameof(ChangeRequest.ImpactOfNonResponse),
            nameof(ChangeRequest.ProposedDescription), nameof(ChangeRequest.ProposedScope), nameof(ChangeRequest.CancelReason));
        builder.Property(x => x.ProposedBudget).HasPrecision(18, 2);
        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.Number).IsUnique();
    }
}
