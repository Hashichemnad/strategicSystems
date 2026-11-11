using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Integration.Domain;

namespace Strategy.Platform.Integration.Infrastructure;

internal sealed class IntegrationClientConfiguration : IEntityTypeConfiguration<IntegrationClient>
{
    public void Configure(EntityTypeBuilder<IntegrationClient> builder)
    {
        builder.ToTable("client");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ClientId).HasMaxLength(40);
        builder.Property(x => x.NameAr).HasMaxLength(IntegrationClient.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(IntegrationClient.NameMaxLength);
        builder.Property(x => x.Description).HasMaxLength(IntegrationClient.DescriptionMaxLength);
        builder.Property(x => x.AuthKind).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.RevokeReason).HasMaxLength(1000);
        builder.Property(x => x.LastUsedIp).HasMaxLength(64);
        builder.HasIndex(x => x.ClientId).IsUnique();
        builder.HasIndex(x => x.ServiceUserId).IsUnique();
    }
}

internal sealed class ClientCredentialConfiguration : IEntityTypeConfiguration<ClientCredential>
{
    public void Configure(EntityTypeBuilder<ClientCredential> builder)
    {
        builder.ToTable("client_credential");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ClientRef).HasColumnName("client_id");
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Prefix).HasMaxLength(16);
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => x.ClientRef);
    }
}

internal sealed class ClientUsageConfiguration : IEntityTypeConfiguration<ClientUsage>
{
    public void Configure(EntityTypeBuilder<ClientUsage> builder)
    {
        builder.ToTable("client_usage");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ClientRef).HasColumnName("client_id");
        builder.Property(x => x.Method).HasMaxLength(10);
        builder.Property(x => x.Path).HasMaxLength(500);
        builder.Property(x => x.Ip).HasMaxLength(64);
        builder.Property(x => x.RefusedReason).HasMaxLength(20);
        builder.HasIndex(x => new { x.ClientRef, x.At });
        builder.HasIndex(x => x.At);
    }
}

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("webhook_subscription");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ClientRef).HasColumnName("client_id");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.TargetUrl).HasMaxLength(WebhookSubscription.UrlMaxLength);
        builder.HasIndex(x => x.ClientRef);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_delivery");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubscriptionRef).HasColumnName("subscription_id");
        builder.Property(x => x.ClientRef).HasColumnName("client_id");
        builder.Property(x => x.EventCode).HasMaxLength(100);
        builder.Property(x => x.EventKey).HasMaxLength(64);
        builder.Property(x => x.ResourceType).HasMaxLength(100);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.HasIndex(x => new { x.SubscriptionRef, x.EventKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.ClientRef, x.CreatedAt });
    }
}

internal sealed class WebhookAttemptConfiguration : IEntityTypeConfiguration<WebhookAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookAttempt> builder)
    {
        builder.ToTable("webhook_attempt");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DeliveryRef).HasColumnName("delivery_id");
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => x.DeliveryRef);
    }
}

internal sealed class ExportScheduleConfiguration : IEntityTypeConfiguration<ExportSchedule>
{
    public void Configure(EntityTypeBuilder<ExportSchedule> builder)
    {
        builder.ToTable("export_schedule");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ClientRef).HasColumnName("client_id");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Format).HasMaxLength(10);
        builder.Property(x => x.Folder).HasMaxLength(500);
        builder.Property(x => x.LastRunStatus).HasMaxLength(500);
        builder.Property(x => x.Watermarks)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, DateTimeOffset>(),
                new ValueComparer<Dictionary<string, DateTimeOffset>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
                    v => new Dictionary<string, DateTimeOffset>(v)));
        builder.HasIndex(x => x.ClientRef);
    }
}
