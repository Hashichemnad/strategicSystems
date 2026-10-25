using Microsoft.Extensions.Logging;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// Records sign-in and account events in <c>identity.auth_event</c> and as structured log events (acceptance check:
/// "all auth events visible in logs"). Never logs passwords, codes or tokens.
/// </summary>
public sealed partial class AuthEventLog(IdentityStoreDbContext db, IRequestContext context, IClock clock, ILogger<AuthEventLog> logger)
{
    public async Task WriteAsync(
        string type,
        bool success,
        Guid? userId = null,
        string? userName = null,
        string? provider = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuthEvent
        {
            Type = type,
            Success = success,
            UserId = userId,
            UserName = Truncate(userName, 256),
            ActorUserId = context.UserId,
            Provider = provider,
            Detail = Truncate(detail, 200),
            IpAddress = context.IpAddress,
            UserAgent = Truncate(context.UserAgent, 512),
            CorrelationId = context.CorrelationId,
            At = clock.UtcNow,
        };
        db.AuthEvents.Add(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogAuthEvent(logger, type, success, userId, userName, provider, detail, context.IpAddress);
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information,
        Message = "Auth event {AuthEventType} success={AuthSuccess} user={AuthUserId} name={AuthUserName} provider={AuthProvider} detail={AuthDetail} ip={ClientIp}")]
    private static partial void LogAuthEvent(
        ILogger logger, string authEventType, bool authSuccess, Guid? authUserId, string? authUserName, string? authProvider, string? authDetail, string? clientIp);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
