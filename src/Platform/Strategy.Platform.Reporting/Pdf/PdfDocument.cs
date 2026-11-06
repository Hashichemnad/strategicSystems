using System.Globalization;
using System.Net;
using System.Text;

namespace Strategy.Platform.Reporting.Pdf;

/// <summary>A column of a report table: header per language.</summary>
public sealed record ReportColumn(string HeaderAr, string HeaderEn, int WidthPercent = 0);

/// <summary>
/// Builds the HTML of a bilingual report (CLAUDE.md §10): title in both languages, a header band with the organisation's names,
/// optional paragraphs and a table laid out in the chosen direction. Everything is HTML-encoded; no script, no external resources.
/// </summary>
public sealed class ReportDocument
{
    private readonly List<(string? Ar, string? En)> _paragraphs = [];
    private readonly List<string?[]> _rows = [];
    private readonly List<string> _blocks = [];
    private IReadOnlyList<ReportColumn> _columns = [];

    public string TitleAr { get; init; } = string.Empty;

    public string TitleEn { get; init; } = string.Empty;

    public string? OrganisationAr { get; init; }

    public string? OrganisationEn { get; init; }

    /// <summary><c>ar</c> lays the table and body out right-to-left; <c>en</c> left-to-right. Titles always show both languages.</summary>
    public string Language { get; init; } = "ar";

    public string PrimaryColor { get; init; } = "#1f3a68";

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public ReportDocument Paragraph(string? ar, string? en)
    {
        _paragraphs.Add((ar, en));
        return this;
    }

    /// <summary>A pre-built HTML block (dashboard renderer only: it encodes every text it writes and emits no script or external resource).</summary>
    internal ReportDocument Block(string html)
    {
        _blocks.Add(html);
        return this;
    }

    public ReportDocument Table(IReadOnlyList<ReportColumn> columns, IEnumerable<string?[]> rows)
    {
        _columns = columns;
        _rows.AddRange(rows);
        return this;
    }

    public string ToHtml()
    {
        var arabic = Language == "ar";
        var dir = arabic ? "rtl" : "ltr";
        var color = System.Text.RegularExpressions.Regex.IsMatch(PrimaryColor, "^#[0-9a-fA-F]{6}$") ? PrimaryColor : "#1f3a68";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(arabic ? "ar" : "en").Append("\" dir=\"").Append(dir).Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(arabic ? TitleAr : TitleEn)).Append("</title><style>").Append(PdfFonts.FontFaceCss())
            .Append("@page{size:A4;margin:16mm 12mm}body{font-family:'").Append(PdfFonts.Family).Append("',sans-serif;font-size:10pt;color:#1b1f24;margin:0}")
            .Append(".band{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:3px solid ").Append(color).Append(";padding-bottom:8px;margin-bottom:12px}")
            .Append(".ar{direction:rtl;text-align:right}.en{direction:ltr;text-align:left}h1{font-size:15pt;margin:0 0 2px;color:").Append(color).Append('}')
            .Append(".org{font-size:9pt;color:#57606a}.meta{font-size:8pt;color:#57606a;margin-bottom:10px}p{margin:0 0 6px;line-height:1.7}")
            .Append(".pair{display:flex;gap:16px}.pair>div{flex:1}")
            .Append("table{width:100%;border-collapse:collapse;font-size:8.5pt}th{background:").Append(color).Append(";color:#fff;font-weight:700}")
            .Append("th,td{border:1px solid #d0d7de;padding:4px 5px;vertical-align:top;text-align:start;word-break:break-word}tr:nth-child(even) td{background:#f6f8fa}")
            .Append("</style></head><body>");

        // Header band: English on the left, Arabic on the right, whatever the body direction.
        html.Append("<div class=\"band\" dir=\"ltr\"><div class=\"en\"><h1>").Append(E(TitleEn)).Append("</h1><div class=\"org\">").Append(E(OrganisationEn))
            .Append("</div></div><div class=\"ar\"><h1>").Append(E(TitleAr)).Append("</h1><div class=\"org\">").Append(E(OrganisationAr)).Append("</div></div></div>");
        html.Append("<div class=\"meta\">").Append(E(GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))).Append("</div>");

        foreach (var (ar, en) in _paragraphs)
        {
            html.Append("<div class=\"pair\" dir=\"ltr\"><div class=\"en\"><p>").Append(E(en)).Append("</p></div><div class=\"ar\"><p>").Append(E(ar)).Append("</p></div></div>");
        }

        foreach (var block in _blocks)
        {
            html.Append(block);
        }

        if (_columns.Count > 0)
        {
            html.Append("<table dir=\"").Append(dir).Append("\"><thead><tr>");
            foreach (var column in _columns)
            {
                html.Append("<th").Append(column.WidthPercent > 0 ? $" style=\"width:{column.WidthPercent}%\"" : string.Empty).Append('>')
                    .Append(E(arabic ? column.HeaderAr : column.HeaderEn)).Append("</th>");
            }

            html.Append("</tr></thead><tbody>");
            foreach (var row in _rows)
            {
                html.Append("<tr>");
                foreach (var cell in row)
                {
                    html.Append("<td dir=\"auto\">").Append(E(cell)).Append("</td>");
                }

                html.Append("</tr>");
            }

            html.Append("</tbody></table>");
        }

        return html.Append("</body></html>").ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
