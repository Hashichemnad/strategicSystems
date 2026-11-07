using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Api;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Identity.Wasel;

/// <summary>
/// Whether a vendor account may enter the portal right now (vendor user and vendor organisation active, Wasel switched on). Implemented by the module
/// that owns the vendor register (Portfolio, ADR 0111); without an implementation nobody enters.
/// </summary>
public interface IVendorSignInPolicy
{
    Task<bool> MayEnterAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>A vendor account as the vendor register sees it.</summary>
public sealed record VendorAccountDto(Guid UserId, string Email, bool Active, bool HasPassword, bool MfaEnabled, DateTimeOffset? LastSignInAt);

/// <summary>Vendor (Wasel) accounts for the module that owns the vendor register: create, invite, disable, sessions (ADR 0110).</summary>
public interface IVendorAccounts
{
    /// <summary>The vendor account of <paramref name="email"/>, created (without password) when missing. Fails when an internal account is meant.</summary>
    Task<Result<VendorAccountDto>> EnsureAsync(string email, string? language, CancellationToken cancellationToken = default);

    /// <summary>E-mails the "set your password" link to <c>/wasel/set-password</c> (only while the account has no password).</summary>
    Task<Result> InviteAsync(Guid userId, string inviterName, string projectName, Uri? requestOrigin, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, VendorAccountDto>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates the account; deactivation also revokes every session.</summary>
    Task SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken = default);

    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

internal sealed class VendorAccounts(StrategyUserManager users, SignInService signIn, Strategy.Platform.Kernel.Time.IClock clock) : IVendorAccounts
{
    public async Task<Result<VendorAccountDto>> EnsureAsync(string email, string? language, CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var existing = await users.Users.FirstOrDefaultAsync(
            u => u.NormalizedEmail == users.NormalizeEmail(normalised) && u.PrincipalType == StrategyClaims.VendorPrincipal, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Dto(existing);
        }

        var user = new StrategyUser
        {
            UserName = "wasel." + normalised,
            Email = normalised,
            EmailConfirmed = true,
            PrincipalType = StrategyClaims.VendorPrincipal,
            Language = language is "ar" or "en" ? language : null,
            CreatedAt = clock.UtcNow,
        };
        var created = await users.CreateAsync(user).ConfigureAwait(false);
        return created.Succeeded ? Dto(user) : IdentityErrors.ToValidation(created, "email");
    }

    public async Task<Result> InviteAsync(Guid userId, string inviterName, string projectName, Uri? requestOrigin, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || user.PrincipalType != StrategyClaims.VendorPrincipal)
        {
            return Error.NotFound();
        }

        return await signIn.SendInvitationAsync(user, requestOrigin, cancellationToken, WaselPaths.SetPassword, IdentityEmails.WaselInvitation,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["inviter"] = inviterName, ["project"] = projectName }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, VendorAccountDto>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var rows = await users.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.PrincipalType == StrategyClaims.VendorPrincipal)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(u => u.Id, Dto);
    }

    public async Task SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null || user.PrincipalType != StrategyClaims.VendorPrincipal || user.Active == active)
        {
            return;
        }

        user.Active = active;
        await users.UpdateAsync(user).ConfigureAwait(false);
        if (!active)
        {
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
            await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken = default) => signIn.RevokeSessionsAsync(userId, null, cancellationToken);

    private static VendorAccountDto Dto(StrategyUser u) => new(u.Id, u.Email ?? string.Empty, u.Active, u.HasLocalPassword, u.TwoFactorEnabled, u.LastSignInAt);
}

internal static class WaselPaths
{
    public const string SetPassword = "wasel/set-password";
}

