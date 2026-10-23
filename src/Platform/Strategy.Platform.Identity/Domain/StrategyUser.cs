using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Access;

namespace Strategy.Platform.Identity.Domain;

/// <summary>A login account. Internal users are linked to an employee (SPEC §0.1); vendors arrive in Phase 11.</summary>
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

    /// <summary>Interim system-administrator flag (ADR 0009).</summary>
    public bool IsSystemAdmin { get; set; }

    public bool Active { get; set; } = true;

    /// <summary><c>ar</c> or <c>en</c>; null = browser default.</summary>
    public string? Language { get; set; }

    /// <summary><c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    public string? Theme { get; set; }

    /// <summary>The user has confirmed an authenticator app (TOTP) as a second factor.</summary>
    public bool TotpEnabled { get; set; }

    /// <summary>
    /// RFC 6238 time step of the last accepted authenticator code. A code is accepted only for a later step, so a seen code
    /// cannot be replayed within the validity window. Cleared when a new authenticator key is set.
    /// </summary>
    public long? TotpLastStep { get; set; }

    /// <summary>The user has chosen email one-time codes as a second factor.</summary>
    public bool EmailOtpEnabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

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
