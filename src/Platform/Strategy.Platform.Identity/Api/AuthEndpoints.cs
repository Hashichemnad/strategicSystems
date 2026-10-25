using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Identity.Api;

/// <summary>Public sign-in endpoints (<c>/api/v1/identity/auth</c>), rate-limited with the <c>auth</c> policy.</summary>
internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void Map(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");
        auth.MapGet("/providers", ProvidersAsync).WithName("ListSignInProviders").AsPublic();
        auth.MapGet("/password-policy", PasswordPolicy).WithName("GetPasswordPolicy").AsPublic();
        auth.MapPost("/login", LoginAsync).WithName("Login").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/mfa/verify", VerifyAsync).WithName("VerifySecondFactor").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/mfa/email-code", EmailCodeAsync).WithName("SendSignInEmailCode").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/mfa/enrol/totp", EnrolTotpAsync).WithName("StartTotpEnrolment").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/mfa/enrol/email", EnrolEmailAsync).WithName("StartEmailEnrolment").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/mfa/enrol/confirm", ConfirmEnrolmentAsync).WithName("ConfirmMfaEnrolment").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/forgot", ForgotAsync).WithName("ForgotPassword").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/password/reset", ResetAsync).WithName("ResetPassword").AsPublic().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/logout", LogoutAsync).WithName("Logout").RequireSignedIn();
    }

    private static async Task<Ok<IReadOnlyList<SignInProviderDto>>> ProvidersAsync(IdentityStoreDbContext db, CancellationToken ct)
    {
        var providers = await db.Providers.AsNoTracking().Where(p => p.Enabled && p.Type != ProviderTypes.Ldap)
            .OrderBy(p => p.Sort).ThenBy(p => p.Code).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<SignInProviderDto>>(providers
            .Select(p => new SignInProviderDto(p.Code, p.Type, p.DisplayNameAr, p.DisplayNameEn, $"/api/v1/identity/external/{p.Code}/challenge"))
            .ToList());
    }

    private static Ok<PasswordPolicyDto> PasswordPolicy(IdentitySettingsCache settings)
    {
        var s = settings.Security;
        return TypedResults.Ok(new PasswordPolicyDto(s.PasswordMinLength, s.PasswordRequireUpper, s.PasswordRequireLower, s.PasswordRequireDigit,
            s.PasswordRequireSymbol, s.PasswordHistory));
    }

    private static async Task<Results<Ok<SignInOutcome>, ProblemHttpResult>> LoginAsync(LoginInput body, SignInService signIn, CancellationToken ct)
    {
        var fields = new List<FieldError>();
        Required(fields, "login", body.Login, 256);
        Required(fields, "password", body.Password, 1024);
        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        return (await signIn.PasswordSignInAsync(body.Login!, body.Password!, ct)).ToOk();
    }

    private static async Task<Results<Ok<SignInOutcome>, ProblemHttpResult>> VerifyAsync(MfaCodeInput body, SignInService signIn, CancellationToken ct)
    {
        var invalid = ValidateCode(body);
        return invalid is not null ? invalid.ToProblem() : (await signIn.VerifySecondFactorAsync(body.Method!, body.Code!, ct)).ToOk();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> EmailCodeAsync(SignInService signIn, CancellationToken ct) =>
        (await signIn.SendEmailCodeAsync(ct)).ToNoContent();

    private static async Task<Results<Ok<TotpSetupDto>, ProblemHttpResult>> EnrolTotpAsync(SignInService signIn) =>
        (await signIn.StartTotpEnrolmentAsync()).ToOk();

    private static async Task<Results<NoContent, ProblemHttpResult>> EnrolEmailAsync(SignInService signIn, CancellationToken ct) =>
        (await signIn.StartEmailEnrolmentAsync(ct)).ToNoContent();

    private static async Task<Results<Ok<EnrolmentResultDto>, ProblemHttpResult>> ConfirmEnrolmentAsync(
        MfaCodeInput body, SignInService signIn, CancellationToken ct)
    {
        var invalid = ValidateCode(body);
        return invalid is not null ? invalid.ToProblem() : (await signIn.ConfirmEnrolmentAsync(body.Method!, body.Code!, ct)).ToOk();
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> ForgotAsync(
        ForgotPasswordInput body, SignInService signIn, HttpContext context, CancellationToken ct)
    {
        var fields = new List<FieldError>();
        Required(fields, "login", body.Login, 256);
        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        await signIn.RequestPasswordResetAsync(body.Login!, RequestOrigin(context), ct);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetAsync(ResetPasswordInput body, SignInService signIn, CancellationToken ct)
    {
        var fields = new List<FieldError>();
        Required(fields, "token", body.Token, 4096);
        Required(fields, "newPassword", body.NewPassword, 1024);
        if (body.UserId is null)
        {
            fields.Add(new FieldError("userId", "validation.notEmpty", "validation.notEmpty"));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        return (await signIn.ResetPasswordAsync(body.UserId!.Value, body.Token!, body.NewPassword!, body.Invitation, ct)).ToNoContent();
    }

    private static async Task<NoContent> LogoutAsync(StrategySignInManager signInManager, AuthEventLog events, HttpContext context, CancellationToken ct)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        await signInManager.SignOutAsync();
        await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await events.WriteAsync(AuthEventTypes.Logout, true, Guid.TryParse(userId, out var id) ? id : null, context.User.Identity?.Name,
            cancellationToken: ct);
        return TypedResults.NoContent();
    }

    internal static Uri? RequestOrigin(HttpContext context) =>
        Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host}", UriKind.Absolute, out var origin) ? origin : null;

    private static Error? ValidateCode(MfaCodeInput body)
    {
        var fields = new List<FieldError>();
        if (body.Method is null || !MfaMethods.All.Contains(body.Method))
        {
            fields.Add(new FieldError("method", "validation.unknownCode", "validation.unknownCode"));
        }

        Required(fields, "code", body.Code, 64);
        return fields.Count > 0 ? Error.Validation(fields) : null;
    }

    internal static void Required(List<FieldError> fields, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new FieldError(field, "validation.notEmpty", "validation.notEmpty"));
        }
        else if (value.Length > maxLength)
        {
            fields.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = maxLength }));
        }
    }

    private static Task SignOutAsync(this HttpContext context, string scheme) =>
        Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context, scheme);
}

public sealed record SignInProviderDto(string Code, string Type, string? DisplayNameAr, string? DisplayNameEn, string ChallengeUrl);

public sealed record PasswordPolicyDto(int MinLength, bool RequireUpper, bool RequireLower, bool RequireDigit, bool RequireSymbol, int History);

internal sealed record LoginInput(string? Login, string? Password);

internal sealed record MfaCodeInput(string? Method, string? Code);

internal sealed record ForgotPasswordInput(string? Login);

internal sealed record ResetPasswordInput(Guid? UserId, string? Token, string? NewPassword, bool Invitation);

internal static class ResultHttpExtensions
{
    public static Results<NoContent, ProblemHttpResult> ToNoContent(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
    }
}
