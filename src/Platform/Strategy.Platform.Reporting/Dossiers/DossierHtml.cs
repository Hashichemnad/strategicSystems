using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Reporting.ReportTypes;

namespace Strategy.Platform.Reporting.Dossiers;

/// <summary>Branding used by dossier outputs.</summary>
public sealed record DossierBranding(string? OrganisationAr, string? OrganisationEn, string PrimaryColor, DateTimeOffset GeneratedAt);

/// <summary>
/// HTML of one or more dossiers for Gotenberg (ADR 0185): cover page, repeated page header / footer (fixed elements repeat on every printed
/// page), sections laid out in the language's direction, SVG charts, Gantt and matrix. Every text is encoded; no script, no external resource.
/// Bilingual: Arabic and English labels / values side by side.
/// </summary>
public static partial class DossierHtml
{
    private static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Colour();

    public static string Build(IReadOnlyList<DossierDocument> documents, string language, DossierBranding branding)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(branding);
        var ar = language != DossierLanguages.English;
        var color = Colour().IsMatch(branding.PrimaryColor) ? branding.PrimaryColor : "#1f3a68";
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(ar ? "ar" : "en").Append("\" dir=\"").Append(ar ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(E(documents.Count == 1 ? (ar ? documents[0].TemplateLabelAr : documents[0].TemplateLabelEn) : "Dossiers")).Append("</title><style>")
            .Append(PdfFonts.FontFaceCss())
            .Append("@page{size:A4;margin:20mm 12mm 18mm}body{font-family:'").Append(PdfFonts.Family).Append("',sans-serif;font-size:9.5pt;color:#1b1f24;margin:0}")
            .Append(".hdr{position:fixed;top:-14mm;inset-inline:0;font-size:8pt;color:#57606a;border-bottom:1px solid #d0d7de;display:flex;justify-content:space-between}")
            .Append(".ftr{position:fixed;bottom:-12mm;inset-inline:0;font-size:8pt;color:#57606a;border-top:1px solid #d0d7de;display:flex;justify-content:space-between}")
            .Append(".cover{height:230mm;display:flex;flex-direction:column;justify-content:center;align-items:center;text-align:center;page-break-after:always}")
            .Append(".cover h1{font-size:24pt;color:").Append(color).Append(";margin:0 0 8px}.cover .sub{font-size:14pt;margin:4px 0}.cover .meta{color:#57606a;margin-top:18px}")
            .Append(".rec{page-break-before:always}.rec:first-of-type{page-break-before:auto}")
            .Append("h2{font-size:12.5pt;color:").Append(color).Append(";border-bottom:2px solid ").Append(color).Append(";padding-bottom:3px;margin:16px 0 8px;page-break-after:avoid}")
            .Append(".sec{page-break-inside:auto}.pb{page-break-before:always}")
            .Append(".grid{display:grid;grid-template-columns:1fr 1fr;gap:4px 18px}.kv{display:flex;gap:8px;border-bottom:1px dotted #d0d7de;padding:2px 0}")
            .Append(".kv b{min-width:38%;color:#57606a;font-weight:600}.pair{display:flex;gap:16px}.pair>div{flex:1}.en{direction:ltr;text-align:left}.ar{direction:rtl;text-align:right}")
            .Append("table{width:100%;border-collapse:collapse;font-size:8.5pt}th{background:").Append(color).Append(";color:#fff;font-weight:700}")
            .Append("th,td{border:1px solid #d0d7de;padding:3px 5px;vertical-align:top;text-align:start;word-break:break-word}tr:nth-child(even) td{background:#f6f8fa}")
            .Append(".empty{color:#57606a;font-style:italic}.matrix td{text-align:center;width:14%;height:26px}")
            .Append("</style></head><body>");
        foreach (var doc in documents)
        {
            Document(html, doc, language, branding, color);
        }

        return html.Append("</body></html>").ToString();
    }