/// <summary>
/// Vendor sign-in (ADR 0110): password, then a second factor that is always required (enrolment at the first sign-in). Signs into the
/// <see cref="AuthSchemes.Wasel"/> cookie only; internal accounts are "invalid credentials" here, as vendor accounts are on the internal login.
/// </summary>
internal sealed class WaselSignInService(
    StrategyUserManager users,
    SignInService signIn,
    IdentitySettingsCache settings,
    AuthEventLog events,
    IUserClaimsPrincipalFactory<StrategyUser> claimsFactory,
    IHttpContextAccessor accessor,
    IEnumerable<IVendorSignInPolicy> policies,
    SecurityNotifier security)
{
    private HttpContext Context => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

    public async Task<Result<SignInOutcome>> PasswordSignInAsync(string login, string password, CancellationToken ct)
    {
        var user = await signIn.FindUserAsync(login, StrategyClaims.VendorPrincipal).ConfigureAwait(false);
        if (user is null || !user.HasLocalPassword)
        {
            await events.WriteAsync(AuthEventTypes.LoginFailed, false, user?.Id, login, "wasel", user is null ? "unknownUser" : "noPassword", ct).ConfigureAwait(false);
            return SignInService.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            await events.WriteAsync(AuthEventTypes.LockedOut, false, user.Id, login, "wasel", cancellationToken: ct).ConfigureAwait(false);
            await security.LockedOutAsync(user, ct).ConfigureAwait(false);
            return SignInService.LockedOut;
        }

        if (!await users.CheckPasswordAsync(user, password).ConfigureAwait(false))
        {
            await users.AccessFailedAsync(user).ConfigureAwait(false);
            var locked = await users.IsLockedOutAsync(user).ConfigureAwait(false);
            await events.WriteAsync(locked ? AuthEventTypes.LockedOut : AuthEventTypes.LoginFailed, false, user.Id, login, "wasel", "password", ct).ConfigureAwait(false);
            if (locked)
            {
                await security.LockedOutAsync(user, ct).ConfigureAwait(false);
            }

            return locked ? SignInService.LockedOut : SignInService.InvalidCredentials;
        }

        if (!user.Active || !await MayEnterAsync(user.Id, ct).ConfigureAwait(false))
        {
            await events.WriteAsync(AuthEventTypes.LoginFailed, false, user.Id, login, "wasel", "notAllowed", ct).ConfigureAwait(false);
            return SignInService.InvalidCredentials;
        }

        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        var enrolment = !user.TwoFactorEnabled;
        await SignInTwoFactorStateAsync(user, enrolment).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.MfaChallenge, true, user.Id, user.UserName, "wasel", enrolment ? "enrolmentRequired" : null, ct).ConfigureAwait(false);
        return enrolment
            ? new SignInOutcome(SignInStatuses.MfaEnrolmentRequired, EnrolmentMethods())
            : new SignInOutcome(SignInStatuses.MfaRequired, await signIn.AvailableMethodsAsync(user).ConfigureAwait(false));
    }

    /// <summary>
    /// First factor completed by an external provider (UAE Pass, ADR 0154): the same checks as the password path (vendor, active, may enter, not
    /// locked out), then the half-signed-in state — the second factor is still required. Returns the failure detail for the audit trail.
    /// </summary>
    public async Task<(SignInOutcome? Outcome, string? Failure)> ExternalFirstFactorAsync(StrategyUser? user, string provider, string login, CancellationToken ct)
    {
        if (user is null || user.PrincipalType != StrategyClaims.VendorPrincipal)
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, user?.Id, login, provider, "noVendorAccount", ct).ConfigureAwait(false);
            return (null, "noVendorAccount");
        }

        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            await events.WriteAsync(AuthEventTypes.LockedOut, false, user.Id, login, provider, cancellationToken: ct).ConfigureAwait(false);
            return (null, "lockedOut");
        }

        if (!user.Active || !await MayEnterAsync(user.Id, ct).ConfigureAwait(false))
        {
            await events.WriteAsync(AuthEventTypes.ExternalFailed, false, user.Id, login, provider, "notAllowed", ct).ConfigureAwait(false);
            return (null, "notAllowed");
        }

        var enrolment = !user.TwoFactorEnabled;
        await SignInTwoFactorStateAsync(user, enrolment).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.MfaChallenge, true, user.Id, user.UserName, provider, enrolment ? "enrolmentRequired" : null, ct).ConfigureAwait(false);
        return (enrolment
            ? new SignInOutcome(SignInStatuses.MfaEnrolmentRequired, EnrolmentMethods())
            : new SignInOutcome(SignInStatuses.MfaRequired, await signIn.AvailableMethodsAsync(user).ConfigureAwait(false)), null);
    }

    public async Task<Result<SignInOutcome>> VerifyAsync(string method, string code, CancellationToken ct)
    {
        var (user, enrolment) = await TwoFactorStateAsync().ConfigureAwait(false);
        if (user is null || enrolment)
        {
            return SignInService.NoTwoFactorState;
        }

        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
        {
            return SignInService.LockedOut;
        }

        var normalised = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var ok = method switch
        {
            MfaMethods.Totp when settings.Security.MfaAllowTotp && user.TotpEnabled =>
                await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, normalised).ConfigureAwait(false),
            MfaMethods.Email when settings.Security.MfaAllowEmail && user.EmailOtpEnabled =>
                await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider, normalised).ConfigureAwait(false),
            MfaMethods.Recovery => (await users.RedeemTwoFactorRecoveryCodeAsync(user, normalised).ConfigureAwait(false)).Succeeded,
            _ => false,
        };
        if (!ok)
        {
            await users.AccessFailedAsync(user).ConfigureAwait(false);
            var locked = await users.IsLockedOutAsync(user).ConfigureAwait(false);
            await events.WriteAsync(locked ? AuthEventTypes.LockedOut : AuthEventTypes.MfaFailed, false, user.Id, user.UserName, "wasel", method, ct).ConfigureAwait(false);
            if (locked)
            {
                await security.LockedOutAsync(user, ct).ConfigureAwait(false);
            }

            return locked ? SignInService.LockedOut : SignInService.InvalidCode;
        }

        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.MfaSucceeded, true, user.Id, user.UserName, "wasel", method, ct).ConfigureAwait(false);
        await SignInAsync(user, ct).ConfigureAwait(false);
        return new SignInOutcome(SignInStatuses.SignedIn, []);
    }

    public async Task<Result> SendEmailCodeAsync(CancellationToken ct)
    {
        var (user, enrolment) = await TwoFactorStateAsync().ConfigureAwait(false);
        if (user is null)
        {
            return SignInService.NoTwoFactorState;
        }

        if (!settings.Security.MfaAllowEmail || (!enrolment && !user.EmailOtpEnabled))
        {
            return Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
        }

        return await signIn.SendCodeAsync(user, ct).ConfigureAwait(false);
    }

    public async Task<Result<TotpSetupDto>> StartTotpAsync()
    {
        var (user, enrolment) = await TwoFactorStateAsync().ConfigureAwait(false);
        if (user is null || !enrolment)
        {
            return SignInService.NoTwoFactorState;
        }

        return settings.Security.MfaAllowTotp
            ? await signIn.NewAuthenticatorKeyAsync(user).ConfigureAwait(false)
            : Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable");
    }

    public async Task<Result<EnrolmentResultDto>> ConfirmEnrolmentAsync(string method, string code, CancellationToken ct)
    {
        var (user, enrolment) = await TwoFactorStateAsync().ConfigureAwait(false);
        if (user is null || !enrolment)
        {
            return SignInService.NoTwoFactorState;
        }

        var enabled = await signIn.EnableMethodAsync(user, method, code, ct).ConfigureAwait(false);
        if (enabled.IsFailure)
        {
            return enabled.Error;
        }

        await SignInAsync(user, ct).ConfigureAwait(false);
        return new EnrolmentResultDto(SignInStatuses.SignedIn, enabled.Value.Codes);
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        var userId = Context.User.UserId();
        await Context.SignOutAsync(AuthSchemes.Wasel).ConfigureAwait(false);
        await Context.SignOutAsync(AuthSchemes.WaselTwoFactor).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.Logout, true, userId, Context.User.Identity?.Name, "wasel", cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<bool> MayEnterAsync(Guid userId, CancellationToken ct)
    {
        var any = false;
        foreach (var policy in policies)
        {
            any = true;
            if (!await policy.MayEnterAsync(userId, ct).ConfigureAwait(false))
            {
                return false;
            }
        }

        return any;
    }

    private async Task SignInAsync(StrategyUser user, CancellationToken ct)
    {
        await Context.SignOutAsync(AuthSchemes.WaselTwoFactor).ConfigureAwait(false);
        var factory = await claimsFactory.CreateAsync(user).ConfigureAwait(false);
        var identity = new ClaimsIdentity(factory.Claims, AuthSchemes.Wasel, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(StrategyClaims.AuthMethod, AuthMethods.Mfa));
        await Context.SignInAsync(AuthSchemes.Wasel, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
        await signIn.MarkSignedInAsync(user, ct).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.Login, true, user.Id, user.UserName, "wasel", AuthMethods.Mfa, ct).ConfigureAwait(false);
    }

    private Task SignInTwoFactorStateAsync(StrategyUser user, bool enrolment)
    {
        var identity = new ClaimsIdentity(AuthSchemes.WaselTwoFactor);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        if (enrolment)
        {
            identity.AddClaim(new Claim(IdentityClaims.Enrolment, "true"));
        }

        return Context.SignInAsync(AuthSchemes.WaselTwoFactor, new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5), IsPersistent = false });
    }

    private async Task<(StrategyUser? User, bool Enrolment)> TwoFactorStateAsync()
    {
        var result = await Context.AuthenticateAsync(AuthSchemes.WaselTwoFactor).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal.FindFirst(ClaimTypes.Name)?.Value is not { } id)
        {
            return (null, false);
        }

        var user = await users.FindByIdAsync(id).ConfigureAwait(false);
        return user is { PrincipalType: StrategyClaims.VendorPrincipal, Active: true }
            ? (user, result.Principal.HasClaim(IdentityClaims.Enrolment, "true"))
            : (null, false);
    }

    private List<string> EnrolmentMethods()
    {
        var methods = signIn.EnrolmentMethods();
        return methods.Count > 0 ? methods : [MfaMethods.Email];
    }
}

