using Microsoft.EntityFrameworkCore;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Infrastructure;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.AI.Application;

/// <summary>
/// Health page (ADR 0151): AI switched on, default provider present and active, calls and failures in the last 24 hours (from the usage log,
/// no provider call — *Test connection* in Settings → AI does that); OCR engine and its languages when text recognition is on.
/// </summary>
internal sealed class AiHealthContributor(ISettingsStore settings, AiDbContext db, IOcrEngine ocr, IClock clock) : IAdminHealthContributor
{
    public string Code => "ai";

    public string Group => AdminHealthGroups.Integrations;

    public int Order => 30;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var ai = await settings.GetAsync<AiSettings>(cancellationToken).ConfigureAwait(false);
        return [await ProviderAsync(ai, cancellationToken).ConfigureAwait(false), await OcrAsync(ai, cancellationToken).ConfigureAwait(false)];
    }

    private async Task<AdminHealthCheck> ProviderAsync(AiSettings ai, CancellationToken cancellationToken)
    {
        if (!ai.Enabled)
        {
            return new AdminHealthCheck("ai", Group, AdminHealthStatuses.NotConfigured, "health.ai.off");
        }

        var provider = ai.DefaultProviderId is { } id
            ? await db.Providers.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.Name, p.Type, p.Active, p.DefaultModel })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (provider is null || !provider.Active)
        {
            return new AdminHealthCheck("ai", Group, AdminHealthStatuses.Error, "health.ai.noProvider");
        }

        var since = clock.UtcNow.AddHours(-24);
        var calls = await db.Usage.AsNoTracking().Where(u => u.OccurredAt >= since)
            .GroupBy(u => u.Success).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);
        var last = await db.Usage.AsNoTracking().OrderByDescending(u => u.OccurredAt).Select(u => new { u.OccurredAt, u.Success, u.ErrorCode })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var failed = calls.GetValueOrDefault(false);
        var ok = calls.GetValueOrDefault(true);
        var (status, key) = last is { Success: false } ? (AdminHealthStatuses.Warning, "health.ai.lastFailed")
            : failed > 0 ? (AdminHealthStatuses.Warning, "health.ai.someFailed")
            : (AdminHealthStatuses.Ok, "health.ai.ok");
        return new AdminHealthCheck("ai", Group, status, key, HealthParams.Of(
            ("provider", provider.Name), ("type", provider.Type), ("model", provider.DefaultModel), ("calls24h", ok + failed), ("failed24h", failed),
            ("lastAt", last?.OccurredAt), ("lastError", last?.ErrorCode)));
    }

    private async Task<AdminHealthCheck> OcrAsync(AiSettings ai, CancellationToken cancellationToken)
    {
        var languages = ai.Recognition.Languages;
        var available = await ocr.IsAvailableAsync(languages, cancellationToken).ConfigureAwait(false);
        var parameters = HealthParams.Of(("languages", languages), ("enabled", ai.Recognition.Enabled));
        return !ai.Recognition.Enabled
            ? new AdminHealthCheck("ocr", Group, AdminHealthStatuses.NotConfigured,
                available ? "health.ocr.offInstalled" : "health.ocr.offMissing", parameters)
            : new AdminHealthCheck("ocr", Group, available ? AdminHealthStatuses.Ok : AdminHealthStatuses.Error,
                available ? "health.ocr.ok" : "health.ocr.missing", parameters);
    }
}
