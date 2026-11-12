using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Strategy.Platform.Reporting.Dossiers;
using Strategy.Platform.Reporting.ReportTypes;

namespace Strategy.UnitTests.Reporting;

/// <summary>Dossier outputs (ADR 0185): every block kind in HTML (for Gotenberg) and Word, right-to-left for Arabic, text encoded.</summary>
public sealed class DossierRenderingTests
{
    private static DossierDocument Sample()
    {
        var counts = new int[5, 5];
        counts[3, 4] = 2;
        return new DossierDocument("t", "ملف المشروع", "Project dossier", Guid.NewGuid(), "PROJ-001", "مشروع <التحول>", "Transformation <project>", "PROJ-001", "PROJ-001",
            new DossierDefinition { HeaderAr = "رأس", HeaderEn = "Head", FooterAr = "تذييل", FooterEn = "Foot" },
            [
                new DossierBlock("overview", "نظرة عامة", "Overview", DossierSectionKinds.Fields, false,
                    Pairs: [new DossierPair("الرقم", "Number", "PROJ-001", "PROJ-001"), new DossierPair("الحالة", "Status", "نشط", "Active")]),
                new DossierBlock("text", "ملاحظة", "Note", DossierSectionKinds.Text, false, Text: ("نص عربي", "English text")),
                new DossierBlock("milestones", "المعالم", "Milestones", DossierSectionKinds.Related, true,
                    Table: new DossierTable(["الرمز", "العنوان"], ["Code", "Title"], [["M1", "تسليم"]], [["M1", "Delivery"]])),
                new DossierBlock("empty", "فارغ", "Empty", DossierSectionKinds.Related, false, Table: new DossierTable(["أ"], ["A"], [], [])),
                new DossierBlock("chart", "رسم", "Chart", DossierSectionKinds.Chart, false,
                    Chart: new DossierChart(ChartTypes.Bar, ["أ", "ب"], ["A", "B"], [((string?)null, (string?)null, (IReadOnlyList<decimal>)[3m, 5m])])),
                new DossierBlock("gantt", "المخطط", "Gantt", DossierSectionKinds.Gantt, false,
                    Gantt: [new DossierGanttItem("تسليم", "Delivery", new DateOnly(2026, 1, 10), new DateOnly(2026, 3, 20), new DateOnly(2026, 1, 12), new DateOnly(2026, 4, 2))]),
                new DossierBlock("matrix", "المصفوفة", "Matrix", DossierSectionKinds.Matrix, false, Matrix: new DossierMatrix(5, counts)),
            ],
            []);
    }

    private static readonly DossierBranding Branding = new("الجهة", "Organisation", "#1f3a68", new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    [InlineData("bilingual")]
    public void Html_has_every_section_in_the_language_direction_with_encoded_text(string language)
    {
        var html = DossierHtml.Build([Sample()], language, Branding);
        html.ShouldContain(language == "en" ? "dir=\"ltr\"" : "dir=\"rtl\"");
        foreach (var key in new[] { "overview", "text", "milestones", "empty", "chart", "gantt", "matrix" })
        {
            html.ShouldContain($"data-section=\"{key}\"");
        }

        html.ShouldContain(language == "en" ? "Overview" : "نظرة عامة");
        if (language == "bilingual")
        {
            html.ShouldContain("نظرة عامة — Overview");
        }

        html.ShouldNotContain("<التحول>");
        html.ShouldNotContain("<project>");
        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldContain("<svg");
        html.ShouldContain("class=\"matrix\"");
    }

    [Fact]
    public void Several_dossiers_go_into_one_html_with_a_page_break_per_record()
    {
        var html = DossierHtml.Build([Sample(), Sample(), Sample()], "ar", Branding);
        System.Text.RegularExpressions.Regex.Count(html, "<section class=\"rec\">").ShouldBe(3);
        html.ShouldContain(".rec{page-break-before:always}");
    }

    private static (List<string> Headings, string Text, Document Doc) Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(stream, false);
        var doc = (Document)word.MainDocumentPart!.Document!.CloneNode(true);
        var headings = doc.Body!.Descendants<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1").Select(p => p.InnerText).ToList();
        word.MainDocumentPart.HeaderParts.ShouldHaveSingleItem();
        word.MainDocumentPart.FooterParts.ShouldHaveSingleItem();
        return (headings, doc.Body.InnerText, doc);
    }

    [Fact]
    public void Word_in_arabic_is_right_to_left_with_a_heading_per_section_and_native_tables()
    {
        var (headings, text, doc) = Read(DossierWord.Build(Sample(), "ar", Branding));
        headings.ShouldBe(["نظرة عامة", "ملاحظة", "المعالم", "فارغ", "رسم", "المخطط", "المصفوفة"]);
        doc.Body!.Elements<SectionProperties>().Single().GetFirstChild<BiDi>().ShouldNotBeNull();
        doc.Body.Descendants<TableProperties>().ShouldAllBe(t => t.GetFirstChild<BiDiVisual>() != null);
        doc.Body.Descendants<RightToLeftText>().ShouldNotBeEmpty();
        text.ShouldContain("مشروع <التحول>"); // stored as text, never markup
        text.ShouldContain("تسليم");
        text.ShouldContain("لا توجد بيانات");
        doc.Body.Descendants<Table>().Count().ShouldBeGreaterThanOrEqualTo(5); // fields, list, chart, Gantt, matrix
        doc.Body.Descendants<Shading>().ShouldContain(s => s.Fill!.Value == "1f3a68"); // Gantt bar months
    }

    [Fact]
    public void Word_in_english_is_left_to_right()
    {
        var (headings, text, doc) = Read(DossierWord.Build(Sample(), "en", Branding));
        headings.ShouldBe(["Overview", "Note", "Milestones", "Empty", "Chart", "Gantt", "Matrix"]);
        doc.Body!.Elements<SectionProperties>().Single().GetFirstChild<BiDi>().ShouldBeNull();
        doc.Body.Descendants<RightToLeftText>().ShouldBeEmpty();
        text.ShouldContain("Delivery");
        text.ShouldContain("Transformation <project>");
    }
}
