using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Persistence;
using Strategy.Platform.Scheduling;

namespace Strategy.Platform.Integration.Sync;

/// <summary>Runs due and requested syncs (§4). Every minute; one run at a time per connection (advisory lock in the engine).</summary>
internal sealed class SalesforceSyncJob(RequestContext requestContext, SyncScheduler scheduler) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "integration.salesforce-sync", "مزامنة Salesforce", "Salesforce sync", "0 * * * * ?", typeof(SalesforceSyncJob),
        "ينسخ بيانات Salesforce إلى هذا النظام حسب الجدول المحدد (اتجاه واحد).", "Copies Salesforce data into this system on the configured schedule (one way).");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"runs={await scheduler.RunDueAsync(cancellationToken).ConfigureAwait(false)}";
}

/// <summary>Late-sync alerts and run-log retention (§G7).</summary>
internal sealed class SyncMonitorJob(RequestContext requestContext, SyncScheduler scheduler) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "integration.sync-monitor", "مراقبة المزامنة", "Sync monitor", "0 0/15 * * * ?", typeof(SyncMonitorJob),
        "ينبّه المسؤولين عند تأخر المزامنة ويحذف سجلات التشغيل القديمة.", "Alerts administrators when the sync is late and removes old run logs.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"alerts={await scheduler.MonitorAsync(cancellationToken).ConfigureAwait(false)}";
}

/// <summary>Picks due connections, runs them, schedules the next run and alerts on failure or delay.</summary>
public sealed class SyncScheduler(IntegrationDbContext db, SyncEngine engine, SyncAdministration administration, SyncAlerts alerts, IClock clock)
{
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.SyncConnections.AsNoTracking()
            .Where(c => (c.Mode == SyncModes.Standby || c.Mode == SyncModes.Reconciling) && (c.RunRequestedAt != null || (c.NextRunAt != null && c.NextRunAt <= now)))
            .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        var runs = 0;
        foreach (var id in due)
        {
            if (await RunOneAsync(id, ct).ConfigureAwait(false) is not null)
            {
                runs++;
            }
        }

