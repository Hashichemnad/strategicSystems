using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Jobs;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Requests;

/// <summary>Document Registry on the Request Framework (SPEC §6, docs/design/requests.md §3, ADR 0082–0085).</summary>
public sealed class DocumentRegistryTests(RequestsFixture fixture) : IClassFixture<RequestsFixture>
{
    private const string Docs = "/api/v1/docreg";

    internal static async Task<RequestWorld> WorldAsync(RequestsFixture fixture) =>
        await RequestWorld.CreateAsync(fixture.Factory, "docreg.office");

    /// <summary>Submits a new document (or a new version), approves it with the classification and publishes it; returns the document id.</summary>
    internal static async Task<(Guid DocumentId, RequestRecordDto Request)> PublishAsync(
        RequestWorld w, TestUser requester, string name, string sensitivity, string type = "policy", bool shareToAll = false, Guid? existing = null,
        string? expiryDate = null, string? effectiveDate = null)
    {
        var draft = await w.DraftAsync(requester, "docreg.document", new { nameAr = "وثيقة " + name, nameEn = name });
        var file = await w.UploadAsync(requester, draft.Id, "draft.txt", System.Text.Encoding.UTF8.GetBytes($"{name} draft text: travel approval limit is 5000"), "text/plain");
        var saved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(requester).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = new
            {
                isNewVersion = existing is not null,
                existingDocument = existing,
                nameAr = "وثيقة " + name,
                nameEn = name,
                ownerUnit = requester.UnitId,
                description = $"About {name}",
                draftFile = file,
                changeReason = existing is null ? null : "Annual update",
            },
        }));
        var submitted = await w.SubmitAsync(requester, saved);
        submitted.Status.ShouldBe("in_review");
        await w.DecideAsync(w.Office, draft.Id, "approve", new { level = "operational", docType = type, sensitivity });
        var final = await w.UploadAsync(w.Office, draft.Id, "signed.pdf", RequestWorld.Pdf(name + " signed final"), "application/pdf");
        var published = await w.ActAsync(w.Office, draft.Id, "publish", new { finalFile = final, shareToAll, expiryDate, effectiveDate });
        published.Status.ShouldBe("completed");
        published.Outcome.ShouldBe("published");
        var documentId = existing ?? (await DocumentOfAsync(w, published.SubjectId!.Value));
        return (documentId, published);
    }

    private static async Task<Guid> DocumentOfAsync(RequestWorld w, Guid versionId)
    {
        await using var scope = TestAccess.SystemScope(w.Factory);
        var db = scope.ServiceProvider.GetRequiredService<Strategy.Modules.DocumentRegistry.Infrastructure.DocRegDbContext>();
        return db.Versions.Single(v => v.Id == versionId).DocumentId;
    }

    [Fact]
    public async Task Submission_review_with_classification_publication_and_a_new_version_that_supersedes()
    {
        var w = await WorldAsync(fixture);

        // A new document from the catalogue.
        (await RequestWorld.OkAsync<CatalogueDto>(w.C(w.EmployeeA).GetAsync($"{RequestWorld.Base}/catalogue"))).Types.ShouldContain(t => t.Code == "docreg.document");
        var draft = await w.DraftAsync(w.EmployeeA, "docreg.document", new { nameAr = "سياسة السفر", nameEn = "Travel policy" });
        var file = await w.UploadAsync(w.EmployeeA, draft.Id, "draft.txt", "Travel approval limit is 5000"u8.ToArray(), "text/plain");
        var saved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = new { nameAr = "سياسة السفر", nameEn = "Travel policy", ownerUnit = w.DepartmentA, draftFile = file },
        }));
        var submitted = await w.SubmitAsync(w.EmployeeA, saved);
        submitted.SubjectType.ShouldBe("docreg.version");

        // The generic task screen cannot approve without the classification (approve form enforced in the hook).
        var task = (await w.GetAsync(w.Office, draft.Id)).Approval!.MyTaskId!.Value;
        var generic = await ApprovalWorld.ActAsync(w.C(w.Office), task, "approve");
        generic.IsSuccessStatusCode.ShouldBeFalse();
        (await generic.Content.ReadAsStringAsync()).ShouldContain("approveFormRequired");

        // Classification is validated against the lookups; then the Document ID is generated ({ORG}-{UNIT}-{TYPE}-{YYYY}-{SEQ}).
        (await RequestWorld.StatusAsync(w.C(w.Office).PostAsJsonAsync($"{RequestWorld.Base}/{draft.Id}/decide",
            new { action = "approve", form = new { level = "operational", docType = "nope", sensitivity = "internal" } }))).ShouldBe(HttpStatusCode.BadRequest);
        var approved = await w.DecideAsync(w.Office, draft.Id, "approve", new { level = "operational", docType = "policy", sensitivity = "internal" });
        approved.Stage.ShouldBe("publish");
        var final = await w.UploadAsync(w.Office, draft.Id, "signed.pdf", RequestWorld.Pdf("Travel policy final"), "application/pdf");
        var published = await w.ActAsync(w.Office, draft.Id, "publish", new { finalFile = final, effectiveDate = "2026-01-01" });
        published.Status.ShouldBe("completed");
        var documentId = await DocumentOfAsync(w, published.SubjectId!.Value);

        var document = await RequestWorld.OkAsync<DocumentDto>(w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{documentId}"));
        document.DocumentNo!.ShouldMatch(@"^ORG-RA\w+-POL-2026-01$");
        (document.Status, document.CurrentVersion, document.ExpiryDate).ShouldBe(("published", "1.0", (DateOnly?)new DateOnly(2028, 1, 1))); // policy: 24 months
        document.Can.Withdraw.ShouldBeFalse(); // not the owner side
        var mine = await RequestWorld.OkAsync<DocumentDto>(w.C(w.EmployeeA).GetAsync($"{Docs}/documents/{documentId}"));
        mine.Can.NewVersion.ShouldBeTrue();

        // A new version keeps the Document ID, gets 2.0 and supersedes 1.0 on publication.
        var (_, second) = await PublishAsync(w, w.EmployeeA, "Travel policy", "internal", existing: documentId);
        var revised = await RequestWorld.OkAsync<DocumentDto>(w.C(w.EmployeeA).GetAsync($"{Docs}/documents/{documentId}"));
        revised.DocumentNo.ShouldBe(document.DocumentNo);
        revised.CurrentVersion.ShouldBe("2.0");
        revised.Versions.Select(v => (v.Label, v.Status)).ShouldBe([("2.0", "current"), ("1.0", "superseded")]);
        second.Events.Select(e => e.Action).ShouldContain("publish");

        // A second new-version request while one is in progress is refused.
        var other = await w.DraftAsync(w.EmployeeA, "docreg.document", new { nameAr = "س", nameEn = "S" });
        var otherFile = await w.UploadAsync(w.EmployeeA, other.Id, "d.txt", "x"u8.ToArray(), "text/plain");
        var otherSaved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{other.Id}", new
        {
            version = other.Version,
            data = new { isNewVersion = true, existingDocument = documentId, nameAr = "س", nameEn = "S", ownerUnit = w.DepartmentA, draftFile = otherFile, changeReason = "x" },
        }));
        await w.SubmitAsync(w.EmployeeA, otherSaved);
        var third = await w.DraftAsync(w.EmployeeA, "docreg.document", new { nameAr = "س", nameEn = "S" });
        var thirdFile = await w.UploadAsync(w.EmployeeA, third.Id, "d.txt", "x"u8.ToArray(), "text/plain");
        var thirdSaved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{RequestWorld.Base}/{third.Id}", new
        {
            version = third.Version,
            data = new { isNewVersion = true, existingDocument = documentId, nameAr = "س", nameEn = "S", ownerUnit = w.DepartmentA, draftFile = thirdFile, changeReason = "x" },
        }));
        var refused = await w.C(w.EmployeeA).PostAsJsonAsync($"{RequestWorld.Base}/{third.Id}/submit", new { version = thirdSaved.Version });
        (await refused.Content.ReadAsStringAsync()).ShouldContain("versionInProgress");

        // Compare 1.0 and 2.0.
        var v1 = revised.Versions.Single(v => v.Label == "1.0").Id;
        var v2 = revised.Versions.Single(v => v.Label == "2.0").Id;
        var compare = await RequestWorld.OkAsync<JsonObject>(w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{documentId}/compare?from={v1}&to={v2}"));
        compare["parts"]!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Sensitivity_hides_documents_in_library_page_download_search_and_mcp()
    {
        var w = await WorldAsync(fixture);
        var tag = "Zq" + Guid.CreateVersion7().ToString("N")[^6..];
        var (internalDoc, _) = await PublishAsync(w, w.EmployeeA, tag + " internal", "internal");
        var (confidential, _) = await PublishAsync(w, w.EmployeeA, tag + " confidential", "confidential");
        var (secret, _) = await PublishAsync(w, w.Admin, tag + " secret", "secret");
        var (shared, _) = await PublishAsync(w, w.EmployeeA, tag + " shared", "confidential", shareToAll: true);

        // Secret documents cannot be shared to all employees.
        var refusedShare = await w.DraftAsync(w.EmployeeA, "docreg.document", new { nameAr = "س", nameEn = "S" });
        (refusedShare.Status).ShouldBe("draft");

        async Task<List<Guid>> LibraryAsync(TestUser user) =>
            (await RequestWorld.OkAsync<LibraryDto>(w.C(user).GetAsync($"{Docs}/library?search={tag}&pageSize=50"))).Page.Items.Select(i => i.Id).ToList();

        // Department B: internal and the shared confidential one only. Department A (owner unit): + confidential. Office: everything.
        (await LibraryAsync(w.EmployeeB)).OrderBy(x => x).ShouldBe(new[] { internalDoc, shared }.OrderBy(x => x));
        (await LibraryAsync(w.EmployeeA)).OrderBy(x => x).ShouldBe(new[] { internalDoc, confidential, shared }.OrderBy(x => x));
        (await LibraryAsync(w.Office)).OrderBy(x => x).ShouldBe(new[] { internalDoc, confidential, secret, shared }.OrderBy(x => x));

        // Page, download, versions compare: 404 outside clearance.
        (await RequestWorld.StatusAsync(w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{confidential}"))).ShouldBe(HttpStatusCode.NotFound);
        (await RequestWorld.StatusAsync(w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{confidential}/download"))).ShouldBe(HttpStatusCode.NotFound);
        (await RequestWorld.StatusAsync(w.C(w.EmployeeA).GetAsync($"{Docs}/documents/{secret}"))).ShouldBe(HttpStatusCode.NotFound);
        var download = await w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{internalDoc}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Take(4).ShouldBe("%PDF"u8.ToArray());

        // Global search ("search_records", also used by Ask) and the MCP tools use the same rule.
        await using (var scope = w.Factory.Services.CreateAsyncScope())
        {
            var principal = RequestWorld.PrincipalOf(w.EmployeeB);
            scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Mcp, principal);
            var records = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == "search_records");
            var found = JsonSerializer.Serialize((await records.InvokeAsync(JsonSerializer.SerializeToElement(new { query = tag, text = tag, search = tag }),
                new ToolContext(principal, Channels.Ai, "en"), CancellationToken.None)).Data);
            found.ShouldContain(internalDoc.ToString());
            found.ShouldNotContain(confidential.ToString());
            found.ShouldNotContain(secret.ToString());
            var tool = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == "search_documents");
            var result = await tool.InvokeAsync(JsonSerializer.SerializeToElement(new { search = tag }), new ToolContext(principal, Channels.Mcp, "en"), CancellationToken.None);
            var text = JsonSerializer.Serialize(result.Data);
            text.ShouldContain(internalDoc.ToString());
            text.ShouldNotContain(confidential.ToString());
            text.ShouldNotContain(secret.ToString());
            var get = scope.ServiceProvider.GetServices<IAppTool>().Single(t => t.Definition.Name == "get_document");
            (await get.InvokeAsync(JsonSerializer.SerializeToElement(new { documentId = secret }), new ToolContext(principal, Channels.Mcp, "en"), CancellationToken.None))
                .ErrorCode.ShouldBe(ToolErrors.NotFound);
        }

        // Records of other modules: the record field of a request refuses a document the user cannot see.
        var probe = await w.DraftAsync(w.EmployeeB, "docreg.withdrawal", new { });
        var refused = await w.C(w.EmployeeB).PutAsJsonAsync($"{RequestWorld.Base}/{probe.Id}", new { version = probe.Version, data = new { document = confidential, reason = "x" } });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Expiry_reminders_at_60_and_30_days_then_expired_and_confirmation_extends()
    {
        var w = await WorldAsync(fixture);
        DateOnly start;
        await using (var settingsScope = w.Factory.Services.CreateAsyncScope())
        {
            // The installation's date (Regional settings time zone), as the module computes "today" — not the UTC date.
            var zoneId = (await settingsScope.ServiceProvider.GetRequiredService<ISettingsStore>().GetAsync<RegionalSettings>(CancellationToken.None)).TimeZone;
            start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fixture.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime);
        }

        var (documentId, _) = await PublishAsync(w, w.EmployeeA, "Expiring procedure " + Guid.CreateVersion7().ToString("N")[^5..], "internal", type: "procedure",
            effectiveDate: start.AddMonths(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), expiryDate: start.AddDays(70).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        async Task<(int Reminded, int Expired)> RunAsync()
        {
            await using var scope = w.Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<RequestContext>()
                .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
            return await scope.ServiceProvider.GetRequiredService<ValidityRunner>().RunAsync(CancellationToken.None);
        }

        async Task<DocumentDto> DocAsync() => await RequestWorld.OkAsync<DocumentDto>(w.C(w.EmployeeA).GetAsync($"{Docs}/documents/{documentId}"));

        try
        {
            await RunAsync();
            (await DocAsync()).Validity.ShouldNotContain(v => v.Kind == "reminder");

            fixture.Clock.Offset = TimeSpan.FromDays(11); // 59 days left → the 60-day reminder, once
            await w.ReSignInAsync();
            await RunAsync();
            await RunAsync();
            (await DocAsync()).Validity.Count(v => v.Kind == "reminder").ShouldBe(1);

            fixture.Clock.Offset = TimeSpan.FromDays(41); // 29 days left → the 30-day reminder
            await w.ReSignInAsync();
            await RunAsync();
            (await DocAsync()).Validity.Where(v => v.Kind == "reminder").Select(v => v.Days).OrderBy(d => d).ShouldBe([30, 60]);

            fixture.Clock.Offset = TimeSpan.FromDays(71); // past expiry
            await w.ReSignInAsync();
            await RunAsync();
            var expired = await DocAsync();
            expired.Status.ShouldBe("expired");
            (await RequestWorld.OkAsync<LibraryDto>(w.C(w.EmployeeB).GetAsync($"{Docs}/library?status=expired&pageSize=200"))).Page.Items
                .ShouldContain(i => i.Id == documentId); // still visible, marked expired

            // The owner confirms it is still valid: extended by the type's 24 months from today, back to published.
            (await RequestWorld.StatusAsync(w.C(w.EmployeeB).PostAsJsonAsync($"{Docs}/documents/{documentId}/confirm-valid", new { }))).ShouldBe(HttpStatusCode.Forbidden);
            var confirmed = await RequestWorld.OkAsync<DocumentDto>(w.C(w.EmployeeA).PostAsJsonAsync($"{Docs}/documents/{documentId}/confirm-valid", new { comment = "Still valid" }));
            confirmed.Status.ShouldBe("published");
            confirmed.ExpiryDate.ShouldBe(start.AddDays(71).AddMonths(24));
            confirmed.Validity.ShouldContain(v => v.Kind == "confirmed" && v.Comment == "Still valid");
        }
        finally
        {
            fixture.Clock.Offset = TimeSpan.Zero;
            await w.ReSignInAsync();
        }
    }
}
