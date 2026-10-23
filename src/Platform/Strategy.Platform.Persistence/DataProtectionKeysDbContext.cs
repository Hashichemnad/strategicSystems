using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Strategy.Platform.Persistence;

/// <summary>
/// The ASP.NET Core Data Protection key ring, persisted in <c>core.data_protection_keys</c> so cookies and encrypted
/// secrets survive restarts and are part of every database backup (ADR 0012).
/// </summary>
public sealed class DataProtectionKeysDbContext(DbContextOptions<DataProtectionKeysDbContext> options)
    : ModuleDbContext(options), IDataProtectionKeyContext
{
    public const string SchemaName = "core";

    /// <summary>Shares the <c>core</c> schema with Organisation, so it keeps its own history table.</summary>
    public const string HistoryTable = "__ef_migrations_history_keys";

    public override string Schema => SchemaName;

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<DataProtectionKey> builder) =>
        builder.ToTable("data_protection_keys");
}
