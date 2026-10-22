using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Strategy.Platform.Identity.Domain;

namespace Strategy.Platform.Identity.Infrastructure;

/// <summary>
/// The current security and SMTP settings, cached in memory. Reloaded at start-up, after every change on this node,
/// and every 30 seconds (so a change on another node takes effect quickly).
/// </summary>
public sealed class IdentitySettingsCache(IServiceScopeFactory scopeFactory)
{
    private volatile SecuritySettings _security = new();
    private volatile SmtpSettings _smtp = new();

    public SecuritySettings Security => _security;

    public SmtpSettings Smtp => _smtp;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
        _security = await db.SecuritySettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? new SecuritySettings();
        _smtp = await db.SmtpSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? new SmtpSettings();
    }
}

internal sealed partial class IdentitySettingsRefresher(IdentitySettingsCache cache, Microsoft.Extensions.Logging.ILogger<IdentitySettingsRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await cache.ReloadAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReloadFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Could not reload identity settings")]
    private static partial void LogReloadFailed(Microsoft.Extensions.Logging.ILogger logger, Exception exception);
}
