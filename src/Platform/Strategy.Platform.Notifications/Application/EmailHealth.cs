using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Infrastructure;

namespace Strategy.Platform.Notifications.Application;

/// <summary>
/// Health page (ADR 0151): is an e-mail server configured, and how did deliveries go in the last 24 hours. Reads the delivery log only; the
/// live test is the *Send test e-mail* action of Settings → Email server.
/// </summary>
internal sealed class EmailHealthContributor(ISmtpSettingsSource smtp, NotifyDbContext db, IClock clock) : IAdminHealthContributor
{
    public string Code => "email";

    public string Group => AdminHealthGroups.Integrations;

    public int Order => 20;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var connection = await smtp.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!connection.IsUsable)
        {
            return [new AdminHealthCheck(Code, Group, AdminHealthStatuses.NotConfigured, "health.email.notConfigured")];
        }

        var since = clock.UtcNow.AddHours(-24);
        var recent = db.Deliveries.AsNoTracking().Where(d => d.At >= since);
        var sent = await recent.CountAsync(d => d.Status == DeliveryStatuses.Sent, cancellationToken).ConfigureAwait(false);
        var failed = await recent.CountAsync(d => d.Status == DeliveryStatuses.Failed, cancellationToken).ConfigureAwait(false);
        var last = await db.Deliveries.AsNoTracking().OrderByDescending(d => d.At).Select(d => new { d.At, d.Status })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var (status, key) = Status(sent, failed, last?.Status);
        return [new AdminHealthCheck(Code, Group, status, key, HealthParams.Of(
            ("host", connection.Host), ("port", connection.Port), ("security", connection.Security), ("sent24h", sent), ("failed24h", failed),
            ("lastAt", last?.At), ("lastStatus", last?.Status)))];
    }

    /// <summary>Last delivery failed → error; some failures in 24 hours → warning; otherwise ok.</summary>
    public static (string Status, string MessageKey) Status(int sent, int failed, string? lastStatus) =>
        lastStatus == DeliveryStatuses.Failed ? (AdminHealthStatuses.Error, "health.email.lastFailed")
        : failed > 0 ? (AdminHealthStatuses.Warning, "health.email.someFailed")
        : sent == 0 ? (AdminHealthStatuses.Ok, "health.email.idle")
        : (AdminHealthStatuses.Ok, "health.email.ok");
}
