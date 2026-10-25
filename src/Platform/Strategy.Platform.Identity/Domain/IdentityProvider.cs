using System.Text.Json.Nodes;

namespace Strategy.Platform.Identity.Domain;

public static class ProviderTypes
{
    public const string Oidc = "oidc";
    public const string UaePass = "uaepass";
    public const string Saml2 = "saml2";
    public const string Ldap = "ldap";

    public static readonly IReadOnlyList<string> All = [Oidc, UaePass, Saml2, Ldap];

    public static bool IsRedirect(string type) => type is Oidc or UaePass or Saml2;
}

public static class LinkMatchFields
{
    public const string Email = "email";
    public const string EmployeeNo = "employee_no";
    public const string UserName = "username";

    public static readonly IReadOnlyList<string> All = [Email, EmployeeNo, UserName];
}

/// <summary>
/// An external identity provider configured at runtime (ADR 0011). Disabled by default. <see cref="Settings"/> holds the
/// type-specific, non-secret settings; <see cref="SecretProtected"/> the client secret / bind password.
/// </summary>
public sealed class IdentityProvider
{
    public const int CodeMaxLength = 40;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Stable code; also the authentication scheme name and part of callback URLs.</summary>
    public string Code { get; set; } = string.Empty;

    public string Type { get; set; } = ProviderTypes.Oidc;

    public string? DisplayNameAr { get; set; }

    public string? DisplayNameEn { get; set; }

    public bool Enabled { get; set; }

    public int Sort { get; set; }

    public JsonObject Settings { get; set; } = [];

    public string? SecretProtected { get; set; }

    /// <summary>How an external identity finds its employee: one of <see cref="LinkMatchFields"/>.</summary>
    public string MatchField { get; set; } = LinkMatchFields.Email;

    public bool AutoLink { get; set; } = true;

    /// <summary>Create a user (never an employee) when the matched employee has no account yet.</summary>
    public bool JitCreateUser { get; set; }

    public DateTimeOffset? LastTestAt { get; set; }

    public bool? LastTestOk { get; set; }

    public string? LastTestMessageKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }

    public string? Setting(string key) => Settings.TryGetPropertyValue(key, out var node) && node is JsonValue value
        ? value.TryGetValue<string>(out var text) ? text : value.ToJsonString()
        : null;

    public bool SettingFlag(string key, bool fallback = false) =>
        Settings.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
}
