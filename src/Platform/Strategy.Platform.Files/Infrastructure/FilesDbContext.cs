using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Files.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Files.Infrastructure;

/// <summary>Schema <c>files</c>: file records, versions and the upgrade staging table. Contents live in the file store.</summary>
public sealed class FilesDbContext(DbContextOptions<FilesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "files";

    public override string Schema => SchemaName;

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<FileVersion> Versions => Set<FileVersion>();

    public DbSet<PendingImport> PendingImports => Set<PendingImport>();
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("file");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Purpose).HasMaxLength(100);
        builder.Property(x => x.OwnerType).HasMaxLength(100);
        builder.Property(x => x.OwnerId).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.DeleteReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OwnerType, x.OwnerId, x.Purpose });
    }
}

internal sealed class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("file_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StorageKey).HasMaxLength(200);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.ContentType).HasMaxLength(150);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.ScanStatus).HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(500);
        builder.HasIndex(x => new { x.FileId, x.VersionNo }).IsUnique();
        builder.HasIndex(x => x.ScanStatus);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PendingImportConfiguration : IEntityTypeConfiguration<PendingImport>
{
    public void Configure(EntityTypeBuilder<PendingImport> builder)
    {
        builder.ToTable("pending_import");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Purpose).HasMaxLength(100);
        builder.Property(x => x.OwnerType).HasMaxLength(100);
        builder.Property(x => x.OwnerId).HasMaxLength(100);
        builder.Property(x => x.FileName).HasMaxLength(255);
    }
}
