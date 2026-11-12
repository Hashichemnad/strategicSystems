namespace Strategy.Platform.Identity.Authentication;

/// <summary>Claim types used only inside Platform.Identity.</summary>
public static class IdentityClaims
{
    /// <summary>External provider code of the session (OIDC / SAML / LDAP).</summary>
    public const string Provider = "strategy:idp";

    /// <summary>Marks the two-factor cookie of a user who must enrol MFA before signing in.</summary>
    public const string Enrolment = "strategy:enrol";

    public const string PreferredLanguage = "strategy:lang";

    /// <summary>Id of the server-side session (<c>identity.user_session</c>) the request belongs to.</summary>
    public const string SessionId = "strategy:sid";
}

/// <summary>Values of the <c>amr</c> claim.</summary>
public static class AuthMethods
{
    public const string Password = "pwd";
    public const string Mfa = "mfa";
    public const string Ldap = "ldap";
    public const string External = "ext";
    public const string Token = "pat";
    public const string OAuth = "oauth";

    /// <summary>An integration client (API key, Basic or OAuth client credentials, ADR 0191).</summary>
    public const string Client = "client";

    /// <summary>Wasel vendor portal session cookie (ADR 0110); used for every <c>/api/wasel</c> request and nothing else.</summary>
    public const string Wasel = "wasel";

    /// <summary>Two-factor state of a vendor between password and second factor (5 minutes).</summary>
    public const string WaselTwoFactor = "wasel.mfa";
}

/// <summary>Authentication scheme names.</summary>
public static class AuthSchemes
{
    /// <summary>Policy scheme: personal access token when an <c>Authorization: Bearer sst_…</c> header is present, else the session cookie.</summary>
    public const string Default = "strategy";

    public const string PersonalAccessToken = "pat";

    /// <summary>OAuth 2.1 access tokens issued by the built-in authorization server (MCP clients, ADR 0032). Registered by Platform.Mcp.</summary>
    public const string OAuth = "oauth";

    /// <summary>Integration client API keys (<c>Bearer sik_…</c> or HTTP Basic), registered by Platform.Integration (ADR 0191).</summary>
    public const string Integration = "integration";

    /// <summary>Wasel vendor portal session cookie (ADR 0110); used for every <c>/api/wasel</c> request and nothing else.</summary>
    public const string Wasel = "wasel";

    /// <summary>Two-factor state of a vendor between password and second factor (5 minutes).</summary>
    public const string WaselTwoFactor = "wasel.mfa";
}
