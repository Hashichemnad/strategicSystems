using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Import;

namespace Strategy.Platform.Identity.Application;

public sealed record UserEmployeeDto(Guid Id, string EmployeeNo, string? NameAr, string? NameEn, string Status);

public sealed record UserDto(
    Guid Id,
    string UserName,
    string? Email,
    UserEmployeeDto? Employee,
    bool Active,
    bool IsSystemAdmin,
    bool LockedOut,
    DateTimeOffset? LockoutEnd,
    bool MfaEnabled,
    bool HasPassword,
    IReadOnlyList<string> ExternalLogins,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    int ActiveSessions,
    int ActiveTokens);

public sealed record CreatedUserDto(UserDto User, bool InvitationSent, string? InvitationMessageKey);

/// <summary>Administration → Users (system administrators, ADR 0009). Every change writes an auth event.</summary>
internal sealed class UserAdministration(
    StrategyUserManager users,
    IdentityStoreDbContext db,
    IEmployeeDirectory employees,
    SignInService signIn,
    AuthEventLog events,
    IRequestContext context,
    IClock clock)
{
    public static Error SelfChange => Error.Rule("identity.user.self", "identity.errors.userSelf");

    public static Error LastAdmin => Error.Rule("identity.user.lastAdmin", "identity.errors.lastAdmin");

    public async Task<PagedResult<UserDto>> ListAsync(PageRequest paging, string? search, bool? active, CancellationToken cancellationToken)
    {
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            var employeeIds = (await employees.SearchAsync(search, cancellationToken).ConfigureAwait(false)).Cast<Guid?>().ToList();
            query = query.Where(u => u.NormalizedUserName!.Contains(term) || u.NormalizedEmail!.Contains(term) || employeeIds.Contains(u.EmployeeId));
        }

        if (active is not null)
        {
            query = query.Where(u => u.Active == active);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await query.OrderBy(u => u.UserName).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<UserDto>(await ToDtosAsync(page, cancellationToken).ConfigureAwait(false), paging.Page, paging.PageSize, total);
    }

    public async Task<Result<UserDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        return user is null ? Error.NotFound() : (await ToDtosAsync([user], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Creates a password-less account for an active employee and emails an invitation to set the password.</summary>
    public async Task<Result<CreatedUserDto>> CreateForEmployeeAsync(
        Guid employeeId, string? userName, bool isSystemAdmin, bool sendInvitation, Uri? requestOrigin, CancellationToken cancellationToken)
    {
        var employee = await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.Rule("identity.user.employeeNotFound", "identity.errors.employeeNotFound");
        }

        if (employee.Status == EmployeeStatus.Left)
        {
            return Error.Rule("identity.user.employeeLeft", "identity.errors.employeeLeft");
        }

        if (await users.Users.AnyAsync(u => u.EmployeeId == employeeId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("identity.user.employeeHasUser", "identity.errors.employeeHasUser");
        }

        var normalisedEmail = users.NormalizeEmail(employee.Email);
        if (await users.Users.AnyAsync(u => u.NormalizedEmail == normalisedEmail, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("identity.user.emailTaken", "identity.errors.emailTaken");
        }

        var user = new StrategyUser
        {
            UserName = (string.IsNullOrWhiteSpace(userName) ? employee.Email : userName).Trim().ToLowerInvariant(),
            Email = employee.Email,
            EmailConfirmed = true,
            EmployeeId = employeeId,
            IsSystemAdmin = isSystemAdmin,
            CreatedAt = clock.UtcNow,
        };
        var created = await users.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return created.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName))
                ? Error.Conflict("identity.user.userNameTaken", "identity.errors.userNameTaken")
                : IdentityErrors.ToValidation(created, "userName");
        }

        await events.WriteAsync(AuthEventTypes.UserCreated, true, user.Id, user.UserName, detail: isSystemAdmin ? "admin" : null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        Result sent = Result.Success();
        if (sendInvitation)
        {
            sent = await signIn.SendInvitationAsync(user, requestOrigin, cancellationToken).ConfigureAwait(false);
        }

        var dto = (await ToDtosAsync([user], cancellationToken).ConfigureAwait(false))[0];
        return new CreatedUserDto(dto, sendInvitation && sent.IsSuccess, sent.IsFailure ? sent.Error.MessageKey : null);
    }

    public Task<Result<UserDto>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken) =>
        ChangeAsync(id, active ? "activate" : "deactivate", guardSelf: !active, async user =>
        {
            if (!active && user.IsSystemAdmin && await IsLastActiveAdminAsync(user.Id, cancellationToken).ConfigureAwait(false))
            {
                return LastAdmin;
            }

            user.Active = active;
            await users.UpdateAsync(user).ConfigureAwait(false);
            if (!active)
            {
                await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
                await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result<UserDto>> SetAdminAsync(Guid id, bool isAdmin, CancellationToken cancellationToken) =>
        ChangeAsync(id, isAdmin ? "admin.grant" : "admin.revoke", guardSelf: !isAdmin, async user =>
        {
            if (!isAdmin && user.IsSystemAdmin && await IsLastActiveAdminAsync(user.Id, cancellationToken).ConfigureAwait(false))
            {
                return LastAdmin;
            }

            user.IsSystemAdmin = isAdmin;
            await users.UpdateAsync(user).ConfigureAwait(false);
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
            await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<UserDto>> UnlockAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "unlock", guardSelf: false, async user =>
        {
            await users.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
            await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<UserDto>> ResetMfaAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "mfa.reset", guardSelf: false, async user =>
        {
            user.EmailOtpEnabled = false;
            user.TotpEnabled = false;
            await users.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);
            await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey").ConfigureAwait(false);
            await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes").ConfigureAwait(false);
            await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<UserDto>> RevokeSessionsAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, "sessions.revoke", guardSelf: false, async user =>
        {
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
            await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }, cancellationToken);

    public async Task<Result> SendPasswordResetAsync(Guid id, Uri? requestOrigin, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound();
        }

        if (!user.Active)
        {
            return Error.Rule("identity.user.inactive", "identity.errors.userInactive");
        }

        return user.HasLocalPassword
            ? await SendResetAsync(user, requestOrigin, cancellationToken).ConfigureAwait(false)
            : await signIn.SendInvitationAsync(user, requestOrigin, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> SendResetAsync(StrategyUser user, Uri? requestOrigin, CancellationToken cancellationToken)
    {
        await signIn.RequestPasswordResetAsync(user.UserName!, requestOrigin, cancellationToken).ConfigureAwait(false);
        var last = await db.AuthEvents.AsNoTracking().Where(e => e.UserId == user.Id && e.Type == AuthEventTypes.PasswordResetRequested)
            .OrderByDescending(e => e.At).FirstAsync(cancellationToken).ConfigureAwait(false);
        return last.Success ? Result.Success() : Error.Rule("identity.email.failed", last.Detail ?? "identity.errors.smtpFailed");
    }

    private async Task<Result<UserDto>> ChangeAsync(
        Guid id, string detail, bool guardSelf, Func<StrategyUser, Task<Result>> change, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound();
        }

        if (guardSelf && user.Id == context.UserId)
        {
            return SelfChange;
        }

        var outcome = await change(user).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        await events.WriteAsync(AuthEventTypes.UserChanged, true, user.Id, user.UserName, detail: detail, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return (await ToDtosAsync([user], cancellationToken).ConfigureAwait(false))[0];
    }

    private Task<bool> IsLastActiveAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        users.Users.AllAsync(u => u.Id == userId || !u.IsSystemAdmin || !u.Active, cancellationToken);

    private async Task<List<UserDto>> ToDtosAsync(IReadOnlyList<StrategyUser> page, CancellationToken cancellationToken)
    {
        var ids = page.Select(u => u.Id).ToList();
        var logins = await db.UserLogins.AsNoTracking().Where(l => ids.Contains(l.UserId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var sessions = await db.Sessions.AsNoTracking().Where(s => ids.Contains(s.UserId) && s.RevokedAt == null)
            .GroupBy(s => s.UserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);
        var tokens = await db.Tokens.AsNoTracking().Where(t => ids.Contains(t.UserId) && t.RevokedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);
        var people = await employees.GetManyAsync(page.Select(u => u.EmployeeId).OfType<Guid>().ToList(), cancellationToken).ConfigureAwait(false);

        return page.Select(u => new UserDto(
            u.Id,
            u.UserName!,
            u.Email,
            u.EmployeeId is { } e && people.TryGetValue(e, out var p) ? new UserEmployeeDto(p.Id, p.EmployeeNo, p.NameAr, p.NameEn, p.Status) : null,
            u.Active,
            u.IsSystemAdmin,
            u.LockoutEnd is { } end && end > now,
            u.LockoutEnd is { } until && until > now ? until : null,
            u.TwoFactorEnabled,
            u.HasLocalPassword,
            logins.Where(l => l.UserId == u.Id).Select(l => l.LoginProvider).Distinct().ToList(),
            u.LastSignInAt,
            u.CreatedAt,
            sessions.GetValueOrDefault(u.Id),
            tokens.GetValueOrDefault(u.Id))).ToList();
    }
}

/// <summary>An employee who leaves can no longer sign in: the account is deactivated and its sessions end.</summary>
internal sealed class DeactivateUserWhenEmployeeLeaves(StrategyUserManager users, SignInService signIn, AuthEventLog events) : IEmployeeLifecycleHook
{
    public async Task EmployeeLeftAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var user = await users.Users.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, cancellationToken).ConfigureAwait(false);
        if (user is not { Active: true })
        {
            return;
        }

        user.Active = false;
        await users.UpdateAsync(user).ConfigureAwait(false);
        await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        await signIn.RevokeSessionsAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
        await events.WriteAsync(AuthEventTypes.UserChanged, true, user.Id, user.UserName, detail: "employeeLeft", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Employee import with <c>create_user = Y</c>: creates accounts and sends invitations (best effort, logged).</summary>
internal sealed class ImportedUserProvisioning(UserAdministration administration, StrategyUserManager users) : IUserProvisioning
{
    public async Task ProvisionAsync(IReadOnlyList<Guid> employeeIds, CancellationToken cancellationToken)
    {
        foreach (var employeeId in employeeIds)
        {
            if (!await users.Users.AnyAsync(u => u.EmployeeId == employeeId, cancellationToken).ConfigureAwait(false))
            {
                await administration.CreateForEmployeeAsync(employeeId, null, isSystemAdmin: false, sendInvitation: true, null, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
