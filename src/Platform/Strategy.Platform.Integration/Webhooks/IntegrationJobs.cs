using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Integration.Clients;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Feeds;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Webhooks;

/// <summary>Sends due webhook deliveries (ADR 0194).</summary>
internal sealed class WebhookDispatchJob(RequestContext requestContext, WebhookDispatcher dispatcher) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "integration.webhooks", "إرسال إشعارات Webhook", "Webhook deliveries", "0 * * * * ?", typeof(WebhookDispatchJob),
        "يرسل أحداث Webhook الموقعة إلى الأنظمة المشتركة ويعيد المحاولة عند الفشل.", "Sends signed webhook events to subscribed systems and retries failures.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (delivered, failed) = await dispatcher.RunDueAsync(cancellationToken).ConfigureAwait(false);
        return $"delivered={delivered} failed={failed}";
    }
}

/// <summary>Runs due scheduled exports as their client (ADR 0193).</summary>
internal sealed class ExportJob(RequestContext requestContext, ScheduledExports exports) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "integration.export", "التصدير المجدول للتكامل", "Scheduled integration exports", "0 0/15 * * * ?", typeof(ExportJob),
        "يكتب التغييرات منذ آخر تشغيل إلى مجلد التصدير لكل جهة تكامل.", "Writes the changes since the last run to each client's export folder.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"runs={await exports.RunDueAsync(cancellationToken).ConfigureAwait(false)}";
}

/// <summary>Usage-log and delivery retention (docs/design/integrations.md §6, §2.1).</summary>
internal sealed class IntegrationMaintenanceJob(RequestContext requestContext, IntegrationDbContext db, ISettingsStore settings, IClock clock) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "integration.maintenance", "صيانة سجلات التكامل", "Integration log retention", "0 40 2 * * ?", typeof(IntegrationMaintenanceJob),
        "يحذف سجلات الاستخدام والتسليم الأقدم من مدة الاحتفاظ.", "Removes usage and delivery records older than their retention.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var options = await settings.GetAsync<IntegrationSettings>(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var usageBefore = now.AddDays(-Math.Max(1, options.UsageRetentionDays));
        var usage = await db.Usage.Where(u => u.At < usageBefore).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var deliveredBefore = now.AddDays(-30);
        var deadBefore = now.AddDays(-90);
        var old = db.Deliveries.Where(d => (d.Status == DeliveryStatuses.Delivered && d.CreatedAt < deliveredBefore) || (d.Status == DeliveryStatuses.Dead && d.CreatedAt < deadBefore));
        await db.Attempts.Where(a => old.Select(d => d.Id).Contains(a.DeliveryRef)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        var deliveries = await old.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return $"usage={usage} deliveries={deliveries}";
    }
}
