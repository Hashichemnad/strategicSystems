using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings.Domain;

namespace Strategy.Platform.Settings.Infrastructure;

/// <summary>Settings, lookups, numbering, feature flags, branding and translation overrides, schema <c>settings</c>.</summary>
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "settings";

    public override string Schema => SchemaName;

    public DbSet<SettingEntry> Entries => Set<SettingEntry>();

    public DbSet<LookupType> LookupTypes => Set<LookupType>();

    public DbSet<LookupValue> LookupValues => Set<LookupValue>();

    public DbSet<NumberingFormat> NumberingFormats => Set<NumberingFormat>();

    public DbSet<NumberingCounter> NumberingCounters => Set<NumberingCounter>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    public DbSet<BrandingAsset> BrandingAssets => Set<BrandingAsset>();

    public DbSet<TranslationOverride> TranslationOverrides => Set<TranslationOverride>();
}

internal sealed class SettingEntryConfiguration : IEntityTypeConfiguration<SettingEntry>
{
    public void Configure(EntityTypeBuilder<SettingEntry> builder)
    {
        builder.ToTable("setting");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.Property(x => x.Value).HasColumnType("jsonb");
        builder.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class LookupTypeConfiguration : IEntityTypeConfiguration<LookupType>
{
    public void Configure(EntityTypeBuilder<LookupType> builder)
    {
        builder.ToTable("lookup_type");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(LookupType.CodeMaxLength);
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.ParentTypeCode).HasMaxLength(LookupType.CodeMaxLength);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class LookupValueConfiguration : IEntityTypeConfiguration<LookupValue>
{
    public void Configure(EntityTypeBuilder<LookupValue> builder)
    {
        builder.ToTable("lookup_value");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TypeCode).HasMaxLength(LookupType.CodeMaxLength);
        builder.Property(x => x.Code).HasMaxLength(LookupValue.CodeMaxLength);
        builder.Property(x => x.LabelAr).HasMaxLength(LookupValue.LabelMaxLength);
        builder.Property(x => x.LabelEn).HasMaxLength(LookupValue.LabelMaxLength);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TypeCode, x.Code }).IsUnique();
        builder.HasOne<LookupType>().WithMany().HasPrincipalKey(t => t.Code).HasForeignKey(x => x.TypeCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LookupValue>().WithMany().HasForeignKey(x => x.ParentValueId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NumberingFormatConfiguration : IEntityTypeConfiguration<NumberingFormat>
{
    public void Configure(EntityTypeBuilder<NumberingFormat> builder)
    {
        builder.ToTable("numbering_format");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Key).HasMaxLength(100);
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.Pattern).HasMaxLength(100);
        builder.Property(x => x.DefaultPattern).HasMaxLength(100);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class NumberingCounterConfiguration : IEntityTypeConfiguration<NumberingCounter>
{
    public void Configure(EntityTypeBuilder<NumberingCounter> builder)
    {
        builder.ToTable("numbering_counter");
        builder.HasKey(x => new { x.FormatKey, x.Partition });
        builder.Property(x => x.FormatKey).HasMaxLength(100);
        builder.Property(x => x.Partition).HasMaxLength(200);
    }
}

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flag");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.SystemCode).HasMaxLength(64);
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class BrandingAssetConfiguration : IEntityTypeConfiguration<BrandingAsset>
{
    public void Configure(EntityTypeBuilder<BrandingAsset> builder)
    {
        builder.ToTable("branding_asset");
        builder.HasKey(x => x.Kind);
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.ContentType).HasMaxLength(64);
    }
}

internal sealed class TranslationOverrideConfiguration : IEntityTypeConfiguration<TranslationOverride>
{
    public void Configure(EntityTypeBuilder<TranslationOverride> builder)
    {
        builder.ToTable("translation_override");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Language).HasMaxLength(8);
        builder.Property(x => x.Namespace).HasMaxLength(64);
        builder.Property(x => x.Key).HasMaxLength(200);
        builder.Property(x => x.Value).HasMaxLength(TranslationOverride.ValueMaxLength);
        builder.HasIndex(x => new { x.Language, x.Namespace, x.Key }).IsUnique();
    }
}
