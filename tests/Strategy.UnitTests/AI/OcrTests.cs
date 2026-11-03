using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Features;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Strategy.UnitTests.AI;

/// <summary>
/// Text recognition of scans with the real Tesseract engine and its Arabic and English data (ADR 0033): CI installs
/// <c>tesseract-ocr tesseract-ocr-ara tesseract-ocr-eng</c>, as the application image does. The samples are rendered pages degraded like
/// office scans (grey, rotated, noise, JPEG).
/// </summary>
public sealed class OcrTests
{
    private static readonly TesseractOcrEngine Engine = new(new ConfigurationBuilder().Build(), NullLogger<TesseractOcrEngine>.Instance);
    private static readonly OcrOptions Ocr = new(Engine, "ara+eng", 20);

    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "AI", "Samples", name));

    [Fact]
    public async Task Tesseract_and_the_arabic_and_english_data_are_installed()
    {
        (await Engine.IsAvailableAsync("ara+eng", CancellationToken.None)).ShouldBeTrue("install tesseract-ocr, tesseract-ocr-ara and tesseract-ocr-eng");
        (await Engine.IsAvailableAsync("ara+xyz", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_scanned_arabic_page_is_read_in_reading_order()
    {
        var result = await DocumentTextExtractor.ExtractAsync(Sample("scan-ar.jpg"), "scan.jpg", Ocr, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Kind.ShouldBe("image");
        result.Value.ScannedPages.ShouldBe(1);
        result.Value.Text.ShouldStartWith("[image scan]");
        result.Value.Text.ShouldContain("الاسم بالعربية: برنامج الأرشيف الرقمي");
        result.Value.Text.ShouldContain("1250000");
        result.Value.Text.ShouldNotContain("‎"); // direction marks removed
    }

    [Fact]
    public async Task A_scanned_english_tiff_is_read()
    {
        var result = await DocumentTextExtractor.ExtractAsync(Sample("scan-en.tif"), "scan.tif", Ocr with { Languages = "eng" }, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Text.ShouldContain("Name (English): Digital archive programme");
        result.Value.Text.ShouldContain("Total budget: 1250000 AED");
    }

    [Fact]
    public async Task Pdf_pages_without_a_text_layer_are_recognised_and_marked_as_scans()
    {
        var pdf = ScannedPdf();

        var result = await DocumentTextExtractor.ExtractAsync(pdf, "scanned.pdf", Ocr, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Kind.ShouldBe("pdf");
        result.Value.ScannedPages.ShouldBe(2);
        result.Value.Text.ShouldContain("[page 1 scan]");
        result.Value.Text.ShouldContain("[page 2 scan]");
        result.Value.Text.ShouldContain("[page 3]\nCover note with a real text layer");
        result.Value.Text.ShouldContain("برنامج الأرشيف الرقمي");
        result.Value.Text.ShouldContain("Digital archive programme");

        // A source quoted from a scanned page is marked as coming from the scan.
        var form = new AiFormDefinition("t", "sample", "ن", "F", "sample.item.create",
            [new("nameAr", AiFieldTypes.Text, "الاسم", "Name"), new("note", AiFieldTypes.Text, "ملاحظة", "Note")]);
        var output = JsonDocument.Parse("""
            {"language":"mixed","fields":[
              {"name":"nameAr","value":"برنامج الأرشيف الرقمي","confidence":0.8,"source":"الاسم بالعربية: برنامج الأرشيف الرقمي"},
              {"name":"note","value":"Cover note","confidence":0.9,"source":"Cover note with a real text layer"}]}
            """).RootElement;
        var fields = ExtractionService.PostProcess(form, output, result.Value.Text, []);
        fields[0].ShouldSatisfyAllConditions(f => f.SourceVerified.ShouldBeTrue(), f => f.FromScan.ShouldBeTrue(), f => f.LocationNumber.ShouldBe(1));
        fields[1].ShouldSatisfyAllConditions(f => f.FromScan.ShouldBeFalse(), f => f.LocationNumber.ShouldBe(3));
    }

    [Fact]
    public async Task The_page_limit_bounds_the_work()
    {
        var result = await DocumentTextExtractor.ExtractAsync(ScannedPdf(), "scanned.pdf", Ocr with { MaxPages = 1 }, CancellationToken.None);

        result.Value.ScannedPages.ShouldBe(1);
        result.Value.Text.ShouldContain("[page 2]\n"); // left without text
    }

    [Fact]
    public async Task Without_ocr_scans_are_explained_not_guessed()
    {
        (await DocumentTextExtractor.ExtractAsync(Sample("scan-en.png"), "scan.png", null, CancellationToken.None)).Error!.Code.ShouldBe("ai.extract.ocrOff");
        var onlyScans = Pdf(page => page.AddJpeg(Sample("scan-ar.jpg"), new PdfRectangle(0, 0, 595, 842)));
        (await DocumentTextExtractor.ExtractAsync(onlyScans, "scan.pdf", null, CancellationToken.None)).Error!.Code.ShouldBe("ai.extract.noText");
    }

    [Fact]
    public async Task A_missing_engine_is_reported_as_unavailable_without_failing()
    {
        var missing = new TesseractOcrEngine(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ocr:TesseractPath"] = "/nonexistent/tesseract" }).Build(),
            NullLogger<TesseractOcrEngine>.Instance);
        (await missing.IsAvailableAsync("ara+eng", CancellationToken.None)).ShouldBeFalse();
        (await missing.RecognizeAsync(Sample("scan-en.png"), "eng", CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, true)]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, false)] // GIF is not accepted for OCR
    public void Images_are_recognised_by_their_signature(byte[] head, bool image) => DocumentTextExtractor.IsImage(head).ShouldBe(image);

    /// <summary>Two scanned pages (Arabic JPEG, English PNG) and one page with a text layer.</summary>
    private static byte[] ScannedPdf() => Pdf(
        page => page.AddJpeg(Sample("scan-ar.jpg"), new PdfRectangle(0, 0, 595, 842)),
        page => page.AddPng(Sample("scan-en.png"), new PdfRectangle(0, 0, 595, 842)),
        page => page.AddText("Cover note with a real text layer", 12, new PdfPoint(50, 700)));

    private static byte[] Pdf(params Action<PageWriter>[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var write in pages)
        {
            write(new PageWriter(builder.AddPage(595, 842), font));
        }

        return builder.Build();
    }

    private sealed class PageWriter(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font)
    {
        public void AddJpeg(byte[] image, PdfRectangle area) => page.AddJpeg(image, area);

        public void AddPng(byte[] image, PdfRectangle area) => page.AddPng(image, area);

        public void AddText(string text, double size, PdfPoint at) => page.AddText(text, size, at, font);
    }
}
