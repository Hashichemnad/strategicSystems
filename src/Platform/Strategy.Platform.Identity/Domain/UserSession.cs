namespace Strategy.Platform.Identity.Domain;

/// <summary>Server-side session (cookie ticket store): lets users see and end their sessions, and admins revoke them.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    /// <summary>The authentication ticket, protected with Data Protection.</summary>
    public byte[] Ticket { get; set; } = [];

    /// <summary><c>pwd</c>, <c>mfa</c>, <c>ldap</c>, <c>ext</c>.</summary>
    public string Method { get; set; } = string.Empty;

    public string? Provider { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive(DateTimeOffset now, TimeSpan idle, TimeSpan absolute) =>
        RevokedAt is null && now - LastSeenAt <= idle && now - CreatedAt <= absolute;
}

public static class TokenScopes
{
    /// <summary>GET / HEAD on the REST API.</summary>
    public const string ApiRead = "api.read";

    /// <summary>Every REST API call the user may make.</summary>
    public const string ApiWrite = "api.write";

    /// <summary>MCP server access (Phase 5).</summary>
    public const string Mcp = "mcp";

    public static readonly IReadOnlyList<string> All = [ApiRead, ApiWrite, Mcp];
}

/// <summary>A personal access token: only the SHA-256 of the secret is stored; the value is shown once.</summary>
public sealed class PersonalAccessToken
{
    public const int NameMaxLength = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Public lookup part of the token (<c>sst_&lt;prefix&gt;_&lt;secret&gt;</c>).</summary>
    public string Prefix { get; set; } = string.Empty;

    public byte[] SecretHash { get; set; } = [];

    public List<string> Scopes { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public string? LastUsedIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

public static class AuthEventTypes
{
    public const string Login = "login";
    public const string LoginFailed = "login.failed";
    public const string LockedOut = "login.lockedOut";
    public const string MfaChallenge = "mfa.challenge";
    public const string MfaSucceeded = "mfa.succeeded";
    public const string MfaFailed = "mfa.failed";
    public const string MfaEnabled = "mfa.enabled";
    public const string MfaDisabled = "mfa.disabled";
    public const string RecoveryCodesGenerated = "mfa.recoveryCodes";
    public const string Logout = "logout";
    public const string SessionRevoked = "session.revoked";
    public const string PasswordChanged = "password.changed";
    public const string PasswordResetRequested = "password.resetRequested";
    public const string PasswordReset = "password.reset";
    public const string TokenCreated = "token.created";
    public const string TokenRevoked = "token.revoked";
    public const string TokenRejected = "token.rejected";
    public const string ExternalLogin = "external.login";
    public const string ExternalFailed = "external.failed";
    public const string ExternalLinked = "external.linked";
    public const string UserCreated = "user.created";
    public const string UserChanged = "user.changed";
    public const string ProviderChanged = "provider.changed";
    public const string SettingsChanged = "settings.changed";
}

/// <summary>Append-only sign-in and account event (interim home until the audit trail, ADR 0014).</summary>
public sealed class AuthEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Type { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? Provider { get; set; }

    public bool Success { get; set; }

    public string? Detail { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset At { get; set; }
}
