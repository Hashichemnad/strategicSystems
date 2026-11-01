using System.Net;
using System.Text;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Reporting.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// A bilingual PDF rendered by the real Gotenberg (headless Chromium): the Arabic font is embedded, Arabic letters are shaped (joined
/// contextual forms, not isolated letters), Arabic lines run right to left from the right margin, and a layout snapshot (words, fonts,
/// rounded positions) catches regressions. Set <c>UPDATE_SNAPSHOTS=1</c> to rewrite the snapshot after an intended change.
/// </summary>
public class PdfRenderingTests(SidecarContainers sidecars) : IClassFixture<SidecarContainers>
{
    private static readonly string SnapshotPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Reporting", "Snapshots", "bilingual-report.layout.json");

    internal static ReportDocument Sample() => new ReportDocument
    {
        TitleAr = "تقرير تجريبي ثنائي اللغة",
        TitleEn = "Bilingual sample report",
        OrganisationAr = "دائرة الاستراتيجية",
        OrganisationEn = "Strategy Department",
        Language = "ar",
        GeneratedAt = new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero),
    }
    .Paragraph("تم اعتماد الخطة الاستراتيجية للمؤسسة", "The organisation's strategic plan was approved")
    .Table(
        [new ReportColumn("المؤشر", "Indicator", 50), new ReportColumn("القيمة", "Value", 25), new ReportColumn("الحالة", "Status", 25)],
        [["رضا المتعاملين", "87%", "على المسار"], ["نسبة الإنجاز", "64%", "متأخر"]]);

    [Fact]
    public async Task Bilingual_pdf_renders_arabic_shaped_right_to_left_with_the_embedded_font_and_matches_the_layout_snapshot()
    {
        using var http = new HttpClient();
        var renderer = new GotenbergPdfRenderer(http, new GotenbergOptions { Url = sidecars.GotenbergUrl.ToString() });
        var pdf = await renderer.RenderAsync(Sample().ToHtml(), CancellationToken.None);
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        var words = page.GetWords().ToList();
        var arabic = words.Where(w => w.Text.Any(IsArabic)).ToList();
        var latin = words.Where(w => !w.Text.Any(IsArabic)).ToList();

        // 1. Fonts: both scripts use the embedded IBM Plex Sans Arabic subsets, no fallback font.
        words.ShouldAllBe(w => w.FontName!.Contains("IBMPlexSansArabic"));
        words.ShouldAllBe(w => w.FontName!.Contains('+')); // subset prefix = embedded

        // 2. Shaping: Arabic glyphs map to contextual presentation forms (a renderer without shaping maps every glyph to the bare letters
        // U+0600–U+06FF; only non-joining isolated letters such as a final alef stay bare), and "الاستراتيجية" uses the lam-alef ligature.
        arabic.ShouldNotBeEmpty();
        var letters = arabic.SelectMany(w => w.Text).Where(IsArabic).ToList();
        (letters.Count(IsPresentationForm) / (double)letters.Count).ShouldBeGreaterThan(0.75);
        arabic.SelectMany(w => w.Text).ShouldContain(c => c >= '\uFEF5' && c <= '\uFEFC');

        // 3. Reading order: the Arabic title line, read right to left, gives back the logical text.
        LogicalLine(arabic, TitleBaseline(arabic)).ShouldBe(Strip(Sample().TitleAr));
        LogicalLine(arabic, Baseline(arabic, "دائرة")).ShouldBe(Strip("دائرة الاستراتيجية"));

        // 4. Direction: the Arabic title ends at the right margin, the English one starts at the left margin; the Arabic table's first
        // column (Indicator) is on the right.
        var titleLine = arabic.Where(w => Math.Abs(w.BoundingBox.Bottom - TitleBaseline(arabic)) < 1).ToList();
        titleLine.Max(w => w.BoundingBox.Right).ShouldBeGreaterThan(page.Width - 60);
        latin.First(w => w.Text == "Bilingual").BoundingBox.Left.ShouldBeLessThan(60);
        var header = arabic.Single(w => Normalise(w.Text) == "المؤشر");
        var valueHeader = arabic.Single(w => Normalise(w.Text) == "القيمة");
        header.BoundingBox.Left.ShouldBeGreaterThan(valueHeader.BoundingBox.Left);

        // 5. Layout snapshot.
        var snapshot = JsonSerializer.Serialize(words.Select(w => new SnapshotWord(
            Normalise(w.Text), w.FontName![(w.FontName!.IndexOf('+', StringComparison.Ordinal) + 1)..],
            Math.Round(w.BoundingBox.Left / 2) * 2, Math.Round(w.BoundingBox.Bottom / 2) * 2)), SnapshotJson);
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(SnapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, snapshot + "\n");
        }

        snapshot.ShouldBe((await File.ReadAllTextAsync(SnapshotPath)).TrimEnd('\n'));
    }

    [Fact]
    public async Task Audit_log_exports_as_a_bilingual_pdf()
    {
        await using var factory = new ApiFactory(await sidecars.CreateDatabaseAsync(), settings: new Dictionary<string, string?>
        {
            ["Gotenberg:Url"] = sidecars.GotenbergUrl.ToString(),
        });
        var admin = await factory.CreateSignedInClientAsync(admin: true);
        foreach (var lang in new[] { "ar", "en" })
        {
            var response = await admin.GetAsync($"/api/v1/audit/entries/export?format=pdf&lang={lang}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
            using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
            var words = document.GetPage(1).GetWords().ToList();
            words.ShouldContain(w => Normalise(w.Text) == "التدقيق"); // "سجل التدقيق" header in both languages
            words.ShouldContain(w => w.Text == "Audit");
        }
    }

    private sealed record SnapshotWord(string Text, string Font, double X, double Y);

    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static double TitleBaseline(List<Word> arabic) => arabic.Max(w => w.BoundingBox.Bottom);

    private static double Baseline(List<Word> arabic, string word) => arabic.First(w => Normalise(w.Text) == word).BoundingBox.Bottom;

    /// <summary>The words of one line, right to left, each read right to left (visual → logical), normalised to base letters.</summary>
    private static string LogicalLine(List<Word> arabic, double baseline) =>
        string.Concat(arabic.Where(w => Math.Abs(w.BoundingBox.Bottom - baseline) < 1).OrderByDescending(w => w.BoundingBox.Left).Select(w => Normalise(w.Text)));

    /// <summary>A shaped word as extracted (presentation forms in visual order) → logical base letters.</summary>
    private static string Normalise(string text) =>
        text.Any(IsArabic) ? new string(text.Reverse().ToArray()).Normalize(NormalizationForm.FormKC) : text;

    private static string Strip(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static bool IsArabic(char c) => c is >= '\u0600' and <= '\u06FF' or >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';

    private static bool IsPresentationForm(char c) => c is >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';
}
