namespace Strategy.Platform.Identity.Authentication;

/// <summary>Claim types used only inside Platform.Identity.</summary>
public static class IdentityClaims
{
    /// <summary>External provider code of the session (OIDC / SAML / LDAP).</summary>
    public const string Provider = "strategy:idp";

    /// <summary>Marks the two-factor cookie of a user who must enrol MFA before signing in.</summary>
    public const string Enrolment = "strategy:enrol";

    public const string PreferredLanguage = "strategy:lang";
}

/// <summary>Values of the <c>amr</c> claim.</summary>
public static class AuthMethods
{
    public const string Password = "pwd";
    public const string Mfa = "mfa";
    public const string Ldap = "ldap";
    public const string External = "ext";
    public const string Token = "pat";
}

/// <summary>Authentication scheme names.</summary>
public static class AuthSchemes
{
    /// <summary>Policy scheme: personal access token when an <c>Authorization: Bearer sst_…</c> header is present, else the session cookie.</summary>
    public const string Default = "strategy";

    public const string PersonalAccessToken = "pat";
}
