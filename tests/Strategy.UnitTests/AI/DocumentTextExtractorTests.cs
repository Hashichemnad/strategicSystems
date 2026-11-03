using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Strategy.Platform.AI.Documents;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Strategy.UnitTests.AI;

public sealed class DocumentTextExtractorTests
{
    [Fact]
    public async Task Word_paragraphs_and_tables_in_arabic_and_english()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var table = new W.Table(new W.TableRow(Cell("البند"), Cell("Budget")), new W.TableRow(Cell("المبلغ"), Cell("1,250,000")));
            main.Document = new W.Document(new W.Body(Paragraph("دراسة جدوى مشروع الأرشيف الرقمي"), Paragraph("Business case: digital archive"), table));
            main.Document.Save();
        }

        var result = await DocumentTextExtractor.ExtractAsync(stream.ToArray(), "case.docx", null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Kind.ShouldBe("docx");
        result.Value.Text.ShouldContain("دراسة جدوى مشروع الأرشيف الرقمي");
        result.Value.Text.ShouldContain("Business case: digital archive");
        result.Value.Text.ShouldContain("المبلغ | 1,250,000");
    }

    [Fact]
    public async Task PowerPoint_slides_with_markers_and_notes()
    {
        var result = await DocumentTextExtractor.ExtractAsync(Slides(["Programme overview for 2027", "عرض البرنامج"], ["Second slide text", "Speaker notes are included"]), "deck.pptx", null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Kind.ShouldBe("pptx");
        result.Value.Sections.ShouldBe(2);
        result.Value.Text.ShouldStartWith("[slide 1]\nProgramme overview for 2027\nعرض البرنامج");
        result.Value.Text.ShouldContain("[slide 2]\nSecond slide text");
    }

    [Fact]
    public async Task Pdf_pages_with_markers()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Project charter for the archive", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Total budget 1250000 AED", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);

        var result = await DocumentTextExtractor.ExtractAsync(builder.Build(), "charter.pdf", null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Kind.ShouldBe("pdf");
        result.Value.Sections.ShouldBe(2);
        result.Value.Text.ShouldContain("[page 1]\nProject charter for the archive");
        result.Value.Text.ShouldContain("[page 2]\nTotal budget 1250000 AED");
    }

    [Theory]
    [InlineData(new[] { "ا", "ب", "ح", "ر", "م" }, "مرحبا")]
    [InlineData(new[] { "2", "0", "2", "7", " ", "ة", "ن", "س" }, "سنة 2027")]
    [InlineData(new[] { "AED", " ", "م", "ه", "ر", "د" }, "درهم AED")]
    [InlineData(new[] { "Plain", " ", "English" }, "Plain English")]
    public void Right_to_left_pdf_lines_are_put_in_reading_order(string[] visual, string logical) =>
        DocumentTextExtractor.Logical(visual).ShouldBe(logical);

    [Fact]
    public async Task Arabic_presentation_forms_become_letters()
    {
        var text = "Name: " + "ﻣﺮﺣﺒﺎ"; // مرحبا in presentation forms
        var result = await DocumentTextExtractor.ExtractAsync(System.Text.Encoding.UTF8.GetBytes(text + " and enough words to count"), "note.txt", null, CancellationToken.None);
        result.Value.Text.ShouldContain("مرحبا");
    }

    [Theory]
    [InlineData("MZ\u0090\u0000binary", "evil.docx", "ai.extract.unsupported")]
    [InlineData("%PDF-1.7 truncated", "broken.pdf", "ai.extract.unreadable")]
    [InlineData("tiny", "short.txt", "ai.extract.noText")]
    public async Task Unsupported_unreadable_and_empty_documents_are_refused(string content, string name, string code) =>
        (await DocumentTextExtractor.ExtractAsync(System.Text.Encoding.Latin1.GetBytes(content), name, null, CancellationToken.None)).Error!.Code.ShouldBe(code);

    [Fact]
    public void The_file_name_does_not_decide_the_kind() =>
        DocumentTextExtractor.Detect(System.Text.Encoding.UTF8.GetBytes("plain words in a text file"), "renamed.pdf").ShouldBe(string.Empty);

    private static W.Paragraph Paragraph(string text) => new(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static W.TableCell Cell(string text) => new(Paragraph(text));

    private static byte[] Slides(params string[][] slides)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());
            uint id = 256;
            foreach (var lines in slides)
            {
                var slidePart = presentation.AddNewPart<SlidePart>();
                var shapes = new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(),
                    new P.Shape(
                        new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 2, Name = "Text" }, new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                        new P.ShapeProperties(),
                        new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(lines[0]))), new A.Paragraph(new A.Run(new A.Text(lines[1]))))));
                slidePart.Slide = new P.Slide(new P.CommonSlideData(shapes));
                presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(slidePart) });
            }

            presentation.Presentation.Save();
        }

        return stream.ToArray();
    }
}
