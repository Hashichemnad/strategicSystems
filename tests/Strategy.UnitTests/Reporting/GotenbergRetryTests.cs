using System.Net;
using Strategy.Platform.Reporting.Pdf;

namespace Strategy.UnitTests.Reporting;

public sealed class GotenbergRetryTests
{
    private sealed class Scripted(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent("%PDF-1.7"u8.ToArray()) });
        }
    }

    private static GotenbergPdfRenderer Renderer(Scripted handler) =>
        new(new HttpClient(handler), new GotenbergOptions { Url = "http://gotenberg:3000" });

    [Fact]
    public async Task A_busy_gotenberg_is_retried_and_the_pdf_is_returned()
    {
        var handler = new Scripted(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        (await Renderer(handler).RenderAsync("<p>x</p>", CancellationToken.None)).Length.ShouldBeGreaterThan(0);
        handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_rejected_document_is_not_retried()
    {
        var handler = new Scripted(HttpStatusCode.BadRequest);
        (await Should.ThrowAsync<PdfRenderingException>(() => Renderer(handler).RenderAsync("<p>x</p>", CancellationToken.None)))
            .MessageKey.ShouldBe("reporting.errors.pdfFailed");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_gotenberg_that_stays_unavailable_fails_after_three_attempts()
    {
        var handler = new Scripted(HttpStatusCode.ServiceUnavailable);
        await Should.ThrowAsync<PdfRenderingException>(() => Renderer(handler).RenderAsync("<p>x</p>", CancellationToken.None));
        handler.Calls.ShouldBe(3);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    public void Transient_statuses(int status, bool transient) => GotenbergPdfRenderer.IsTransient(status).ShouldBe(transient);
}
