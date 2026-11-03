using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Tasks.Infrastructure;

/// <summary>Schema <c>tasks</c> (docs/design/tasks.md §2).</summary>
public sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "tasks";

    public override string Schema => SchemaName;

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TimesheetPeriod> Periods => Set<TimesheetPeriod>();

    public DbSet<AssignmentGroup> Groups => Set<AssignmentGroup>();

    public DbSet<TaskComment> Comments => Set<TaskComment>();

    public DbSet<FollowUp> FollowUps => Set<FollowUp>();

    public DbSet<ChecklistItem> Checklist => Set<ChecklistItem>();

    public DbSet<TaskDependency> Dependencies => Set<TaskDependency>();

    public DbSet<TaskEvent> Events => Set<TaskEvent>();
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("task");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.TitleAr).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(x => x.Description).HasMaxLength(TaskItem.DescriptionMaxLength);
        builder.Property(x => x.TypeCode).HasMaxLength(100);
        builder.Property(x => x.Template).HasMaxLength(20);
        builder.Property(x => x.SourceCode).HasMaxLength(100);
        builder.Property(x => x.CategoryCode).HasMaxLength(100);
        builder.Property(x => x.Priority).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.LinkedType).HasMaxLength(100);
        builder.Property(x => x.RejectionReason).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.Property(x => x.CancelReason).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.Property(x => x.CompletionComment).HasMaxLength(TaskItem.ReasonMaxLength * 2);
        builder.Property(x => x.FeedbackComment).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.Property(x => x.EstimatedHours).HasPrecision(9, 2);
        builder.Property(x => x.Recurrence).HasColumnType("jsonb");
        builder.HasMany(x => x.Periods).WithOne().HasForeignKey(p => p.TaskId);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(x => x.AssigneeEmployeeId);
        builder.HasIndex(x => x.CreatorEmployeeId);
        builder.HasIndex(x => x.UnitId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DueAt);
        builder.HasIndex(x => x.GroupId);
        builder.HasIndex(x => x.ParentTaskId);
        builder.HasIndex(x => new { x.LinkedType, x.LinkedId });
        builder.HasIndex(x => x.NextOccurrenceOn).HasFilter("next_occurrence_on IS NOT NULL");
    }
}

internal sealed class TimesheetPeriodConfiguration : IEntityTypeConfiguration<TimesheetPeriod>
{
    public void Configure(EntityTypeBuilder<TimesheetPeriod> builder)
    {
        builder.ToTable("timesheet_period");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        builder.Property(x => x.HoldReason).HasMaxLength(TaskItem.ReasonMaxLength);
        builder.Property(x => x.EndEvent).HasMaxLength(30);
        builder.HasIndex(x => x.Number).IsUnique().HasFilter("number IS NOT NULL");
    }
}

internal sealed class AssignmentGroupConfiguration : IEntityTypeConfiguration<AssignmentGroup>
{
    public void Configure(EntityTypeBuilder<AssignmentGroup> builder)
    {
        builder.ToTable("assignment_group");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TitleAr).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(x => x.CompletionRule).HasMaxLength(10);
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("comment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Text).HasMaxLength(TaskComment.TextMaxLength);
        builder.Property(x => x.Kind).HasMaxLength(30);
        builder.HasIndex(x => x.TaskId);
    }
}

internal sealed class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> builder)
    {
        builder.ToTable("follow_up");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TaskId, x.EmployeeId }, "ux_follow_up_active").IsUnique().HasFilter("active");
        builder.HasIndex(x => x.EmployeeId);
    }
}

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_item");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Text).HasMaxLength(ChecklistItem.TextMaxLength);
        builder.HasIndex(x => x.TaskId);
    }
}

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("dependency");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TaskId, x.DependsOnTaskId }, "ux_dependency_active").IsUnique().HasFilter("active");
        builder.HasIndex(x => x.DependsOnTaskId);
    }
}

internal sealed class TaskEventConfiguration : IEntityTypeConfiguration<TaskEvent>
{
    public void Configure(EntityTypeBuilder<TaskEvent> builder)
    {
        builder.ToTable("task_event");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Event).HasMaxLength(40);
        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(TaskItem.ReasonMaxLength * 2);
        builder.Property(x => x.Data).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TaskId, x.OccurredAt });
    }
}
