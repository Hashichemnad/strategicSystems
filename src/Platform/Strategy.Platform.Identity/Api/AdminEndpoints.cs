using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Email;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Identity.Api;

public static class IdentityPermissions
{
    public const string UserManage = "identity.user.manage";
    public const string ProviderManage = "identity.provider.manage";
    public const string SecurityManage = "identity.security.manage";
    public const string SmtpManage = "identity.smtp.manage";
}

/// <summary>Administration endpoints (<c>/api/v1/identity/admin</c>), guarded by the identity permissions (ADR 0016).</summary>
internal static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        var users = admin.MapGroup("/users");
        users.MapGet("/", ListUsersAsync).WithName("ListUsers").RequirePermission(IdentityPermissions.UserManage);
        users.MapGet("/{id:guid}", GetUserAsync).WithName("GetUser").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/", CreateUserAsync).WithName("CreateUser").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/activate", (Guid id, UserAdministration a, CancellationToken ct) => Run(a.SetActiveAsync(id, true, ct)))
            .WithName("ActivateUser").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/deactivate", (Guid id, UserAdministration a, CancellationToken ct) => Run(a.SetActiveAsync(id, false, ct)))
            .WithName("DeactivateUser").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/unlock", (Guid id, UserAdministration a, CancellationToken ct) => Run(a.UnlockAsync(id, ct)))
            .WithName("UnlockUser").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/reset-mfa", (Guid id, UserAdministration a, CancellationToken ct) => Run(a.ResetMfaAsync(id, ct)))
            .WithName("ResetUserMfa").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/revoke-sessions", (Guid id, UserAdministration a, CancellationToken ct) => Run(a.RevokeSessionsAsync(id, ct)))
            .WithName("RevokeUserSessions").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/send-password-reset", SendResetAsync).WithName("SendUserPasswordReset").RequirePermission(IdentityPermissions.UserManage);
        users.MapGet("/{id:guid}/tokens", UserTokensAsync).WithName("ListUserTokens").RequirePermission(IdentityPermissions.UserManage);
        users.MapPost("/{id:guid}/tokens/{tokenId:guid}/revoke", RevokeUserTokenAsync).WithName("RevokeUserToken")
            .RequirePermission(IdentityPermissions.UserManage);

        admin.MapGet("/auth-events", AuthEventsAsync).WithName("ListAuthEvents").RequirePermission(IdentityPermissions.UserManage);

        admin.MapGet("/security-settings", GetSecurity).WithName("GetSecuritySettings").RequirePermission(IdentityPermissions.SecurityManage);
        admin.MapPut("/security-settings", UpdateSecurityAsync).WithName("UpdateSecuritySettings").RequirePermission(IdentityPermissions.SecurityManage);

        admin.MapGet("/smtp", GetSmtp).WithName("GetSmtpSettings").RequirePermission(IdentityPermissions.SmtpManage);
        admin.MapPut("/smtp", UpdateSmtpAsync).WithName("UpdateSmtpSettings").RequirePermission(IdentityPermissions.SmtpManage);
        admin.MapPost("/smtp/test", TestSmtpAsync).WithName("TestSmtpSettings").RequirePermission(IdentityPermissions.SmtpManage);
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Run(Task<Result<UserDto>> action) => (await action).ToOk();

    private static async Task<Results<Ok<PagedResult<UserDto>>, ProblemHttpResult>> ListUsersAsync(
        int? page, int? pageSize, string? search, bool? active, UserAdministration administration, CancellationToken ct)
    {
        var paging = new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, null);
        if (paging.Page < 1 || paging.PageSize < 1 || paging.PageSize > PageRequest.MaxPageSize || search?.Length > 200)
        {
            return Error.Validation([new FieldError("pageSize", "validation.inclusiveBetween", "validation.inclusiveBetween")]).ToProblem();
        }

        return TypedResults.Ok(await administration.ListAsync(paging, search, active, ct));
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> GetUserAsync(Guid id, UserAdministration administration, CancellationToken ct) =>
        (await administration.GetAsync(id, ct)).ToOk();

    private static async Task<Results<Ok<CreatedUserDto>, ProblemHttpResult>> CreateUserAsync(
        CreateUserInput body, UserAdministration administration, HttpContext context, CancellationToken ct)
    {
        if (body.EmployeeId is null)
        {
            return Error.Validation([new FieldError("employeeId", "validation.notEmpty", "validation.notEmpty")]).ToProblem();
        }

        if (body.UserName is { Length: > 256 })
        {
            return Error.Validation([new FieldError("userName", "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = 256 })]).ToProblem();
        }

        return (await administration.CreateForEmployeeAsync(
            body.EmployeeId.Value, body.UserName, body.SendInvitation ?? true, AuthEndpoints.RequestOrigin(context), ct)).ToOk();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SendResetAsync(
        Guid id, UserAdministration administration, HttpContext context, CancellationToken ct) =>
        (await administration.SendPasswordResetAsync(id, AuthEndpoints.RequestOrigin(context), ct)).ToNoContent();

    private static async Task<Ok<IReadOnlyList<AccessTokenDto>>> UserTokensAsync(Guid id, AccessTokenService tokens, CancellationToken ct) =>
        TypedResults.Ok(await tokens.ListAsync(id, ct));

    private static async Task<Results<Ok<AccessTokenDto>, ProblemHttpResult>> RevokeUserTokenAsync(
        Guid id, Guid tokenId, AccessTokenService tokens, AuthEventLog events, CancellationToken ct)
    {
        var result = await tokens.RevokeAsync(tokenId, id, ct);
        if (result.IsSuccess)
        {
            await events.WriteAsync(AuthEventTypes.TokenRevoked, true, id, detail: result.Value.Prefix, cancellationToken: ct);
        }

        return result.ToOk();
    }

    private static async Task<Ok<PagedResult<AuthEventDto>>> AuthEventsAsync(
        int? page, int? pageSize, Guid? userId, string? type, IdentityStoreDbContext db, CancellationToken ct)
    {
        var paging = new PageRequest(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, PageRequest.MaxPageSize), null);
        var query = db.AuthEvents.AsNoTracking();
        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(e => e.Type == type);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip(paging.Skip).Take(paging.PageSize)
            .Select(e => new AuthEventDto(e.Id, e.Type, e.Success, e.UserId, e.UserName, e.ActorUserId, e.Provider, e.Detail, e.IpAddress, e.At))
            .ToListAsync(ct);
        return TypedResults.Ok(new PagedResult<AuthEventDto>(items, paging.Page, paging.PageSize, total));
    }

    // ---- Security settings ----

    private static Ok<SecuritySettingsDto> GetSecurity(IdentitySettingsCache cache) => TypedResults.Ok(SecuritySettingsDto.From(cache.Security));

    private static async Task<Results<Ok<SecuritySettingsDto>, ProblemHttpResult>> UpdateSecurityAsync(
        SecuritySettingsDto body, IdentityStoreDbContext db, IdentitySettingsCache cache, AuthEventLog events, CancellationToken ct)
    {
        var fields = new List<FieldError>();
        Range(fields, "passwordMinLength", body.PasswordMinLength, 8, 128);
        Range(fields, "passwordHistory", body.PasswordHistory, 0, 24);
        Range(fields, "lockoutThreshold", body.LockoutThreshold, 3, 20);
        Range(fields, "lockoutMinutes", body.LockoutMinutes, 1, 1440);
        Range(fields, "sessionIdleMinutes", body.SessionIdleMinutes, 5, 720);
        Range(fields, "sessionAbsoluteHours", body.SessionAbsoluteHours, 1, 24);
        Range(fields, "tokenMaxDays", body.TokenMaxDays, 1, 730);
        Range(fields, "tokenDefaultDays", body.TokenDefaultDays, 1, body.TokenMaxDays);
        if (!MfaPolicies.Values.Contains(body.MfaPolicy))
        {
            fields.Add(new FieldError("mfaPolicy", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!body.MfaAllowTotp && !body.MfaAllowEmail)
        {
            fields.Add(new FieldError("mfaAllowTotp", "identity.settings.noMfaMethod", "identity.errors.noMfaMethod"));
        }

        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        var settings = await db.SecuritySettings.SingleAsync(ct);
        db.Entry(settings).Property(s => s.Version).OriginalValue = body.Version;
        body.ApplyTo(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict().ToProblem();
        }

        await cache.ReloadAsync(ct);
        await events.WriteAsync(AuthEventTypes.SettingsChanged, true, detail: "security", cancellationToken: ct);
        return TypedResults.Ok(SecuritySettingsDto.From(cache.Security));
    }

    // ---- SMTP ----

    private static Ok<SmtpSettingsDto> GetSmtp(IdentitySettingsCache cache) => TypedResults.Ok(SmtpSettingsDto.From(cache.Smtp));

    private static async Task<Results<Ok<SmtpSettingsDto>, ProblemHttpResult>> UpdateSmtpAsync(
        SmtpSettingsInput body, IdentityStoreDbContext db, IdentitySettingsCache cache, ISecretProtector protector, AuthEventLog events,
        CancellationToken ct)
    {
        var fields = ValidateSmtp(body);
        if (fields.Count > 0)
        {
            return Error.Validation(fields).ToProblem();
        }

        var settings = await db.SmtpSettings.SingleAsync(ct);
        db.Entry(settings).Property(s => s.Version).OriginalValue = body.Version;
        body.ApplyTo(settings, protector);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict().ToProblem();
        }

        await cache.ReloadAsync(ct);
        await events.WriteAsync(AuthEventTypes.SettingsChanged, true, detail: "smtp", cancellationToken: ct);
        return TypedResults.Ok(SmtpSettingsDto.From(cache.Smtp));
    }

    /// <summary>Sends a test message with the saved settings, or with unsaved ones from the form (blank password = saved one).</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> TestSmtpAsync(
        SmtpTestInput body, IdentitySettingsCache cache, ISecretProtector protector, AuthEventLog events, ISettingsStore store, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.To) || !System.Net.Mail.MailAddress.TryCreate(body.To, out _))
        {
            return Error.Validation([new FieldError("to", "validation.email", "validation.email")]).ToProblem();
        }

        var settings = cache.Smtp.Clone();
        var password = protector.Unprotect(SecretPurposes.SmtpPassword, settings.PasswordProtected);
        if (body.Settings is { } draft)
        {
            var fields = ValidateSmtp(draft);
            if (fields.Count > 0)
            {
                return Error.Validation(fields).ToProblem();
            }

            draft.ApplyTo(settings, protector);
            password = string.IsNullOrEmpty(draft.Password) ? password : draft.Password;
        }

        try
        {
            await SmtpIdentityEmailSender.SendWithAsync(
                settings, password, new IdentityEmail(body.To, "test", new Dictionary<string, string>()), ct, await store.GetAsync<BrandingSettings>(ct));
        }
        catch (EmailDeliveryException exception)
        {
            await events.WriteAsync(AuthEventTypes.SettingsChanged, false, detail: "smtp.test", cancellationToken: ct);
            return Error.Rule("identity.smtp.testFailed", exception.MessageKey).ToProblem();
        }

        await events.WriteAsync(AuthEventTypes.SettingsChanged, true, detail: "smtp.test", cancellationToken: ct);
        return TypedResults.NoContent();
    }

    private static List<FieldError> ValidateSmtp(SmtpSettingsInput body)
    {
        var fields = new List<FieldError>();
        if (body.Enabled)
        {
            AuthEndpoints.Required(fields, "host", body.Host, 255);
            AuthEndpoints.Required(fields, "fromAddress", body.FromAddress, 256);
        }

        Range(fields, "port", body.Port, 1, 65535);
        if (!SmtpSecurityModes.Values.Contains(body.Security))
        {
            fields.Add(new FieldError("security", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!string.IsNullOrWhiteSpace(body.FromAddress) && !System.Net.Mail.MailAddress.TryCreate(body.FromAddress, out _))
        {
            fields.Add(new FieldError("fromAddress", "validation.email", "validation.email"));
        }

        if (!string.IsNullOrWhiteSpace(body.PublicBaseUrl)
            && (!Uri.TryCreate(body.PublicBaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http") || url.PathAndQuery != "/"))
        {
            fields.Add(new FieldError("publicBaseUrl", "validation.url", "validation.url"));
        }

        return fields;
    }

    private static void Range(List<FieldError> fields, string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            fields.Add(new FieldError(field, "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = min, ["to"] = max }));
        }
    }
}

internal sealed record CreateUserInput(Guid? EmployeeId, string? UserName, bool? SendInvitation);

public sealed record AuthEventDto(
    Guid Id, string Type, bool Success, Guid? UserId, string? UserName, Guid? ActorUserId, string? Provider, string? Detail, string? IpAddress,
    DateTimeOffset At);

public sealed record SecuritySettingsDto(
    int PasswordMinLength,
    bool PasswordRequireUpper,
    bool PasswordRequireLower,
    bool PasswordRequireDigit,
    bool PasswordRequireSymbol,
    int PasswordHistory,
    int LockoutThreshold,
    int LockoutMinutes,
    int SessionIdleMinutes,
    int SessionAbsoluteHours,
    string MfaPolicy,
    bool MfaAllowTotp,
    bool MfaAllowEmail,
    bool RequireLocalMfaAfterSso,
    int TokenDefaultDays,
    int TokenMaxDays,
    uint Version)
{
    public static SecuritySettingsDto From(SecuritySettings s) => new(
        s.PasswordMinLength, s.PasswordRequireUpper, s.PasswordRequireLower, s.PasswordRequireDigit, s.PasswordRequireSymbol, s.PasswordHistory,
        s.LockoutThreshold, s.LockoutMinutes, s.SessionIdleMinutes, s.SessionAbsoluteHours, s.MfaPolicy, s.MfaAllowTotp, s.MfaAllowEmail,
        s.RequireLocalMfaAfterSso, s.TokenDefaultDays, s.TokenMaxDays, s.Version);

    public void ApplyTo(SecuritySettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.PasswordMinLength = PasswordMinLength;
        s.PasswordRequireUpper = PasswordRequireUpper;
        s.PasswordRequireLower = PasswordRequireLower;
        s.PasswordRequireDigit = PasswordRequireDigit;
        s.PasswordRequireSymbol = PasswordRequireSymbol;
        s.PasswordHistory = PasswordHistory;
        s.LockoutThreshold = LockoutThreshold;
        s.LockoutMinutes = LockoutMinutes;
        s.SessionIdleMinutes = SessionIdleMinutes;
        s.SessionAbsoluteHours = SessionAbsoluteHours;
        s.MfaPolicy = MfaPolicy;
        s.MfaAllowTotp = MfaAllowTotp;
        s.MfaAllowEmail = MfaAllowEmail;
        s.RequireLocalMfaAfterSso = RequireLocalMfaAfterSso;
        s.TokenDefaultDays = TokenDefaultDays;
        s.TokenMaxDays = TokenMaxDays;
    }
}

public sealed record SmtpSettingsDto(
    bool Enabled, string? Host, int Port, string Security, string? UserName, bool HasPassword, string? FromAddress, string? FromNameAr,
    string? FromNameEn, string? PublicBaseUrl, uint Version)
{
    public static SmtpSettingsDto From(SmtpSettings s) => new(
        s.Enabled, s.Host, s.Port, s.Security, s.UserName, !string.IsNullOrEmpty(s.PasswordProtected), s.FromAddress, s.FromNameAr, s.FromNameEn,
        s.PublicBaseUrl, s.Version);
}

/// <summary>SMTP form. A blank <see cref="Password"/> keeps the stored one; <see cref="ClearPassword"/> removes it.</summary>
public sealed record SmtpSettingsInput(
    bool Enabled, string? Host, int Port, string Security, string? UserName, string? Password, bool ClearPassword, string? FromAddress,
    string? FromNameAr, string? FromNameEn, string? PublicBaseUrl, uint Version)
{
    public void ApplyTo(SmtpSettings s, ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(protector);
        s.Enabled = Enabled;
        s.Host = Host?.Trim();
        s.Port = Port;
        s.Security = Security;
        s.UserName = string.IsNullOrWhiteSpace(UserName) ? null : UserName.Trim();
        if (ClearPassword)
        {
            s.PasswordProtected = null;
        }
        else if (!string.IsNullOrEmpty(Password))
        {
            s.PasswordProtected = protector.Protect(SecretPurposes.SmtpPassword, Password);
        }

        s.FromAddress = FromAddress?.Trim();
        s.FromNameAr = FromNameAr?.Trim();
        s.FromNameEn = FromNameEn?.Trim();
        s.PublicBaseUrl = string.IsNullOrWhiteSpace(PublicBaseUrl) ? null : PublicBaseUrl.Trim().TrimEnd('/');
    }
}

internal sealed record SmtpTestInput(string? To, SmtpSettingsInput? Settings);
