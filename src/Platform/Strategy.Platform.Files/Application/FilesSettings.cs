using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Files.Application;

/// <summary>Settings → Files: global upload limit and the optional ClamAV scan (SPEC §0.2 "size / type limits, virus-scan hook").</summary>
public sealed record FilesSettings(
    int MaxUploadMegabytes = 25,
    bool ScanEnabled = false,
    bool ScanRequired = false,
    string ClamAvHost = "clamav",
    int ClamAvPort = 3310) : ISettingsArea
{
    public static string Key => "files";

    public static List<FieldError> Validate(FilesSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = new List<FieldError>();
        if (value.MaxUploadMegabytes is < 1 or > 500)
        {
            errors.Add(new FieldError("maxUploadMegabytes", "validation.range", "validation.range", new Dictionary<string, object?> { ["min"] = 1, ["max"] = 500 }));
        }

        if (value.ScanEnabled && string.IsNullOrWhiteSpace(value.ClamAvHost))
        {
            errors.Add(new FieldError("clamAvHost", "validation.required", "validation.required"));
        }

        if (value.ClamAvPort is < 1 or > 65535)
        {
            errors.Add(new FieldError("clamAvPort", "validation.range", "validation.range", new Dictionary<string, object?> { ["min"] = 1, ["max"] = 65535 }));
        }

        if (value.ScanRequired && !value.ScanEnabled)
        {
            errors.Add(new FieldError("scanRequired", "files.errors.scanRequiresEnabled", "files.errors.scanRequiresEnabled"));
        }

        return errors;
    }
}
