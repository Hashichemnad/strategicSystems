using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.DocumentRegistry.Infrastructure;

/// <summary>Schema <c>docreg</c> (docs/design/requests.md §3.2).</summary>
public sealed class DocRegDbContext(DbContextOptions<DocRegDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "docreg";

    public override string Schema => SchemaName;

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> Versions => Set<DocumentVersion>();

    public DbSet<Acknowledgement> Acknowledgements => Set<Acknowledgement>();

    public DbSet<ValidityEvent> ValidityEvents => Set<ValidityEvent>();
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("document");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentNo).HasMaxLength(80);
        builder.Property(x => x.NameAr).HasMaxLength(Document.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(Document.NameMaxLength);
        builder.Property(x => x.Description).HasMaxLength(Document.TextMaxLength);
        builder.Property(x => x.Level).HasMaxLength(100);
        builder.Property(x => x.DocType).HasMaxLength(100);
        builder.Property(x => x.Sensitivity).HasMaxLength(100);
        builder.Property(x => x.CurrentVersionLabel).HasMaxLength(20);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.WithdrawnReason).HasMaxLength(2000);
        builder.Property(x => x.SearchText).HasMaxLength(10000);
        builder.HasTrigramIndex(nameof(Document.SearchText)); // global search (ADR 0160)
        builder.HasIndex(x => x.DocumentNo).IsUnique();
        builder.HasIndex(x => new { x.Status, x.Sensitivity });
        builder.HasIndex(x => x.OwnerUnitId);
        builder.HasIndex(x => x.ExpiryDate);
    }
}

internal sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_version");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Label).HasMaxLength(20);
        builder.Property(x => x.RequestNo).HasMaxLength(60);
        builder.Property(x => x.ChangeSummary).HasMaxLength(4000);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.FinalFileName).HasMaxLength(300);
        builder.Property(x => x.SummaryAr).HasMaxLength(8000);
        builder.Property(x => x.SummaryEn).HasMaxLength(8000);
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.RequestId).IsUnique();
    }
}

internal sealed class AcknowledgementConfiguration : IEntityTypeConfiguration<Acknowledgement>
{
    public void Configure(EntityTypeBuilder<Acknowledgement> builder)
    {
        builder.ToTable("acknowledgement");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.VersionId, x.EmployeeId }).IsUnique();
    }
}

internal sealed class ValidityEventConfiguration : IEntityTypeConfiguration<ValidityEvent>
{
    public void Configure(EntityTypeBuilder<ValidityEvent> builder)
    {
        builder.ToTable("validity_event");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(30);
        builder.Property(x => x.Comment).HasMaxLength(2000);
        builder.HasIndex(x => new { x.DocumentId, x.Kind });
    }
}