    private static void Document(StringBuilder html, DossierDocument doc, string language, DossierBranding branding, string color)
    {
        var d = doc.Definition;
        var bilingual = language == DossierLanguages.Bilingual;
        var ar = language != DossierLanguages.English;
        string T(string? arText, string? enText) => bilingual ? $"{arText} — {enText}" : ar ? arText ?? enText ?? string.Empty : enText ?? arText ?? string.Empty;
        html.Append("<section class=\"rec\">");
        html.Append("<div class=\"hdr\"><span>").Append(E(T(d.HeaderAr ?? doc.TemplateLabelAr, d.HeaderEn ?? doc.TemplateLabelEn))).Append("</span><span>")
            .Append(E(doc.Number)).Append("</span></div>");
        html.Append("<div class=\"ftr\"><span>").Append(E(T(branding.OrganisationAr, branding.OrganisationEn))).Append("</span><span>").Append(E(T(d.FooterAr, d.FooterEn)))
            .Append("</span><span>").Append(E(branding.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))).Append("</span></div>");
        if (d.Cover.Show)
        {
            html.Append("<div class=\"cover\"><h1>").Append(E(T(d.Cover.TitleAr ?? doc.TemplateLabelAr, d.Cover.TitleEn ?? doc.TemplateLabelEn))).Append("</h1>");
            html.Append("<div class=\"sub\">").Append(E(T(doc.NameAr, doc.NameEn))).Append("</div>");
            if (doc.SubtitleAr is not null)
            {
                html.Append("<div class=\"sub\">").Append(E(ar ? doc.SubtitleAr : doc.SubtitleEn)).Append("</div>");
            }

            html.Append("<div class=\"meta\">");
            if (d.Cover.ShowOrganisation)
            {
                html.Append(E(T(branding.OrganisationAr, branding.OrganisationEn))).Append("<br>");
            }

            if (d.Cover.ShowDate)
            {
                html.Append(E(branding.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            html.Append("</div></div>");
        }

        foreach (var block in doc.Blocks)
        {
            html.Append("<div class=\"sec").Append(block.PageBreak ? " pb" : string.Empty).Append("\" data-section=\"").Append(E(block.Key)).Append("\"><h2>")
                .Append(E(T(block.TitleAr, block.TitleEn))).Append("</h2>");
            Block(html, block, language, color);
            html.Append("</div>");
        }

        html.Append("</section>");
    }

    private static void Block(StringBuilder html, DossierBlock block, string language, string color)
    {
        var bilingual = language == DossierLanguages.Bilingual;
        var ar = language != DossierLanguages.English;
        string Empty() => bilingual ? "لا توجد بيانات — No data" : ar ? "لا توجد بيانات" : "No data";
        switch (block)
        {
            case { Pairs: { } pairs }:
                html.Append("<div class=\"grid\">");
                foreach (var p in pairs)
                {
                    var label = bilingual ? $"{p.LabelAr} / {p.LabelEn}" : ar ? p.LabelAr : p.LabelEn;
                    var value = bilingual && p.ValueAr != p.ValueEn ? $"{p.ValueAr} / {p.ValueEn}" : ar ? p.ValueAr : p.ValueEn;
                    html.Append("<div class=\"kv\"><b>").Append(E(label)).Append("</b><span dir=\"auto\">").Append(E(value ?? "—")).Append("</span></div>");
                }

                html.Append("</div>");
                break;

            case { Text: { } text }:
                if (bilingual)
                {
                    html.Append("<div class=\"pair\" dir=\"ltr\"><div class=\"en\"><p>").Append(E(text.En)).Append("</p></div><div class=\"ar\"><p>").Append(E(text.Ar)).Append("</p></div></div>");
                }
                else
                {
                    html.Append("<p>").Append(E(ar ? text.Ar ?? text.En : text.En ?? text.Ar)).Append("</p>");
                }

                break;

            case { Table: { } table }:
                if (table.RowsAr.Count == 0)
                {
                    html.Append("<p class=\"empty\">").Append(E(Empty())).Append("</p>");
                    break;
                }

                html.Append("<table><thead><tr>");
                var headers = ar ? table.HeadersAr : table.HeadersEn;
                for (var i = 0; i < headers.Count; i++)
                {
                    html.Append("<th>").Append(E(bilingual ? $"{table.HeadersAr[i]} / {table.HeadersEn[i]}" : headers[i])).Append("</th>");
                }

                html.Append("</tr></thead><tbody>");
                var rows = ar ? table.RowsAr : table.RowsEn;
                foreach (var row in rows)
                {
                    html.Append("<tr>");
                    foreach (var cell in row)
                    {
                        html.Append("<td dir=\"auto\">").Append(E(cell)).Append("</td>");
                    }

                    html.Append("</tr>");
                }

                html.Append("</tbody></table>");
                break;

            case { Chart: { } chart }:
                if (chart.CategoriesAr.Count == 0)
                {
                    html.Append("<p class=\"empty\">").Append(E(Empty())).Append("</p>");
                    break;
                }

                html.Append(ChartSvg.Render(chart.Type, ar ? chart.CategoriesAr : chart.CategoriesEn,
                    chart.Series.Select(s => (ar ? s.NameAr : s.NameEn, s.Values)).ToList(), ar, color));
                break;

            case { Gantt: { } items }:
                if (items.Count == 0)
                {
                    html.Append("<p class=\"empty\">").Append(E(Empty())).Append("</p>");
                    break;
                }

                html.Append(GanttSvg(items, ar, color));
                break;

            case { Matrix: { } matrix }:
                html.Append(MatrixHtml(matrix, ar, bilingual));
                break;
        }
    }

    /// <summary>Months between the earliest and latest date; one bar per item (planned, and actual underneath), mirrored for Arabic.</summary>
    public static string GanttSvg(IReadOnlyList<DossierGanttItem> items, bool rtl, string color)
    {
        var from = items.Min(i => new[] { i.Start, i.ActualStart }.Where(x => x is not null).Min()!.Value);
        var to = items.Max(i => new[] { i.End, i.ActualEnd }.Where(x => x is not null).Max()!.Value);
        var start = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1).AddMonths(1);
        var days = Math.Max(1, end.DayNumber - start.DayNumber);
        const int width = 680;
        const int labelWidth = 170;
        const int rowHeight = 20;
        var chartWidth = width - labelWidth - 8;
        var height = 22 + (items.Count * rowHeight) + 4;
        double X(DateOnly d)
        {
            var offset = (d.DayNumber - start.DayNumber) / (double)days * chartWidth;
            return rtl ? width - labelWidth - offset : labelWidth + offset;
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\" width=\"100%\" role=\"img\">");
        for (var m = start; m < end; m = m.AddMonths(1))
        {
            var x = X(m);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{x:0.#}\" x2=\"{x:0.#}\" y1=\"14\" y2=\"{height}\" stroke=\"#eaeef2\"/>")
                .Append(CultureInfo.InvariantCulture, $"<text x=\"{x + (rtl ? -2 : 2):0.#}\" y=\"11\" font-size=\"7\" text-anchor=\"{(rtl ? "end" : "start")}\" fill=\"#57606a\">{m:yyyy-MM}</text>");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var y = 22 + (i * rowHeight);
            var label = (rtl ? item.LabelAr ?? item.LabelEn : item.LabelEn ?? item.LabelAr) ?? "—";
            label = label.Length > 34 ? label[..33] + "…" : label;
            var lx = rtl ? width - 4 : 4;
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{lx}\" y=\"{y + 10}\" font-size=\"8\" text-anchor=\"{(rtl ? "end" : "start")}\" fill=\"#1b1f24\">{E(label)}</text>");
            void Bar(DateOnly a, DateOnly b, double top, double h, string fill)
            {
                var x1 = X(a);
                var x2 = X(b.AddDays(1));
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{Math.Min(x1, x2):0.#}\" y=\"{top:0.#}\" width=\"{Math.Max(2, Math.Abs(x2 - x1)):0.#}\" height=\"{h}\" rx=\"2\" fill=\"{fill}\"/>");
            }

            Bar(item.Start!.Value, item.End!.Value, y, 9, color);
            if (item.ActualStart is not null || item.ActualEnd is not null)
            {
                Bar(item.ActualStart ?? item.Start.Value, item.ActualEnd ?? item.End.Value, y + 10, 5, "#2da44e");
            }
        }

        return svg.Append("</svg>").ToString();
    }

    public static string MatrixColour(int likelihood, int impact, int size)
    {
        var score = likelihood * impact / (double)(size * size);
        return score >= 0.6 ? "#ff8182" : score >= 0.35 ? "#ffb77c" : score >= 0.15 ? "#fff8c5" : "#dafbe1";
    }

    private static string MatrixHtml(DossierMatrix matrix, bool ar, bool bilingual)
    {
        var html = new StringBuilder("<table class=\"matrix\" dir=\"ltr\"><tbody>");
        var likelihood = bilingual ? "الاحتمالية / Likelihood" : ar ? "الاحتمالية" : "Likelihood";
        var impact = bilingual ? "الأثر / Impact" : ar ? "الأثر" : "Impact";
        for (var l = matrix.Size; l >= 1; l--)
        {
            html.Append("<tr><th>").Append(E(likelihood)).Append(' ').Append(l).Append("</th>");
            for (var i = 1; i <= matrix.Size; i++)
            {
                var count = matrix.Counts[l - 1, i - 1];
                html.Append("<td style=\"background:").Append(MatrixColour(l, i, matrix.Size)).Append("\">").Append(count == 0 ? string.Empty : count.ToString(CultureInfo.InvariantCulture))
                    .Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("<tr><th></th>");
        for (var i = 1; i <= matrix.Size; i++)
        {
            html.Append("<th>").Append(E(impact)).Append(' ').Append(i).Append("</th>");
        }

        return html.Append("</tr></tbody></table>").ToString();
    }
}
