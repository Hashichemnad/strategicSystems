using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Demo;

/// <summary>A record created by the optional demo pack (ADR 0153), table <c>core.demo_record</c>.</summary>
public sealed class DemoRecord : AuditableEntity
{
    public const int CodeMaxLength = 100;

    private DemoRecord()
    {
    }

    public string Contributor { get; private set; } = null!;

    public string ResourceType { get; private set; } = null!;

    public Guid ResourceId { get; private set; }

    /// <summary>Optional lookup key for other contributors (e.g. <c>kpi.card.1</c>, <c>person:director</c>).</summary>
    public string? Key { get; private set; }

    /// <summary>Demo person key who created the record and removes it again.</summary>
    public string? Actor { get; private set; }

    public string PackVersion { get; private set; } = null!;

    public DateTimeOffset? RemovedAt { get; private set; }

    public static DemoRecord Create(string contributor, string resourceType, Guid resourceId, string? key, string? actor, string packVersion) => new()
    {
        Actor = actor,
        Contributor = contributor,
        ResourceType = resourceType,
        ResourceId = resourceId,
        Key = key,
        PackVersion = packVersion,
    };

    public void MarkRemoved(DateTimeOffset at) => RemovedAt ??= at;
}

internal sealed class DemoRecordConfiguration : IEntityTypeConfiguration<DemoRecord>
{
    public void Configure(EntityTypeBuilder<DemoRecord> builder)
    {
        builder.ToTable("demo_record");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Contributor).HasMaxLength(DemoRecord.CodeMaxLength);
        builder.Property(x => x.ResourceType).HasMaxLength(DemoRecord.CodeMaxLength);
        builder.Property(x => x.Key).HasMaxLength(DemoRecord.CodeMaxLength * 2);
        builder.Property(x => x.Actor).HasMaxLength(DemoRecord.CodeMaxLength);
        builder.Property(x => x.PackVersion).HasMaxLength(20);
        builder.HasIndex(x => new { x.Contributor, x.ResourceType, x.ResourceId }).IsUnique();
        builder.HasIndex(x => x.Key);
    }
}

/// <summary>Tracker of the demo pack's records. Saves immediately: each record is tracked right after its own module saved it.</summary>
internal sealed class DemoRecordStore(OrgDbContext db, IClock clock) : IDemoRecordStore
{
    public async Task TrackAsync(
        string contributor, string resourceType, Guid resourceId, string? key, string? actor, string packVersion, CancellationToken cancellationToken)
    {
        if (await db.DemoRecords.AnyAsync(r => r.Contributor == contributor && r.ResourceType == resourceType && r.ResourceId == resourceId, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        db.DemoRecords.Add(DemoRecord.Create(contributor, resourceType, resourceId, key, actor, packVersion));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DemoRecordRef>> ActiveAsync(string? contributor, CancellationToken cancellationToken) =>
        await db.DemoRecords.AsNoTracking()
            .Where(r => r.RemovedAt == null && (contributor == null || r.Contributor == contributor))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new DemoRecordRef(r.Contributor, r.ResourceType, r.ResourceId, r.Key, r.Actor))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task MarkRemovedAsync(string contributor, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        foreach (var record in await db.DemoRecords.Where(r => r.Contributor == contributor && r.RemovedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            record.MarkRemoved(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
