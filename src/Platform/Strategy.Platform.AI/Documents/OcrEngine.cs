using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Strategy.Platform.AI.Documents;

/// <summary>Text recognition for scanned pages and images (ADR 0033). Implementations never throw for a bad image: they return null.</summary>
public interface IOcrEngine
{
    /// <summary>True when the engine and every language in <paramref name="languages"/> (e.g. <c>ara+eng</c>) are installed.</summary>
    Task<bool> IsAvailableAsync(string languages, CancellationToken cancellationToken);

    Task<string?> RecognizeAsync(byte[] image, string languages, CancellationToken cancellationToken);
}

/// <summary>
/// Tesseract OCR (Apache-2.0) run as a separate process: <c>tesseract &lt;image&gt; stdout -l ara+eng</c>. Installed in the application
/// image with the Arabic and English language data (deploy/Dockerfile); <c>Ocr:TesseractPath</c> points elsewhere. No native interop: a crash
/// or a hang of the engine only fails that page (time-out <c>Ocr:TimeoutSeconds</c>, default 60).
/// </summary>
public sealed partial class TesseractOcrEngine(IConfiguration configuration, ILogger<TesseractOcrEngine> logger) : IOcrEngine
{
    private readonly string _path = configuration["Ocr:TesseractPath"] is { Length: > 0 } path ? path : "tesseract";
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(configuration.GetValue("Ocr:TimeoutSeconds", 60));
    private IReadOnlySet<string>? _languages;

    public async Task<bool> IsAvailableAsync(string languages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(languages);
        if (_languages is null)
        {
            var output = await RunAsync(["--list-langs"], cancellationToken).ConfigureAwait(false);
            _languages = output is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => !l.Contains(' ', StringComparison.Ordinal))
                    .ToHashSet(StringComparer.Ordinal);
        }

        var wanted = languages.Split('+', StringSplitOptions.RemoveEmptyEntries);
        return wanted.Length > 0 && wanted.All(_languages.Contains);
    }

    public async Task<string?> RecognizeAsync(byte[] image, string languages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var file = Path.Combine(Path.GetTempPath(), "strategy-ocr-" + Guid.CreateVersion7().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(file, image, cancellationToken).ConfigureAwait(false);
            return await RunAsync([file, "stdout", "-l", languages, "--psm", "3"], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private async Task<string?> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["OMP_THREAD_LIMIT"] = "1"; // predictable CPU use per page on a shared server
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        Process? process = null;
        try
        {
            process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                LogFailed(logger, process.ExitCode, Short(await errors.ConfigureAwait(false)));
                return null;
            }

            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, _timeout.TotalSeconds);
            return null;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            LogMissing(logger, _path, exception);
            return null;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            process?.Dispose();
        }
    }

    private static string Short(string text) => text.Length > 300 ? text[..300] : text;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tesseract failed with exit code {ExitCode}: {Errors}")]
    private static partial void LogFailed(ILogger logger, int exitCode, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tesseract did not finish within {Seconds} s")]
    private static partial void LogTimedOut(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tesseract could not be started from {Path}")]
    private static partial void LogMissing(ILogger logger, string path, Exception exception);
}
