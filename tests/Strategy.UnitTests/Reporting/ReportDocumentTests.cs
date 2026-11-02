using Strategy.Platform.Reporting.Pdf;

namespace Strategy.UnitTests.Reporting;

public class ReportDocumentTests
{
    [Fact]
    public void Html_is_encoded_bilingual_and_laid_out_in_the_chosen_direction_with_embedded_fonts()
    {
        var html = new ReportDocument { TitleAr = "تقرير <b>", TitleEn = "Report <script>", Language = "ar", PrimaryColor = "red;}" }
            .Paragraph("فقرة", "Paragraph")
            .Table([new ReportColumn("العمود", "Column")], [["<img src=x onerror=alert(1)>"]])
            .ToHtml();

        html.ShouldStartWith("<!doctype html><html lang=\"ar\" dir=\"rtl\">");
        html.ShouldContain("تقرير &lt;b&gt;");
        html.ShouldContain("Report &lt;script&gt;");
        html.ShouldNotContain("<script>");
        html.ShouldNotContain("<img");
        html.ShouldContain("<table dir=\"rtl\">");
        html.ShouldContain("<th>العمود</th>");
        html.ShouldContain("#1f3a68"); // invalid colour replaced
        html.ShouldContain("url('ibm-plex-sans-arabic-arabic-400-normal.woff2')");
        PdfFonts.All.Count.ShouldBe(4);
    }

    [Fact]
    public void English_reports_run_left_to_right_with_english_headers()
    {
        var html = new ReportDocument { TitleAr = "ت", TitleEn = "T", Language = "en" }.Table([new ReportColumn("العمود", "Column")], []).ToHtml();

        html.ShouldStartWith("<!doctype html><html lang=\"en\" dir=\"ltr\">");
        html.ShouldContain("<th>Column</th>");
    }
}
