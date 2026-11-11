using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Net;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary>Sends one webhook request. The default goes through the outbound URL policy (connect-time SSRF guard, no redirects, ADR 0155).</summary>
public interface IWebhookTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class PolicyWebhookTransport(OutboundUrlPolicy policy) : IWebhookTransport, IDisposable
{
    private readonly HttpClient _client = new(policy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Sends due deliveries (ADR 0194): signed POST, 2xx = delivered; otherwise the attempt is logged and the next one scheduled with exponential
/// back-off, until the maximum — then the delivery is dead (dead-letter list). Times come from <see cref="IClock"/>.
/// </summary>
public sealed class WebhookDispatcher(IntegrationDbContext db, IWebhookTransport transport, ISecretProtector secrets, ISettingsStore settings, IClock clock)
{
    public const string SecretPurpose = "integration.webhook.secret";

    /// <returns>(delivered, failed) in this run.</returns>
    public async Task<(int Delivered, int Failed)> RunDueAsync(CancellationToken ct, int batch = 100)
    {
        var options = await settings.GetAsync<IntegrationSettings>(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var due = await db.Deliveries.Where(d => (d.Status == DeliveryStatuses.Pending || d.Status == DeliveryStatuses.Retrying) && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(batch).ToListAsync(ct).ConfigureAwait(false);
        var subscriptionIds = due.Select(d => d.SubscriptionRef).Distinct().ToList();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        var clientIds = due.Select(d => d.ClientRef).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        int delivered = 0, failed = 0;
        foreach (var delivery in due)
        {
            var ok = await AttemptAsync(delivery, subscriptions.GetValueOrDefault(delivery.SubscriptionRef), clients.GetValueOrDefault(delivery.ClientRef), options, ct)
                .ConfigureAwait(false);
            _ = ok ? delivered++ : failed++;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return (delivered, failed);
    }

    private async Task<bool> AttemptAsync(
        WebhookDelivery delivery, WebhookSubscription? subscription, IntegrationClient? client, IntegrationSettings options, CancellationToken ct)
    {
        var now = clock.UtcNow;
        delivery.Attempts++;
        int? status = null;
        string? error = null;
        var watch = Stopwatch.StartNew();
        if (subscription is null || !subscription.Active || client is null || !client.IsUsable(now))
        {
            error = "subscription or client inactive";
        }
        else
        {
            try
            {
                var secret = secrets.Unprotect(SecretPurpose, subscription.ProtectedSecret) ?? string.Empty;
                var timestamp = now.ToUnixTimeSeconds();
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl)
                {
                    Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add(WebhookSigner.EventHeader, delivery.EventCode);
                request.Headers.Add(WebhookSigner.DeliveryHeader, delivery.Id.ToString());
                request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(secret, timestamp, delivery.Payload));
                request.Headers.UserAgent.ParseAdd("Strategy-Webhooks/1.0");
                using var response = await transport.SendAsync(request, TimeSpan.FromSeconds(Math.Clamp(options.WebhookTimeoutSeconds, 1, 60)), ct).ConfigureAwait(false);
                status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    error = "HTTP " + status;
                }
            }
            catch (OutboundRefusedException ex)
            {
                error = "refused by the outbound URL policy: " + ex.Reason;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                error = ex is HttpRequestException { InnerException: OutboundRefusedException refused }
                    ? "refused by the outbound URL policy: " + refused.Reason
                    : ex is TaskCanceledException or OperationCanceledException ? "time-out" : Truncate(ex.Message);
            }
        }

        db.Attempts.Add(new WebhookAttempt
        {
            DeliveryRef = delivery.Id, Number = delivery.Attempts, At = now, StatusCode = status, DurationMs = (int)watch.ElapsedMilliseconds, Error = Truncate(error),
        });
        delivery.LastStatusCode = status;
        delivery.LastError = Truncate(error);
        if (error is null)
        {
            delivery.Status = DeliveryStatuses.Delivered;
            delivery.DeliveredAt = now;
            return true;
        }

        if (delivery.Attempts >= Math.Max(1, options.WebhookMaxAttempts))
        {
            delivery.Status = DeliveryStatuses.Dead;
        }
        else
        {
            delivery.Status = DeliveryStatuses.Retrying;
            delivery.NextAttemptAt = now + WebhookSigner.Backoff(delivery.Attempts, options.WebhookRetryBaseSeconds);
        }

        return false;
    }

    private static string? Truncate(string? text) => text is { Length: > 500 } ? text[..500] : text;
}
