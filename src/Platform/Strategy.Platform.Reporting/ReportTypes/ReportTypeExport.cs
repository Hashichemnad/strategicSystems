using System.Net;
using System.Text;
using ClosedXML.Excel;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>A line of an exported report: a group heading (with its subtotals), a detail row, or the grand total.</summary>
public sealed record ExportLine(string Kind, int Level, IReadOnlyList<string?> Cells, IReadOnlyList<string?> Tones);

/// <summary>
/// Exports of type-based reports (ADR 0182): Excel (group lines with subtotals, RTL sheet for Arabic), PDF (chart as SVG above the grouped
/// table, bilingual header, Gotenberg) and CSV (UTF-8 with BOM, formula guard). Every export is built from the same run as the screen.
/// </summary>
public sealed class ReportTypeExport(IPdfRenderer pdf, ISettingsStore settings)
{
    public const string CsvContentType = "text/csv; charset=utf-8";

    private static string Fn(string function, bool ar) => function switch
    {
        SummaryFunctions.Sum => ar ? "المجموع" : "Sum",
        SummaryFunctions.Average => ar ? "المتوسط" : "Avg",
        SummaryFunctions.Min => ar ? "الأدنى" : "Min",
        SummaryFunctions.Max => ar ? "الأعلى" : "Max",
        _ => function,
    };

    /// <summary>Header row, then lines in display order: per group a heading with count and aggregates under their columns, the detail rows of leaf groups, the grand total.</summary>
    public static (IReadOnlyList<string> Headers, IReadOnlyList<ExportLine> Lines) Lines(ReportTypeRunDto run, string language)
    {
        ArgumentNullException.ThrowIfNull(run);
        var ar = language == "ar";
        var headers = run.Columns.Select(c => ar ? c.LabelAr : c.LabelEn).ToList();
        var lines = new List<ExportLine>();
        string?[] AggregateCells(ReportGroupDto group, string? label)
        {
            var cells = new string?[run.Columns.Count];
            for (var i = 0; i < run.Columns.Count; i++)
            {
                var values = group.Aggregates.Where(a => a.Column == run.Columns[i].Code && a.Value is not null).Select(a => $"{Fn(a.Function, ar)}: {a.Value}").ToList();
                cells[i] = values.Count == 0 ? null : string.Join(" · ", values);
            }

            var count = ar ? $"العدد: {group.Count}" : $"Count: {group.Count}";
            cells[0] = label is null ? count + (cells[0] is null ? string.Empty : " · " + cells[0]) : $"{label} ({group.Count})" + (cells[0] is null ? string.Empty : " · " + cells[0]);
            return cells;
        }

        if (run.GroupBy.Count == 0)
        {
            lines.AddRange(run.Rows.Select(r => new ExportLine("row", 0, r.Cells, r.Tones)));
        }
        else
        {
            var byPath = run.Rows.GroupBy(r => string.Join('\u001f', r.GroupKeys), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var group in run.Groups)
            {
                var indent = new string(' ', (group.Level - 1) * 3);
                lines.Add(new ExportLine("group", group.Level, AggregateCells(group, indent + (group.Label ?? "—")), new string?[run.Columns.Count]));
                if (group.Level == run.GroupBy.Count && byPath.TryGetValue(string.Join('\u001f', group.Keys), out var rows))
                {
                    lines.AddRange(rows.Select(r => new ExportLine("row", group.Level, r.Cells, r.Tones)));
                }
            }
        }

        if (run.GroupBy.Count > 0 || run.GrandTotal.Aggregates.Count > 0)
        {
            var total = AggregateCells(run.GrandTotal, ar ? "الإجمالي" : "Grand total");
            lines.Add(new ExportLine("total", 0, total, new string?[run.Columns.Count]));
        }

        return (headers, lines);
    }

