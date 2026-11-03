using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Strategy.Platform.Kernel.Results;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Strategy.Platform.AI.Documents;

/// <summary>Plain text of an uploaded document with page / slide markers (<c>[page 2]</c>, <c>[slide 3]</c>).</summary>
public sealed record ExtractedText(string Kind, string Text, int Sections, bool Truncated);

/// <summary>
/// Text extraction for document-to-form (ADR 0030), free libraries only: PdfPig (Apache-2.0) for PDF, DocumentFormat.OpenXml (MIT) for Word and
/// PowerPoint, plain UTF-8 text. The kind is recognised from the bytes, not from the file name. Arabic: PDF letters are put into reading order
/// from their positions (right-to-left lines, left-to-right runs of Latin letters and digits kept), and presentation forms are normalised (NFKC).
/// </summary>
public static class DocumentTextExtractor
{
    public const int MaxCharacters = 60_000;
    public const long MaxBytes = 10 * 1024 * 1024;

    public static Result<ExtractedText> Extract(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaxBytes)
        {
            return Error.Rule("ai.extract.size", "ai.errors.extractSize", new Dictionary<string, object?> { ["maxMb"] = MaxBytes / 1024 / 1024 });
        }

        try
        {
            var (kind, text, sections) = Detect(content, fileName) switch
            {
                "pdf" => ("pdf", Pdf(content, out var pages), pages),
                "docx" => ("docx", Word(content), 1),
                "pptx" => ("pptx", Slides(content, out var slides), slides),
                "txt" => ("txt", Encoding.UTF8.GetString(content), 1),
                _ => (string.Empty, string.Empty, 0),
            };
            if (kind.Length == 0)
            {
                return Error.Rule("ai.extract.unsupported", "ai.errors.extractUnsupported");
            }

            text = Clean(text);
            if (text.Count(char.IsLetterOrDigit) < 20)
            {
                return Error.Rule("ai.extract.noText", "ai.errors.extractNoText");
            }

            var truncated = text.Length > MaxCharacters;
            return new ExtractedText(kind, truncated ? text[..MaxCharacters] : text, sections, truncated);
        }
#pragma warning disable CA1031 // any parser failure of an uploaded file is the same answer for the user
        catch (Exception)
#pragma warning restore CA1031
        {
            return Error.Rule("ai.extract.unreadable", "ai.errors.extractUnreadable");
        }
    }

    /// <summary><c>pdf</c>, <c>docx</c>, <c>pptx</c>, <c>txt</c> or empty, from the content.</summary>
    public static string Detect(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.AsSpan().StartsWith("%PDF-"u8))
        {
            return "pdf";
        }

        if (content.AsSpan().StartsWith("PK\u0003\u0004"u8))
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            if (zip.GetEntry("word/document.xml") is not null)
            {
                return "docx";
            }

            return zip.GetEntry("ppt/presentation.xml") is not null ? "pptx" : string.Empty;
        }

        var looksText = string.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase) && !content.Contains((byte)0);
        if (looksText)
        {
            try
            {
                _ = new UTF8Encoding(false, true).GetString(content);
                return "txt";
            }
            catch (DecoderFallbackException)
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static string Pdf(byte[] content, out int pages)
    {
        using var document = PdfDocument.Open(content);
        var builder = new StringBuilder();
        pages = document.NumberOfPages;
        foreach (var page in document.GetPages())
        {
            builder.Append("[page ").Append(page.Number).Append("]\n");
            foreach (var line in Lines(page.Letters))
            {
                builder.Append(line).Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Letters grouped into lines (top to bottom) and put into reading order.</summary>
    internal static IEnumerable<string> Lines(IReadOnlyList<Letter> letters)
    {
        var lines = new List<List<Letter>>();
        foreach (var letter in letters.Where(l => !string.IsNullOrEmpty(l.Value)).OrderByDescending(l => l.StartBaseLine.Y))
        {
            var tolerance = Math.Max(1.5, letter.PointSize * 0.4);
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].StartBaseLine.Y - letter.StartBaseLine.Y) <= tolerance);
            if (line is null)
            {
                lines.Add([letter]);
            }
            else
            {
                line.Add(letter);
            }
        }

        foreach (var line in lines)
        {
            yield return LineText(line.OrderBy(l => l.StartBaseLine.X).ToList());
        }
    }

    /// <summary>Visual left-to-right letters → logical text. A line with more Arabic than Latin letters is read right to left.</summary>
    internal static string LineText(IReadOnlyList<Letter> visual)
    {
        var tokens = new List<string>();
        for (var i = 0; i < visual.Count; i++)
        {
            if (i > 0)
            {
                var gap = visual[i].StartBaseLine.X - visual[i - 1].EndBaseLine.X;
                if (gap > Math.Max(visual[i].Width, 1) * 0.3 && visual[i].Value != " " && visual[i - 1].Value != " ")
                {
                    tokens.Add(" ");
                }
            }

            tokens.Add(visual[i].Value);
        }

        return Logical(tokens);
    }

    /// <summary>Reorders visual tokens into logical order when the line is right-to-left (public for tests).</summary>
    public static string Logical(IReadOnlyList<string> visualTokens)
    {
        ArgumentNullException.ThrowIfNull(visualTokens);
        var text = string.Concat(visualTokens);
        var arabic = text.Count(IsArabic);
        var latin = text.Count(c => char.IsAsciiLetter(c));
        if (arabic <= latin)
        {
            return text;
        }

        var reversed = visualTokens.Reverse().ToList();
        var result = new StringBuilder();
        var i = 0;
        while (i < reversed.Count)
        {
            if (IsLtr(reversed[i]))
            {
                var j = i;
                while (j + 1 < reversed.Count && (IsLtr(reversed[j + 1]) || (reversed[j + 1] is "." or "," or "/" or "-" or ":" && j + 2 < reversed.Count && IsLtr(reversed[j + 2]))))
                {
                    j++;
                }

                for (var k = j; k >= i; k--)
                {
                    result.Append(reversed[k]);
                }

                i = j + 1;
            }
            else
            {
                result.Append(reversed[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool IsLtr(string token) => token.Length > 0 && token.All(c => char.IsAsciiLetterOrDigit(c));

    internal static bool IsArabic(char c) => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

    private static string Word(byte[] content)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(content), false);
        var body = document.MainDocumentPart?.Document?.Body;
        var builder = new StringBuilder();
        if (body is null)
        {
            return string.Empty;
        }

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case W.Paragraph paragraph:
                    builder.Append(paragraph.InnerText).Append('\n');
                    break;
                case W.Table table:
                    foreach (var row in table.Elements<W.TableRow>())
                    {
                        builder.Append(string.Join(" | ", row.Elements<W.TableCell>().Select(c => string.Join(" ", c.Elements<W.Paragraph>().Select(p => p.InnerText)))))
                            .Append('\n');
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static string Slides(byte[] content, out int count)
    {
        using var document = PresentationDocument.Open(new MemoryStream(content), false);
        var presentation = document.PresentationPart!;
        var ids = presentation.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];
        var builder = new StringBuilder();
        count = ids.Count;
        var n = 0;
        foreach (var id in ids)
        {
            var slide = (SlidePart)presentation.GetPartById(id.RelationshipId!.Value!);
            builder.Append("[slide ").Append(++n).Append("]\n");
            AppendParagraphs(builder, slide.Slide?.Descendants<A.Paragraph>() ?? []);
            if (slide.NotesSlidePart?.NotesSlide is { } notes)
            {
                AppendParagraphs(builder, notes.Descendants<A.Paragraph>());
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static void AppendParagraphs(StringBuilder builder, IEnumerable<A.Paragraph> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            var text = string.Concat(paragraph.Descendants<A.Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.Append(text).Append('\n');
            }
        }
    }

    /// <summary>NFKC (Arabic presentation forms → letters, ligatures split), no control characters, no runs of blank lines.</summary>
    internal static string Clean(string text)
    {
        var normalised = text.Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new StringBuilder(normalised.Length);
        var blank = 0;
        foreach (var line in normalised.Split('\n'))
        {
            var cleaned = new string(line.Where(c => c == '\t' || !char.IsControl(c)).ToArray()).TrimEnd();
            blank = cleaned.Length == 0 ? blank + 1 : 0;
            if (blank <= 1)
            {
                builder.Append(cleaned).Append('\n');
            }
        }

        return builder.ToString().Trim();
    }
}