        return runs;
    }

    /// <summary>Runs a connection now (requested or due) and schedules the next run. Null when another run holds the lock.</summary>
    public async Task<SyncRun?> RunOneAsync(Guid connectionId, CancellationToken ct)
    {
        var connection = await db.SyncConnections.FirstAsync(c => c.Id == connectionId, ct).ConfigureAwait(false);
        var requested = connection.RunRequestedAt is not null;
        var full = requested && connection.RunRequestedFull;
        var objects = requested && connection.RunRequestedObjects is { } json ? SyncJson.Read<List<string>>(json) : null;
        var by = requested ? connection.RunRequestedBy : null;
        connection.RunRequestedAt = null;
        connection.RunRequestedObjects = null;
        connection.RunRequestedFull = false;
        if (!requested || connection.NextRunAt is null || connection.NextRunAt <= clock.UtcNow)
        {
            connection.NextRunAt = await administration.NextRunAsync(connection, clock.UtcNow, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var run = await engine.RunAsync(connectionId, requested ? (full ? RunTriggers.Full : RunTriggers.Manual) : RunTriggers.Schedule, full, objects, by, ct)
            .ConfigureAwait(false);
        if (run?.Status == RunStatuses.Failed)
        {
            await alerts.FailedAsync(connection, run, ct).ConfigureAwait(false);
        }

        return run;
    }

    public async Task<int> MonitorAsync(CancellationToken ct)
    {
        var sent = 0;
        var now = clock.UtcNow;
        foreach (var connection in await db.SyncConnections.Where(c => c.Mode == SyncModes.Standby || c.Mode == SyncModes.Reconciling).ToListAsync(ct).ConfigureAwait(false))
        {
            var lastSuccess = await db.SyncRuns.AsNoTracking()
                .Where(r => r.ConnectionId == connection.Id && (r.Status == RunStatuses.Succeeded || r.Status == RunStatuses.Partial))
                .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, ct).ConfigureAwait(false) ?? connection.UpdatedAt;
            if (now - lastSuccess > TimeSpan.FromMinutes(SyncAdministration.LateMinutes(connection)) && connection.LateAlertSentAt is null)
            {
                await alerts.LateAsync(connection, lastSuccess, ct).ConfigureAwait(false);
                connection.LateAlertSentAt = now;
                sent++;
            }
        }

        var retentionBefore = now.AddDays(-180);
        var oldRuns = db.SyncRuns.Where(r => r.StartedAt < retentionBefore).Select(r => r.Id);
        await db.SyncRunLog.Where(l => oldRuns.Contains(l.RunId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SyncRuns.Where(r => r.StartedAt < retentionBefore).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sent;
    }
}

/// <summary>Bilingual e-mail / in-app alerts to the holders of <c>integration.sync.manage</c> (§G7).</summary>
public sealed class SyncAlerts(INotificationService notifications, IUserRoleQuery access, IntegrationDbContext db)
{
    public static readonly NotificationTemplateDefinition Failed = new(
        "integration.sync.failed", IntegrationPermissions.SystemCode, "فشل مزامنة Salesforce", "Salesforce sync failed",
        "فشلت مزامنة {{source}}", "The {{source}} sync failed",
        "مرحباً {{name}}،\nفشلت مزامنة {{source}} التي بدأت في {{at}}: {{reason}}.\n{{button}}",
        "Hello {{name}},\nThe {{source}} sync that started at {{at}} failed: {{reason}}.\n{{button}}", "فتح حالة المزامنة", "Open sync status",
        ["name", "source", "at", "reason"]);

    public static readonly NotificationTemplateDefinition Late = new(
        "integration.sync.late", IntegrationPermissions.SystemCode, "تأخر مزامنة Salesforce", "Salesforce sync late",
        "تأخرت مزامنة {{source}}", "The {{source}} sync is late",
        "مرحباً {{name}}،\nلم تنجح مزامنة {{source}} منذ {{at}}. تحقق من الاتصال وحالة Salesforce.\n{{button}}",
        "Hello {{name}},\nThe {{source}} sync has not succeeded since {{at}}. Check the connection and Salesforce.\n{{button}}", "فتح حالة المزامنة", "Open sync status",
        ["name", "source", "at"]);

    public const string Link = "/settings/integrations/salesforce-sync";

    public Task FailedAsync(SyncConnection connection, SyncRun run, CancellationToken ct) =>
        SendAsync(Failed.Key, connection, new Dictionary<string, string>
        {
            ["at"] = run.StartedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            ["reason"] = run.MessageKey ?? "-",
        }, $"sync-failed:{run.Id}", ct);

    public Task LateAsync(SyncConnection connection, DateTimeOffset lastSuccess, CancellationToken ct) =>
        SendAsync(Late.Key, connection, new Dictionary<string, string> { ["at"] = lastSuccess.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) },
            $"sync-late:{connection.Id}:{lastSuccess.ToUnixTimeSeconds()}", ct);

    private async Task SendAsync(string template, SyncConnection connection, Dictionary<string, string> values, string key, CancellationToken ct)
    {
        var users = await access.UsersWithPermissionAsync(IntegrationPermissions.SyncManage, ct).ConfigureAwait(false);
        if (users.Count == 0)
        {
            return;
        }

        var source = await db.SourceSystems.AsNoTracking().Where(s => s.Code == connection.SourceSystem).Select(s => s.NameEn ?? s.NameAr).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        values["source"] = source ?? connection.SourceSystem;
        await notifications.SendAsync(new NotificationRequest(template, [.. users.Select(NotificationRecipient.User)], values, Link, IdempotencyKey: key), null, ct)
            .ConfigureAwait(false);
    }
}

/// <summary>Health page (Phase 18): last run, lateness, exceptions, failover mode (§G7).</summary>
internal sealed class SyncHealthContributor(IntegrationDbContext db, IClock clock) : IAdminHealthContributor
{
    public string Code => "salesforceSync";

    public string Group => AdminHealthGroups.Integrations;

    public int Order => 60;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<AdminHealthCheck>();
        foreach (var c in await db.SyncConnections.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (c.Mode == SyncModes.Disabled)
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.NotConfigured, "health.salesforceSync.disabled", HealthParams.Of(("source", c.SourceSystem))));
                continue;
            }

            if (c.Mode == SyncModes.Primary)
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.Warning, "health.salesforceSync.primary", HealthParams.Of(("source", c.SourceSystem))));
                continue;
            }

            var runs = db.SyncRuns.AsNoTracking().Where(r => r.ConnectionId == c.Id && r.Status != RunStatuses.Running);
            var last = await runs.OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var lastSuccess = await runs.Where(r => r.Status == RunStatuses.Succeeded || r.Status == RunStatuses.Partial)
                .MaxAsync(r => (DateTimeOffset?)r.FinishedAt, cancellationToken).ConfigureAwait(false);
            var exceptions = await db.SyncExceptions.CountAsync(e => e.ConnectionId == c.Id && e.Status == ExceptionStatuses.Open, cancellationToken).ConfigureAwait(false);
            var parameters = HealthParams.Of(("source", c.SourceSystem), ("at", lastSuccess), ("exceptions", exceptions), ("reason", last?.MessageKey));
            if (last?.Status == RunStatuses.Failed)
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.Error, "health.salesforceSync.failed", parameters));
            }
            else if (lastSuccess is null || clock.UtcNow - lastSuccess > TimeSpan.FromMinutes(SyncAdministration.LateMinutes(c)))
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.Warning, "health.salesforceSync.late", parameters));
            }
            else if (exceptions > 0)
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.Warning, "health.salesforceSync.exceptions", parameters));
            }
            else
            {
                checks.Add(new AdminHealthCheck(Code, Group, AdminHealthStatuses.Ok, "health.salesforceSync.ok", parameters));
            }
        }

        return checks;
    }
}

