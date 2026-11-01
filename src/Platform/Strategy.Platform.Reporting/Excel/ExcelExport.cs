using ClosedXML.Excel;

namespace Strategy.Platform.Reporting.Excel;

/// <summary>One-sheet Excel export: header row in the user's language, a right-to-left sheet for Arabic, frozen header, auto filter.</summary>
public static class ExcelExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        sheet.RightToLeft = rightToLeft;
        for (var c = 0; c < headers.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case null:
                        break;
                    case DateTimeOffset d:
                        cell.Value = d.UtcDateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                        break;
                    case long or int or decimal or double:
                        cell.Value = Convert.ToDouble(row[c], System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case bool b:
                        cell.Value = b;
                        break;
                    default:
                        // Text cells never become formulas (a value starting with '=' stays text).
                        cell.SetValue(row[c]!.ToString());
                        break;
                }
            }

            r++;
        }

        var header = sheet.Range(1, 1, 1, Math.Max(1, headers.Count));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f3a68");
        header.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(1);
        if (r > 2)
        {
            sheet.Range(1, 1, r - 1, headers.Count).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
