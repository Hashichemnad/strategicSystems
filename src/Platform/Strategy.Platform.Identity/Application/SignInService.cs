using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;

namespace Strategy.Platform.Identity.Application;

public static class SignInStatuses
{
    public const string SignedIn = "signedIn";
    public const string MfaRequired = "mfaRequired";
    public const string MfaEnrolmentRequired = "mfaEnrolmentRequired";
}

public static class MfaMethods
{
    public const string Totp = "totp";
    public const string Email = "email";
    public const string Recovery = "recovery";

    public static readonly IReadOnlyList<string> All = [Totp, Email, Recovery];
}

/// <summary>Outcome of a sign-in step. <see cref="Methods"/> lists the second factors the user may use (or enrol).</summary>
public sealed record SignInOutcome(string Status, IReadOnlyList<string> Methods);

public sealed record TotpSetupDto(string SharedKey, string AuthenticatorUri);

public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

/// <summary>Result of a completed MFA enrolment: signed in, plus the recovery codes to show once.</summary>
public sealed record EnrolmentResultDto(string Status, IReadOnlyList<string> RecoveryCodes);

/// <summary>Verifies a user name + password against a directory (LDAP / AD). Implemented in <c>External/</c>.</summary>
public interface IDirectoryPasswordSignIn
{
    /// <summary>
    /// Tries the enabled directory providers. Returns the linked user when the directory accepted the password,
    /// null when no directory knows the user or the password is wrong.
    /// </summary>
    Task<DirectorySignInResult?> SignInAsync(string userName, string password, StrategyUser? knownUser, CancellationToken cancellationToken);
}

public sealed record DirectorySignInResult(StrategyUser? User, string ProviderCode, bool PasswordAccepted, string? FailureCode = null);

