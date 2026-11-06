using Strategy.Platform.Audit;

namespace Strategy.Platform.Identity.Domain;

/// <summary>
/// First-run setup (ADR 0150), one row: the one-time setup token (only its SHA-256 is stored), when the first administrator was created
/// through the wizard, and when the guided configuration was completed. Installations that existed before Phase 18 get a completed row
/// from the migration.
/// </summary>
[Audited("identity.setup_state")]
public sealed class SetupState
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000150");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>SHA-256 of the current setup token; null once consumed or never issued.</summary>
    [NotAudited]
    public byte[]? TokenHash { get; set; }

    public DateTimeOffset? TokenIssuedAt { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset? TokenConsumedAt { get; set; }

    /// <summary>The first administrator created by the wizard's first stage.</summary>
    public Guid? BootstrappedBy { get; set; }

    public DateTimeOffset? BootstrappedAt { get; set; }

    /// <summary>Guided configuration (stage 2) finished or explicitly closed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    public uint Version { get; set; }
}
