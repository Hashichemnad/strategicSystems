using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Api;

/// <summary>Self-service profile (<c>/api/v1/identity/me</c>): the caller's own account only.</summary>
internal static class MeEndpoints
{
    private static Error InteractiveOnly => new("auth.interactiveOnly", "auth.errors.interactiveOnly", ErrorKind.Forbidden);

    public static void Map(RouteGroupBuilder group)
    {
        var me = group.MapGroup("/me");
        me.MapGet("/", GetMeAsync).WithName("GetMe").RequireSignedIn();
        me.MapPut("/preferences", PreferencesAsync).WithName("UpdateMyPreferences").RequireSignedIn();
        me.MapPost("/password", ChangePasswordAsync).WithName("ChangeMyPassword").RequireSignedIn();
        me.MapGet("/mfa", MfaAsync).WithName("GetMyMfa").RequireSignedIn();
        me.MapPost("/mfa/totp", StartTotpAsync).WithName("StartMyTotpSetup").RequireSignedIn();
        me.MapPost("/mfa/email", StartEmailAsync).WithName("StartMyEmailMfaSetup").RequireSignedIn();
        me.MapPost("/mfa/confirm", ConfirmMfaAsync).WithName("ConfirmMyMfaSetup").RequireSignedIn();
        me.MapPost("/mfa/disable", DisableMfaAsync).WithName("DisableMyMfaMethod").RequireSignedIn();
        me.MapPost("/mfa/recovery-codes", RecoveryCodesAsync).WithName("RegenerateMyRecoveryCodes").RequireSignedIn();
        me.MapGet("/sessions", SessionsAsync).WithName("ListMySessions").RequireSignedIn();
        me.MapPost("/sessions/{id:guid}/revoke", RevokeSessionAsync).WithName("RevokeMySession").RequireSignedIn();
        me.MapPost("/sessions/revoke-others", RevokeOtherSessionsAsync).WithName("RevokeMyOtherSessions").RequireSignedIn();
        me.MapGet("/tokens", TokensAsync).WithName("ListMyTokens").RequireSignedIn();
        me.MapPost("/tokens", CreateTokenAsync).WithName("CreateMyToken").RequireSignedIn();
        me.MapPost("/tokens/{id:guid}/revoke", RevokeTokenAsync).WithName("RevokeMyToken").RequireSignedIn();
    }

