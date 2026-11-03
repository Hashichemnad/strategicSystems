using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow.Domain;

namespace Strategy.Platform.Workflow.Infrastructure;

/// <summary>Schema <c>workflow</c>: process versions and steps, instances, tasks, assignees, history, idempotency records.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "workflow";

    public override string Schema => SchemaName;

    public DbSet<ProcessVersion> Versions => Set<ProcessVersion>();

    public DbSet<ProcessStep> Steps => Set<ProcessStep>();

    public DbSet<ApprovalInstance> Instances => Set<ApprovalInstance>();

    public DbSet<ApprovalTask> Tasks => Set<ApprovalTask>();

    public DbSet<TaskAssignee> Assignees => Set<TaskAssignee>();

    public DbSet<HistoryEntry> History => Set<HistoryEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
}

internal sealed class ProcessVersionConfiguration : IEntityTypeConfiguration<ProcessVersion>
{
    public void Configure(EntityTypeBuilder<ProcessVersion> builder)
    {
        builder.ToTable("process_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProcessKey).HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ProcessKey, x.VersionNo }).IsUnique();
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.VersionId);
        builder.Ignore(x => x.OrderedSteps);
    }
}

internal sealed class ProcessStepConfiguration : IEntityTypeConfiguration<ProcessStep>
{
    public void Configure(EntityTypeBuilder<ProcessStep> builder)
    {
        builder.ToTable("process_step");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Rule).HasColumnType("jsonb");
        builder.Property(x => x.Condition).HasMaxLength(2000);
        builder.Property(x => x.CustomActions).HasColumnType("jsonb");
        builder.Property(x => x.Escalation).HasColumnType("jsonb");
        builder.Property(x => x.OnNoApprover).HasMaxLength(20);
        builder.Property(x => x.NotificationTemplate).HasMaxLength(150);
        builder.Property(x => x.SlaMode).HasMaxLength(20).HasDefaultValue(SlaModes.Working);
        builder.Property(x => x.BusinessHoursSource).HasMaxLength(20).HasDefaultValue(BusinessHoursSources.Default);
        builder.HasIndex(x => new { x.VersionId, x.Sort }).IsUnique();
    }
}

internal sealed class InstanceConfiguration : IEntityTypeConfiguration<ApprovalInstance>
{
    public void Configure(EntityTypeBuilder<ApprovalInstance> builder)
    {
        builder.ToTable("instance");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ProcessKey).HasMaxLength(150);
        builder.Property(x => x.ResourceType).HasMaxLength(100);
        builder.Property(x => x.SubjectNumber).HasMaxLength(100);
        builder.Property(x => x.TitleAr).HasMaxLength(500);
        builder.Property(x => x.TitleEn).HasMaxLength(500);
        builder.Property(x => x.Link).HasMaxLength(500);
        builder.Property(x => x.SubmitterName).HasMaxLength(300);
        builder.Property(x => x.Variables).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(ApprovalTask.CommentMaxLength);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
        builder.HasIndex(x => new { x.SubmitterUserId, x.Status });
        // At most one approval in progress per process and record.
        builder.HasIndex(x => new { x.ProcessKey, x.ResourceId }, "ix_instance_one_in_progress").IsUnique().HasFilter("status = 'in_progress'");
        builder.Ignore(x => x.Subject);
    }
}

internal sealed class TaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.ToTable("task");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StepCode).HasMaxLength(50);
        builder.Property(x => x.StepNameAr).HasMaxLength(200);
        builder.Property(x => x.StepNameEn).HasMaxLength(200);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Outcome).HasMaxLength(50);
        builder.Property(x => x.SlaMode).HasMaxLength(20);
        builder.Property(x => x.BusinessHoursNameAr).HasMaxLength(200);
        builder.Property(x => x.BusinessHoursNameEn).HasMaxLength(200);
        builder.Property(x => x.ActedByName).HasMaxLength(300);
        builder.Property(x => x.OnBehalfOfName).HasMaxLength(300);
        builder.Property(x => x.Comment).HasMaxLength(ApprovalTask.CommentMaxLength);
        builder.HasIndex(x => x.InstanceId);
        builder.HasIndex(x => new { x.Status, x.DueAt });
        builder.HasMany(x => x.Assignees).WithOne().HasForeignKey(a => a.TaskId);
        builder.Ignore(x => x.ActiveAssignees);
    }
}

internal sealed class AssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> builder)
    {
        builder.ToTable("task_assignee");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(300);
        builder.Property(x => x.Source).HasMaxLength(20);
        builder.Property(x => x.RemovedReason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.UserId, x.Active });
        builder.HasIndex(x => new { x.EmployeeId, x.Active });
    }
}

internal sealed class HistoryConfiguration : IEntityTypeConfiguration<HistoryEntry>
{
    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("history");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Seq).UseIdentityByDefaultColumn();
        builder.Property(x => x.Event).HasMaxLength(30);
        builder.Property(x => x.StepNameAr).HasMaxLength(200);
        builder.Property(x => x.StepNameEn).HasMaxLength(200);
        builder.Property(x => x.ActorName).HasMaxLength(300);
        builder.Property(x => x.OnBehalfOfName).HasMaxLength(300);
        builder.Property(x => x.Comment).HasMaxLength(ApprovalTask.CommentMaxLength);
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.InstanceId, x.Seq });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_record");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.Property(x => x.Fingerprint).HasMaxLength(64);
        builder.Property(x => x.ContentType).HasMaxLength(200);
        builder.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
    }
}
