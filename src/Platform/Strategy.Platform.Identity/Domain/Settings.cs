namespace Strategy.Platform.Identity.Domain;

public static class MfaPolicies
{
    public const string Optional = "optional";
    public const string Admins = "admins";
    public const string All = "all";

    public static readonly IReadOnlyList<string> Values = [Optional, Admins, All];
}

/// <summary>Installation-wide sign-in rules (singleton row, Administration → Security). Defaults per docs/design/identity.md.</summary>
public sealed class SecuritySettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    public int PasswordMinLength { get; set; } = 12;

    public bool PasswordRequireUpper { get; set; } = true;

    public bool PasswordRequireLower { get; set; } = true;

    public bool PasswordRequireDigit { get; set; } = true;

    public bool PasswordRequireSymbol { get; set; } = true;

    /// <summary>How many previous passwords may not be reused (0 = off).</summary>
    public int PasswordHistory { get; set; } = 5;

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int SessionIdleMinutes { get; set; } = 30;

    public int SessionAbsoluteHours { get; set; } = 12;

    /// <summary>One of <see cref="MfaPolicies"/>.</summary>
    public string MfaPolicy { get; set; } = MfaPolicies.Admins;

    public bool MfaAllowTotp { get; set; } = true;

    public bool MfaAllowEmail { get; set; } = true;

    /// <summary>Also ask for the local second factor after an external (SSO) sign-in.</summary>
    public bool RequireLocalMfaAfterSso { get; set; }

    public int TokenDefaultDays { get; set; } = 90;

    public int TokenMaxDays { get; set; } = 365;

    public uint Version { get; set; }

    public SecuritySettings Clone() => (SecuritySettings)MemberwiseClone();
}

public static class SmtpSecurityModes
{
    public const string None = "none";
    public const string StartTls = "starttls";
    public const string Tls = "tls";

    public static readonly IReadOnlyList<string> Values = [None, StartTls, Tls];
}

/// <summary>Outgoing mail server (singleton row, Administration → Email server). Reused by Notifications in Phase 3.</summary>
public sealed class SmtpSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = SingletonId;

    public bool Enabled { get; set; }

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string Security { get; set; } = SmtpSecurityModes.StartTls;

    public string? UserName { get; set; }

    /// <summary>Protected with Data Protection (ADR 0012); never returned by the API.</summary>
    public string? PasswordProtected { get; set; }

    public string? FromAddress { get; set; }

    public string? FromNameAr { get; set; }

    public string? FromNameEn { get; set; }

    /// <summary>Public address of the application, used for links in emails (never taken from request headers).</summary>
    public string? PublicBaseUrl { get; set; }

    public uint Version { get; set; }

    public SmtpSettings Clone() => (SmtpSettings)MemberwiseClone();
}