/// <summary>Wasel sign-in and profile (<c>/api/wasel/v1/auth</c>, <c>/api/wasel/v1/me</c>), rate-limited like the internal sign-in.</summary>
internal static class WaselAuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/wasel/v1").WithTags("wasel");
        var auth = group.MapGroup("/auth");
        auth.MapPost("/login", LoginAsync).WithName("WaselLogin").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/mfa/verify", VerifyAsync).WithName("WaselVerifySecondFactor").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/mfa/email-code", EmailCodeAsync).WithName("WaselSendEmailCode").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/mfa/enrol/totp", EnrolTotpAsync).WithName("WaselStartTotpEnrolment").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/mfa/enrol/email", EmailCodeAsync).WithName("WaselStartEmailEnrolment").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/mfa/enrol/confirm", ConfirmAsync).WithName("WaselConfirmMfaEnrolment").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/password/forgot", ForgotAsync).WithName("WaselForgotPassword").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/password/reset", ResetAsync).WithName("WaselResetPassword").AsPublic().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        auth.MapPost("/logout", LogoutAsync).WithName("WaselLogout").RequireSignedIn();
        WaselUaePassEndpoints.Map(auth);
        group.MapGet("/me", MeAsync).WithName("GetWaselMe").RequireSignedIn();
        group.MapPut("/me/preferences", PreferencesAsync).WithName("UpdateWaselPreferences").RequireSignedIn();
    }

    private static async Task<Results<Ok<SignInOutcome>, ProblemHttpResult>> LoginAsync(WaselLoginInput body, WaselSignInService signIn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Login) || string.IsNullOrWhiteSpace(body.Password) || body.Login.Length > 256 || body.Password.Length > 1024)
        {
            return Error.Validation([new FieldError("login", "validation.notEmpty", "validation.notEmpty")]).ToProblem();
        }

        return (await signIn.PasswordSignInAsync(body.Login, body.Password, ct)).ToOk();
    }

    private static async Task<Results<Ok<SignInOutcome>, ProblemHttpResult>> VerifyAsync(WaselCodeInput body, WaselSignInService signIn, CancellationToken ct) =>
        Invalid(body) is { } invalid ? invalid.ToProblem() : (await signIn.VerifyAsync(body.Method!, body.Code!, ct)).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> EmailCodeAsync(WaselSignInService signIn, CancellationToken ct) =>
        (await signIn.SendEmailCodeAsync(ct)).ToNoContent();

    private static async Task<Results<Ok<TotpSetupDto>, ProblemHttpResult>> EnrolTotpAsync(WaselSignInService signIn) => (await signIn.StartTotpAsync()).ToOk();

    private static async Task<Results<Ok<EnrolmentResultDto>, ProblemHttpResult>> ConfirmAsync(WaselCodeInput body, WaselSignInService signIn, CancellationToken ct) =>
        Invalid(body) is { } invalid ? invalid.ToProblem() : (await signIn.ConfirmEnrolmentAsync(body.Method!, body.Code!, ct)).ToOk();

    private static async Task<Accepted> ForgotAsync(WaselForgotInput body, SignInService signIn, HttpContext context, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(body.Login) && body.Login.Length <= 256)
        {
            await signIn.RequestPasswordResetAsync(body.Login, AuthEndpoints.RequestOrigin(context), ct, StrategyClaims.VendorPrincipal, WaselPaths.SetPassword,
                IdentityEmails.WaselPasswordReset);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetAsync(
        WaselResetInput body, SignInService signIn, StrategyUserManager users, CancellationToken ct)
    {
        if (body.UserId is not { } userId || string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.NewPassword))
        {
            return Error.Validation([new FieldError("newPassword", "validation.notEmpty", "validation.notEmpty")]).ToProblem();
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.PrincipalType != StrategyClaims.VendorPrincipal)
        {
            return new Error("auth.resetTokenInvalid", "auth.errors.resetTokenInvalid", ErrorKind.Validation).ToProblem();
        }

        return (await signIn.ResetPasswordAsync(userId, body.Token, body.NewPassword, body.Invitation, ct)).ToNoContent();
    }

    private static async Task<NoContent> LogoutAsync(WaselSignInService signIn, CancellationToken ct)
    {
        await signIn.SignOutAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<WaselMeDto>, ProblemHttpResult>> MeAsync(HttpContext context, StrategyUserManager users)
    {
        var user = context.User.UserId() is { } id ? await users.FindByIdAsync(id.ToString()) : null;
        return user is null || user.PrincipalType != StrategyClaims.VendorPrincipal
            ? Error.NotFound().ToProblem()
            : TypedResults.Ok(new WaselMeDto(user.Id, user.Email ?? string.Empty, user.Language, user.TwoFactorEnabled, user.LastSignInAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PreferencesAsync(WaselPreferencesInput body, HttpContext context, StrategyUserManager users)
    {
        if (body.Language is not ("ar" or "en"))
        {
            return Error.Validation([new FieldError("language", "validation.unknownCode", "validation.unknownCode")]).ToProblem();
        }

        var user = context.User.UserId() is { } id ? await users.FindByIdAsync(id.ToString()) : null;
        if (user is null || user.PrincipalType != StrategyClaims.VendorPrincipal)
        {
            return Error.NotFound().ToProblem();
        }

        user.Language = body.Language;
        await users.UpdateAsync(user);
        return TypedResults.NoContent();
    }

    private static Error? Invalid(WaselCodeInput body) =>
        body.Method is null || !MfaMethods.All.Contains(body.Method) || string.IsNullOrWhiteSpace(body.Code) || body.Code.Length > 64
            ? Error.Validation([new FieldError("code", "validation.notEmpty", "validation.notEmpty")])
            : null;
}

public sealed record WaselMeDto(Guid Id, string Email, string? Language, bool MfaEnabled, DateTimeOffset? LastSignInAt);

internal sealed record WaselLoginInput(string? Login, string? Password);

internal sealed record WaselCodeInput(string? Method, string? Code);

internal sealed record WaselForgotInput(string? Login);

internal sealed record WaselResetInput(Guid? UserId, string? Token, string? NewPassword, bool Invitation);

internal sealed record WaselPreferencesInput(string? Language);
