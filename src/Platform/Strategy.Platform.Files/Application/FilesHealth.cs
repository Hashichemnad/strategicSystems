using Microsoft.Extensions.Configuration;
using Strategy.Platform.Files.Storage;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Files.Application;

/// <summary>
/// Health page (ADR 0151): the file store accepts a write (probe object under <c>.health/</c>), free disk space of the files volume and of the
/// temporary directory (warning below <c>Health:DiskWarningPercent</c> (15 %) or 2 GB, error below 5 % or 500 MB), and the virus scanner
/// answers when scanning is on.
/// </summary>
internal sealed class FilesHealthContributor(
    FileStorageOptions options, IFileStore store, IVirusScanner scanner, ISettingsStore settings, IConfiguration configuration) : IAdminHealthContributor
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    public string Code => "storage";

    public string Group => AdminHealthGroups.Storage;

    public int Order => 40;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new List<AdminHealthCheck> { await StorageAsync(cancellationToken).ConfigureAwait(false) };
        var local = string.Equals(options.Provider, "local", StringComparison.OrdinalIgnoreCase);
        var paths = new List<(string Code, string Path)>();
        if (local)
        {
            paths.Add(("files", Path.GetFullPath(options.LocalPath)));
        }

        paths.Add(("temp", Path.GetTempPath()));
        var warningPercent = configuration.GetValue("Health:DiskWarningPercent", 15d);
        var items = paths.Select(p => Disk(p.Code, p.Path, warningPercent)).ToList();
        checks.Add(new AdminHealthCheck("disk", Group, AdminHealthStatuses.Worst(items.Select(i => i.Status)),
            items.Any(i => i.Status != AdminHealthStatuses.Ok) ? "health.disk.low" : "health.disk.ok", null, items));
        checks.Add(await AntivirusAsync(cancellationToken).ConfigureAwait(false));
        return checks;
    }

    private async Task<AdminHealthCheck> StorageAsync(CancellationToken cancellationToken)
    {
        var parameters = HealthParams.Of(("provider", options.Provider), ("bucket", options.S3Bucket));
        try
        {
            using var probe = new MemoryStream("ok"u8.ToArray());
            await store.WriteAsync($".health/{Guid.CreateVersion7():N}", probe, cancellationToken).ConfigureAwait(false);
            if (string.Equals(options.Provider, "local", StringComparison.OrdinalIgnoreCase))
            {
                // Local probes are removed; object stores keep the two-byte probe (the store never deletes, see IFileStore).
                foreach (var file in Directory.EnumerateFiles(Path.Combine(Path.GetFullPath(options.LocalPath), ".health")))
                {
                    File.Delete(file);
                }
            }

            return new AdminHealthCheck("storage", Group, AdminHealthStatuses.Ok, "health.storage.ok", parameters);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AdminHealthCheck("storage", Group, AdminHealthStatuses.Error, "health.storage.notWritable",
                HealthParams.Of(("provider", options.Provider), ("error", exception.GetType().Name)));
        }
    }

    /// <summary>Free space of the drive holding <paramref name="path"/>; status by percentage and absolute thresholds.</summary>
    public static AdminHealthItem Disk(string code, string path, double warningPercent)
    {
        try
        {
            var drive = new DriveInfo(DriveName(path));
            var free = drive.AvailableFreeSpace;
            var total = drive.TotalSize;
            return new AdminHealthItem(code, DiskStatus(free, total, warningPercent), "health.disk.item",
                HealthParams.Of(("freeBytes", free), ("totalBytes", total), ("path", path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new AdminHealthItem(code, AdminHealthStatuses.Warning, "health.disk.unknown", HealthParams.Of(("path", path)));
        }
    }

    public static string DiskStatus(long free, long total, double warningPercent)
    {
        var percent = total > 0 ? free * 100d / total : 0;
        return percent < 5 || free < Gigabyte / 2 ? AdminHealthStatuses.Error
            : percent < warningPercent || free < 2 * Gigabyte ? AdminHealthStatuses.Warning
            : AdminHealthStatuses.Ok;
    }

    private static string DriveName(string path)
    {
        var current = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return Path.GetPathRoot(current) ?? current;
        }

        // On Linux DriveInfo reads statvfs of the name: the deepest existing parent of the path is on the same file system.
        while (!Directory.Exists(current) && Path.GetDirectoryName(current) is { } parent)
        {
            current = parent;
        }

        return current;
    }

    private async Task<AdminHealthCheck> AntivirusAsync(CancellationToken cancellationToken)
    {
        var files = await settings.GetAsync<FilesSettings>(cancellationToken).ConfigureAwait(false);
        if (!files.ScanEnabled)
        {
            return new AdminHealthCheck("antivirus", Group, AdminHealthStatuses.NotConfigured, "health.antivirus.off");
        }

        using var empty = new MemoryStream();
        var outcome = await scanner.ScanAsync(empty, files.ClamAvHost, files.ClamAvPort, cancellationToken).ConfigureAwait(false);
        var parameters = HealthParams.Of(("host", files.ClamAvHost), ("port", files.ClamAvPort), ("required", files.ScanRequired));
        return outcome == ScanOutcome.Clean
            ? new AdminHealthCheck("antivirus", Group, AdminHealthStatuses.Ok, "health.antivirus.ok", parameters)
            : new AdminHealthCheck("antivirus", Group, files.ScanRequired ? AdminHealthStatuses.Error : AdminHealthStatuses.Warning,
                "health.antivirus.unavailable", parameters);
    }
}
