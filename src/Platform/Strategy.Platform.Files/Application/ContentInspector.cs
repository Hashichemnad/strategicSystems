using System.IO.Compression;
using System.Text;

namespace Strategy.Platform.Files.Application;

/// <summary>
/// Identifies a file by its bytes (magic number) and checks that the name's extension says the same thing. SVG, HTML, scripts and
/// executables are never recognised, so they can never pass an allow-list.
/// </summary>
public static class ContentInspector
{
    private static readonly Dictionary<string, (string Kind, string ContentType)> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = (FileKinds.Pdf, "application/pdf"),
        [".png"] = (FileKinds.Png, "image/png"),
        [".jpg"] = (FileKinds.Jpeg, "image/jpeg"),
        [".jpeg"] = (FileKinds.Jpeg, "image/jpeg"),
        [".gif"] = (FileKinds.Gif, "image/gif"),
        [".webp"] = (FileKinds.Webp, "image/webp"),
        [".docx"] = (FileKinds.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        [".xlsx"] = (FileKinds.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        [".pptx"] = (FileKinds.Pptx, "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        [".doc"] = (FileKinds.Doc, "application/msword"),
        [".xls"] = (FileKinds.Xls, "application/vnd.ms-excel"),
        [".ppt"] = (FileKinds.Ppt, "application/vnd.ms-powerpoint"),
        [".txt"] = (FileKinds.Text, "text/plain; charset=utf-8"),
        [".csv"] = (FileKinds.Csv, "text/csv; charset=utf-8"),
    };

    public static string ContentTypeOf(string kind) => Extensions.Values.First(v => v.Kind == kind).ContentType;

    /// <summary>The kind the bytes and the extension agree on, or null.</summary>
    public static string? Identify(string fileName, ReadOnlySpan<byte> head, Func<Stream>? openZip = null)
    {
        if (!Extensions.TryGetValue(Path.GetExtension(fileName), out var claimed))
        {
            return null;
        }

        var sniffed = Sniff(head, openZip);
        if (sniffed is null)
        {
            return null;
        }

        // Plain text is the absence of a binary signature: CSV and TXT share it.
        if (sniffed == FileKinds.Text && claimed.Kind is FileKinds.Text or FileKinds.Csv)
        {
            return claimed.Kind;
        }

        // Legacy Office files share the OLE container signature.
        if (sniffed == FileKinds.Doc && claimed.Kind is FileKinds.Doc or FileKinds.Xls or FileKinds.Ppt)
        {
            return claimed.Kind;
        }

        return sniffed == claimed.Kind ? sniffed : null;
    }

    public static string? Sniff(ReadOnlySpan<byte> head, Func<Stream>? openZip = null)
    {
        if (head.StartsWith("%PDF-"u8))
        {
            return FileKinds.Pdf;
        }

        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return FileKinds.Png;
        }

        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return FileKinds.Jpeg;
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return FileKinds.Gif;
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return FileKinds.Webp;
        }

        if (head.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
        {
            return FileKinds.Doc;
        }

        if (head.StartsWith("PK\u0003\u0004"u8))
        {
            return openZip is null ? null : OfficeKind(openZip);
        }

        return IsText(head) ? FileKinds.Text : null;
    }

    /// <summary>An OOXML package names its main part in <c>[Content_Types].xml</c>; any other ZIP (or a broken one) is refused.</summary>
    private static string? OfficeKind(Func<Stream> open)
    {
        try
        {
            using var stream = open();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var types = zip.GetEntry("[Content_Types].xml");
            if (types is null)
            {
                return null;
            }

            using var reader = new StreamReader(types.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            if (xml.Contains("wordprocessingml.document.main", StringComparison.Ordinal))
            {
                return FileKinds.Docx;
            }

            if (xml.Contains("spreadsheetml.sheet.main", StringComparison.Ordinal))
            {
                return FileKinds.Xlsx;
            }

            return xml.Contains("presentationml.presentation.main", StringComparison.Ordinal) ? FileKinds.Pptx : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Valid UTF-8 without NUL or other control bytes (tabs and line breaks allowed); an optional BOM is accepted.</summary>
    private static bool IsText(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
        {
            return false;
        }

        if (head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            head = head[3..];
        }

        foreach (var b in head)
        {
            if (b < 0x09 || (b > 0x0D && b < 0x20) || b == 0x7F)
            {
                return false;
            }
        }

        // Cut-off multi-byte sequences at the end of the sample are fine; anything else must decode.
        var trimmed = head;
        for (var i = 0; i < 3 && trimmed.Length > 0 && (trimmed[^1] & 0x80) != 0; i++)
        {
            trimmed = trimmed[..^1];
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(trimmed);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Keeps the name readable but safe: no path, no control characters, at most 200 characters.</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var clean = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?' or '/')).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "file";
        }

        if (clean.Length > 200)
        {
            var extension = Path.GetExtension(clean);
            clean = clean[..(200 - extension.Length)] + extension;
        }

        return clean;
    }
}
