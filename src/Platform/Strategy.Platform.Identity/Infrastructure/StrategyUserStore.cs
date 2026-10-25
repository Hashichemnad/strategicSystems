using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Strategy.Platform.Identity.Domain;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Identity user store on <see cref="IdentityStoreDbContext"/>. Adds protection the default store lacks: the authenticator
/// (TOTP) key is encrypted with Data Protection, and recovery codes are stored as SHA-256 hashes only.
/// </summary>
internal sealed class StrategyUserStore(IdentityStoreDbContext context, ISecretProtector protector, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<StrategyUser, IdentityStoreDbContext, Guid>(context, describer)
{
    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenName = "RecoveryCodes";

    public override Task SetAuthenticatorKeyAsync(StrategyUser user, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.TotpLastStep = null; // a new key starts with no used codes
        return SetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, protector.Protect(SecretPurposes.AuthenticatorKey, key), cancellationToken);
    }

    public override async Task<string?> GetAuthenticatorKeyAsync(StrategyUser user, CancellationToken cancellationToken) =>
        protector.Unprotect(SecretPurposes.AuthenticatorKey,
            await GetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, cancellationToken).ConfigureAwait(false));

    public override Task ReplaceCodesAsync(StrategyUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, string.Join(";", recoveryCodes.Select(Hash)), cancellationToken);

    public override async Task<bool> RedeemCodeAsync(StrategyUser user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var hashes = stored.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hash = Hash(code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var match = hashes.FirstOrDefault(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(hash)));
        if (match is null)
        {
            return false;
        }

        hashes.Remove(match);
        await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, string.Join(";", hashes), cancellationToken).ConfigureAwait(false);
        return true;
    }

    public override async Task<int> CountCodesAsync(StrategyUser user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return stored.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant())));
}