    public static byte[] Csv(ReportTypeRunDto run, string language)
    {
        var (headers, lines) = Lines(run, language);
        var csv = new StringBuilder();
        static string Q(string? v)
        {
            var value = v ?? string.Empty;
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        csv.AppendJoin(',', headers.Select(Q)).Append("\r\n");
        foreach (var line in lines)
        {
            csv.AppendJoin(',', line.Cells.Select(Q)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    public static byte[] Excel(ReportTypeRunDto run, string language, string title)
    {
        var (headers, lines) = Lines(run, language);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(title));
        sheet.RightToLeft = language == "ar";
        for (var c = 0; c < headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        var r = 2;
        foreach (var line in lines)
        {
            for (var c = 0; c < line.Cells.Count; c++)
            {
                if (line.Cells[c] is { } text)
                {
                    sheet.Cell(r, c + 1).SetValue(text);
                }

                if (line.Tones[c] is { } tone)
                {
                    sheet.Cell(r, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml(ToneColour(tone));
                }
            }

            if (line.Kind != "row")
            {
                var range = sheet.Range(r, 1, r, Math.Max(1, headers.Count));
                range.Style.Font.Bold = true;
                range.Style.Fill.BackgroundColor = XLColor.FromHtml(line.Kind == "total" ? "#d0d7de" : "#eaeef2");
            }

            r++;
        }

        var header = sheet.Range(1, 1, 1, Math.Max(1, headers.Count));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f3a68");
        header.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
        if (run.Chart is { } chart)
        {
            var data = workbook.Worksheets.Add(language == "ar" ? "الرسم البياني" : "Chart data");
            data.RightToLeft = language == "ar";
            data.Cell(1, 1).Value = string.Empty;
            for (var s = 0; s < chart.Series.Count; s++)
            {
                data.Cell(1, s + 2).Value = chart.Series[s].Label ?? (language == "ar" ? chart.MeasureLabelAr : chart.MeasureLabelEn);
            }

            for (var i = 0; i < chart.Categories.Count; i++)
            {
                data.Cell(i + 2, 1).Value = chart.Categories[i] ?? "—";
                for (var s = 0; s < chart.Series.Count; s++)
                {
                    data.Cell(i + 2, s + 2).Value = (double)chart.Series[s].Values[i];
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray());
        clean = string.IsNullOrWhiteSpace(clean) ? "Report" : clean;
        return clean.Length > 31 ? clean[..31] : clean;
    }

    public static string ToneColour(string tone) => tone switch
    {
        "good" => "#dafbe1",
        "warning" => "#fff8c5",
        "bad" => "#ffebe9",
        _ => "#ddf4ff",
    };

    public async Task<Result<byte[]>> PdfAsync(ReportTypeRunDto run, string language, string title, CancellationToken ct)
    {
        var (headers, lines) = Lines(run, language);
        var ar = language == "ar";
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var document = new ReportDocument
        {
            TitleAr = title,
            TitleEn = title,
            OrganisationAr = branding.OrganisationNameAr,
            OrganisationEn = branding.OrganisationNameEn,
            Language = language,
            PrimaryColor = branding.PrimaryColor,
        };
        var html = new StringBuilder();
        if (run.Chart is { } chart)
        {
            html.Append("<div style=\"margin:0 0 12px\">")
                .Append(ChartSvg.Render(chart.Type, chart.Categories, chart.Series.Select(s => (s.Label, s.Values)).ToList(), ar, branding.PrimaryColor))
                .Append("</div>");
        }

        var dir = ar ? "rtl" : "ltr";
        html.Append("<table dir=\"").Append(dir).Append("\"><thead><tr>");
        foreach (var h in headers)
        {
            html.Append("<th>").Append(WebUtility.HtmlEncode(h)).Append("</th>");
        }

        html.Append("</tr></thead><tbody>");
        foreach (var line in lines)
        {
            var style = line.Kind switch
            {
                "group" => " style=\"font-weight:700;background:#eaeef2\"",
                "total" => " style=\"font-weight:700;background:#d0d7de\"",
                _ => string.Empty,
            };
            html.Append("<tr").Append(style).Append('>');
            for (var c = 0; c < line.Cells.Count; c++)
            {
                var tone = line.Tones[c] is { } t ? $" style=\"background:{ToneColour(t)}\"" : string.Empty;
                html.Append("<td dir=\"auto\"").Append(tone).Append('>').Append(WebUtility.HtmlEncode(line.Cells[c] ?? string.Empty)).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
        if (run.Truncated)
        {
            html.Append("<p>").Append(ar ? "تم اقتطاع النتائج عند حد عدد الصفوف." : "Results were cut at the row limit.").Append("</p>");
        }

        document.Block(html.ToString());
        try
        {
            return await pdf.RenderAsync(document.ToHtml(), ct).ConfigureAwait(false);
        }
        catch (PdfRenderingException)
        {
            return Error.Rule("reporting.pdfFailed", "reporting.errors.pdfFailed");
        }
    }
}
