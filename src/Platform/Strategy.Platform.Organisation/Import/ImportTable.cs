using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Strategy.Platform.Organisation.Import;

/// <summary>A column of an import template: stable key, bilingual header label, required flag.</summary>
public sealed record ImportColumn(string Key, string LabelEn, string LabelAr, bool Required = false);

/// <summary>One data row of an uploaded file: 1-based sheet row number plus values by column key.</summary>
public sealed record ImportRow(int Number, IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string key] => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Reads .xlsx and .csv uploads into <see cref="ImportRow"/>s and writes templates and reports.</summary>
public static class ImportTable
{
    public const int MaxRows = 20_000;

    /// <summary>
    /// Header detection: a row whose cells match the column keys (row 2 of our template, hidden) wins; otherwise the first
    /// row is read as headers, matching keys, English labels or Arabic labels (also "English / عربي").
    /// </summary>
    public static IReadOnlyList<ImportRow> Read(Stream content, string fileName, IReadOnlyList<ImportColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(content) : ReadXlsx(content);
        if (rows.Count == 0)
        {
            return [];
        }

        var headerIndex = rows.FindIndex(r => r.Cells.Count(c => columns.Any(col => col.Key == c?.Trim())) >= Math.Min(2, columns.Count));
        if (headerIndex < 0)
        {
            headerIndex = 0;
        }

        var map = new Dictionary<int, string>();
        var header = rows[headerIndex].Cells;
        for (var i = 0; i < header.Count; i++)
        {
            if (Match(header[i], columns) is { } key)
            {
                map[i] = key;
            }
        }

        var result = new List<ImportRow>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (index, key) in map)
            {
                values[key] = index < row.Cells.Count ? row.Cells[index] : null;
            }

            result.Add(new ImportRow(row.Number, values));
            if (result.Count > MaxRows)
            {
                throw new InvalidDataException("Too many rows.");
            }
        }

        return result;
    }

    /// <summary>Template: bilingual header row, hidden key row, frozen panes, required columns marked with *.</summary>
    public static byte[] Template(string sheetName, IReadOnlyList<ImportColumn> columns, IEnumerable<IReadOnlyList<string?>> examples)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(examples);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        WriteHeader(sheet, columns);
        var rowNumber = 3;
        foreach (var example in examples)
        {
            for (var i = 0; i < example.Count; i++)
            {
                sheet.Cell(rowNumber, i + 1).SetValue(example[i] ?? string.Empty);
            }

            rowNumber++;
        }

        sheet.Columns().AdjustToContents(1, 3);
        return Save(workbook);
    }

    /// <summary>Validation report: the uploaded rows with an extra "errors" column.</summary>
    public static byte[] Report(string sheetName, IReadOnlyList<ImportColumn> columns, IReadOnlyList<ImportRow> rows, ILookup<int, string> errors)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(errors);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        var all = columns.Append(new ImportColumn("errors", "Errors", "الأخطاء")).ToList();
        WriteHeader(sheet, all);
        var rowNumber = 3;
        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                sheet.Cell(rowNumber, i + 1).SetValue(row[columns[i].Key] ?? string.Empty);
            }

            var messages = string.Join("; ", errors[row.Number]);
            var cell = sheet.Cell(rowNumber, all.Count);
            cell.SetValue(messages);
            if (messages.Length > 0)
            {
                cell.Style.Font.FontColor = XLColor.DarkRed;
            }

            rowNumber++;
        }

        sheet.Columns().AdjustToContents(1, 3);
        return Save(workbook);
    }

    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];
        if (DateOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : throw new FormatException(value);
    }

    public static bool? ParseYesNo(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "y" or "yes" or "true" or "1" or "نعم" => true,
            "n" or "no" or "false" or "0" or "لا" => false,
            _ => throw new FormatException(value),
        };

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<ImportColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var label = sheet.Cell(1, i + 1);
            label.SetValue($"{column.LabelEn} / {column.LabelAr}{(column.Required ? " *" : string.Empty)}");
            label.Style.Font.Bold = true;
            label.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            sheet.Cell(2, i + 1).SetValue(column.Key);
        }

        sheet.Row(2).Hide();
        sheet.SheetView.FreezeRows(2);
    }

    private static string? Match(string? header, IReadOnlyList<ImportColumn> columns)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var text = header.Replace("*", string.Empty, StringComparison.Ordinal).Trim();
        foreach (var column in columns)
        {
            if (string.Equals(text, column.Key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, column.LabelEn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, column.LabelAr, StringComparison.Ordinal)
                || string.Equals(text, $"{column.LabelEn} / {column.LabelAr}", StringComparison.OrdinalIgnoreCase))
            {
                return column.Key;
            }
        }

        return null;
    }

    private sealed record RawRow(int Number, List<string?> Cells);

    private static List<RawRow> ReadXlsx(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return [];
        }

        var lastColumn = used.LastColumn().ColumnNumber();
        var rows = new List<RawRow>();
        foreach (var row in sheet.RowsUsed())
        {
            var cells = new List<string?>();
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = row.Cell(c);
                cells.Add(cell.DataType switch
                {
                    XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
                    XLDataType.Boolean => cell.GetBoolean() ? "Y" : "N",
                    _ => cell.GetString(),
                });
            }

            rows.Add(new RawRow(row.RowNumber(), cells));
        }

        return rows;
    }

    /// <summary>RFC 4180 CSV (comma, double-quote escaping), UTF-8 with or without BOM.</summary>
    private static List<RawRow> ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var rows = new List<RawRow>();
        var cells = new List<string?>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowStart = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (ch == '\n')
                    {
                        line++;
                    }

                    cell.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                cells.Add(cell.ToString());
                cell.Clear();
                rows.Add(new RawRow(rowStart, cells));
                cells = [];
                line++;
                rowStart = line;
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            rows.Add(new RawRow(rowStart, cells));
        }

        return rows;
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
