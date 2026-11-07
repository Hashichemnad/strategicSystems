using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Requests.Infrastructure;

/// <summary>Schema <c>requests</c> (docs/design/requests.md §2).</summary>
public sealed class RequestsDbContext(DbContextOptions<RequestsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "requests";

    public override string Schema => SchemaName;

    public DbSet<RequestType> Types => Set<RequestType>();

    public DbSet<Request> Requests => Set<Request>();

    public DbSet<RequestEvent> Events => Set<RequestEvent>();
}

internal sealed class RequestTypeConfiguration : IEntityTypeConfiguration<RequestType>
{
    public void Configure(EntityTypeBuilder<RequestType> builder)
    {
        builder.ToTable("request_type");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Submitters);
        builder.Ignore(x => x.Form);
        builder.Ignore(x => x.Stages);
        builder.Ignore(x => x.Templates);
        builder.Ignore(x => x.IsBuiltIn);
        builder.Property(x => x.Code).HasMaxLength(60);
        builder.Property(x => x.SystemCode).HasMaxLength(40);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.NameAr).HasMaxLength(RequestType.NameMaxLength);
        builder.Property(x => x.NameEn).HasMaxLength(RequestType.NameMaxLength);
        builder.Property(x => x.DescriptionAr).HasMaxLength(RequestType.TextMaxLength);
        builder.Property(x => x.DescriptionEn).HasMaxLength(RequestType.TextMaxLength);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.Icon).HasMaxLength(40);
        builder.Property(x => x.NumberPrefix).HasMaxLength(20);
        builder.Property(x => x.SubmittersJson).HasColumnName("submitters").HasColumnType("jsonb");
        builder.Property(x => x.FormJson).HasColumnName("form").HasColumnType("jsonb");
        builder.Property(x => x.StagesJson).HasColumnName("stages").HasColumnType("jsonb");
        builder.Property(x => x.TemplatesJson).HasColumnName("pdf_templates").HasColumnType("jsonb");
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("request");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Ignore(x => x.Data);
        builder.Ignore(x => x.ProcessData);
        builder.Property(x => x.Number).HasMaxLength(60);
        builder.Property(x => x.TypeCode).HasMaxLength(60);
        builder.Property(x => x.SystemCode).HasMaxLength(40);
        builder.Property(x => x.TitleAr).HasMaxLength(Request.TitleMaxLength);
        builder.Property(x => x.TitleEn).HasMaxLength(Request.TitleMaxLength);
        builder.Property(x => x.TitleSearch).HasMaxLength(1000);
        builder.HasTrigramIndex(nameof(Request.TitleSearch)); // global search (ADR 0160)
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Stage).HasMaxLength(60);
        builder.Property(x => x.DataJson).HasColumnName("data").HasColumnType("jsonb");
        builder.Property(x => x.ProcessDataJson).HasColumnName("process_data").HasColumnType("jsonb");
        builder.Property(x => x.SubjectType).HasMaxLength(60);
        builder.Property(x => x.Outcome).HasMaxLength(60);
        builder.Property(x => x.CancelReason).HasMaxLength(2000);
        builder.Property(x => x.Source).HasMaxLength(20);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.TypeCode, x.Status, x.Stage });
        builder.HasIndex(x => x.RequesterUserId);
        builder.HasIndex(x => x.RequesterEmployeeId);
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId });
    }
}

internal sealed class RequestEventConfiguration : IEntityTypeConfiguration<RequestEvent>
{
    public void Configure(EntityTypeBuilder<RequestEvent> builder)
    {
        builder.ToTable("request_event");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(60);
        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20);
        builder.Property(x => x.FromStage).HasMaxLength(60);
        builder.Property(x => x.ToStage).HasMaxLength(60);
        builder.Property(x => x.ActorName).HasMaxLength(300);
        builder.Property(x => x.Comment).HasMaxLength(4000);
        builder.Property(x => x.DataJson).HasColumnName("data").HasColumnType("jsonb");
        builder.HasIndex(x => new { x.RequestId, x.At });
    }
}
