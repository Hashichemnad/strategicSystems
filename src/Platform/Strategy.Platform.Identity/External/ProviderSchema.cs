using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Identity.External;

public static class FieldKinds
{
    public const string Text = "text";
    public const string Url = "url";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Select = "select";
    public const string Multiline = "multiline";
}

/// <summary>One setting of a provider type. Labels come from the UI translations (<c>admin:providers.fields.&lt;key&gt;</c>).</summary>
public sealed record ProviderField(
    string Key,
    string Kind,
    bool Required = false,
    JsonNode? Default = null,
    IReadOnlyList<string>? Options = null,
    string? Group = null);

/// <summary>Settings form of a provider type: its fields, whether it has a secret, and whether the secret is required.</summary>
public sealed record ProviderTypeSchema(string Type, IReadOnlyList<ProviderField> Fields, bool HasSecret, bool SecretRequired, string DefaultMatchField);

/// <summary>
/// The settings each provider type accepts (ADR 0011). The server validates against this list and the UI renders its
/// form from it, so adding a setting is one change here plus its translations.
/// </summary>
public static partial class ProviderSchema
{
    public const string Mapping = "mapping";
    public const string Connection = "connection";

    /// <summary>UAE Pass endpoints by environment (product preset, overridable per installation).</summary>
    public static readonly IReadOnlyDictionary<string, (string Authorize, string Token, string UserInfo)> UaePassEnvironments =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["staging"] = ("https://stg-id.uaepass.ae/idshub/authorize", "https://stg-id.uaepass.ae/idshub/token", "https://stg-id.uaepass.ae/idshub/userinfo"),
            ["production"] = ("https://id.uaepass.ae/idshub/authorize", "https://id.uaepass.ae/idshub/token", "https://id.uaepass.ae/idshub/userinfo"),
        };

    public const string DefaultLdapFilter = "(&(objectClass=person)(|(uid={0})(sAMAccountName={0})))";

    private static readonly string[] Reserved = ["strategy", "pat", "identity", "cookies", "external", "callback", "saml2"];

    public static readonly IReadOnlyList<ProviderTypeSchema> Types =
    [
        new(ProviderTypes.Oidc,
        [
            new("authority", FieldKinds.Url, Required: true, Group: Connection),
            new("clientId", FieldKinds.Text, Required: true, Group: Connection),
            new("scopes", FieldKinds.Text, Default: "openid profile email", Group: Connection),
            new("responseMode", FieldKinds.Select, Default: "query", Options: ["query", "form_post"], Group: Connection),
            new("getClaimsFromUserInfo", FieldKinds.Boolean, Default: true, Group: Connection),
            new("allowInsecureHttp", FieldKinds.Boolean, Default: false, Group: Connection),
            new("claimSubject", FieldKinds.Text, Required: true, Default: "sub", Group: Mapping),
            new("claimEmail", FieldKinds.Text, Default: "email", Group: Mapping),
            new("claimEmployeeNo", FieldKinds.Text, Group: Mapping),
            new("claimUserName", FieldKinds.Text, Default: "preferred_username", Group: Mapping),
        ], HasSecret: true, SecretRequired: false, LinkMatchFields.Email),
        new(ProviderTypes.UaePass,
        [
            new("environment", FieldKinds.Select, Required: true, Default: "staging", Options: ["staging", "production"], Group: Connection),
            new("clientId", FieldKinds.Text, Required: true, Group: Connection),
            new("scopes", FieldKinds.Text, Default: "urn:uae:digitalid:profile:general", Group: Connection),
            new("acrValues", FieldKinds.Text, Default: "urn:safelayer:tws:policies:authentication:level:low", Group: Connection),
            new("allowedUserTypes", FieldKinds.Text, Default: "SOP2,SOP3", Group: Connection),
            new("authorizationEndpoint", FieldKinds.Url, Group: Connection),
            new("tokenEndpoint", FieldKinds.Url, Group: Connection),
            new("userInfoEndpoint", FieldKinds.Url, Group: Connection),
            new("allowInsecureHttp", FieldKinds.Boolean, Default: false, Group: Connection),
            new("claimSubject", FieldKinds.Text, Required: true, Default: "sub", Group: Mapping),
            new("claimEmail", FieldKinds.Text, Default: "email", Group: Mapping),
            new("claimEmployeeNo", FieldKinds.Text, Group: Mapping),
            new("claimUserName", FieldKinds.Text, Group: Mapping),
            new("claimUserType", FieldKinds.Text, Default: "userType", Group: Mapping),
        ], HasSecret: true, SecretRequired: true, LinkMatchFields.Email),
        new(ProviderTypes.Saml2,
        [
            new("idpEntityId", FieldKinds.Text, Required: true, Group: Connection),
            new("metadataUrl", FieldKinds.Url, Group: Connection),
            new("ssoUrl", FieldKinds.Url, Group: Connection),
            new("idpCertificate", FieldKinds.Multiline, Group: Connection),
            new("spEntityId", FieldKinds.Text, Group: Connection),
            new("allowUnsolicited", FieldKinds.Boolean, Default: false, Group: Connection),
            new("allowInsecureHttp", FieldKinds.Boolean, Default: false, Group: Connection),
            new("claimSubject", FieldKinds.Text, Required: true, Default: "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", Group: Mapping),
            new("claimEmail", FieldKinds.Text, Default: "email", Group: Mapping),
            new("claimEmployeeNo", FieldKinds.Text, Group: Mapping),
            new("claimUserName", FieldKinds.Text, Group: Mapping),
        ], HasSecret: false, SecretRequired: false, LinkMatchFields.Email),
        new(ProviderTypes.Ldap,
        [
            new("host", FieldKinds.Text, Required: true, Group: Connection),
            new("port", FieldKinds.Number, Required: true, Default: 636, Group: Connection),
            new("security", FieldKinds.Select, Required: true, Default: "ldaps", Options: ["ldaps", "starttls", "none"], Group: Connection),
            new("bindDn", FieldKinds.Text, Group: Connection),
            new("baseDn", FieldKinds.Text, Required: true, Group: Connection),
            new("userFilter", FieldKinds.Text, Required: true, Default: DefaultLdapFilter, Group: Connection),
            new("timeoutSeconds", FieldKinds.Number, Default: 10, Group: Connection),
            new("claimEmail", FieldKinds.Text, Default: "mail", Group: Mapping),
            new("claimEmployeeNo", FieldKinds.Text, Default: "employeeNumber", Group: Mapping),
            new("claimUserName", FieldKinds.Text, Default: "uid", Group: Mapping),
        ], HasSecret: true, SecretRequired: false, LinkMatchFields.UserName),
    ];

    public static ProviderTypeSchema? For(string type) => Types.FirstOrDefault(t => t.Type == type);

    /// <summary>Fills defaults for missing keys and drops unknown keys.</summary>
    public static JsonObject Normalise(string type, JsonObject? settings)
    {
        var schema = For(type) ?? throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown provider type.");
        var result = new JsonObject();
        foreach (var field in schema.Fields)
        {
            var value = settings is not null && settings.TryGetPropertyValue(field.Key, out var node) ? node : null;
            if (value is JsonValue v && v.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
            {
                value = null;
            }

            var chosen = value ?? field.Default;
            if (chosen is not null)
            {
                result[field.Key] = chosen.DeepClone();
            }
        }

        return result;
    }

    public static IReadOnlyList<FieldError> Validate(string type, JsonObject settings, bool hasSecret)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var schema = For(type);
        var errors = new List<FieldError>();
        if (schema is null)
        {
            errors.Add(new FieldError("type", "validation.unknownCode", "validation.unknownCode"));
            return errors;
        }

        var allowHttp = settings.TryGetPropertyValue("allowInsecureHttp", out var h) && h is JsonValue hv && hv.TryGetValue<bool>(out var hb) && hb;
        foreach (var field in schema.Fields)
        {
            settings.TryGetPropertyValue(field.Key, out var node);
            var key = "settings." + field.Key;
            var text = node is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;
            if (field.Required && node is null)
            {
                errors.Add(new FieldError(key, "validation.notEmpty", "validation.notEmpty"));
                continue;
            }

            if (node is null)
            {
                continue;
            }

            switch (field.Kind)
            {
                case FieldKinds.Url when text is null || !Uri.TryCreate(text, UriKind.Absolute, out var url)
                    || (url.Scheme != Uri.UriSchemeHttps && !(allowHttp && url.Scheme == Uri.UriSchemeHttp)):
                    errors.Add(new FieldError(key, "validation.httpsUrl", "validation.httpsUrl"));
                    break;
                case FieldKinds.Boolean when node is not JsonValue b || !b.TryGetValue<bool>(out _):
                case FieldKinds.Number when node is not JsonValue n || !n.TryGetValue<int>(out _):
                    errors.Add(new FieldError(key, "validation.invalid", "validation.invalid"));
                    break;
                case FieldKinds.Select when text is null || field.Options?.Contains(text) != true:
                    errors.Add(new FieldError(key, "validation.unknownCode", "validation.unknownCode"));
                    break;
                case FieldKinds.Text or FieldKinds.Multiline when text is null || text.Length > (field.Kind == FieldKinds.Multiline ? 20_000 : 1000):
                    errors.Add(new FieldError(key, "validation.invalid", "validation.invalid"));
                    break;
            }
        }

        if (schema.SecretRequired && !hasSecret)
        {
            errors.Add(new FieldError("secret", "validation.notEmpty", "validation.notEmpty"));
        }

        switch (type)
        {
            case ProviderTypes.Saml2:
                var hasMetadata = settings.ContainsKey("metadataUrl");
                if (!hasMetadata && !(settings.ContainsKey("ssoUrl") && settings.ContainsKey("idpCertificate")))
                {
                    errors.Add(new FieldError("settings.metadataUrl", "identity.provider.samlSource", "identity.errors.samlSource"));
                }

                if (settings.TryGetPropertyValue("idpCertificate", out var pem) && pem is JsonValue pv && pv.TryGetValue<string>(out var pemText)
                    && ParseCertificate(pemText) is null)
                {
                    errors.Add(new FieldError("settings.idpCertificate", "identity.provider.certificate", "identity.errors.certificate"));
                }

                break;
            case ProviderTypes.Ldap:
                var filter = settings["userFilter"]?.GetValue<string>() ?? string.Empty;
                if (!filter.Contains("{0}", StringComparison.Ordinal) || !filter.StartsWith('(') || !filter.EndsWith(')'))
                {
                    errors.Add(new FieldError("settings.userFilter", "identity.provider.ldapFilter", "identity.errors.ldapFilter"));
                }

                var port = settings["port"]?.GetValue<int>() ?? 0;
                if (port is < 1 or > 65535)
                {
                    errors.Add(new FieldError("settings.port", "validation.inclusiveBetween", "validation.inclusiveBetween",
                        new Dictionary<string, object?> { ["from"] = 1, ["to"] = 65535 }));
                }

                break;
        }

        return errors;
    }

    public static bool IsValidCode(string? code) =>
        code is not null && CodePattern().IsMatch(code) && !Reserved.Contains(code, StringComparer.Ordinal);

    /// <summary>A certificate from PEM text (with or without the BEGIN / END lines) or base64 DER; null when unreadable.</summary>
    public static X509Certificate2? ParseCertificate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var body = text.Contains("-----BEGIN", StringComparison.Ordinal)
                ? string.Concat(text.Split('\n').Where(l => !l.StartsWith("-----", StringComparison.Ordinal)).Select(l => l.Trim()))
                : string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(body));
        }
        catch (Exception exception) when (exception is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$")]
    private static partial Regex CodePattern();
}
