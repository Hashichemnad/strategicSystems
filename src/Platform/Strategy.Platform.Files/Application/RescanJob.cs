using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Files.Domain;
using Strategy.Platform.Files.Infrastructure;
using Strategy.Platform.Files.Storage;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Files.Application;

/// <summary>Every 10 minutes: scans versions kept "pending" because ClamAV was unreachable while scanning is required.</summary>
internal sealed class RescanJob(RequestContext requestContext, FilesDbContext db, IFileStore store, IVirusScanner scanner, ISettingsStore settings)
    : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "files.rescan-pending", "إعادة فحص الملفات المعلّقة", "Re-scan pending files", "0 0/10 * * * ?", typeof(RescanJob));

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var options = await settings.GetAsync<FilesSettings>(cancellationToken).ConfigureAwait(false);
        if (!options.ScanEnabled)
        {
            return "scan disabled";
        }

        var pending = await db.Versions.Where(v => v.ScanStatus == ScanStatuses.Pending).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        var clean = 0;
        var infected = 0;
        foreach (var version in pending)
        {
            await using var content = await store.OpenReadAsync(version.StorageKey, cancellationToken).ConfigureAwait(false);
            var outcome = await scanner.ScanAsync(content, options.ClamAvHost, options.ClamAvPort, cancellationToken).ConfigureAwait(false);
            if (outcome == ScanOutcome.Unavailable)
            {
                break;
            }

            version.ScanStatus = outcome == ScanOutcome.Clean ? ScanStatuses.Clean : ScanStatuses.Infected;
            clean += outcome == ScanOutcome.Clean ? 1 : 0;
            infected += outcome == ScanOutcome.Infected ? 1 : 0;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return $"pending={pending.Count} clean={clean} infected={infected}";
    }
}
