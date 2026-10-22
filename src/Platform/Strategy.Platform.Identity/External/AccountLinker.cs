using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Domain;

namespace Strategy.Platform.Identity.External;

/// <summary>What an external provider told us about the person.</summary>
public sealed record ExternalIdentity(string Subject, string? Email, string? EmployeeNo, string? UserName, string? UserType = null);

/// <summary>Outcome of linking: the user to sign in, or a failure message key (shown on the login page).</summary>
public sealed record LinkResult(StrategyUser? User, string? FailureKey, bool NewlyLinked = false);

/// <summary>
/// Finds the account for an external identity (ADR 0011): existing link → active employee matched by the provider's
/// match field (email / employee number / user name) → optional just-in-time user for that employee.
/// Employees are never created by sign-in.
/// </summary>
public sealed class AccountLinker(StrategyUserManager users, IEmployeeDirectory employees, IdentityStoreDbContext db, IClock clock)
{
    public const string NoMatch = "auth.errors.externalNoMatch";
    public const string NoAccount = "auth.errors.externalNoAccount";
    public const string Inactive = "auth.errors.externalInactive";
    public const string UserTypeNotAllowed = "auth.errors.externalUserType";

    public async Task<LinkResult> LinkAsync(IdentityProvider provider, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(identity);

        var linked = await users.FindByLoginAsync(provider.Code, identity.Subject).ConfigureAwait(false);
        if (linked is not null)
        {
            return linked.Active ? new LinkResult(linked, null) : new LinkResult(null, Inactive);
        }

        if (!provider.AutoLink)
        {
            return new LinkResult(null, NoMatch);
        }

        var employee = await FindEmployeeAsync(provider.MatchField, identity, cancellationToken).ConfigureAwait(false);
        if (employee is null || employee.Status == EmployeeStatus.Left)
        {
            return new LinkResult(null, NoMatch);
        }

        var user = await users.Users.FirstOrDefaultAsync(u => u.EmployeeId == employee.Id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            if (!provider.JitCreateUser)
            {
                return new LinkResult(null, NoAccount);
            }

            user = new StrategyUser
            {
                UserName = (identity.UserName ?? employee.Email).Trim().ToLowerInvariant(),
                Email = employee.Email,
                EmailConfirmed = true,
                EmployeeId = employee.Id,
                CreatedAt = clock.UtcNow,
            };
            if (await users.FindByNameAsync(user.UserName).ConfigureAwait(false) is not null)
            {
                user.UserName = employee.Email.Trim().ToLowerInvariant();
            }

            var created = await users.CreateAsync(user).ConfigureAwait(false);
            if (!created.Succeeded)
            {
                return new LinkResult(null, NoAccount);
            }
        }

        if (!user.Active)
        {
            return new LinkResult(null, Inactive);
        }

        // One account per provider: re-linking to another subject is an administrator decision.
        if (await db.UserLogins.AnyAsync(l => l.UserId == user.Id && l.LoginProvider == provider.Code, cancellationToken).ConfigureAwait(false))
        {
            return new LinkResult(null, NoMatch);
        }

        var added = await users.AddLoginAsync(user, new UserLoginInfo(provider.Code, identity.Subject, provider.DisplayNameEn ?? provider.Code))
            .ConfigureAwait(false);
        return added.Succeeded ? new LinkResult(user, null, NewlyLinked: true) : new LinkResult(null, NoMatch);
    }

    private async Task<EmployeeReference?> FindEmployeeAsync(string matchField, ExternalIdentity identity, CancellationToken cancellationToken) =>
        matchField switch
        {
            LinkMatchFields.Email when !string.IsNullOrWhiteSpace(identity.Email) =>
                await employees.FindByEmailAsync(identity.Email, cancellationToken).ConfigureAwait(false),
            LinkMatchFields.EmployeeNo when !string.IsNullOrWhiteSpace(identity.EmployeeNo) =>
                await employees.FindByEmployeeNoAsync(identity.EmployeeNo, cancellationToken).ConfigureAwait(false),
            LinkMatchFields.UserName when !string.IsNullOrWhiteSpace(identity.UserName) =>
                await FindByUserNameAsync(identity.UserName, cancellationToken).ConfigureAwait(false),
            _ => null,
        };

    /// <summary>User-name matching: the employee of the existing account with that user name (set by the administrator).</summary>
    private async Task<EmployeeReference?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(userName).ConfigureAwait(false);
        if (user?.EmployeeId is { } employeeId)
        {
            return await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
