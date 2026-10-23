using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;

namespace Strategy.Platform.Identity.External;

/// <summary>A user found in the directory: distinguished name plus the mapped attributes.</summary>
public sealed record DirectoryEntry(string DistinguishedName, string? Email, string? EmployeeNo, string? UserName);

/// <summary>
/// LDAP / Active Directory sign-in (System.DirectoryServices.Protocols). Searches the user with the service account,
/// binds as the user's DN to check the password, then links the account (ADR 0011). All input is escaped (RFC 4515).
/// </summary>
internal sealed partial class LdapDirectory(
    IdentityStoreDbContext db,
    ISecretProtector protector,
    AccountLinker linker,
    StrategyUserManager users,
    ILogger<LdapDirectory> logger) : IDirectoryPasswordSignIn
{
    public async Task<DirectorySignInResult?> SignInAsync(string userName, string password, StrategyUser? knownUser, CancellationToken cancellationToken)
    {
        var providers = await db.Providers.AsNoTracking().Where(p => p.Enabled && p.Type == ProviderTypes.Ldap)
            .OrderBy(p => p.Sort).ThenBy(p => p.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var provider in providers)
        {
            DirectoryEntry? entry;
            try
            {
                entry = await Task.Run(() => FindUser(provider, userName), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is LdapException or DirectoryOperationException or InvalidOperationException)
            {
                LogDirectoryError(logger, provider.Code, exception.Message);
                continue;
            }

            if (entry is null)
            {
                continue;
            }

            var accepted = await Task.Run(() => CheckPassword(provider, entry.DistinguishedName, password), cancellationToken).ConfigureAwait(false);
            var identity = new ExternalIdentity(entry.DistinguishedName, entry.Email, entry.EmployeeNo, entry.UserName ?? userName);
            if (!accepted)
            {
                // Count the failure against the linked account (lockout), without linking anything new.
                var linked = await users.FindByLoginAsync(provider.Code, entry.DistinguishedName).ConfigureAwait(false) ?? knownUser;
                return new DirectorySignInResult(linked, provider.Code, PasswordAccepted: false);
            }

            var link = await linker.LinkAsync(provider, identity, cancellationToken).ConfigureAwait(false);
            return new DirectorySignInResult(link.User, provider.Code, PasswordAccepted: true, link.FailureKey);
        }

        return null;
    }

    /// <summary>Service bind and, when a user name is given, a search for it. Used by the "Test connection" button.</summary>
    public async Task<(bool Ok, string MessageKey, DirectoryEntry? Entry)> TestAsync(
        IdentityProvider provider, string? testUserName, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var connection = Connect(provider);
                BindService(connection, provider);
                if (string.IsNullOrWhiteSpace(testUserName))
                {
                    return (true, "admin.providers.test.ldapBound", (DirectoryEntry?)null);
                }

                var entry = Search(connection, provider, testUserName);
                return entry is null
                    ? (false, "admin.providers.test.ldapUserNotFound", null)
                    : (true, "admin.providers.test.ldapUserFound", entry);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is LdapException or DirectoryOperationException or InvalidOperationException)
        {
            LogDirectoryError(logger, provider.Code, exception.Message);
            return (false, "admin.providers.test.ldapFailed", null);
        }
    }

    private DirectoryEntry? FindUser(IdentityProvider provider, string userName)
    {
        using var connection = Connect(provider);
        BindService(connection, provider);
        return Search(connection, provider, userName);
    }

    private static bool CheckPassword(IdentityProvider provider, string distinguishedName, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false; // an empty password would be an anonymous ("unauthenticated") bind that many servers accept
        }

        try
        {
            using var connection = Connect(provider);
            connection.Bind(new NetworkCredential(distinguishedName, password));
            return true;
        }
        catch (LdapException exception) when (exception.ErrorCode == 49)
        {
            return false; // invalid credentials
        }
    }

    private void BindService(LdapConnection connection, IdentityProvider provider)
    {
        var bindDn = provider.Setting("bindDn");
        if (string.IsNullOrWhiteSpace(bindDn))
        {
            connection.AuthType = AuthType.Anonymous;
            connection.Bind();
        }
        else
        {
            connection.Bind(new NetworkCredential(bindDn, protector.Unprotect(SecretPurposes.ProviderSecret, provider.SecretProtected) ?? string.Empty));
        }
    }

    private static DirectoryEntry? Search(LdapConnection connection, IdentityProvider provider, string userName)
    {
        var mailAttribute = provider.Setting("claimEmail");
        var numberAttribute = provider.Setting("claimEmployeeNo");
        var nameAttribute = provider.Setting("claimUserName");
        var filter = (provider.Setting("userFilter") ?? ProviderSchema.DefaultLdapFilter).Replace("{0}", EscapeFilter(userName.Trim()), StringComparison.Ordinal);
        var attributes = new[] { mailAttribute, numberAttribute, nameAttribute }.OfType<string>().ToArray();
        var request = new SearchRequest(provider.Setting("baseDn"), filter, SearchScope.Subtree, attributes) { SizeLimit = 2 };
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count != 1)
        {
            return null; // none, or ambiguous: never guess which account was meant
        }

        var entry = response.Entries[0];
        return new DirectoryEntry(entry.DistinguishedName, Attribute(entry, mailAttribute), Attribute(entry, numberAttribute), Attribute(entry, nameAttribute));
    }

    private static LdapConnection Connect(IdentityProvider provider)
    {
        var port = int.TryParse(provider.Setting("port"), out var p) ? p : 636;
        var security = provider.Setting("security") ?? "ldaps";
        var connection = new LdapConnection(new LdapDirectoryIdentifier(provider.Setting("host"), port))
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(int.TryParse(provider.Setting("timeoutSeconds"), out var t) ? Math.Clamp(t, 1, 60) : 10),
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (security == "ldaps")
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }
        else if (security == "starttls")
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        return connection;
    }

    private static string? Attribute(SearchResultEntry entry, string? name)
    {
        if (name is null || !entry.Attributes.Contains(name))
        {
            return null;
        }

        var values = entry.Attributes[name].GetValues(typeof(string));
        return values.Length > 0 ? values[0] as string : null;
    }

    /// <summary>RFC 4515 escaping of a value placed in a search filter.</summary>
    public static string EscapeFilter(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => ch.ToString(),
            });
        }

        return builder.ToString();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LDAP provider {Code} failed: {Reason}")]
    private static partial void LogDirectoryError(ILogger logger, string code, string reason);
}
