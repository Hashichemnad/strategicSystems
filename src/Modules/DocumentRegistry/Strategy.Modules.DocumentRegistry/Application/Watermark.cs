using System.Net;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Reporting.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Strategy.Modules.DocumentRegistry.Application;

/// <summary>
/// "Uncontrolled when printed" stamp on downloaded PDFs (ADR 0085): the bilingual stamp is rendered by Gotenberg (Arabic shaping, embedded fonts) and
/// copied onto every page with PdfPig. The stored file is never changed; when the stamp cannot be produced the original is returned (null).
/// </summary>
internal sealed partial class Watermark(IPdfRenderer renderer, ILogger<Watermark> logger)
{
    public async Task<byte[]?> StampAsync(byte[] pdf, string textAr, string textEn, string reference, CancellationToken ct)
    {
        try
        {
            var html = $$"""
                <!doctype html><html><head><meta charset="utf-8"/><style>
                @page { size: A4; margin: 0 } html, body { margin: 0; background: transparent }
                .stamp { position: fixed; bottom: 8mm; left: 0; right: 0; text-align: center; font-family: 'IBM Plex Sans Arabic', sans-serif;
                  font-size: 10pt; color: #b91c1c; letter-spacing: .5px }
                </style></head><body><div class="stamp"><span dir="rtl">{{WebUtility.HtmlEncode(textAr)}}</span> — <span dir="ltr">{{WebUtility.HtmlEncode(textEn)}}</span>
                · {{WebUtility.HtmlEncode(reference)}}</div></body></html>
                """;
            var stamp = await renderer.RenderAsync(html, ct).ConfigureAwait(false);
            using var original = PdfDocument.Open(pdf);
            using var stampDocument = PdfDocument.Open(stamp);
            var stampPage = stampDocument.GetPage(1);
            var builder = new PdfDocumentBuilder();
            for (var i = 1; i <= original.NumberOfPages; i++)
            {
                var page = builder.AddPage(original, i);
                page.CopyFrom(stampPage);
            }

            return builder.Build();
        }
#pragma warning disable CA1031 // The download must never fail because of the stamp (ADR 0085).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogStampFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The uncontrolled-copy stamp could not be applied; the original file is served.")]
    private static partial void LogStampFailed(ILogger logger, Exception exception);
}
