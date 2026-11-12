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

    /// <summary>Space-separated scopes of a personal access token or OAuth access token.</summary>
    public const string TokenScopes = "strategy:scopes";

    /// <summary>The client behind a token call: <c>pat:&lt;prefix&gt;</c> or the OAuth client id (MCP audit trail).</summary>
    public const string Client = "strategy:client";

    /// <summary>The integration client (ADR 0191) behind a call: its public client id. Only on integration principals.</summary>
    public const string IntegrationClient = "strategy:integration";

    /// <summary>The integration client's acting unit (Unit / Unit-and-below scopes, ADR 0191).</summary>
    public const string ActingUnit = "strategy:acting-unit";

    /// <summary>Space-separated system codes an integration client may read (ADR 0191).</summary>
    public const string AllowedSystems = "strategy:systems";

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

    /// <summary>A call by an integration client (service account, ADR 0191) — never a person.</summary>
    public static bool IsIntegrationClient(this ClaimsPrincipal principal) =>
        principal.IsInternal() && principal.FindFirst(IntegrationClient)?.Value is { Length: > 0 };

    /// <summary>The integration scope of an integration principal, or null for everyone else.</summary>
    public static IntegrationScope? IntegrationScopeOf(this ClaimsPrincipal principal)
    {
        if (!principal.IsIntegrationClient())
        {
            return null;
        }

        var unit = Guid.TryParse(principal.FindFirst(ActingUnit)?.Value, out var id) ? id : (Guid?)null;
        var systems = (principal.FindFirst(AllowedSystems)?.Value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new IntegrationScope(unit, new SortedSet<string>(systems, StringComparer.Ordinal));
    }
}

/// <summary>What narrows an integration client's access snapshot (ADR 0191): its acting unit and its allowed systems.</summary>
public sealed record IntegrationScope(Guid? ActingUnitId, IReadOnlySet<string> Systems)
{
    /// <summary>A stable cache key.</summary>
    public string Key => (ActingUnitId?.ToString("N") ?? "-") + ":" + string.Join(',', Systems.Order(StringComparer.Ordinal));
}
