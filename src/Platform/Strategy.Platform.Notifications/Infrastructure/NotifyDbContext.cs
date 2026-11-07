using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Notifications.Infrastructure;

/// <summary>Schema <c>notify</c>: templates, in-app notifications, preferences and the e-mail delivery log.</summary>
public sealed class NotifyDbContext(DbContextOptions<NotifyDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notify";

    public override string Schema => SchemaName;

    public override string AuditSystemCode => "notifications";

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<InAppNotification> Notifications => Set<InAppNotification>();

    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    public DbSet<EmailDelivery> Deliveries => Set<EmailDelivery>();

    public DbSet<SentRequest> SentRequests => Set<SentRequest>();
}

internal sealed class TemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("template");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(150);
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.SystemCode).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.SubjectAr).HasMaxLength(300);
        builder.Property(x => x.SubjectEn).HasMaxLength(300);
        builder.Property(x => x.BodyAr).HasMaxLength(8000);
        builder.Property(x => x.BodyEn).HasMaxLength(8000);
        builder.Property(x => x.ButtonAr).HasMaxLength(100);
        builder.Property(x => x.ButtonEn).HasMaxLength(100);
        builder.Property(x => x.Placeholders).HasMaxLength(500);
    }
}

internal sealed class InAppNotificationConfiguration : IEntityTypeConfiguration<InAppNotification>
{
    public void Configure(EntityTypeBuilder<InAppNotification> builder)
    {
        builder.ToTable("notification");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TemplateKey).HasMaxLength(150);
        builder.Property(x => x.TitleAr).HasMaxLength(300);
        builder.Property(x => x.TitleEn).HasMaxLength(300);
        builder.Property(x => x.Values).HasColumnType("jsonb");
        builder.Property(x => x.Link).HasMaxLength(500);
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.UserId, x.ReadAt });
    }
}

internal sealed class PreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("preference");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventKey).HasMaxLength(150);
        builder.HasIndex(x => new { x.UserId, x.EventKey }).IsUnique();
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        builder.ToTable("delivery");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.To).HasMaxLength(320);
        builder.Property(x => x.TemplateKey).HasMaxLength(150);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(SentRequestConfiguration.KeyMaxLength + 330);
        builder.HasIndex(x => x.At);
        builder.HasIndex(x => x.IdempotencyKey).HasFilter("idempotency_key is not null");
    }
}

internal sealed class SentRequestConfiguration : IEntityTypeConfiguration<SentRequest>
{
    public const int KeyMaxLength = 200;

    public void Configure(EntityTypeBuilder<SentRequest> builder)
    {
        builder.ToTable("sent_request");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(KeyMaxLength);
        builder.Property(x => x.TemplateKey).HasMaxLength(150);
        builder.HasIndex(x => x.CreatedAt);
    }
}
