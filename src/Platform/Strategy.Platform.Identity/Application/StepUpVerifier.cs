using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Identity.Application;

/// <summary>
/// MFA re-prompt for a sensitive action of the signed-in user (e.g. "Make this system primary", ADR 0207): a fresh TOTP or e-mail code is
/// verified at the moment of the action. Successes and failures are written to the security event log (<c>mfa.stepUp</c>).
/// </summary>
public interface IStepUpVerifier
{
    /// <summary>The methods the user can use for a re-prompt (<c>totp</c>, <c>email</c>); empty when the user has no MFA.</summary>
    Task<IReadOnlyList<string>> MethodsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Sends an e-mail code to the user (method <c>email</c>).</summary>
    Task<Result> SendEmailCodeAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Verifies the code; failure → validation error <c>auth.errors.invalidCode</c> on field <c>code</c>.</summary>
    Task<Result> VerifyAsync(ClaimsPrincipal user, string method, string? code, string purpose, CancellationToken cancellationToken = default);
}

internal sealed class StepUpVerifier(StrategyUserManager users, SignInService signIn, AuthEventLog events) : IStepUpVerifier
{
    public const string EventType = "mfa.stepUp";

    private static Error InvalidCode => Error.Validation([new FieldError("code", "invalidCode", "auth.errors.invalidCode")]);

    public async Task<IReadOnlyList<string>> MethodsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var account = await users.GetUserAsync(user).ConfigureAwait(false);
        if (account is null)
        {
            return [];
        }

        return [.. (await signIn.AvailableMethodsAsync(account).ConfigureAwait(false)).Where(m => m is MfaMethods.Totp or MfaMethods.Email)];
    }

    public async Task<Result> SendEmailCodeAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var account = await users.GetUserAsync(user).ConfigureAwait(false);
        if (account is null || !(await MethodsAsync(user, cancellationToken).ConfigureAwait(false)).Contains(MfaMethods.Email))
        {
            return Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
        }

        return await signIn.SendCodeAsync(account, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> VerifyAsync(ClaimsPrincipal user, string method, string? code, string purpose, CancellationToken cancellationToken = default)
    {
        var account = await users.GetUserAsync(user).ConfigureAwait(false);
        if (account is null)
        {
            return Error.Forbidden();
        }

        var methods = await MethodsAsync(user, cancellationToken).ConfigureAwait(false);
        if (methods.Count == 0)
        {
            return Error.Rule("auth.mfaRequired", "auth.errors.mfaRequiredForAction");
        }

        var normalised = (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var ok = normalised.Length > 0 && methods.Contains(method) && method switch
        {
            MfaMethods.Totp => await users.VerifyTwoFactorTokenAsync(account, users.Options.Tokens.AuthenticatorTokenProvider, normalised).ConfigureAwait(false),
            MfaMethods.Email => await users.VerifyTwoFactorTokenAsync(account, TokenOptions.DefaultEmailProvider, normalised).ConfigureAwait(false),
            _ => false,
        };
        await events.WriteAsync(EventType, ok, account.Id, account.UserName, detail: purpose + ":" + method, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ok ? Result.Success() : InvalidCode;
    }
}
