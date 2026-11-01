using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
internal sealed class GotenbergPdfRenderer(HttpClient http, GotenbergOptions options) : IPdfRenderer
{
    public async Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new PdfRenderingException("reporting.errors.pdfNotConfigured");
        }

        using var form = new MultipartFormDataContent();
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

        try
        {
            using var response = await http.PostAsync(new Uri(new Uri(options.Url.TrimEnd('/') + "/"), "forms/chromium/convert/html"), form, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PdfRenderingException("reporting.errors.pdfFailed");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new PdfRenderingException("reporting.errors.pdfFailed", exception);
        }
    }
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
        return services;
    }
}
