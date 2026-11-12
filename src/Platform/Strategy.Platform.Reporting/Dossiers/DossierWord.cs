using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Strategy.Platform.Reporting.Dossiers;

/// <summary>
/// Word (.docx) dossiers with DocumentFormat.OpenXml (MIT, ADR 0185): heading styles, cover page, page header / footer, label / value tables,
/// related lists, and charts, Gantt and the risk matrix as native shaded tables. Arabic: right-to-left section, paragraphs (<c>bidi</c>), runs
/// (<c>rtl</c>) and tables (<c>bidiVisual</c>).
/// </summary>
public static class DossierWord
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string Font = "Arial";

    public static byte[] Build(DossierDocument doc, string language, DossierBranding branding)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(branding);
        var rtl = language != DossierLanguages.English;
        var bilingual = language == DossierLanguages.Bilingual;
        var color = branding.PrimaryColor.TrimStart('#');
        string T(string? ar, string? en) => bilingual ? $"{ar} — {en}" : rtl ? ar ?? en ?? string.Empty : en ?? ar ?? string.Empty;

        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            AddStyles(main, color, rtl);
            var body = new Body();
            main.Document = new Document(body);
            var d = doc.Definition;

            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(Paragraph(T(d.HeaderAr ?? doc.TemplateLabelAr, d.HeaderEn ?? doc.TemplateLabelEn) + "  ·  " + doc.Number, rtl, "Small"));
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(Paragraph(
                T(branding.OrganisationAr, branding.OrganisationEn) + "  ·  " + T(d.FooterAr, d.FooterEn) + "  ·  "
                + branding.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rtl, "Small"));

            if (d.Cover.Show)
            {
                body.Append(Paragraph(T(d.Cover.TitleAr ?? doc.TemplateLabelAr, d.Cover.TitleEn ?? doc.TemplateLabelEn), rtl, "Title", JustificationValues.Center));
                body.Append(Paragraph(T(doc.NameAr, doc.NameEn), rtl, "Subtitle", JustificationValues.Center));
                if (doc.SubtitleAr is not null)
                {
                    body.Append(Paragraph(rtl ? doc.SubtitleAr : doc.SubtitleEn ?? doc.SubtitleAr, rtl, "Subtitle", JustificationValues.Center));
                }

                if (d.Cover.ShowOrganisation)
                {
                    body.Append(Paragraph(T(branding.OrganisationAr, branding.OrganisationEn), rtl, null, JustificationValues.Center));
                }

                if (d.Cover.ShowDate)
                {
                    body.Append(Paragraph(branding.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rtl, null, JustificationValues.Center));
                }

                body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }

            foreach (var block in doc.Blocks)
            {
                if (block.PageBreak)
                {
                    body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                }

                body.Append(Paragraph(T(block.TitleAr, block.TitleEn), rtl, "Heading1"));
                Block(body, block, rtl, bilingual, color);
            }

            var section = new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1134, Bottom = 1134, Left = 850, Right = 850, Header = 567, Footer = 567, Gutter = 0 });
            if (rtl)
            {
                section.Append(new BiDi());
            }

            body.Append(section);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static void Block(Body body, DossierBlock block, bool rtl, bool bilingual, string color)
    {
        string Empty() => bilingual ? "لا توجد بيانات — No data" : rtl ? "لا توجد بيانات" : "No data";
        switch (block)
        {
            case { Pairs: { } pairs }:
                body.Append(Table(
                    null,
                    pairs.Select(p => (IReadOnlyList<string?>)[bilingual ? $"{p.LabelAr} / {p.LabelEn}" : rtl ? p.LabelAr : p.LabelEn,
                        (bilingual && p.ValueAr != p.ValueEn ? $"{p.ValueAr} / {p.ValueEn}" : rtl ? p.ValueAr : p.ValueEn) ?? "—"]).ToList(),
                    rtl, color, firstColumnShade: "F6F8FA"));
                break;

            case { Text: { } text }:
                if (bilingual)
                {
                    body.Append(Paragraph(text.Ar, true, null));
                    body.Append(Paragraph(text.En, false, null));
                }
                else
                {
                    body.Append(Paragraph(rtl ? text.Ar ?? text.En : text.En ?? text.Ar, rtl, null));
                }

                break;

            case { Table: { } table }:
                if (table.RowsAr.Count == 0)
                {
                    body.Append(Paragraph(Empty(), rtl, null));
                    break;
                }

                var headers = table.HeadersAr.Select((h, i) => bilingual ? $"{h} / {table.HeadersEn[i]}" : rtl ? h : table.HeadersEn[i]).ToList();
                body.Append(Table(headers, rtl ? table.RowsAr : table.RowsEn, rtl, color));
                break;

            case { Chart: { } chart }:
                if (chart.CategoriesAr.Count == 0)
                {
                    body.Append(Paragraph(Empty(), rtl, null));
                    break;
                }

                // The chart as its values: one row per category, one column per series (an editable, accessible table in Word).
                var categories = rtl ? chart.CategoriesAr : chart.CategoriesEn;
                var seriesNames = chart.Series.Select((s, i) => (rtl ? s.NameAr : s.NameEn) ?? (chart.Series.Count == 1 ? (rtl ? "القيمة" : "Value") : $"#{i + 1}")).ToList();
                var max = chart.Series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max();
                var chartRows = categories.Select((c, i) => (IReadOnlyList<string?>)new List<string?> { c ?? "—" }
                    .Concat(chart.Series.Select(s => s.Values[i].ToString("0.##", CultureInfo.InvariantCulture)))
                    .Append(max <= 0 ? string.Empty : new string('█', (int)Math.Round(chart.Series[0].Values[i] / max * 20)))
                    .ToList()).ToList();
                body.Append(Table([rtl ? "الفئة" : "Category", .. seriesNames, string.Empty], chartRows, rtl, color));
                break;

            case { Gantt: { } items }:
                if (items.Count == 0)
                {
                    body.Append(Paragraph(Empty(), rtl, null));
                    break;
                }

                body.Append(Gantt(items, rtl, color));
                break;

            case { Matrix: { } matrix }:
                body.Append(Matrix(matrix, rtl, bilingual));
                break;
        }
    }

    private static Table Gantt(IReadOnlyList<DossierGanttItem> items, bool rtl, string color)
    {
        var from = items.Min(i => new[] { i.Start, i.ActualStart }.Where(x => x is not null).Min()!.Value);
        var to = items.Max(i => new[] { i.End, i.ActualEnd }.Where(x => x is not null).Max()!.Value);
        var months = new List<DateOnly>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to && months.Count < 36; m = m.AddMonths(1))
        {
            months.Add(m);
        }

        var table = NewTable(rtl);
        var head = new TableRow();
        head.Append(Cell(rtl ? "البند" : "Item", rtl, color, bold: true, white: true));
        foreach (var m in months)
        {
            head.Append(Cell(m.ToString("yy-MM", CultureInfo.InvariantCulture), rtl, color, bold: true, white: true, small: true));
        }

        table.Append(head);
        foreach (var item in items)
        {
            var row = new TableRow();
            row.Append(Cell((rtl ? item.LabelAr ?? item.LabelEn : item.LabelEn ?? item.LabelAr) ?? "—", rtl, null));
            foreach (var m in months)
            {
                var monthEnd = m.AddMonths(1).AddDays(-1);
                var planned = item.Start <= monthEnd && item.End >= m;
                var actualStart = item.ActualStart ?? item.Start;
                var actualEnd = item.ActualEnd;
                var actual = actualEnd is not null && actualStart <= monthEnd && actualEnd >= m;
                row.Append(Cell(actual ? "✓" : string.Empty, rtl, planned ? color : null, white: planned, small: true));
            }

            table.Append(row);
        }

        return table;
    }

    private static Table Matrix(DossierMatrix matrix, bool rtl, bool bilingual)
    {
        var table = NewTable(rtl);
        var likelihood = bilingual ? "الاحتمالية / Likelihood" : rtl ? "الاحتمالية" : "Likelihood";
        var impact = bilingual ? "الأثر / Impact" : rtl ? "الأثر" : "Impact";
        for (var l = matrix.Size; l >= 1; l--)
        {
            var row = new TableRow();
            row.Append(Cell($"{likelihood} {l}", rtl, null, bold: true, small: true));
            for (var i = 1; i <= matrix.Size; i++)
            {
                var count = matrix.Counts[l - 1, i - 1];
                row.Append(Cell(count == 0 ? string.Empty : count.ToString(CultureInfo.InvariantCulture), rtl, DossierHtml.MatrixColour(l, i, matrix.Size).TrimStart('#'), center: true));
            }

            table.Append(row);
        }

        var axis = new TableRow();
        axis.Append(Cell(string.Empty, rtl, null));
        for (var i = 1; i <= matrix.Size; i++)
        {
            axis.Append(Cell($"{impact} {i}", rtl, null, bold: true, small: true, center: true));
        }

        table.Append(axis);
        return table;
    }

    private static Table Table(IReadOnlyList<string>? headers, IReadOnlyList<IReadOnlyList<string?>> rows, bool rtl, string color, string? firstColumnShade = null)
    {
        var table = NewTable(rtl);
        if (headers is not null)
        {
            var head = new TableRow(new TableRowProperties(new TableHeader()));
            foreach (var h in headers)
            {
                head.Append(Cell(h, rtl, color, bold: true, white: true));
            }

            table.Append(head);
        }

        foreach (var r in rows)
        {
            var row = new TableRow();
            for (var i = 0; i < r.Count; i++)
            {
                row.Append(Cell(r[i] ?? string.Empty, rtl, i == 0 ? firstColumnShade : null, bold: i == 0 && firstColumnShade is not null));
            }

            table.Append(row);
        }

        return table;
    }

    private static Table NewTable(bool rtl)
    {
        var props = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "D0D7DE" }));
        if (rtl)
        {
            props.Append(new BiDiVisual());
        }

        return new Table(props);
    }

    private static TableCell Cell(string text, bool rtl, string? shade, bool bold = false, bool white = false, bool small = false, bool center = false)
    {
        var cellProps = new TableCellProperties();
        if (shade is not null)
        {
            cellProps.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shade });
        }

        var paragraph = Paragraph(text, rtl, null, center ? JustificationValues.Center : null);
        foreach (var run in paragraph.Elements<Run>())
        {
            var rp = run.RunProperties ??= new RunProperties();
            if (bold)
            {
                rp.Append(new Bold(), new BoldComplexScript());
            }

            if (white)
            {
                rp.Append(new Color { Val = "FFFFFF" });
            }

            if (small)
            {
                rp.Append(new FontSize { Val = "14" }, new FontSizeComplexScript { Val = "14" });
            }
        }

        return new TableCell(cellProps, paragraph);
    }

    private static Paragraph Paragraph(string? text, bool rtl, string? style, JustificationValues? justification = null)
    {
        var props = new ParagraphProperties();
        if (style is not null)
        {
            props.Append(new ParagraphStyleId { Val = style });
        }

        if (rtl)
        {
            props.Append(new BiDi());
        }

        if (justification is { } j)
        {
            props.Append(new Justification { Val = j });
        }

        var runProps = new RunProperties(new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font });
        if (rtl)
        {
            runProps.Append(new RightToLeftText());
        }

        var lines = (text ?? string.Empty).Split('\n');
        var run = new Run(runProps);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return new Paragraph(props, run);
    }

    private static void AddStyles(MainDocumentPart main, string color, bool rtl)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        Style Para(string id, string name, int sizeHalfPoints, bool bold, string? colour, int spacingAfter)
        {
            var runProps = new StyleRunProperties(
                new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font },
                new FontSize { Val = sizeHalfPoints.ToString(CultureInfo.InvariantCulture) },
                new FontSizeComplexScript { Val = sizeHalfPoints.ToString(CultureInfo.InvariantCulture) });
            if (bold)
            {
                runProps.Append(new Bold(), new BoldComplexScript());
            }

            if (colour is not null)
            {
                runProps.Append(new Color { Val = colour });
            }

            var paraProps = new StyleParagraphProperties(new SpacingBetweenLines { After = spacingAfter.ToString(CultureInfo.InvariantCulture) });
            if (rtl)
            {
                paraProps.Append(new BiDi());
            }

            return new Style(new StyleName { Val = name }, paraProps, runProps) { Type = StyleValues.Paragraph, StyleId = id };
        }

        var heading = Para("Heading1", "heading 1", 26, true, color, 120);
        heading.Append(new PrimaryStyle());
        styles.Styles = new Styles(
            Para("Normal", "Normal", 19, false, null, 60),
            heading,
            Para("Title", "Title", 44, true, color, 240),
            Para("Subtitle", "Subtitle", 28, false, "57606A", 160),
            Para("Small", "Small", 15, false, "57606A", 0));
        styles.Styles.Save();
    }
}
