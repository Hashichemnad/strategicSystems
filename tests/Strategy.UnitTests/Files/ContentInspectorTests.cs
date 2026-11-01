using System.IO.Compression;
using System.Text;
using Strategy.Platform.Files;
using Strategy.Platform.Files.Application;

namespace Strategy.UnitTests.Files;

public class ContentInspectorTests
{
    public static TheoryData<byte[], string, string?> Cases => new()
    {
        { "%PDF-1.7 ..."u8.ToArray(), "a.pdf", FileKinds.Pdf },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], "a.png", FileKinds.Png },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0], "a.JPG", FileKinds.Jpeg },
        { "GIF89a..."u8.ToArray(), "a.gif", FileKinds.Gif },
        { "RIFF\0\0\0\0WEBPVP8 "u8.ToArray(), "a.webp", FileKinds.Webp },
        { [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0], "old.xls", FileKinds.Xls },
        { "name,value\nأ,1\n"u8.ToArray(), "data.csv", FileKinds.Csv },
        { [0xEF, 0xBB, 0xBF, .. "ملاحظة"u8.ToArray()], "note.txt", FileKinds.Text },
        // Disguised or dangerous content.
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], "a.jpg", null },
        { "<svg onload=alert(1)>"u8.ToArray(), "a.png", null },
        { "<svg onload=alert(1)>"u8.ToArray(), "a.svg", null },
        { "<html></html>"u8.ToArray(), "a.html", null },
        { "MZ\u0090\0"u8.ToArray(), "a.exe", null },
        { "%PDF-1.7"u8.ToArray(), "a.docx", null },
        { "text with a NUL \0 byte"u8.ToArray(), "a.txt", null },
        { [0xC3, 0x28, 0x41, 0x42], "bad-utf8.txt", null },
        { [], "empty.txt", null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Kind_comes_from_the_bytes_and_must_match_the_extension(byte[] content, string name, string? expected) =>
        ContentInspector.Identify(name, content).ShouldBe(expected);

    [Theory]
    [InlineData("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "a.docx", FileKinds.Docx)]
    [InlineData("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", "a.xlsx", FileKinds.Xlsx)]
    [InlineData("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", "a.docx", null)]
    [InlineData("payload.js", "application/javascript", "a.xlsx", null)]
    public void Office_packages_are_recognised_by_their_main_part(string part, string contentType, string name, string? expected)
    {
        var zip = Zip(part, contentType);
        ContentInspector.Identify(name, zip.AsSpan(0, Math.Min(zip.Length, 8192)), () => new MemoryStream(zip)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\temp\\report<1>.pdf", "report1.pdf")]
    [InlineData("تقرير.pdf", "تقرير.pdf")]
    [InlineData("\u0000\u0001", "file")]
    public void File_names_are_made_safe(string input, string expected) => ContentInspector.SafeName(input).ShouldBe(expected);

    private static byte[] Zip(string part, string contentType)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open(), Encoding.UTF8))
            {
                writer.Write($"<Types><Override PartName=\"/{part}\" ContentType=\"{contentType}\"/></Types>");
            }

            using var entry = new StreamWriter(archive.CreateEntry(part).Open());
            entry.Write("<x/>");
        }

        return memory.ToArray();
    }
}