/// <summary>
/// Seeds the Salesforce source system (code <c>salesforce</c>, editable), the "ADoF Salesforce" mapping pack (embedded, imported once) and
/// a disabled connection that uses it. Never overwrites an administrator's changes.
/// </summary>
internal sealed partial class SyncSeeder : IDatabaseSeeder
{
    public const string PackResourcePrefix = "sync-pack/adof-salesforce/";
    public const string PackCode = "adof-salesforce";

    public int Order => 80;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IntegrationDbContext>();
        var source = await db.SourceSystems.FirstOrDefaultAsync(s => s.Code == SyncAdministration.SalesforceSource, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            db.SourceSystems.Add(SourceSystem.Create(SyncAdministration.SalesforceSource, "سيلزفورس", "Salesforce", "salesforce", null));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var installed = await db.MappingPacks.AsNoTracking().Where(p => p.Code == PackCode).Select(p => (int?)p.PackVersion).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (installed is { } version)
        {
            var shipped = await LoadPackAsync(cancellationToken).ConfigureAwait(false);
            if (version < shipped.Version && await LoadPreviousAsync(version, cancellationToken).ConfigureAwait(false) is { } previous)
            {
                var (replaced, kept) = await services.GetRequiredService<SyncAdministration>().UpgradeShippedPackAsync(shipped, previous, cancellationToken).ConfigureAwait(false);
                LogPackUpgraded(services.GetRequiredService<ILogger<SyncSeeder>>(), PackCode, version, shipped.Version, string.Join(", ", replaced), string.Join(", ", kept));
            }
        }
        else
        {
            var document = await LoadPackAsync(cancellationToken).ConfigureAwait(false);
            var imported = await services.GetRequiredService<SyncAdministration>().ImportPackAsync(document, cancellationToken, audited: false).ConfigureAwait(false);
            if (imported.IsFailure)
            {
                throw new InvalidOperationException("The ADoF Salesforce mapping pack is invalid: " + string.Join(", ", imported.Error!.Fields?.Select(f => f.Field + " " + f.MessageKey) ?? []));
            }
        }

        if (!await db.SyncConnections.AnyAsync(c => c.SourceSystem == SyncAdministration.SalesforceSource, cancellationToken).ConfigureAwait(false))
        {
            var pack = await db.MappingPacks.FirstAsync(p => p.Code == PackCode, cancellationToken).ConfigureAwait(false);
            db.SyncConnections.Add(new SyncConnection
            {
                SourceSystem = SyncAdministration.SalesforceSource,
                MappingPackId = pack.Id,
                Schedule = SyncJson.Write(new SyncScheduleSettings()),
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The objects an older pack version shipped that the current version changes (<c>Packs/upgrades/adof-salesforce-v{n}.json</c>, ADR 0213).</summary>
    public static async Task<MappingPackDocument?> LoadPreviousAsync(int version, CancellationToken cancellationToken)
    {
        await using var stream = typeof(SyncSeeder).Assembly.GetManifestResourceStream($"sync-pack-upgrade/{PackCode}-v{version}.json");
        return stream is null ? null : await System.Text.Json.JsonSerializer.DeserializeAsync<MappingPackDocument>(stream, SyncJson.Options, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Mapping pack {PackCode} upgraded from version {From} to {To}: replaced (validate again) [{Replaced}], kept because changed by an administrator (re-import the shipped pack to take the fixes) [{Kept}]")]
    private static partial void LogPackUpgraded(ILogger logger, string packCode, int from, int to, string replaced, string kept);

    /// <summary>The embedded ADoF pack: <c>pack.json</c> (code, names, version) plus every other file's <c>objects</c>, in file-name order.</summary>
    public static async Task<MappingPackDocument> LoadPackAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(SyncSeeder).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(PackResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        MappingPackDocument? header = null;
        var objects = new List<MappingPackObject>();
        foreach (var name in names)
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            var part = await System.Text.Json.JsonSerializer.DeserializeAsync<MappingPackDocument>(stream, SyncJson.Options, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Empty mapping pack part {name}.");
            if (name.EndsWith("/pack.json", StringComparison.Ordinal))
            {
                header = part;
            }

            objects.AddRange(part.Objects);
        }

        return (header ?? throw new InvalidOperationException("The ADoF Salesforce mapping pack header is missing.")) with { Objects = objects };
    }
}

/// <summary>The mapping packs shipped with the product (embedded resources, ADR 0203).</summary>
public static class ShippedMappingPacks
{
    /// <summary>The "ADoF Salesforce" pack as shipped (before any administrator change).</summary>
    public static Task<MappingPackDocument> AdofSalesforceAsync(CancellationToken cancellationToken = default) => SyncSeeder.LoadPackAsync(cancellationToken);

    /// <summary>The objects pack <paramref name="version"/> shipped that the current pack changes (upgrade snapshot, ADR 0213); null when none.</summary>
    public static Task<MappingPackDocument?> AdofSalesforcePreviousAsync(int version, CancellationToken cancellationToken = default) =>
        SyncSeeder.LoadPreviousAsync(version, cancellationToken);
}
