using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Teams.Infrastructure;

/// <summary>Schema <c>teams</c> (docs/design/teams.md §2–§3).</summary>
public sealed class TeamsDbContext(DbContextOptions<TeamsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "teams";

    public override string Schema => SchemaName;

    public DbSet<TeamType> Types => Set<TeamType>();

    public DbSet<MemberRole> Roles => Set<MemberRole>();

    public DbSet<EvaluationTemplate> Templates => Set<EvaluationTemplate>();

    public DbSet<RewardMatrix> RewardMatrices => Set<RewardMatrix>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> Members => Set<TeamMember>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<Attendance> Attendance => Set<Attendance>();

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    public DbSet<TeamReport> Reports => Set<TeamReport>();

    public DbSet<TeamEvaluation> Evaluations => Set<TeamEvaluation>();

    public DbSet<MemberEvaluation> MemberEvaluations => Set<MemberEvaluation>();
}

internal sealed class TeamTypeConfiguration : IEntityTypeConfiguration<TeamType>
{
    public void Configure(EntityTypeBuilder<TeamType> builder)
    {
        builder.ToTable("team_type");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Permanence).HasMaxLength(20);
        builder.Property(x => x.DefaultCadence).HasMaxLength(20);
        builder.Property(x => x.EvaluationTrigger).HasMaxLength(40);
        builder.Property(x => x.EvaluatorRule).HasMaxLength(40);
        builder.Property(x => x.EvaluatorMemberRole).HasMaxLength(100);
        builder.Property(x => x.LeaderEvaluatorRule).HasMaxLength(40);
        builder.Property(x => x.LeaderEvaluatorMemberRole).HasMaxLength(100);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class MemberRoleConfiguration : IEntityTypeConfiguration<MemberRole>
{
    public void Configure(EntityTypeBuilder<MemberRole> builder)
    {
        builder.ToTable("member_role");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class EvaluationTemplateConfiguration : IEntityTypeConfiguration<EvaluationTemplate>
{
    public void Configure(EntityTypeBuilder<EvaluationTemplate> builder)
    {
        builder.ToTable("evaluation_template");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Content).HasColumnType("jsonb");
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.Year, x.VersionNo }).IsUnique();
    }
}

internal sealed class RewardMatrixConfiguration : IEntityTypeConfiguration<RewardMatrix>
{
    public void Configure(EntityTypeBuilder<RewardMatrix> builder)
    {
        builder.ToTable("reward_matrix");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Content).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.Year, x.VersionNo }).IsUnique();
    }
}

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("team");
        builder.HasQuickSearch("number", "name_ar", "name_en", "subject"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(Team.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(Team.NameMaxLength);
        builder.Property(x => x.TypeCode).HasMaxLength(100);
        builder.Property(x => x.Subject).HasMaxLength(Team.NameMaxLength * 2);
        builder.Property(x => x.Objectives).HasMaxLength(Team.TextMaxLength);
        builder.Property(x => x.Mandate).HasMaxLength(Team.TextMaxLength);
        builder.Property(x => x.ApprovedByCode).HasMaxLength(100);
        builder.Property(x => x.MemoMode).HasMaxLength(20);
        builder.Property(x => x.MemoNumber).HasMaxLength(100);
        builder.Property(x => x.MemoDraft).HasMaxLength(Team.TextMaxLength);
        builder.Property(x => x.Cadence).HasMaxLength(20);
        builder.Property(x => x.ReportTo).HasMaxLength(300);
        builder.Property(x => x.Workload).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(40);
        builder.Property(x => x.StatusReason).HasMaxLength(2000);
        builder.Property(x => x.EvaluatorRule).HasMaxLength(40);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.OwningUnitId);
        builder.HasIndex(x => x.LeaderEmployeeId);
        builder.HasIndex(x => x.TypeCode);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_member");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExternalName).HasMaxLength(300);
        builder.Property(x => x.ExternalOrganisation).HasMaxLength(300);
        builder.Property(x => x.ExternalEmail).HasMaxLength(300);
        builder.Property(x => x.ExternalPhone).HasMaxLength(50);
        builder.Property(x => x.RoleCode).HasMaxLength(100);
        builder.Property(x => x.Responsibilities).HasMaxLength(4000);
        builder.Property(x => x.RequiredSkills).HasMaxLength(2000);
        builder.Property(x => x.AllocationPercent).HasPrecision(9, 4);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.LeaveReason).HasMaxLength(2000);
        builder.Ignore(x => x.IsExternal);
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => x.TeamId);
        builder.HasIndex(x => x.EmployeeId);
    }
}

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("meeting");
        builder.HasQuickSearch("number", "title", "location"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.Title).HasMaxLength(Team.NameMaxLength);
        builder.Property(x => x.Location).HasMaxLength(300);
        builder.Property(x => x.Agenda).HasMaxLength(Meeting.TextMaxLength);
        builder.Property(x => x.Minutes).HasMaxLength(Meeting.TextMaxLength);
        builder.Property(x => x.Decisions).HasMaxLength(Meeting.TextMaxLength);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.MinutesDelayHours).HasPrecision(12, 2);
        builder.Property(x => x.CancelReason).HasMaxLength(2000);
        builder.Ignore(x => x.MinutesSubmitted);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(x => new { x.TeamId, x.ScheduledAt });
    }
}

