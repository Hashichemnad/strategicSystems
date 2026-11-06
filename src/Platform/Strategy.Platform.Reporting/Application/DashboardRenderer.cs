using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Application;

/// <summary>
/// Dashboards as PDF (bilingual template, tiles, tables and server-drawn SVG bars / lines — no client JavaScript in the renderer, ADR 0096 /
/// 0145) and as Excel (one sheet per widget).
/// </summary>
public sealed class DashboardRenderer(IPdfRenderer pdf, ISettingsStore settings)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string Format(decimal? value, string? kind) => value is not { } v ? "—" : kind switch
    {
        "ratio" or "score" => (v * 100).ToString("0.#", Invariant) + " %",
        "money" => v.ToString("#,0", Invariant),
        "hours" or "days" => v.ToString("0.#", Invariant),
        _ => v.ToString("#,0.##", Invariant),
    };

    private static string Title(WidgetConfig w, WidgetDataDto d, bool ar) =>
        (ar ? w.TitleAr ?? d.MetricLabelAr : w.TitleEn ?? d.MetricLabelEn) ?? w.Id;

    private static string Label(WidgetDataDto d, string dim, string code, bool ar)
    {
        var l = d.Labels?.FirstOrDefault(x => x.Dimension == dim && x.Code == code);
        return (ar ? l?.LabelAr ?? l?.LabelEn : l?.LabelEn ?? l?.LabelAr) ?? code;
    }

    /// <summary>Unit names for the pseudo-dimension <c>unit</c>.</summary>
    public delegate string UnitName(string id);

    public async Task<Result<ExportedFile>> PdfAsync(DashboardDataDto dashboard, string language, UnitName unitName, CancellationToken ct)
    {
        var ar = language == "ar";
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var document = new ReportDocument
        {
            TitleAr = dashboard.TitleAr,
            TitleEn = dashboard.TitleEn,
            OrganisationAr = branding.OrganisationNameAr,
            OrganisationEn = branding.OrganisationNameEn,
            Language = language,
            PrimaryColor = branding.PrimaryColor,
        };
        document.Paragraph($"الفترة: {dashboard.Period.From} – {dashboard.Period.To}", $"Period: {dashboard.Period.From} – {dashboard.Period.To}");
        var color = System.Text.RegularExpressions.Regex.IsMatch(branding.PrimaryColor, "^#[0-9a-fA-F]{6}$") ? branding.PrimaryColor : "#1f3a68";
        var tiles = new StringBuilder("<div style=\"display:flex;flex-wrap:wrap;gap:8px;margin:8px 0\">");
        var body = new StringBuilder();
        foreach (var (w, d) in dashboard.Widgets.Zip(dashboard.Data))
        {
            if (d.State == "unavailable" || w.Type == WidgetTypes.Brief)
            {
                continue;
            }

            var title = E(Title(w, d, ar));
            switch (w.Type)
            {
                case WidgetTypes.Tile or WidgetTypes.Gauge:
                    tiles.Append("<div style=\"border:1px solid #d0d7de;border-radius:6px;padding:6px 10px;min-width:120px\"><div style=\"font-size:8pt;color:#57606a\">")
                        .Append(title).Append("</div><div style=\"font-size:15pt;font-weight:700;color:").Append(RagColor(d.Rag, color)).Append("\">")
                        .Append(E(Format(d.Value, d.ValueKind))).Append("</div>");
                    if (d.Previous is not null)
                    {
                        tiles.Append("<div style=\"font-size:7.5pt;color:#57606a\">").Append(ar ? "الفترة السابقة: " : "Previous: ").Append(E(Format(d.Previous, d.ValueKind))).Append("</div>");
                    }

                    tiles.Append("</div>");
                    break;
                case WidgetTypes.Line when d.Trend is { Count: > 0 }:
                    body.Append(Heading(title)).Append(LineSvg(d.Trend, d.ValueKind, ar, color));
                    break;
                case WidgetTypes.List when d.Items is { } items:
                    body.Append(Heading(title)).Append(Table(
                        [ar ? "الرقم" : "Number", ar ? "العنوان" : "Title", ar ? "التاريخ" : "Date"],
                        items.Select(i => new[] { i.Number, ar ? i.TitleAr ?? i.TitleEn : i.TitleEn ?? i.TitleAr, i.Date?.ToString("yyyy-MM-dd", Invariant) }), ar));
                    break;
                case WidgetTypes.Scorecard when d.Scorecard is { } card:
                    body.Append(Heading(title)).Append(ScorecardTable(card, ar));
                    break;
                case WidgetTypes.Bubble when d.Bubbles is { } bubbles:
                    body.Append(Heading(title)).Append(Table(
                        [ar ? "البند" : "Item", "X", "Y", ar ? "الحجم" : "Size"],
                        bubbles.Select(b => new[] { Label(d, w.Dimension ?? string.Empty, b.Code, ar), Format(b.X, null), Format(b.Y, null), Format(b.Size, null) }), ar));
                    break;
                default:
                    if (d.Rows is { Count: > 0 } rows && w.Dimension is { } dim)
                    {
                        string Name(string dimension, string code) => dimension == "unit" ? unitName(code) : Label(d, dimension, code, ar);
                        body.Append(Heading(title));
                        if (w.Series is { } series)
                        {
                            var columns = rows.Select(r => r.Key[series]).Distinct(StringComparer.Ordinal).ToList();
                            var pivot = rows.GroupBy(r => r.Key[dim]).Select(g =>
                                new[] { Name(dim, g.Key) }.Concat(columns.Select(c => Format(g.FirstOrDefault(r => r.Key[series] == c)?.Value, d.ValueKind))).ToArray());
                            body.Append(Table([string.Empty, .. columns.Select(c => Name(series, c))], pivot.Select(p => p.Cast<string?>().ToArray()), ar));
                        }
                        else
                        {
                            body.Append(BarsSvg(rows.Take(12).Select(r => (Name(dim, r.Key[dim]), r.Value)).ToList(), d.ValueKind, ar, color));
                        }
                    }
                    else if (d.Value is not null)
                    {
                        body.Append(Heading(title)).Append("<p>").Append(E(Format(d.Value, d.ValueKind))).Append("</p>");
                    }

                    break;
            }
        }

        document.Block(tiles.Append("</div>").ToString()).Block(body.ToString());
        try
        {
            var bytes = await pdf.RenderAsync(document.ToHtml(), ct).ConfigureAwait(false);
            return new ExportedFile(bytes, "application/pdf", FileName(dashboard.Code, "pdf"));
        }
        catch (PdfRenderingException)
        {
            return Error.Rule("reporting.pdfFailed", "reporting.errors.pdfFailed");
        }
    }

    public static string FileName(string code, string extension) =>
        new string(code.Select(c => char.IsLetterOrDigit(c) || c is '-' ? c : '-').ToArray()) + "." + extension;

    private static string RagColor(string? rag, string fallback) => rag switch
    {
        "green" => "#1a7f37",
        "amber" => "#9a6700",
        "red" => "#cf222e",
        _ => fallback,
    };

    private static string Heading(string title) => "<h3 style=\"font-size:11pt;margin:14px 0 6px\">" + title + "</h3>";

    private static string Table(IReadOnlyList<string> headers, IEnumerable<string?[]> rows, bool ar)
    {
        var html = new StringBuilder("<table dir=\"").Append(ar ? "rtl" : "ltr").Append("\"><thead><tr>");
        foreach (var h in headers)
        {
            html.Append("<th>").Append(E(h)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var cell in row)
            {
                html.Append("<td dir=\"auto\">").Append(E(cell)).Append("</td>");
            }

            html.Append("</tr>");
        }

        return html.Append("</tbody></table>").ToString();
    }

    private static string ScorecardTable(ScorecardDto card, bool ar)
    {
        var html = new StringBuilder("<table dir=\"").Append(ar ? "rtl" : "ltr").Append("\"><thead><tr><th>").Append(ar ? "الإدارة" : "Department").Append("</th>");
        foreach (var c in card.Columns)
        {
            html.Append("<th>").Append(E(ar ? c.LabelAr : c.LabelEn)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var row in card.Rows)
        {
            html.Append("<tr><td dir=\"auto\">").Append(E(ar ? row.NameAr ?? row.NameEn : row.NameEn ?? row.NameAr)).Append("</td>");
            foreach (var (cell, column) in row.Cells.Zip(card.Columns))
            {
                html.Append("<td style=\"color:").Append(RagColor(cell.Rag, "#1b1f24")).Append(";font-weight:700\">").Append(E(Format(cell.Value, column.ValueKind))).Append("</td>");
            }

            html.Append("</tr>");
        }

        return html.Append("</tbody></table>").ToString();
    }

    /// <summary>Horizontal bars; in Arabic the bars grow from the right.</summary>
    internal static string BarsSvg(IReadOnlyList<(string Label, decimal? Value)> rows, string? kind, bool ar, string color)
    {
        const int width = 640, rowHeight = 20, labelWidth = 200, valueWidth = 70;
        var max = rows.Select(r => r.Value ?? 0).DefaultIfEmpty(0).Max();
        var height = rows.Count * rowHeight + 4;
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" role=\"img\" style=\"display:block;margin:4px 0\">");
        for (var i = 0; i < rows.Count; i++)
        {
            var (label, value) = rows[i];
            var length = max <= 0 ? 0 : (double)((value ?? 0) / max) * (width - labelWidth - valueWidth - 10);
            var y = i * rowHeight + 2;
            var barX = ar ? width - labelWidth - length : labelWidth;
            var labelX = ar ? width : 0;
            var valueX = ar ? width - labelWidth - length - 4 : labelWidth + length + 4;
            svg.Append(Invariant, $"<text x=\"{labelX}\" y=\"{y + 14}\" font-size=\"9\" text-anchor=\"{(ar ? "end" : "start")}\" fill=\"#1b1f24\">{E(Truncate(label, 34))}</text>");
            svg.Append(Invariant, $"<rect x=\"{barX:0.#}\" y=\"{y + 3}\" width=\"{length:0.#}\" height=\"{rowHeight - 7}\" fill=\"{color}\" rx=\"2\"/>");
            svg.Append(Invariant, $"<text x=\"{valueX:0.#}\" y=\"{y + 14}\" font-size=\"9\" text-anchor=\"{(ar ? "end" : "start")}\" fill=\"#57606a\">{E(Format(value, kind))}</text>");
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>A 12-month line; months run right-to-left in Arabic.</summary>
    internal static string LineSvg(IReadOnlyList<TrendPointDto> points, string? kind, bool ar, string color)
    {
        const int width = 640, height = 160, padX = 40, padY = 20;
        var values = points.Select(p => p.Value).ToList();
        var max = values.OfType<decimal>().DefaultIfEmpty(0).Max();
        var min = Math.Min(0, values.OfType<decimal>().DefaultIfEmpty(0).Min());
        var span = max - min == 0 ? 1 : max - min;
        double X(int i) => points.Count <= 1 ? width / 2.0 : ar
            ? width - padX - (i * (width - 2.0 * padX) / (points.Count - 1))
            : padX + (i * (width - 2.0 * padX) / (points.Count - 1));
        double Y(decimal v) => height - padY - (double)((v - min) / span) * (height - 2 * padY);
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height + 16}\" role=\"img\" style=\"display:block;margin:4px 0\">");
        svg.Append(Invariant, $"<line x1=\"{padX}\" y1=\"{height - padY}\" x2=\"{width - padX}\" y2=\"{height - padY}\" stroke=\"#d0d7de\"/>");
        var path = string.Join(' ', points.Select((p, i) => p.Value is { } v ? string.Create(Invariant, $"{X(i):0.#},{Y(v):0.#}") : null).OfType<string>());
        if (path.Length > 0)
        {
            svg.Append("<polyline fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" points=\"").Append(path).Append("\"/>");
        }

        for (var i = 0; i < points.Count; i++)
        {
            svg.Append(Invariant, $"<text x=\"{X(i):0.#}\" y=\"{height + 10}\" font-size=\"7.5\" text-anchor=\"middle\" fill=\"#57606a\">{E(points[i].Month[2..])}</text>");
            if (points[i].Value is { } v)
            {
                svg.Append(Invariant, $"<text x=\"{X(i):0.#}\" y=\"{Y(v) - 5:0.#}\" font-size=\"7.5\" text-anchor=\"middle\" fill=\"#1b1f24\">{E(Format(v, kind))}</text>");
            }
        }

        return svg.Append("</svg>").ToString();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    public static ExportedFile Excel(DashboardDataDto dashboard, string language, UnitName unitName)
    {
        var ar = language == "ar";
        using var workbook = new XLWorkbook { RightToLeft = ar };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (w, d) in dashboard.Widgets.Zip(dashboard.Data))
        {
            if (d.State == "unavailable" || w.Type == WidgetTypes.Brief)
            {
                continue;
            }

            var name = new string(Title(w, d, ar).Where(c => !"[]*?/\\:".Contains(c, StringComparison.Ordinal)).ToArray());
            name = name.Length > 28 ? name[..28] : name;
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = (name.Length > 25 ? name[..25] : name) + " " + n.ToString(Invariant);
            }

            var sheet = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(unique) ? w.Id : unique);
            sheet.RightToLeft = ar;
            var rows = new List<string?[]>();
            string[] headers;
            string Name(string dimension, string code) => dimension == "unit" ? unitName(code) : Label(d, dimension, code, ar);
            if (d.Scorecard is { } card)
            {
                headers = [ar ? "الإدارة" : "Department", .. card.Columns.Select(c => ar ? c.LabelAr : c.LabelEn)];
                rows.AddRange(card.Rows.Select(r => new[] { ar ? r.NameAr ?? r.NameEn : r.NameEn ?? r.NameAr }
                    .Concat(r.Cells.Zip(card.Columns).Select(p => Format(p.First.Value, p.Second.ValueKind))).ToArray()));
            }
            else if (d.Items is { } items)
            {
                headers = [ar ? "الرقم" : "Number", ar ? "العنوان" : "Title", ar ? "الحالة" : "Status", ar ? "التاريخ" : "Date"];
                rows.AddRange(items.Select(i => new[] { i.Number, ar ? i.TitleAr ?? i.TitleEn : i.TitleEn ?? i.TitleAr, i.StatusCode, i.Date?.ToString("yyyy-MM-dd", Invariant) }));
            }
            else if (d.Rows is { Count: > 0 } breakdown && w.Dimension is { } dim)
            {
                headers = w.Series is { } s
                    ? [ar ? "البند" : "Item", ar ? "الفئة" : "Series", ar ? "القيمة" : "Value", ar ? "الفترة السابقة" : "Previous"]
                    : [ar ? "البند" : "Item", ar ? "القيمة" : "Value", ar ? "الفترة السابقة" : "Previous"];
                rows.AddRange(breakdown.Select(r => w.Series is { } series
                    ? new[] { Name(dim, r.Key[dim]), Name(series, r.Key[series]), Format(r.Value, d.ValueKind), Format(r.Previous, d.ValueKind) }
                    : new[] { Name(dim, r.Key[dim]), Format(r.Value, d.ValueKind), Format(r.Previous, d.ValueKind) }));
            }
            else if (d.Trend is { } trend && w.Type == WidgetTypes.Line)
            {
                headers = [ar ? "الشهر" : "Month", ar ? "القيمة" : "Value"];
                rows.AddRange(trend.Select(p => new[] { p.Month, Format(p.Value, d.ValueKind) }));
            }
            else
            {
                headers = [ar ? "القيمة" : "Value", ar ? "الفترة السابقة" : "Previous"];
                rows.Add([Format(d.Value, d.ValueKind), Format(d.Previous, d.ValueKind)]);
            }

            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
                sheet.Cell(1, c + 1).Style.Font.Bold = true;
            }

            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cell(r + 2, c + 1).Value = rows[r][c];
                }
            }

            sheet.Columns().AdjustToContents(1, 200);
        }

        if (workbook.Worksheets.Count == 0)
        {
            workbook.Worksheets.Add("-");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportedFile(stream.ToArray(), ExcelExport.ContentType, FileName(dashboard.Code, "xlsx"));
    }
}
