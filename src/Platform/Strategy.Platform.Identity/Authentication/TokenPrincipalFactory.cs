using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;

namespace Strategy.Platform.Identity.Authentication;

/// <summary>
/// The principal for a token call (OAuth access token, ADR 0032): the same claims as a session, re-read from the user on every call so a
/// deactivated or locked-out user is refused at once, plus the authentication method, the token's scopes and the client.
/// </summary>
public sealed class TokenPrincipalFactory(StrategyUserManager users, IUserClaimsPrincipalFactory<StrategyUser> claimsFactory)
{
    /// <summary>Null when the user does not exist, is inactive or is locked out.</summary>
    public async Task<ClaimsPrincipal?> CreateAsync(Guid userId, string scheme, string authMethod, IEnumerable<string> scopes, string? client)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || !user.Active || await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return null;
        }

        var principal = await claimsFactory.CreateAsync(user).ConfigureAwait(false);
        var identity = new ClaimsIdentity(principal.Claims, scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, authMethod));
        identity.AddClaim(new Claim(StrategyClaims.TokenScopes, string.Join(' ', scopes)));
        if (!string.IsNullOrEmpty(client))
        {
            identity.AddClaim(new Claim(StrategyClaims.Client, client));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>The user's display name and whether they may still use tokens (OAuth token endpoint).</summary>
    public async Task<(bool Usable, string? UserName)> CheckAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        return user is null || !user.Active || await users.IsLockedOutAsync(user).ConfigureAwait(false) ? (false, null) : (true, user.UserName);
    }
}
