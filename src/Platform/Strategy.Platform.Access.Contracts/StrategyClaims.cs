using System.Security.Claims;

namespace Strategy.Platform.Access;

/// <summary>Claim types issued by Platform.Identity and read by access checks.</summary>
public static class StrategyClaims
{
    /// <summary><c>internal</c> or <c>vendor</c> (Wasel, Phase 11).</summary>
    public const string PrincipalType = "strategy:principal";

    /// <summary>Employee linked to the user, if any.</summary>
    public const string EmployeeId = "strategy:employee";

    /// <summary>How the session was authenticated: <c>pwd</c>, <c>mfa</c>, <c>ext</c>, <c>ldap</c>, <c>pat</c>.</summary>
    public const string AuthMethod = "amr";

    /// <summary>Space-separated scopes of a personal access token.</summary>
    public const string TokenScopes = "strategy:scopes";

    public const string InternalPrincipal = "internal";
    public const string VendorPrincipal = "vendor";

    /// <summary>In-process work without a person behind it (seeding, install commands, scheduled jobs). Never issued to HTTP callers.</summary>
    public const string SystemPrincipal = "system";

    public const string SystemAuthenticationType = "strategy-system";

    /// <summary>The principal for system work; set it on the request context of jobs and install commands.</summary>
    public static ClaimsPrincipal CreateSystemPrincipal() =>
        new(new ClaimsIdentity([new Claim(PrincipalType, SystemPrincipal)], SystemAuthenticationType));

    public static bool IsSystem(this ClaimsPrincipal principal) =>
        principal.Identity?.AuthenticationType == SystemAuthenticationType && principal.FindFirst(PrincipalType)?.Value == SystemPrincipal;

    public static bool IsAuthenticated(this ClaimsPrincipal principal) => principal.Identity?.IsAuthenticated == true;

    public static bool IsInternal(this ClaimsPrincipal principal) =>
        principal.IsAuthenticated() && principal.FindFirst(PrincipalType)?.Value == InternalPrincipal;

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public static Guid? EmployeeIdOf(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(EmployeeId)?.Value, out var id) ? id : null;
}