/// <summary>
/// Local and directory sign-in with lockout, the MFA policy, enrolment and password reset (docs/design/identity.md,
/// sign-in state machine). Errors never reveal whether an account exists.
/// </summary>
internal sealed class SignInService(
    StrategyUserManager users,
    StrategySignInManager signIn,
    IdentitySettingsCache settings,
    AuthEventLog events,
    INotificationService notifications,
    Strategy.Platform.Kernel.Time.IClock clock,
    IEnumerable<IDirectoryPasswordSignIn> directories,
    IdentityStoreDbContext db,
    IUserRoleQuery roles,
    IHostEnvironment environment,
    SecurityNotifier security)
{
    public const string InvitationTokenProvider = "Invitation";
    public const string InvitationPurpose = "SetPassword";

    public static Error InvalidCredentials => new("auth.invalidCredentials", "auth.errors.invalidCredentials", ErrorKind.Validation);

    public static Error LockedOut => new("auth.lockedOut", "auth.errors.lockedOut", ErrorKind.Validation);

    public static Error InvalidCode => new("auth.invalidCode", "auth.errors.invalidCode", ErrorKind.Validation);

    public static Error NoTwoFactorState => new("auth.mfaExpired", "auth.errors.mfaExpired", ErrorKind.Unauthorized);

    public async Task<Result<SignInOutcome>> PasswordSignInAsync(string login, string password, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(login).ConfigureAwait(false);

        // Directory accounts (no local password) and unknown users go to LDAP / AD when a directory is enabled.
        if (user is null || !user.HasLocalPassword)
        {
            foreach (var directory in directories)
            {
                var result = await directory.SignInAsync(login, password, user, cancellationToken).ConfigureAwait(false);
                if (result is null)
                {
                    continue;
                }

                if (result.User is not null && !result.PasswordAccepted)
                {
                    await users.AccessFailedAsync(result.User).ConfigureAwait(false);
                    var locked = await users.IsLockedOutAsync(result.User).ConfigureAwait(false);
                    await events.WriteAsync(locked ? AuthEventTypes.LockedOut : AuthEventTypes.LoginFailed, false, result.User.Id, login,
                        result.ProviderCode, "ldap.password", cancellationToken).ConfigureAwait(false);
                    if (locked)
                    {
                        await security.LockedOutAsync(result.User, cancellationToken).ConfigureAwait(false);
                    }

                    return locked ? LockedOut : InvalidCredentials;
                }

                if (result.User is null)
                {
                    await events.WriteAsync(AuthEventTypes.LoginFailed, false, null, login, result.ProviderCode, result.FailureCode ?? "ldap.noMatch",
                        cancellationToken).ConfigureAwait(false);
                    return InvalidCredentials;
                }

                if (!await signIn.CanSignInAsync(result.User).ConfigureAwait(false) || await users.IsLockedOutAsync(result.User).ConfigureAwait(false))
                {
                    var locked = await users.IsLockedOutAsync(result.User).ConfigureAwait(false);
                    await events.WriteAsync(locked ? AuthEventTypes.LockedOut : AuthEventTypes.LoginFailed, false, result.User.Id, login,
                        result.ProviderCode, locked ? "lockedOut" : "notAllowed", cancellationToken).ConfigureAwait(false);
                    if (locked)
                    {
                        await security.LockedOutAsync(result.User, cancellationToken).ConfigureAwait(false);
                    }

                    return locked ? LockedOut : InvalidCredentials;
                }

                await users.ResetAccessFailedCountAsync(result.User).ConfigureAwait(false);
                return await CompleteFirstFactorAsync(result.User, AuthMethods.Ldap, result.ProviderCode, external: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            await events.WriteAsync(AuthEventTypes.LoginFailed, false, user?.Id, login, detail: user is null ? "unknownUser" : "noPassword",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return InvalidCredentials;
        }

        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true).ConfigureAwait(false);
        if (check.IsLockedOut)
        {
            await events.WriteAsync(AuthEventTypes.LockedOut, false, user.Id, login, cancellationToken: cancellationToken).ConfigureAwait(false);
            await security.LockedOutAsync(user, cancellationToken).ConfigureAwait(false);
            return LockedOut;
        }

        if (!check.Succeeded)
        {
            await events.WriteAsync(AuthEventTypes.LoginFailed, false, user.Id, login, detail: check.IsNotAllowed ? "notAllowed" : "password",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return InvalidCredentials;
        }

        return await CompleteFirstFactorAsync(user, AuthMethods.Password, null, external: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>After the first factor: sign in, ask for the second factor, or require MFA enrolment (security settings).</summary>
    public async Task<Result<SignInOutcome>> CompleteFirstFactorAsync(
        StrategyUser user, string method, string? provider, bool external, CancellationToken cancellationToken)
    {
        var security = settings.Security;
        var hasMfa = await users.GetTwoFactorEnabledAsync(user).ConfigureAwait(false);
        var mfaApplies = !external || security.RequireLocalMfaAfterSso;

        if (mfaApplies && hasMfa)
        {
            var methods = await AvailableMethodsAsync(user).ConfigureAwait(false);
            await signIn.SignInTwoFactorStateAsync(user, enrolment: false, provider).ConfigureAwait(false);
            await events.WriteAsync(AuthEventTypes.MfaChallenge, true, user.Id, user.UserName, provider, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new SignInOutcome(SignInStatuses.MfaRequired, methods);
        }

        if (mfaApplies && await MfaRequiredForAsync(user, cancellationToken).ConfigureAwait(false))
        {
            await signIn.SignInTwoFactorStateAsync(user, enrolment: true, provider).ConfigureAwait(false);
            await events.WriteAsync(AuthEventTypes.MfaChallenge, true, user.Id, user.UserName, provider, "enrolmentRequired", cancellationToken)
                .ConfigureAwait(false);
            return new SignInOutcome(SignInStatuses.MfaEnrolmentRequired, EnrolmentMethods());
        }

        await SignInAsync(user, method, provider, cancellationToken).ConfigureAwait(false);
        return new SignInOutcome(SignInStatuses.SignedIn, []);
    }

    public async Task<Result<SignInOutcome>> VerifySecondFactorAsync(string method, string code, CancellationToken cancellationToken)
    {
        var (user, enrolment) = await signIn.GetTwoFactorStateAsync().ConfigureAwait(false);
        if (user is null || enrolment)
        {
            return NoTwoFactorState;
        }

        var normalised = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        Microsoft.AspNetCore.Identity.SignInResult result;
        switch (method)
        {
            case MfaMethods.Totp when settings.Security.MfaAllowTotp && user.TotpEnabled:
                result = await signIn.TwoFactorAuthenticatorSignInAsync(normalised, isPersistent: false, rememberClient: false).ConfigureAwait(false);
                break;
            case MfaMethods.Email when settings.Security.MfaAllowEmail && user.EmailOtpEnabled:
                result = await signIn.TwoFactorSignInAsync(TokenOptions.DefaultEmailProvider, normalised, isPersistent: false, rememberClient: false)
                    .ConfigureAwait(false);
                break;
            case MfaMethods.Recovery:
                result = await signIn.TwoFactorRecoveryCodeSignInAsync(normalised).ConfigureAwait(false);
                break;
            default:
                return InvalidCode;
        }

        if (result.IsLockedOut)
        {
            await events.WriteAsync(AuthEventTypes.LockedOut, false, user.Id, user.UserName, detail: "mfa", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await security.LockedOutAsync(user, cancellationToken).ConfigureAwait(false);
            return LockedOut;
        }

        if (!result.Succeeded)
        {
            await events.WriteAsync(AuthEventTypes.MfaFailed, false, user.Id, user.UserName, detail: method, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return InvalidCode;
        }

        await MarkSignedInAsync(user, cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.MfaSucceeded, true, user.Id, user.UserName, detail: method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.Login, true, user.Id, user.UserName, detail: AuthMethods.Mfa, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new SignInOutcome(SignInStatuses.SignedIn, []);
    }

    /// <summary>Sends an email sign-in code to a user in the two-factor (or enrolment) state.</summary>
    public async Task<Result> SendEmailCodeAsync(CancellationToken cancellationToken)
    {
        var (user, enrolment) = await signIn.GetTwoFactorStateAsync().ConfigureAwait(false);
        if (user is null)
        {
            return NoTwoFactorState;
        }

        if (!settings.Security.MfaAllowEmail || (!enrolment && !user.EmailOtpEnabled))
        {
            return Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
        }

        return await SendCodeAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> SendCodeAsync(StrategyUser user, CancellationToken cancellationToken)
    {
        var code = await users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider).ConfigureAwait(false);
        return await TrySendAsync(IdentityEmails.EmailCode, user, Values(user, ("code", code)), null, cancellationToken, expiresIn: TimeSpan.FromMinutes(10))
            .ConfigureAwait(false);
    }

    // ---- Enrolment (policy requires MFA and the user has none yet) ----

    public async Task<Result<TotpSetupDto>> StartTotpEnrolmentAsync()
    {
        var user = await EnrolmentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return NoTwoFactorState;
        }

        return settings.Security.MfaAllowTotp
            ? await NewAuthenticatorKeyAsync(user).ConfigureAwait(false)
            : Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
    }

    public async Task<Result<EnrolmentResultDto>> ConfirmEnrolmentAsync(string method, string code, CancellationToken cancellationToken)
    {
        var user = await EnrolmentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return NoTwoFactorState;
        }

        var enabled = await EnableMethodAsync(user, method, code, cancellationToken).ConfigureAwait(false);
        if (enabled.IsFailure)
        {
            return enabled.Error;
        }

        await signIn.ClearTwoFactorStateAsync().ConfigureAwait(false);
        await SignInAsync(user, AuthMethods.Mfa, null, cancellationToken).ConfigureAwait(false);
        return new EnrolmentResultDto(SignInStatuses.SignedIn, enabled.Value.Codes);
    }

    public async Task<Result> StartEmailEnrolmentAsync(CancellationToken cancellationToken)
    {
        var user = await EnrolmentUserAsync().ConfigureAwait(false);
        if (user is null)
        {
            return NoTwoFactorState;
        }

        return settings.Security.MfaAllowEmail
            ? await SendCodeAsync(user, cancellationToken).ConfigureAwait(false)
            : Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
    }

    // ---- Shared by enrolment and the profile ----

    public async Task<TotpSetupDto> NewAuthenticatorKeyAsync(StrategyUser user)
    {
        await users.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        var key = (await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false))!;
        var issuer = UrlEncoder.Default.Encode("Strategy");
        var account = UrlEncoder.Default.Encode(user.Email ?? user.UserName ?? user.Id.ToString());
        return new TotpSetupDto(
            FormatKey(key),
            string.Create(CultureInfo.InvariantCulture, $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6"));
    }

    /// <summary>Verifies the code of a new method, turns MFA on and returns fresh recovery codes.</summary>
    public async Task<Result<RecoveryCodesDto>> EnableMethodAsync(StrategyUser user, string method, string code, CancellationToken cancellationToken)
    {
        var normalised = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        switch (method)
        {
            case MfaMethods.Totp when settings.Security.MfaAllowTotp:
                if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, normalised).ConfigureAwait(false))
                {
                    return InvalidCode;
                }

                user.TotpEnabled = true;
                break;
            case MfaMethods.Email when settings.Security.MfaAllowEmail:
                if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, normalised).ConfigureAwait(false))
                {
                    return InvalidCode;
                }

                user.EmailOtpEnabled = true;
                break;
            default:
                return Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
        }

        await users.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).ConfigureAwait(false))!.ToList();
        await events.WriteAsync(AuthEventTypes.MfaEnabled, true, user.Id, user.UserName, detail: method, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await security.NotifyAsync(user, SecurityEmails.MfaEnrolled, values: SecurityNotifier.Method(method), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new RecoveryCodesDto(codes);
    }

    public async Task<IReadOnlyList<string>> AvailableMethodsAsync(StrategyUser user)
    {
        var methods = new List<string>();
        if (settings.Security.MfaAllowTotp && user.TotpEnabled && await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false) is not null)
        {
            methods.Add(MfaMethods.Totp);
        }

        if (settings.Security.MfaAllowEmail && user.EmailOtpEnabled)
        {
            methods.Add(MfaMethods.Email);
        }

        methods.Add(MfaMethods.Recovery);
        return methods;
    }

    /// <summary>"Administrators" are holders of a role that requires MFA (ADR 0016).</summary>
    public async Task<bool> MfaRequiredForAsync(StrategyUser user, CancellationToken cancellationToken) =>
        settings.Security.MfaPolicy switch
        {
            MfaPolicies.All => true,
            MfaPolicies.Admins => await roles.RequiresMfaAsync(user.Id, user.EmployeeId, cancellationToken).ConfigureAwait(false),
            _ => false,
        };

    public async Task SignInAsync(StrategyUser user, string method, string? provider, CancellationToken cancellationToken)
    {
        var claims = new List<Claim> { new(StrategyClaims.AuthMethod, method) };
        if (provider is not null)
        {
            claims.Add(new Claim(IdentityClaims.Provider, provider));
        }

        await signIn.SignInWithClaimsAsync(user, isPersistent: false, claims).ConfigureAwait(false);
        await MarkSignedInAsync(user, cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.Login, true, user.Id, user.UserName, provider, method, cancellationToken).ConfigureAwait(false);
    }

    // ---- Password reset and invitation ----

    /// <summary>Always succeeds from the caller's point of view (no account enumeration); sends the email when the account exists.</summary>
    public async Task<Result> RequestPasswordResetAsync(
        string login, Uri? requestOrigin, CancellationToken cancellationToken, string principalType = StrategyClaims.InternalPrincipal, string path = "reset-password",
        NotificationTemplateDefinition? template = null)
    {
        var user = await FindUserAsync(login, principalType).ConfigureAwait(false);
        if (user is null || !user.Active || user.Email is null)
        {
            await events.WriteAsync(AuthEventTypes.PasswordResetRequested, false, user?.Id, login, detail: "noAccount", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Error.Rule("identity.email.failed", "identity.errors.smtpFailed");
        }

        var link = await ResetLinkAsync(user, requestOrigin, path).ConfigureAwait(false);
        var sent = link is null
            ? Error.Rule("identity.email.publicUrlMissing", "identity.errors.publicUrlMissing")
            : await TrySendAsync(template ?? IdentityEmails.PasswordReset, user, Values(user), link, cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.PasswordResetRequested, sent.IsSuccess, user.Id, user.UserName,
            detail: sent.IsSuccess ? null : sent.Error!.MessageKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        return sent;
    }

    public async Task<Result> SendInvitationAsync(
        StrategyUser user, Uri? requestOrigin, CancellationToken cancellationToken, string path = "reset-password", NotificationTemplateDefinition? template = null,
        IReadOnlyDictionary<string, string>? extraValues = null)
    {
        var token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider, InvitationPurpose).ConfigureAwait(false);
        var link = PublicLink(requestOrigin, path, user, token, invitation: true);
        var values = Values(user, ("userName", user.UserName!));
        foreach (var (key, value) in extraValues ?? new Dictionary<string, string>())
        {
            values[key] = value;
        }

        return link is null
            ? Error.Rule("identity.email.publicUrlMissing", "identity.errors.publicUrlMissing")
            : await TrySendAsync(template ?? IdentityEmails.Invitation, user, values, link, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string token, string newPassword, bool invitation, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        var invalid = new Error("auth.resetTokenInvalid", "auth.errors.resetTokenInvalid", ErrorKind.Validation);
        if (user is null || !user.Active)
        {
            return invalid;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return invalid;
        }

        IdentityResult result;
        if (invitation)
        {
            if (!await users.VerifyUserTokenAsync(user, InvitationTokenProvider, InvitationPurpose, decoded).ConfigureAwait(false))
            {
                return invalid;
            }

            if (user.HasLocalPassword)
            {
                return invalid; // an invitation link works once
            }

            result = await users.AddPasswordAsync(user, newPassword).ConfigureAwait(false);
        }
        else
        {
            result = await users.ResetPasswordAsync(user, decoded, newPassword).ConfigureAwait(false);
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return invalid;
            }
        }

        if (!result.Succeeded)
        {
            return IdentityErrors.ToValidation(result, "newPassword", settings.Security);
        }

        await users.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        await RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.PasswordReset, true, user.Id, user.UserName, detail: invitation ? "invitation" : null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!invitation)
        {
            await security.NotifyAsync(user, SecurityEmails.PasswordReset, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    public async Task RevokeSessionsAsync(Guid userId, Guid? exceptSessionId, CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The account of <paramref name="principalType"/> signing in with a user name or e-mail. Internal and vendor (Wasel, ADR 0110) accounts never
    /// sign in through each other's endpoints: an account of the other type is "not found".
    /// </summary>
    public async Task<StrategyUser?> FindUserAsync(string login, string principalType = StrategyClaims.InternalPrincipal)
    {
        var trimmed = login.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var byName = await users.FindByNameAsync(trimmed).ConfigureAwait(false);
        if (byName is not null && byName.PrincipalType == principalType)
        {
            return byName;
        }

        return await users.Users.Where(u => u.NormalizedEmail == users.NormalizeEmail(trimmed) && u.PrincipalType == principalType).OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync().ConfigureAwait(false);
    }

    private async Task<StrategyUser?> EnrolmentUserAsync()
    {
        var (user, enrolment) = await signIn.GetTwoFactorStateAsync().ConfigureAwait(false);
        return enrolment ? user : null;
    }

    public List<string> EnrolmentMethods()
    {
        var methods = new List<string>();
        if (settings.Security.MfaAllowTotp)
        {
            methods.Add(MfaMethods.Totp);
        }

        if (settings.Security.MfaAllowEmail)
        {
            methods.Add(MfaMethods.Email);
        }

        return methods;
    }

    /// <summary>Records the completed sign-in; remembers the device and sends the optional new-device e-mail (ADR 0162).</summary>
    public async Task MarkSignedInAsync(StrategyUser user, CancellationToken cancellationToken = default)
    {
        user.LastSignInAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);
        await security.SignedInAsync(user, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResetLinkAsync(StrategyUser user, Uri? requestOrigin, string path)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        return PublicLink(requestOrigin, path, user, token, invitation: false);
    }

    /// <summary>
    /// Links in emails use the configured public URL (Email server settings). The request's own origin is used only in
    /// Development / Testing, so a forged Host header can never redirect a reset link elsewhere.
    /// </summary>
    private string? PublicLink(Uri? requestOrigin, string path, StrategyUser user, string token, bool invitation)
    {
        var configured = settings.Smtp.PublicBaseUrl;
        var baseUrl = !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : environment.IsDevelopment() || environment.IsEnvironment("Testing") ? requestOrigin?.GetLeftPart(UriPartial.Authority) : null;
        if (baseUrl is null)
        {
            return null;
        }

        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"{baseUrl}/{path}?user={user.Id:D}&token={encoded}{(invitation ? "&invite=1" : string.Empty)}";
    }

    /// <summary>Queues a mandatory, sensitive identity e-mail (outbox with retry); fails only when e-mail is not set up at all.</summary>
    private Task<Result> TrySendAsync(
        NotificationTemplateDefinition template, StrategyUser user, Dictionary<string, string> values, string? link, CancellationToken cancellationToken,
        TimeSpan? expiresIn = null) =>
        notifications.SendAsync(
            new NotificationRequest(
                template.Key,
                [NotificationRecipient.Address(user.Email!, user.UserName, user.Language)],
                values,
                link,
                NotificationChannels.Email,
                expiresIn is { } ttl ? clock.UtcNow + ttl : null,
                Sensitive: true),
            cancellationToken: cancellationToken);

    private static Dictionary<string, string> Values(StrategyUser user, params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = user.UserName ?? string.Empty };
        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }

        return values;
    }

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return builder.ToString().TrimEnd().ToLowerInvariant();
    }
}

/// <summary>Turns ASP.NET Core Identity errors into field errors with translatable keys (<c>identity.password.*</c>).</summary>
public static class IdentityErrors
{
    public static Error ToValidation(IdentityResult result, string field, SecuritySettings? security = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Error.Validation(result.Errors.Select(e => new FieldError(
            field,
            "identity." + char.ToLowerInvariant(e.Code[0]) + e.Code[1..],
            "identity.errors." + char.ToLowerInvariant(e.Code[0]) + e.Code[1..],
            ParamsFor(e.Code, security))).ToList());
    }

    private static Dictionary<string, object?>? ParamsFor(string code, SecuritySettings? security) => code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) => new() { ["length"] = security?.PasswordMinLength },
        "PasswordReused" => new() { ["count"] = security?.PasswordHistory },
        _ => null,
    };
}
