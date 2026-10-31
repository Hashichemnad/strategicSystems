using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Application;

/// <summary>
/// UserManager whose password and lockout rules come from the security settings at runtime (Administration → Security),
/// and which keeps a password history.
/// </summary>
public sealed class StrategyUserManager : UserManager<StrategyUser>
{
    private readonly IdentityStoreDbContext _db;
    private readonly IClock _clock;

    public StrategyUserManager(
        IUserStore<StrategyUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<StrategyUser> passwordHasher,
        IEnumerable<IUserValidator<StrategyUser>> userValidators,
        IEnumerable<IPasswordValidator<StrategyUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<StrategyUser>> logger,
        IdentitySettingsCache settings,
        IdentityStoreDbContext db,
        IClock clock)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(settings);
        _db = db;
        _clock = clock;
        Options = WithSettings(optionsAccessor.Value, settings.Security);
    }

    public static IdentityOptions WithSettings(IdentityOptions baseOptions, SecuritySettings security)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);
        ArgumentNullException.ThrowIfNull(security);
        return new IdentityOptions
        {
            ClaimsIdentity = baseOptions.ClaimsIdentity,
            Stores = baseOptions.Stores,
            Tokens = baseOptions.Tokens,
            SignIn = baseOptions.SignIn,
            User = baseOptions.User,
            Password = new PasswordOptions
            {
                RequiredLength = security.PasswordMinLength,
                RequireUppercase = security.PasswordRequireUpper,
                RequireLowercase = security.PasswordRequireLower,
                RequireDigit = security.PasswordRequireDigit,
                RequireNonAlphanumeric = security.PasswordRequireSymbol,
                RequiredUniqueChars = 1,
            },
            Lockout = new LockoutOptions
            {
                AllowedForNewUsers = true,
                MaxFailedAccessAttempts = security.LockoutThreshold,
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(security.LockoutMinutes),
            },
        };
    }

    protected override async Task<IdentityResult> UpdatePasswordHash(StrategyUser user, string? newPassword, bool validatePassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        var previousHash = user.PasswordHash;
        var result = await base.UpdatePasswordHash(user, newPassword!, validatePassword).ConfigureAwait(false);
        if (result.Succeeded && newPassword is not null)
        {
            if (previousHash is not null)
            {
                _db.PasswordHistory.Add(new PasswordHistoryEntry { UserId = user.Id, Hash = previousHash, CreatedAt = _clock.UtcNow });
            }

            user.PasswordChangedAt = _clock.UtcNow;
        }

        return result;
    }
}

/// <summary>Refuses the last N passwords (security settings), checked against the stored hashes.</summary>
internal sealed class PasswordHistoryValidator(IdentityStoreDbContext db, IdentitySettingsCache settings) : IPasswordValidator<StrategyUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<StrategyUser> manager, StrategyUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        var keep = settings.Security.PasswordHistory;
        if (keep <= 0 || password is null)
        {
            return IdentityResult.Success;
        }

        var hashes = await db.PasswordHistory.AsNoTracking().Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedAt).Take(keep - 1).Select(h => h.Hash).ToListAsync().ConfigureAwait(false);
        if (user.PasswordHash is not null)
        {
            hashes.Add(user.PasswordHash);
        }

        var reused = hashes.Any(hash => manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed);
        return reused
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordReused", Description = "PasswordReused" })
            : IdentityResult.Success;
    }
}

/// <summary>Only active internal users may sign in (in addition to lockout).</summary>
public sealed class StrategySignInManager(
    UserManager<StrategyUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<StrategyUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<StrategyUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<StrategyUser> confirmation)
    : SignInManager<StrategyUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(StrategyUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Active && await base.CanSignInAsync(user).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs the user in after the first factor, but keeps them in the two-factor state (<see cref="IdentityConstants.TwoFactorUserIdScheme"/>):
    /// with <paramref name="enrolment"/> they may only enrol a second factor.
    /// </summary>
    public Task SignInTwoFactorStateAsync(StrategyUser user, bool enrolment, string? method)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id.ToString()));
        if (enrolment)
        {
            identity.AddClaim(new Claim(IdentityClaims.Enrolment, "true"));
        }

        if (method is not null)
        {
            identity.AddClaim(new Claim(StrategyClaims.AuthMethod, method));
        }

        return Context.SignInAsync(
            IdentityConstants.TwoFactorUserIdScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5), IsPersistent = false });
    }

    /// <summary>The user in the two-factor state, and whether that state is enrolment-only.</summary>
    public async Task<(StrategyUser? User, bool Enrolment)> GetTwoFactorStateAsync()
    {
        var result = await Context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal.FindFirst(ClaimTypes.Name)?.Value is not { } id)
        {
            return (null, false);
        }

        var user = await UserManager.FindByIdAsync(id).ConfigureAwait(false);
        return (user, result.Principal.HasClaim(IdentityClaims.Enrolment, "true"));
    }

    public Task ClearTwoFactorStateAsync() => Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
}

/// <summary>Adds the claims access checks rely on: principal type and employee. Roles are read from Platform.Access per request.</summary>
internal sealed class StrategyClaimsFactory(UserManager<StrategyUser> userManager, IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<StrategyUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(StrategyUser user)
    {
        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        identity.AddClaim(new Claim(StrategyClaims.PrincipalType, user.PrincipalType));
        if (user.EmployeeId is { } employeeId)
        {
            identity.AddClaim(new Claim(StrategyClaims.EmployeeId, employeeId.ToString()));
        }

        return identity;
    }
}
