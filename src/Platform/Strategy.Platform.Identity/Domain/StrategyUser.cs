using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;

namespace Strategy.Platform.Identity.Domain;

/// <summary>A login account. Internal users are linked to an employee (SPEC §0.1); vendors arrive in Phase 11.</summary>
[Audited("identity.user")]
public sealed class StrategyUser : IdentityUser<Guid>
{
    public StrategyUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString("N");
        LockoutEnabled = true;
    }

    public Guid? EmployeeId { get; set; }

    /// <summary><see cref="StrategyClaims.InternalPrincipal"/> or <see cref="StrategyClaims.VendorPrincipal"/>.</summary>
    public string PrincipalType { get; set; } = StrategyClaims.InternalPrincipal;

    public bool Active { get; set; } = true;

    /// <summary><c>ar</c> or <c>en</c>; null = browser default.</summary>
    public string? Language { get; set; }

    /// <summary><c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    public string? Theme { get; set; }

    /// <summary>Chosen theme (<c>classic</c>, <c>enterprise</c>, <c>modern</c>); used only while Settings → Appearance allows choosing (ADR 0171).</summary>
    public string? UiTheme { get; set; }

    /// <summary>The user's own default application (ADR 0173); null = the landing rule of their role.</summary>
    public string? LandingApp { get; set; }

    /// <summary>Pinned applications in the launcher, comma-separated app codes (ADR 0172).</summary>
    public string? FavouriteApps { get; set; }

    /// <summary>The user has confirmed an authenticator app (TOTP) as a second factor.</summary>
    public bool TotpEnabled { get; set; }

    /// <summary>
    /// RFC 6238 time step of the last accepted authenticator code. A code is accepted only for a later step, so a seen code
    /// cannot be replayed within the validity window. Cleared when a new authenticator key is set.
    /// </summary>
    [NotAudited]
    public long? TotpLastStep { get; set; }

    /// <summary>The user has chosen email one-time codes as a second factor.</summary>
    public bool EmailOtpEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [NotAudited]
    public DateTimeOffset? LastSignInAt { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public bool HasLocalPassword => PasswordHash is not null;
}

public sealed class PasswordHistoryEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A device / network the user signed in from (ADR 0162): only a SHA-256 fingerprint of browser family, OS family and IP prefix — no raw IP or
/// user agent. Technical data for the optional "new device" e-mail; at most <see cref="MaxPerUser"/> per user.
/// </summary>
public sealed class KnownDevice
{
    public const int MaxPerUser = 20;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