internal sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("attendance");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => new { x.MeetingId, x.MemberId }).IsUnique();
    }
}

internal sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> builder)
    {
        builder.ToTable("recommendation");
        builder.HasQuickSearch("number", "text"); // global search (ADR 0160)
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Text).HasMaxLength(Recommendation.TextMaxLength);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.ClosureNote).HasMaxLength(4000);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(x => x.TeamId);
        builder.HasIndex(x => x.OwnerEmployeeId);
    }
}

internal sealed class TeamReportConfiguration : IEntityTypeConfiguration<TeamReport>
{
    public void Configure(EntityTypeBuilder<TeamReport> builder)
    {
        builder.ToTable("team_report");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Summary).HasMaxLength(Meeting.TextMaxLength);
        builder.HasIndex(x => new { x.TeamId, x.PeriodStart }).IsUnique();
        builder.HasIndex(x => x.DueAt);
    }
}

internal sealed class TeamEvaluationConfiguration : IEntityTypeConfiguration<TeamEvaluation>
{
    public void Configure(EntityTypeBuilder<TeamEvaluation> builder)
    {
        builder.ToTable("team_evaluation");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.Cycle).HasMaxLength(20);
        builder.Property(x => x.Trigger).HasMaxLength(40);
        builder.Property(x => x.EvaluatorRule).HasMaxLength(40);
        builder.Property(x => x.Criteria).HasColumnType("jsonb");
        builder.Property(x => x.Flags).HasColumnType("jsonb");
        builder.Property(x => x.Score).HasPrecision(9, 2);
        builder.Property(x => x.Workload).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.ReturnComment).HasMaxLength(4000);
        builder.Property(x => x.CancelReason).HasMaxLength(2000);
        builder.Property(x => x.RewardStatus).HasMaxLength(20);
        builder.Property(x => x.RewardTotal).HasPrecision(18, 2);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(x => new { x.TeamId, x.Cycle }, "ux_team_evaluation_cycle").IsUnique().HasFilter("status <> 'cancelled'");
        builder.HasIndex(x => x.EvaluatorEmployeeId);
    }
}

internal sealed class MemberEvaluationConfiguration : IEntityTypeConfiguration<MemberEvaluation>
{
    public void Configure(EntityTypeBuilder<MemberEvaluation> builder)
    {
        builder.ToTable("member_evaluation");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExternalName).HasMaxLength(300);
        builder.Property(x => x.RoleCode).HasMaxLength(100);
        builder.Property(x => x.Answers).HasColumnType("jsonb");
        builder.Property(x => x.Comment).HasMaxLength(4000);
        builder.Property(x => x.Grade).HasMaxLength(100);
        builder.Property(x => x.AttendanceRate).HasPrecision(9, 4);
        builder.Property(x => x.CalculatedReward).HasPrecision(18, 2);
        builder.Property(x => x.ApprovedReward).HasPrecision(18, 2);
        builder.Property(x => x.AdjustmentReason).HasMaxLength(2000);
        builder.Property(x => x.CertificateNumber).HasMaxLength(50);
        builder.HasIndex(x => new { x.EvaluationId, x.MemberId }).IsUnique();
        builder.HasIndex(x => x.EmployeeId);
    }
}
