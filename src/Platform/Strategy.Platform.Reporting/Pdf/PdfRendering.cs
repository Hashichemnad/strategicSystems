using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Strategy.Platform.Http.Health;

namespace Strategy.Platform.Reporting.Pdf;

/// <summary>Renders an HTML document to PDF. Fonts embedded in this assembly can be referenced by file name in <c>@font-face</c>.</summary>
public interface IPdfRenderer
{
    /// <summary>Throws <see cref="PdfRenderingException"/> when the renderer is not configured or fails.</summary>
    Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken);
}

public sealed class PdfRenderingException(string messageKey, Exception? inner = null) : Exception(messageKey, inner)
{
    public string MessageKey { get; } = messageKey;
}

/// <summary>Options: <c>Gotenberg:Url</c> (e.g. <c>http://gotenberg:3000</c>); empty disables PDF output.</summary>
public sealed class GotenbergOptions
{
    public string? Url { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// <c>POST /forms/chromium/convert/html</c>: <c>index.html</c> plus the font files, A4, backgrounds printed. Chromium shapes Arabic
/// (joining, ligatures, bidi) itself, which is why PDFs go through it rather than a .NET PDF library.
/// </summary>
internal sealed partial class GotenbergPdfRenderer(HttpClient http, GotenbergOptions options, ILogger<GotenbergPdfRenderer>? logger = null)
    : IPdfRenderer
{
    /// <summary>
    /// Back-off before the second and third attempt. Gotenberg answers 503 while it restarts Chromium (by default after every 10 conversions)
    /// or when a conversion waits too long under concurrent load; such failures pass within seconds, so a business PDF is retried twice
    /// instead of failing the user's export.
    /// </summary>
    internal static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new PdfRenderingException("reporting.errors.pdfNotConfigured");
        }

        var endpoint = new Uri(new Uri(options.Url.TrimEnd('/') + "/"), "forms/chromium/convert/html");
        for (var attempt = 0; ; attempt++)
        {
            var last = attempt >= RetryDelays.Length;
            try
            {
                using var form = Form(html);
                using var response = await http.PostAsync(endpoint, form, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }

                var status = (int)response.StatusCode;
                LogFailed(_logger, status, attempt + 1, null);
                if (last || !IsTransient(status))
                {
                    throw new PdfRenderingException("reporting.errors.pdfFailed");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                LogFailed(_logger, 0, attempt + 1, exception);
                if (last)
                {
                    throw new PdfRenderingException("reporting.errors.pdfFailed", exception);
                }
            }

            await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Busy, restarting or overloaded Gotenberg (or a proxy in front of it); other errors (e.g. 400 for invalid HTML) are final.</summary>
    internal static bool IsTransient(int status) => status is 429 or 502 or 503 or 504;

    private static MultipartFormDataContent Form(string html)
    {
        var form = new MultipartFormDataContent();
        var index = new StringContent(html, System.Text.Encoding.UTF8);
        index.Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        form.Add(index, "files", "index.html");
        foreach (var (name, bytes) in PdfFonts.All)
        {
            var font = new ByteArrayContent(bytes);
            font.Headers.ContentType = new MediaTypeHeaderValue("font/woff2");
            form.Add(font, "files", name);
        }

        form.Add(new StringContent("true"), "printBackground");
        form.Add(new StringContent("true"), "preferCssPageSize");
        form.Add(new StringContent("8.27"), "paperWidth");
        form.Add(new StringContent("11.7"), "paperHeight");
        return form;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "PDF rendering by Gotenberg failed (HTTP status {Status}; 0 = no response), attempt {Attempt}")]
    private static partial void LogFailed(ILogger logger, int status, int attempt, Exception? exception);
}

/// <summary>The embedded fonts (IBM Plex Sans Arabic, OFL): file name → bytes, and the matching <c>@font-face</c> rules.</summary>
public static class PdfFonts
{
    public const string Family = "IBM Plex Sans Arabic";

    private static readonly Lazy<IReadOnlyList<(string Name, byte[] Bytes)>> Fonts = new(Load);

    public static IReadOnlyList<(string Name, byte[] Bytes)> All => Fonts.Value;

    private const string ArabicRange = "U+0600-06FF,U+0750-077F,U+0870-088E,U+0890-0891,U+0897-08E1,U+08E3-08FF,U+200C-200E,U+2010-2011,U+204F,U+2E41,U+FB50-FDFF,U+FE70-FE74,U+FE76-FEFC";
    private const string LatinRange = "U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD";

    /// <summary>CSS that loads the fonts from the files sent next to <c>index.html</c>.</summary>
    public static string FontFaceCss()
    {
        var css = new System.Text.StringBuilder();
        foreach (var weight in new[] { 400, 700 })
        {
            foreach (var (subset, range) in new[] { ("arabic", ArabicRange), ("latin", LatinRange) })
            {
                css.Append("@font-face{font-family:'").Append(Family).Append("';font-style:normal;font-weight:").Append(weight)
                    .Append(";src:url('ibm-plex-sans-arabic-").Append(subset).Append('-').Append(weight).Append("-normal.woff2') format('woff2');unicode-range:")
                    .Append(range).Append(";}");
            }
        }

        return css.ToString();
    }

    private static List<(string, byte[])> Load()
    {
        var assembly = typeof(PdfFonts).Assembly;
        return assembly.GetManifestResourceNames().Where(n => n.EndsWith(".woff2", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return (n[(n.IndexOf("ibm-plex", StringComparison.Ordinal))..], memory.ToArray());
            }).ToList();
    }
}

/// <summary>Readiness of the PDF renderer; tagged <c>optional</c> so an install without PDFs stays ready.</summary>
internal sealed class GotenbergHealthCheck(HttpClient http, GotenbergOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            return HealthCheckResult.Healthy("PDF rendering not configured");
        }

        try
        {
            using var response = await http.GetAsync(new Uri(new Uri(options.Url.TrimEnd('/') + "/"), "health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"Gotenberg answered {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Degraded("Gotenberg unreachable", exception);
        }
    }
}

/// <summary>Health page (ADR 0151): Gotenberg answers <c>/health</c> (round trip in ms), or PDF output is not configured.</summary>
internal sealed class GotenbergAdminHealthContributor(IHttpClientFactory clients, GotenbergOptions options) : IAdminHealthContributor
{
    public const string ClientName = "gotenberg-health";

    public string Code => "pdf";

    public string Group => AdminHealthGroups.Integrations;

    public int Order => 25;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            return [new AdminHealthCheck(Code, Group, AdminHealthStatuses.NotConfigured, "health.pdf.notConfigured")];
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var http = clients.CreateClient(ClientName);
            using var response = await http.GetAsync(new Uri(new Uri(options.Url.TrimEnd('/') + "/"), "health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Ok, "health.pdf.ok", HealthParams.Of(("latencyMs", watch.ElapsedMilliseconds)))]
                : [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Error, "health.pdf.badStatus", HealthParams.Of(("status", (int)response.StatusCode)))];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Error, "health.pdf.unreachable", HealthParams.Of(("error", exception.GetType().Name)))];
        }
    }
}

public static class ReportingServiceCollectionExtensions
{
    public const string OptionalTag = "optional";

    public static IServiceCollection AddPlatformReporting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Gotenberg").Get<GotenbergOptions>() ?? new GotenbergOptions();
        services.AddSingleton(options);
        services.AddHttpClient<IPdfRenderer, GotenbergPdfRenderer>(c => c.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds)));
        services.AddHttpClient<GotenbergHealthCheck>(c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddHealthChecks().AddCheck<GotenbergHealthCheck>("gotenberg", tags: [OptionalTag]);
        services.AddHttpClient(GotenbergAdminHealthContributor.ClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddAdminHealthContributor<GotenbergAdminHealthContributor>();
        return services;
    }
}
