using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Authentication;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Keeps session tickets on the server (<c>identity.user_session</c>); the cookie only carries the session id.
/// Enforces the idle and absolute timeouts from the security settings on every request, and makes
/// "sign out this session / everywhere" immediate.
/// </summary>
internal sealed class SessionTicketStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider protectionProvider,
    IdentitySettingsCache settings,
    IClock clock) : ITicketStore
{
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(1);

    private IDataProtector Protector => protectionProvider.CreateProtector(SecretPurposes.SessionTicket);

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) => StoreAsync(ticket, null!, cancellationToken);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var now = clock.UtcNow;
        var session = new UserSession
        {
            UserId = ticket.Principal.UserId() ?? throw new InvalidOperationException("Ticket without user id."),
            Ticket = Protect(ticket),
            Method = ticket.Principal.FindFirst(StrategyClaims.AuthMethod)?.Value ?? "pwd",
            Provider = ticket.Principal.FindFirst(IdentityClaims.Provider)?.Value,
            CreatedAt = now,
            LastSeenAt = now,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(httpContext?.Request.Headers.UserAgent.ToString(), 512),
        };

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return session.Id.ToString("N");
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        if (session is { RevokedAt: null })
        {
            session.Ticket = Protect(ticket);
            session.LastSeenAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var security = settings.Security;
        if (!session.IsActive(now, TimeSpan.FromMinutes(security.SessionIdleMinutes), TimeSpan.FromHours(security.SessionAbsoluteHours)))
        {
            if (session.RevokedAt is null)
            {
                session.RevokedAt = now;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        if (now - session.LastSeenAt >= LastSeenResolution)
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return TicketSerializer.Default.Deserialize(Protector.Unprotect(session.Ticket));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        if (session is { RevokedAt: null })
        {
            session.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private byte[] Protect(AuthenticationTicket ticket) => Protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Configures the application cookie to use the server-side session store.</summary>
internal sealed class SessionStoreCookieSetup(SessionTicketStore store) : Microsoft.Extensions.Options.IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme)
        {
            options.SessionStore = store;
        }
    }
}
