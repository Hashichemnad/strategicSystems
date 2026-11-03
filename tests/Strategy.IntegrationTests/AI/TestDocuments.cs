using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Strategy.IntegrationTests.AI;

/// <summary>Small Word documents for extraction tests, built in memory.</summary>
public static class TestDocuments
{
    public static byte[] Docx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(p => new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    public static MultipartFormDataContent Upload(byte[] content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return form;
    }
}
