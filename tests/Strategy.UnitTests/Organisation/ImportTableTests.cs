using System.Text;
using Strategy.Platform.Organisation.Import;

namespace Strategy.UnitTests.Organisation;

public class ImportTableTests
{
    [Fact]
    public void Template_round_trips_through_the_reader_using_the_hidden_key_row()
    {
        var template = OrgImporter.Template(ImportKinds.Units);

        var rows = ImportTable.Read(new MemoryStream(template), "units.xlsx", OrgImporter.UnitColumns);

        rows.Count.ShouldBe(3);
        rows[0].Number.ShouldBe(3);
        rows[0]["code"].ShouldBe("ORG");
        rows[1]["parent_code"].ShouldBe("ORG");
        rows[2]["cost_centre"].ShouldBe("CC-210");
    }

    [Fact]
    public void Csv_reads_quotes_commas_newlines_and_arabic_with_english_or_key_headers()
    {
        var csv = "﻿code,Parent code,الاسم بالعربية,Name (English),level_code\r\n"
            + "FIN,ORG,\"الإدارة، المالية\",\"Finance \"\"Dept\"\"\",department\r\n"
            + "\r\n"
            + "OPS,,\"سطر\nثان\",Ops,sector\n";

        var rows = ImportTable.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "units.csv", OrgImporter.UnitColumns);

        rows.Count.ShouldBe(2);
        rows[0]["name_ar"].ShouldBe("الإدارة، المالية");
        rows[0]["name_en"].ShouldBe("Finance \"Dept\"");
        rows[0]["parent_code"].ShouldBe("ORG");
        rows[1]["parent_code"].ShouldBeNull();
        rows[1]["name_ar"].ShouldBe("سطر\nثان");
    }

    [Theory]
    [InlineData("2025-03-01", 2025, 3, 1)]
    [InlineData("01/03/2025", 2025, 3, 1)]
    [InlineData("2025-03-01T00:00:00", 2025, 3, 1)]
    public void Dates_parse_in_the_accepted_formats(string value, int year, int month, int day) =>
        ImportTable.ParseDate(value).ShouldBe(new DateOnly(year, month, day));

    [Fact]
    public void Unknown_date_or_yes_no_values_are_format_errors()
    {
        Should.Throw<FormatException>(() => ImportTable.ParseDate("March 1st"));
        Should.Throw<FormatException>(() => ImportTable.ParseYesNo("maybe"));
        ImportTable.ParseYesNo("نعم").ShouldBe(true);
        ImportTable.ParseYesNo("N").ShouldBe(false);
        ImportTable.ParseYesNo(null).ShouldBeNull();
    }

    [Fact]
    public void Report_adds_an_errors_column()
    {
        var rows = new List<ImportRow> { new(3, new Dictionary<string, string?> { ["code"] = "X" }) };
        var report = ImportTable.Report("units", OrgImporter.UnitColumns, rows, new[] { (3, "level_code: validation.notEmpty") }.ToLookup(x => x.Item1, x => x.Item2));

        var read = ImportTable.Read(new MemoryStream(report), "r.xlsx", [.. OrgImporter.UnitColumns, new ImportColumn("errors", "Errors", "الأخطاء")]);
        read[0]["errors"].ShouldBe("level_code: validation.notEmpty");
    }
}
