using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Files;

/// <summary>Documents of a record (SPEC §0.2): allow-list and magic number, size, versions, authorised and audited downloads.</summary>
public class FileServiceTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task Allowed_file_is_stored_versioned_listed_and_downloaded_by_the_owner_with_an_audit_entry()
    {
        var (client, itemId) = await ItemAsync();
        var uploaded = await UploadAsync(client, itemId, Pdf, "report.pdf");
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var file = (await uploaded.Content.ReadFromJsonAsync<FileDto>())!;
        file.ContentType.ShouldBe("application/pdf");
        file.VersionNo.ShouldBe(1);

        var second = Encoding.ASCII.GetBytes("%PDF-1.4\n% second version\n%%EOF\n");
        using (var form = Form(second, "report-v2.pdf"))
        {
            form.Add(new StringContent("Updated figures"), "comment");
            var version = await client.PostAsync($"/api/v1/files/{file.Id}/versions", form);
            version.StatusCode.ShouldBe(HttpStatusCode.OK, await version.Content.ReadAsStringAsync());
            (await version.Content.ReadFromJsonAsync<FileDto>())!.VersionNo.ShouldBe(2);
        }

        var list = await client.GetFromJsonAsync<List<FileDto>>($"/api/v1/files/owners/sample.item/{itemId}");
        list!.ShouldHaveSingleItem().Name.ShouldBe("report-v2.pdf");
        var versions = (await client.GetFromJsonAsync<List<FileVersionDto>>($"/api/v1/files/{file.Id}/versions"))!;
        versions.Select(v => v.VersionNo).ShouldBe([2, 1]);
        versions[0].Comment.ShouldBe("Updated figures");

        var current = await client.GetAsync($"/api/v1/files/{file.Id}/content");
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await current.Content.ReadAsByteArrayAsync()).ShouldBe(second);
        current.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        current.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
        var first = await client.GetAsync($"/api/v1/files/{file.Id}/content?versionId={versions[1].Id}&download=true");
        (await first.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);
        first.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");

        var admin = await api.AdminClientAsync();
        var audit = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?recordId={itemId}&kind=event&pageSize=50");
        audit!.Items.Count(e => e.Action == AuditActions.FileUpload && e.Success == true).ShouldBe(2);
        audit.Items.Count(e => e.Action == AuditActions.FileDownload).ShouldBe(2);
    }

    [Fact]
    public async Task Disallowed_and_disguised_types_are_refused_and_the_refusal_is_audited()
    {
        var (client, itemId) = await ItemAsync();
        var cases = new (byte[] Content, string Name)[]
        {
            ("MZ\u0090\0\u0003\0\0\0"u8.ToArray(), "setup.exe"),
            ("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray(), "logo.png"),
            ("<html><script>alert(1)</script></html>"u8.ToArray(), "page.html"),
            (Pdf, "report.docx"),
            ("PK\u0003\u0004 not an office file"u8.ToArray(), "sheet.xlsx"),
        };
        foreach (var (content, name) in cases)
        {
            var response = await UploadAsync(client, itemId, content, name);
            var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
            problem.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("files.errors.typeNotAllowed", name);
        }

        (await client.GetFromJsonAsync<List<FileDto>>($"/api/v1/files/owners/sample.item/{itemId}"))!.ShouldBeEmpty();
        var admin = await api.AdminClientAsync();
        var audit = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?recordId={itemId}&action=file.upload");
        audit!.Items.Count(e => e.Success == false && e.Data!.Contains("typeNotAllowed")).ShouldBe(cases.Length);
    }

    [Fact]
    public async Task Too_large_and_empty_uploads_are_refused()
    {
        var (client, itemId) = await ItemAsync();
        var large = new byte[(20 * 1024 * 1024) + 10];
        Pdf.CopyTo(large, 0);

        var tooLarge = await (await UploadAsync(client, itemId, large, "big.pdf")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        tooLarge.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("files.errors.tooLarge");
        var empty = await (await UploadAsync(client, itemId, [], "empty.pdf")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        empty.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("files.errors.empty");
    }

    [Fact]
    public async Task A_download_by_an_unauthorised_user_is_refused_as_not_found_and_audited()
    {
        var (owner, itemId) = await ItemAsync();
        var file = (await (await UploadAsync(owner, itemId, Pdf, "secret.pdf")).Content.ReadFromJsonAsync<FileDto>())!;

        var ownOnly = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, Strategy.Platform.Access.DataScope.Own));
        var intruder = await api.Factory.CreateUserAsync(roles: ownOnly);
        var client = await api.Factory.SignInAsync(intruder);

        await (await client.GetAsync($"/api/v1/files/{file.Id}/content")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");
        (await client.GetAsync($"/api/v1/files/owners/sample.item/{itemId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/files/{file.Id}/versions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await UploadAsync(client, itemId, Pdf, "x.pdf")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/v1/files/{file.Id}/delete", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/files/owners/unknown.type/x")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var admin = await api.AdminClientAsync();
        var denied = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/v1/audit/entries?action={AuditActions.FileDownloadDenied}&recordId={file.Id}");
        var entry = denied!.Items.ShouldHaveSingleItem();
        entry.Success.ShouldBe(false);
        entry.UserId.ShouldBe(intruder.Id);
        var downloads = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?action={AuditActions.FileDownload}&recordId={itemId}");
        downloads!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Readers_without_edit_access_cannot_upload_and_deleted_files_disappear_with_a_reason()
    {
        var (owner, itemId) = await ItemAsync();
        var file = (await (await UploadAsync(owner, itemId, Pdf, "a.pdf")).Content.ReadFromJsonAsync<FileDto>())!;
        var readAll = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, Strategy.Platform.Access.DataScope.All));
        var reader = await api.Factory.CreateSignedInClientAsync(roles: readAll);
        (await reader.GetAsync($"/api/v1/files/{file.Id}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UploadAsync(reader, itemId, Pdf, "b.pdf")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync($"/api/v1/files/{file.Id}/delete", new { reason = "no" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await owner.PostAsJsonAsync($"/api/v1/files/{file.Id}/delete", new { reason = "Uploaded by mistake" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<List<FileDto>>($"/api/v1/files/owners/sample.item/{itemId}"))!.ShouldBeEmpty();
        (await owner.GetAsync($"/api/v1/files/{file.Id}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var admin = await api.AdminClientAsync();
        var audit = await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>($"/api/v1/audit/entries?action={AuditActions.FileDelete}&recordId={itemId}");
        audit!.Items.ShouldHaveSingleItem().Reason.ShouldBe("Uploaded by mistake");
    }

    private async Task<(HttpClient Client, Guid ItemId)> ItemAsync()
    {
        var client = await api.Factory.CreateSignedInClientAsync(roles: TestIdentity.SampleEditor);
        var created = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "With documents" });
        return (client, (await created.Content.ReadFromJsonAsync<SampleItemDto>())!.Id);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid itemId, byte[] content, string name)
    {
        using var form = Form(content, name);
        return await client.PostAsync($"/api/v1/files/owners/sample.item/{itemId}", form);
    }

    private static MultipartFormDataContent Form(byte[] content, string name)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", name } };
    }
}
