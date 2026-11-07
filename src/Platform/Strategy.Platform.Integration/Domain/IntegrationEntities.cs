using System.Text.RegularExpressions;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Integration.Domain;

/// <summary>A system records come from (ADR 0164). Never deleted: deactivated.</summary>
[Audited(EntityType)]
public sealed partial class SourceSystem : AuditableEntity
{
    public const string EntityType = "integration.source_system";
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private SourceSystem()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool Active { get; private set; } = true;

    /// <summary>Lower-case letters, digits, '-' and '_', starting with a letter or digit, 2–50 characters.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    public static SourceSystem Create(string code, string? nameAr, string? nameEn, string typeCode, string? description) =>
        new() { Code = code, NameAr = nameAr, NameEn = nameEn, TypeCode = typeCode, Description = description };

    public void Update(string? nameAr, string? nameEn, string typeCode, string? description)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        TypeCode = typeCode;
        Description = description;
    }

    public void SetActive(bool active) => Active = active;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{1,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}

/// <summary>(source system, entity type, external ID) → record (ADR 0164). Both directions are unique per source and type.</summary>
[Audited(EntityType)]
public sealed class ExternalRef : AuditableEntity
{
    public const string EntityType = "integration.external_ref";
    public const string ExternalIndex = "ux_external_ref_external";
    public const string RecordIndex = "ux_external_ref_record";

    private ExternalRef()
    {
    }

    public string SourceSystem { get; private set; } = string.Empty;

    public string EntityTypeCode { get; private set; } = string.Empty;

    public string ExternalId { get; private set; } = string.Empty;

    public Guid RecordId { get; private set; }

    public bool IsOrigin { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public DateTimeOffset? SourceModifiedAt { get; private set; }

    public string? SourceHash { get; private set; }

    public string SyncStatus { get; private set; } = SyncStatuses.Synced;

    public static ExternalRef Create(ExternalReferenceInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reference = new ExternalRef
        {
            SourceSystem = input.SourceSystem,
            EntityTypeCode = input.EntityType,
            ExternalId = input.ExternalId,
            RecordId = input.RecordId,
            FirstSeenAt = now,
        };
        reference.Refresh(input, now);
        return reference;
    }

    public void Refresh(ExternalReferenceInput input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        IsOrigin |= input.IsOrigin;
        LastSyncedAt = now;
        SourceModifiedAt = input.SourceModifiedAt ?? SourceModifiedAt;
        SourceHash = input.SourceHash ?? SourceHash;
        SyncStatus = input.SyncStatus ?? SyncStatus;
    }

    public ExternalReference ToContract() =>
        new(Id, SourceSystem, EntityTypeCode, ExternalId, RecordId, IsOrigin, FirstSeenAt, LastSyncedAt, SourceModifiedAt, SourceHash, SyncStatus, Version);
}
