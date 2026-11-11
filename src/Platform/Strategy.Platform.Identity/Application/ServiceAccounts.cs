using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Application;

/// <summary>
/// Service accounts of integration clients (ADR 0191): users with principal type <see cref="PrincipalType"/>, no password, no e-mail, no
/// employee. They hold role assignments like any user, never sign in interactively and are not listed with the people.
/// </summary>
public sealed class ServiceAccounts(StrategyUserManager users, IClock clock)
{
    public const string PrincipalType = "integration";

    /// <summary>Creates the account; <paramref name="name"/> becomes its user name (<c>integration.&lt;name&gt;</c>).</summary>
    public async Task<Guid> CreateAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var user = new StrategyUser
        {
            UserName = "integration." + name.ToLowerInvariant(),
            PrincipalType = PrincipalType,
            Active = true,
            CreatedAt = clock.UtcNow,
            LockoutEnabled = false,
        };
        var result = await users.CreateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Service account not created: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        return user.Id;
    }

    /// <summary>True when the account exists, is a service account and is active.</summary>
    public async Task<bool> IsUsableAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.PrincipalType == PrincipalType && u.Active, cancellationToken).ConfigureAwait(false);

    public async Task SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken)
    {
        var user = await users.Users.FirstOrDefaultAsync(u => u.Id == userId && u.PrincipalType == PrincipalType, cancellationToken).ConfigureAwait(false);
        if (user is not null && user.Active != active)
        {
            user.Active = active;
            await users.UpdateAsync(user).ConfigureAwait(false);
        }
    }
}