    private static async Task<Results<Ok<MeDto>, ProblemHttpResult>> GetMeAsync(
        HttpContext context, StrategyUserManager users, IEmployeeDirectory employees, IOrgDirectory org, CancellationToken ct)
    {
        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        MeEmployeeDto? employee = null;
        if (user.EmployeeId is { } employeeId && await employees.GetAsync(employeeId, ct) is { } e)
        {
            var unit = await org.GetUnitAsync(e.UnitId, ct);
            employee = new MeEmployeeDto(e.Id, e.EmployeeNo, e.NameAr, e.NameEn, e.UnitId, unit?.NameAr, unit?.NameEn);
        }

        return TypedResults.Ok(new MeDto(
            user.Id, user.UserName!, user.Email, user.PrincipalType, user.Language, user.Theme, employee,
            user.TwoFactorEnabled, user.HasLocalPassword, context.User.FindFirst(StrategyClaims.AuthMethod)?.Value ?? AuthMethods.Password));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PreferencesAsync(
        PreferencesInput body, HttpContext context, StrategyUserManager users)
    {
        var fields = new List<FieldError>();
        if (body.Language is not null && body.Language is not ("ar" or "en"))
        {
            fields.Add(new FieldError("language", "validation.unknownCode", "validation.unknownCode"));
        }

        if (body.Theme is not null && body.Theme is not ("light" or "dark" or "system"))
        {
            fields.Add(new FieldError("theme", "validation.unknownCode", "validation.unknownCode"));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        user.Language = body.Language ?? user.Language;
        user.Theme = body.Theme ?? user.Theme;
        await users.UpdateAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordInput body, HttpContext context, StrategyUserManager users, SignInService signIn, StrategySignInManager signInManager,
        AuthEventLog events, IdentitySettingsCache settings, CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var fields = new List<FieldError>();
        AuthEndpoints.Required(fields, "newPassword", body.NewPassword, 1024);
        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (!user.HasLocalPassword)
        {
            return Error.Rule("identity.password.external", "identity.errors.passwordManagedExternally").ToProblem();
        }

        var result = await users.ChangePasswordAsync(user, body.CurrentPassword ?? string.Empty, body.NewPassword!);
        if (!result.Succeeded)
        {
            return result.Errors.Any(e => e.Code == nameof(Microsoft.AspNetCore.Identity.IdentityErrorDescriber.PasswordMismatch))
                ? Error.Validation([new FieldError("currentPassword", "identity.passwordMismatch", "identity.errors.passwordMismatch")]).ToProblem()
                : IdentityErrors.ToValidation(result, "newPassword", settings.Security).ToProblem();
        }

        await signIn.RevokeSessionsAsync(user.Id, CurrentSessionId(context), ct);
        await signInManager.RefreshSignInAsync(user);
        await events.WriteAsync(AuthEventTypes.PasswordChanged, true, user.Id, user.UserName, cancellationToken: ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MyMfaDto>, ProblemHttpResult>> MfaAsync(
        HttpContext context, StrategyUserManager users, SignInService signIn, IdentitySettingsCache settings)
    {
        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        var methods = user.TwoFactorEnabled ? await signIn.AvailableMethodsAsync(user) : [];
        return TypedResults.Ok(new MyMfaDto(
            user.TwoFactorEnabled,
            methods.Contains(MfaMethods.Totp),
            methods.Contains(MfaMethods.Email),
            user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0,
            await signIn.MfaRequiredForAsync(user, context.RequestAborted),
            settings.Security.MfaAllowTotp,
            settings.Security.MfaAllowEmail));
    }

    private static async Task<Results<Ok<TotpSetupDto>, ProblemHttpResult>> StartTotpAsync(
        HttpContext context, StrategyUserManager users, SignInService signIn, IdentitySettingsCache settings)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (!settings.Security.MfaAllowTotp)
        {
            return Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable").ToProblem();
        }

        // Replacing an existing authenticator would lock the user out of it immediately; disable it first.
        var methods = user.TwoFactorEnabled ? await signIn.AvailableMethodsAsync(user) : [];
        if (methods.Contains(MfaMethods.Totp))
        {
            return Error.Rule("auth.totpAlreadyEnabled", "auth.errors.totpAlreadyEnabled").ToProblem();
        }

        return TypedResults.Ok(await signIn.NewAuthenticatorKeyAsync(user));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> StartEmailAsync(
        HttpContext context, StrategyUserManager users, SignInService signIn, IdentitySettingsCache settings, CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        return settings.Security.MfaAllowEmail
            ? (await signIn.SendCodeAsync(user, ct)).ToNoContent()
            : Error.Rule("auth.methodNotAvailable", "auth.errors.methodNotAvailable").ToProblem();
    }

    private static async Task<Results<Ok<RecoveryCodesDto>, ProblemHttpResult>> ConfirmMfaAsync(
        MfaConfirmInput body, HttpContext context, StrategyUserManager users, SignInService signIn, StrategySignInManager signInManager,
        CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        var result = await signIn.EnableMethodAsync(user, body.Method ?? string.Empty, body.Code ?? string.Empty, ct);
        if (result.IsSuccess)
        {
            await signInManager.RefreshSignInAsync(user);
        }

        return result.ToOk();
    }

    private static async Task<Results<Ok<MyMfaDto>, ProblemHttpResult>> DisableMfaAsync(
        MfaDisableInput body, HttpContext context, StrategyUserManager users, SignInService signIn, StrategySignInManager signInManager,
        IdentitySettingsCache settings, AuthEventLog events, CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        var methods = (await signIn.AvailableMethodsAsync(user)).Where(m => m != MfaMethods.Recovery).ToList();
        if (body.Method is null || !methods.Contains(body.Method))
        {
            return Error.Validation([new FieldError("method", "validation.unknownCode", "validation.unknownCode")]).ToProblem();
        }

        var remaining = methods.Where(m => m != body.Method).ToList();
        if (remaining.Count == 0 && await signIn.MfaRequiredForAsync(user, context.RequestAborted))
        {
            return Error.Rule("auth.mfaRequired", "auth.errors.mfaRequiredByPolicy").ToProblem();
        }

        if (body.Method == MfaMethods.Totp)
        {
            user.TotpEnabled = false;
            await users.UpdateAsync(user);
            await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        }
        else
        {
            user.EmailOtpEnabled = false;
            await users.UpdateAsync(user);
        }

        if (remaining.Count == 0)
        {
            await users.SetTwoFactorEnabledAsync(user, false);
            await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        }

        await signInManager.RefreshSignInAsync(user);
        await events.WriteAsync(AuthEventTypes.MfaDisabled, true, user.Id, user.UserName, detail: body.Method, cancellationToken: ct);
        return (await MfaAsync(context, users, signIn, settings)).Result switch
        {
            Ok<MyMfaDto> ok => TypedResults.Ok(ok.Value),
            ProblemHttpResult problem => problem,
            _ => Error.NotFound().ToProblem(),
        };
    }

    private static async Task<Results<Ok<RecoveryCodesDto>, ProblemHttpResult>> RecoveryCodesAsync(
        HttpContext context, StrategyUserManager users, AuthEventLog events, CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem();
        }

        var user = await CurrentAsync(context, users);
        if (user is null)
        {
            return Error.NotFound().ToProblem();
        }

        if (!user.TwoFactorEnabled)
        {
            return Error.Rule("auth.mfaNotEnabled", "auth.errors.mfaNotEnabled").ToProblem();
        }

        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        await events.WriteAsync(AuthEventTypes.RecoveryCodesGenerated, true, user.Id, user.UserName, cancellationToken: ct);
        return TypedResults.Ok(new RecoveryCodesDto(codes));
    }

    private static async Task<Ok<IReadOnlyList<SessionDto>>> SessionsAsync(
        HttpContext context, IdentityStoreDbContext db, IdentitySettingsCache settings, IClock clock, CancellationToken ct)
    {
        var userId = context.User.UserId();
        var current = CurrentSessionId(context);
        var security = settings.Security;
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt).ToListAsync(ct);
        return TypedResults.Ok<IReadOnlyList<SessionDto>>(sessions
            .Where(s => s.IsActive(clock.UtcNow, TimeSpan.FromMinutes(security.SessionIdleMinutes), TimeSpan.FromHours(security.SessionAbsoluteHours)))
            .Select(s => new SessionDto(s.Id, s.Method, s.Provider, s.CreatedAt, s.LastSeenAt, s.IpAddress, s.UserAgent, s.Id == current))
            .ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeSessionAsync(
        Guid id, HttpContext context, IdentityStoreDbContext db, AuthEventLog events, IClock clock, CancellationToken ct)
    {
        var userId = context.User.UserId();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (session is null)
        {
            return Error.NotFound().ToProblem();
        }

        session.RevokedAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await events.WriteAsync(AuthEventTypes.SessionRevoked, true, userId, context.User.Identity?.Name, cancellationToken: ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> RevokeOtherSessionsAsync(
        HttpContext context, StrategyUserManager users, SignInService signIn, StrategySignInManager signInManager, AuthEventLog events,
        CancellationToken ct)
    {
        var user = await CurrentAsync(context, users);
        if (user is not null)
        {
            // New security stamp: token-less cookies elsewhere fail validation too; this session is re-issued.
            await users.UpdateSecurityStampAsync(user);
            await signIn.RevokeSessionsAsync(user.Id, CurrentSessionId(context), ct);
            if (!context.User.IsToken())
            {
                await signInManager.RefreshSignInAsync(user);
            }

            await events.WriteAsync(AuthEventTypes.SessionRevoked, true, user.Id, user.UserName, detail: "others", cancellationToken: ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<AccessTokenDto>>> TokensAsync(HttpContext context, AccessTokenService tokens, CancellationToken ct) =>
        TypedResults.Ok(await tokens.ListAsync(context.User.UserId()!.Value, ct));

    private static async Task<Results<Ok<CreatedAccessTokenDto>, ProblemHttpResult>> CreateTokenAsync(
        CreateTokenInput body, HttpContext context, AccessTokenService tokens, AuthEventLog events, IFeatureFlags features, CancellationToken ct)
    {
        if (context.User.IsToken())
        {
            return InteractiveOnly.ToProblem(); // a token can never mint another token
        }

        if (!await features.IsEnabledAsync(IdentityFeatures.AccessTokens, ct))
        {
            return new Error("feature.disabled", "errors.featureDisabled", ErrorKind.Disabled).ToProblem();
        }

        var fields = new List<FieldError>();
        AuthEndpoints.Required(fields, "name", body.Name, PersonalAccessToken.NameMaxLength);
        if (body.Scopes is null || body.Scopes.Count == 0 || body.Scopes.Any(s => !TokenScopes.All.Contains(s)))
        {
            fields.Add(new FieldError("scopes", "validation.unknownCode", "validation.unknownCode"));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        var result = await tokens.CreateAsync(context.User.UserId()!.Value, body.Name!, body.Scopes!, body.Days, ct);
        if (result.IsSuccess)
        {
            await events.WriteAsync(AuthEventTypes.TokenCreated, true, context.User.UserId(), context.User.Identity?.Name,
                detail: result.Value.Details.Prefix, cancellationToken: ct);
        }

        return result.ToOk();
    }

    private static async Task<Results<Ok<AccessTokenDto>, ProblemHttpResult>> RevokeTokenAsync(
        Guid id, HttpContext context, AccessTokenService tokens, AuthEventLog events, CancellationToken ct)
    {
        var result = await tokens.RevokeAsync(id, context.User.UserId()!.Value, ct);
        if (result.IsSuccess)
        {
            await events.WriteAsync(AuthEventTypes.TokenRevoked, true, context.User.UserId(), context.User.Identity?.Name,
                detail: result.Value.Prefix, cancellationToken: ct);
        }

        return result.ToOk();
    }

    private static async Task<StrategyUser?> CurrentAsync(HttpContext context, StrategyUserManager users) =>
        context.User.UserId() is { } id ? await users.FindByIdAsync(id.ToString()) : null;

    private static Guid? CurrentSessionId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst(IdentityClaims.SessionId)?.Value, out var id) ? id : null;
}

public sealed record MeEmployeeDto(Guid Id, string EmployeeNo, string? NameAr, string? NameEn, Guid UnitId, string? UnitNameAr, string? UnitNameEn);

public sealed record MeDto(
    Guid Id,
    string UserName,
    string? Email,
    string PrincipalType,
    string? Language,
    string? Theme,
    MeEmployeeDto? Employee,
    bool MfaEnabled,
    bool HasPassword,
    string AuthMethod);

public sealed record MyMfaDto(
    bool Enabled, bool Totp, bool Email, int RecoveryCodesLeft, bool RequiredByPolicy, bool TotpAllowed, bool EmailAllowed);

public sealed record SessionDto(
    Guid Id, string Method, string? Provider, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string? IpAddress, string? UserAgent, bool Current);

internal sealed record PreferencesInput(string? Language, string? Theme);

internal sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword);

internal sealed record MfaConfirmInput(string? Method, string? Code);

internal sealed record MfaDisableInput(string? Method);

internal sealed record CreateTokenInput(string? Name, IReadOnlyList<string>? Scopes, int? Days);
