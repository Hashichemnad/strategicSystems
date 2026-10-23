using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Accounts, sign-in settings, providers, sessions and tokens in schema <c>identity</c> (ADR 0010). The ASP.NET Core Identity
/// tables are mapped here by hand (the context derives from <see cref="ModuleDbContext"/> for the shared conventions).
/// </summary>
public sealed class IdentityStoreDbContext(DbContextOptions<IdentityStoreDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<StrategyUser> Users => Set<StrategyUser>();

    public DbSet<IdentityUserLogin<Guid>> UserLogins => Set<IdentityUserLogin<Guid>>();

    public DbSet<IdentityUserToken<Guid>> UserTokens => Set<IdentityUserToken<Guid>>();

    public DbSet<IdentityUserClaim<Guid>> UserClaims => Set<IdentityUserClaim<Guid>>();

    public DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();

    public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();

    public DbSet<SmtpSettings> SmtpSettings => Set<SmtpSettings>();

    public DbSet<IdentityProvider> Providers => Set<IdentityProvider>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<PersonalAccessToken> Tokens => Set<PersonalAccessToken>();

    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();
}

internal sealed class StrategyUserConfiguration : IEntityTypeConfiguration<StrategyUser>
{
    public void Configure(EntityTypeBuilder<StrategyUser> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.UserName).HasMaxLength(256);
        builder.Property(u => u.NormalizedUserName).HasMaxLength(256);
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256);
        builder.Property(u => u.PhoneNumber).HasMaxLength(30);
        builder.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(u => u.PrincipalType).HasMaxLength(20);
        builder.Property(u => u.Language).HasMaxLength(10);
        builder.Property(u => u.Theme).HasMaxLength(10);
        builder.Ignore(u => u.HasLocalPassword);
        builder.HasIndex(u => u.NormalizedUserName).IsUnique();
        builder.HasIndex(u => u.NormalizedEmail);
        builder.HasIndex(u => u.EmployeeId).IsUnique();
    }
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable("user_logins");
        builder.HasKey(l => new { l.LoginProvider, l.ProviderKey });
        builder.Property(l => l.LoginProvider).HasMaxLength(128);
        builder.Property(l => l.ProviderKey).HasMaxLength(512);
        builder.HasIndex(l => l.UserId);
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable("user_tokens");
        builder.HasKey(t => new { t.UserId, t.LoginProvider, t.Name });
        builder.Property(t => t.LoginProvider).HasMaxLength(128);
        builder.Property(t => t.Name).HasMaxLength(128);
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.ToTable("user_claims");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.UserId);
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PasswordHistoryEntry> builder)
    {
        builder.ToTable("password_history");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => new { p.UserId, p.CreatedAt });
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SecuritySettingsConfiguration : IEntityTypeConfiguration<SecuritySettings>
{
    public void Configure(EntityTypeBuilder<SecuritySettings> builder)
    {
        builder.ToTable("security_settings");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.MfaPolicy).HasMaxLength(20);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasData(new SecuritySettings());
    }
}

internal sealed class SmtpSettingsConfiguration : IEntityTypeConfiguration<SmtpSettings>
{
    public void Configure(EntityTypeBuilder<SmtpSettings> builder)
    {
        builder.ToTable("smtp_settings");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Host).HasMaxLength(255);
        builder.Property(s => s.Security).HasMaxLength(20);
        builder.Property(s => s.UserName).HasMaxLength(255);
        builder.Property(s => s.FromAddress).HasMaxLength(256);
        builder.Property(s => s.FromNameAr).HasMaxLength(200);
        builder.Property(s => s.FromNameEn).HasMaxLength(200);
        builder.Property(s => s.PublicBaseUrl).HasMaxLength(500);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasData(new SmtpSettings());
    }
}

internal sealed class IdentityProviderConfiguration : IEntityTypeConfiguration<IdentityProvider>
{
    public void Configure(EntityTypeBuilder<IdentityProvider> builder)
    {
        builder.ToTable("identity_provider");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(IdentityProvider.CodeMaxLength);
        builder.Property(p => p.Type).HasMaxLength(20);
        builder.Property(p => p.DisplayNameAr).HasMaxLength(200);
        builder.Property(p => p.DisplayNameEn).HasMaxLength(200);
        builder.Property(p => p.MatchField).HasMaxLength(20);
        builder.Property(p => p.LastTestMessageKey).HasMaxLength(200);
        builder.Property(p => p.Settings).HasColumnType("jsonb")
            .HasConversion(v => v.ToJsonString(null), v => (JsonObject)JsonNode.Parse(v, null, default)!)
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<JsonObject>(
                (a, b) => a!.ToJsonString(null) == b!.ToJsonString(null),
                v => v.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
                v => (JsonObject)JsonNode.Parse(v.ToJsonString(null), null, default)!));
        builder.Property(p => p.Version).IsRowVersion();
        builder.HasIndex(p => p.Code).IsUnique();
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_session");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Method).HasMaxLength(20);
        builder.Property(s => s.Provider).HasMaxLength(IdentityProvider.CodeMaxLength);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(512);
        builder.HasIndex(s => new { s.UserId, s.RevokedAt });
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        builder.ToTable("personal_access_token");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(PersonalAccessToken.NameMaxLength);
        builder.Property(t => t.Prefix).HasMaxLength(16);
        builder.Property(t => t.LastUsedIp).HasMaxLength(64);
        builder.HasIndex(t => t.Prefix).IsUnique();
        builder.HasIndex(t => t.UserId);
        builder.HasOne<StrategyUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuthEventConfiguration : IEntityTypeConfiguration<AuthEvent>
{
    public void Configure(EntityTypeBuilder<AuthEvent> builder)
    {
        builder.ToTable("auth_event");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(50);
        builder.Property(e => e.UserName).HasMaxLength(256);
        builder.Property(e => e.Provider).HasMaxLength(IdentityProvider.CodeMaxLength);
        builder.Property(e => e.Detail).HasMaxLength(200);
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.HasIndex(e => e.At);
        builder.HasIndex(e => new { e.UserId, e.At });
    }
}
