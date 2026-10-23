using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Authenticator (TOTP) codes for sign-in, enrolment and the profile. Same codes and drift window as the built-in provider,
/// but each code is accepted once: the accepted time step is stored on the user and only a later step is accepted after it.
/// The user row's concurrency stamp makes two simultaneous uses of one code fail for the second.
/// </summary>
internal sealed class ReplayProtectedAuthenticatorTokenProvider(IClock clock) : IUserTwoFactorTokenProvider<StrategyUser>
{
    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<StrategyUser> manager, StrategyUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false));
    }

    /// <summary>Codes come from the user's app, never from the server.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<StrategyUser> manager, StrategyUser user) => Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<StrategyUser> manager, StrategyUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        var key = TotpCode.DecodeBase32(await manager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false));
        if (key is null || TotpCode.MatchingStep(key, token ?? string.Empty, clock.UtcNow) is not { } step)
        {
            return false;
        }

        if (user.TotpLastStep is { } last && step <= last)
        {
            return false; // already used, or older than a code already used
        }

        user.TotpLastStep = step;
        return (await manager.UpdateAsync(user).ConfigureAwait(false)).Succeeded;
    }
}
