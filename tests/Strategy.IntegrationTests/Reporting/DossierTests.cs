using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Notifications;
using Strategy.IntegrationTests.Portfolio;
using Strategy.IntegrationTests.Tasks;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Dossiers;
using Strategy.Platform.Reporting.Infrastructure;
using UglyToad.PdfPig;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// Record dossiers (SPEC-ADDENDUM E3, ADRs 0184–0186) with the real Gotenberg: the seeded project dossier renders every section in Arabic and English
/// as PDF and Word, sections the runner may not read are omitted, a filtered set gives a merged PDF or a ZIP, and a scheduled dossier is e-mailed to
/// each project's manager as them.
/// </summary>
public sealed class DossierTests(SidecarContainers sidecars) : IClassFixture<SidecarContainers>, IAsyncLifetime
{
    private const string Template = "portfolio.project-dossier";
    private ApiFactory _factory = null!;
    private PortfolioWorld _w = null!;
    private TestUser _full = null!;
    private HttpClient _fullClient = null!;
    private (Guid Delivery, string DeliveryNumber, Guid Delayed, string DelayedNumber, Guid Other) _seed;

    public async ValueTask InitializeAsync()
    {
        _factory = new ApiFactory(await sidecars.CreateDatabaseAsync(), settings: new Dictionary<string, string?> { ["Gotenberg:Url"] = sidecars.GotenbergUrl.ToString() });
        _ = _factory.Server;
        _w = await PortfolioWorld.CreateAsync(_factory);

        // This installation opens Performance Management to named people only (not to everyone, the development default).
        await using (var access = TestAccess.SystemScope(_factory))
        {
            var accessDb = access.ServiceProvider.GetRequiredService<Strategy.Platform.Access.Infrastructure.AccessDbContext>();
            var administration = access.ServiceProvider.GetRequiredService<Strategy.Platform.Access.Application.AccessAdministration>();
            foreach (var grant in await accessDb.SystemAccess.Where(g => g.Active && g.SystemCode == "kpi" && g.PrincipalKind == "everyone").Select(g => g.Id).ToListAsync())
            {
                (await administration.RevokeSystemAccessAsync(grant, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            }
        }

        // A reader of everything the project dossier binds to: the PM Office (projects with costs), KPI cards and results, teams.
        var kpiRole = await _factory.CreateRoleAsync("kpi", (KpiPermissions.CardRead, DataScope.All), (KpiPermissions.ResultRead, DataScope.All));
        _full = await _factory.CreateUserAsync(unitId: _w.OrganisationId, roles: [PortfolioPermissions.PmOfficeRoleKey, kpiRole, "teams.viewer", "reporting.report-author"]);
        await _factory.GrantSystemAccessAsync("kpi", Strategy.Platform.Access.Application.AccessGrants.User, _full.Id);
        await _factory.GrantSystemAccessAsync("teams", Strategy.Platform.Access.Application.AccessGrants.User, _full.Id);
        _fullClient = await _factory.SignInAsync(_full);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var r1 = await _w.SubmitAsync(await _w.CreateWithCostFileAsync(PortfolioWorld.Valid("Delivery", budget: 240_000m)));
        var r2 = await _w.SubmitAsync(await _w.CreateWithCostFileAsync(PortfolioWorld.Valid("Delayed", budget: 120_000m)));
        var r3 = await _w.SubmitAsync(await _w.CreateWithCostFileAsync(PortfolioWorld.Valid("Other", budget: 60_000m), _w.OtherRequester), _w.OtherRequester);
        await using var scope = TestAccess.SystemScope(_factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var requests = await db.Requests.Where(r => r.Id == r1.Id || r.Id == r2.Id || r.Id == r3.Id).ToDictionaryAsync(r => r.Id);
        var delivery = Project.FromRequest($"DOS-{_w.Suffix}-1", requests[r1.Id], _w.Director.EmployeeId);
        delivery.SetPm(_w.Requester.EmployeeId!.Value, DateTimeOffset.UtcNow);
        var delayed = Project.FromRequest($"DOS-{_w.Suffix}-2", requests[r2.Id], _w.Director.EmployeeId);
        delayed.SetProgress(0.1m, 0.5m, ProgressCalculator.Delayed);
        var other = Project.FromRequest($"DOS-{_w.Suffix}-3", requests[r3.Id], null);
        db.Projects.AddRange(delivery, delayed, other);
        db.Milestones.Add(Milestone.Create(delivery.Id, $"{delivery.Number}-1", new MilestoneValues(
            "تحليل", "Analysis", null, false, false, today.AddDays(-60), today.AddDays(-20), 0.4m, 20_000m, true, null, null, null, false, null)));
        db.Milestones.Add(Milestone.Create(delivery.Id, $"{delivery.Number}-2", new MilestoneValues(
            "تسليم", "Delivery", null, false, false, today.AddDays(-20), today.AddDays(40), 0.6m, 30_000m, true, null, null, null, false, null)));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, delivery.Id, RecordPrincipalKinds.Employee,
            _w.Requester.EmployeeId!.Value, PortfolioPermissions.PmRole);
        _seed = (delivery.Id, delivery.Number, delayed.Id, delayed.Number, other.Id);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<DossierDocument?> BuildAsync(TestUser user, Guid recordId)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(_factory, user);
        var service = scope.ServiceProvider.GetRequiredService<DossierTemplateService>();
        var template = (await service.ListAsync(CancellationToken.None)).Single(t => t.Code == Template);
        return await service.BuildAsync(template, recordId, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static List<string> Headings(byte[] docx)
    {
        using var word = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return word.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading1").Select(p => p.InnerText).ToList();
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static async Task<byte[]> RenderAsync(HttpClient client, Guid recordId, string format, string language)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/reporting/dossiers/{Template}/render", new { recordId, format, language });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe(format == "pdf" ? "application/pdf" : DossierWord.ContentType);
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Fact]
    public async Task The_project_dossier_renders_every_seeded_section_in_arabic_and_english_as_pdf_and_word_and_omits_sections_the_user_may_not_see()
    {
        var seeded = DossierSeeds.All.Single(s => s.Code == Template).Definition.Sections;

        // The full reader: every seeded section, none omitted.
        var doc = (await BuildAsync(_full, _seed.Delivery)).ShouldNotBeNull();
        doc.OmittedSections.ShouldBeEmpty();
        doc.Blocks.Select(b => b.Key).ShouldBe(seeded.Select(s => s.Key));
        doc.Number.ShouldBe(_seed.DeliveryNumber);
        doc.Blocks.Single(b => b.Key == "milestones").Table!.RowsEn.Select(r => r[2]).ShouldBe(["Analysis", "Delivery"]);
        doc.Blocks.Single(b => b.Key == "gantt").Gantt!.Count.ShouldBe(2);
        doc.Blocks.Single(b => b.Key == "budget").Pairs!.Single(p => p.LabelEn == "Budget").ValueEn.ShouldBe("240,000.00");

        // Word in both languages: one heading per section, in the language.
        Headings(await RenderAsync(_fullClient, _seed.Delivery, "docx", "en")).ShouldBe(seeded.Select(s => s.TitleEn!));
        Headings(await RenderAsync(_fullClient, _seed.Delivery, "docx", "ar")).ShouldBe(seeded.Select(s => s.TitleAr!));

        // PDF in both languages (Gotenberg): every English section title is in the English PDF; the Arabic PDF carries the Arabic titles.
        var english = PdfText(await RenderAsync(_fullClient, _seed.Delivery, "pdf", "en"));
        foreach (var section in seeded)
        {
            english.ShouldContain(section.TitleEn!.Split(' ')[0]);
        }

        english.ShouldContain(_seed.DeliveryNumber);
        var arabicPdf = await RenderAsync(_fullClient, _seed.Delivery, "pdf", "ar");
        using (var arabic = PdfDocument.Open(arabicPdf))
        {
            arabic.NumberOfPages.ShouldBeGreaterThan(2);
            var letters = arabic.GetPages().SelectMany(p => p.Letters).Select(l => l.Value).ToList();
            letters.Any(v => v.Any(c => c is >= '\u0600' and <= '\u06FF' or >= '\uFB50' and <= '\uFEFF')).ShouldBeTrue();
        }

        // The department viewer reads the project but may not open Performance Management: the KPI section is left out, the rest stays (Teams is a
        // shared system: the team section shows only the members the viewer may read).
        var viewerDoc = (await BuildAsync(_w.DepartmentViewer, _seed.Delivery)).ShouldNotBeNull();
        viewerDoc.OmittedSections.ShouldBe(["kpis"]);
        viewerDoc.Blocks.Select(b => b.Key).ShouldBe(seeded.Select(s => s.Key).Where(k => k != "kpis"));
        var viewerHeadings = Headings(await RenderAsync(_w.C(_w.DepartmentViewer), _seed.Delivery, "docx", "en"));
        viewerHeadings.ShouldNotContain("KPIs with results");
        viewerHeadings.ShouldContain("Milestones");

        // A project outside the runner's scope does not exist for them.
        (await _w.C(_w.OtherRequester).PostAsJsonAsync($"/api/v1/reporting/dossiers/{Template}/render", new { recordId = _seed.Delivery, format = "pdf", language = "en" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BuildAsync(_w.OtherRequester, _seed.Delivery)).ShouldBeNull();
    }

    [Fact]
    public async Task Every_seeded_template_is_valid_and_offered_to_readers_of_its_record_type()
    {
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            (await db.DossierTemplates.Select(t => t.Code).ToListAsync()).ShouldBe(DossierSeeds.All.Select(s => s.Code), ignoreOrder: true);
        }

        var usable = await _fullClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/dossier-templates");
        var codes = usable.EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToList();
        codes.ShouldContain(Template);
        codes.ShouldContain("kpi.card-dossier");
        var records = await _fullClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/dossier-templates/{Template}/records?q=DOS-{_w.Suffix}");
        records.EnumerateArray().Select(r => r.GetProperty("number").GetString()).ShouldBe([$"DOS-{_w.Suffix}-3", $"DOS-{_w.Suffix}-2", $"DOS-{_w.Suffix}-1"]);
        var mine = await _w.C(_w.OtherRequester).GetFromJsonAsync<JsonElement>($"/api/v1/reporting/dossier-templates/{Template}/records?q=DOS-{_w.Suffix}");
        mine.EnumerateArray().Select(r => r.GetProperty("number").GetString()).ShouldBe([$"DOS-{_w.Suffix}-3"]);
    }

    [Fact]
    public async Task A_filtered_set_gives_a_merged_pdf_or_a_zip_with_one_dossier_per_visible_record()
    {
        var filters = new[] { new { field = "a.unit", @operator = "eq", values = new[] { _w.DepartmentId.ToString("D") } } };

        // Merged PDF: both department projects, one after the other.
        var merged = await _fullClient.PostAsJsonAsync($"/api/v1/reporting/dossiers/{Template}/bulk", new { filters, format = "pdf", language = "en" });
        merged.StatusCode.ShouldBe(HttpStatusCode.OK, await merged.Content.ReadAsStringAsync());
        var text = PdfText(await merged.Content.ReadAsByteArrayAsync());
        text.ShouldContain(_seed.DeliveryNumber);
        text.ShouldContain(_seed.DelayedNumber);
        text.ShouldNotContain($"DOS-{_w.Suffix}-3");

        // ZIP of Word files, named after the projects.
        var zip = await _fullClient.PostAsJsonAsync($"/api/v1/reporting/dossiers/{Template}/bulk", new { filters, format = "zip-docx", language = "ar" });
        zip.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var archive = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync())))
        {
            archive.Entries.Select(e => e.Name).ShouldBe([$"{_seed.DeliveryNumber}.docx", $"{_seed.DelayedNumber}.docx"], ignoreOrder: true);
        }

        // The other department's requester asking for the department's projects gets none of them.
        var none = await _w.C(_w.OtherRequester).PostAsJsonAsync($"/api/v1/reporting/dossiers/{Template}/bulk", new { filters, format = "zip-pdf", language = "en" });
        none.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_scheduled_dossier_is_e_mailed_to_each_project_manager_with_only_their_projects()
    {
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var templateId = (await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().DossierTemplates.SingleAsync(t => t.Code == Template)).Id;
            var created = await _fullClient.PostAsJsonAsync("/api/v1/reporting/schedules", new
            {
                name = "Monthly project dossier",
                targetKind = "dossier",
                targetId = templateId,
                frequency = "monthly",
                time = "07:00",
                day = 1,
                periodRule = "previous-month",
                format = "pdf",
                active = true,
                recipients = Array.Empty<object>(),
                dossier = new
                {
                    filters = new[] { new { field = "a.status", @operator = "eq", values = new[] { "active" } } },
                    language = "en",
                    recipientField = "a.pm",
                },
            });
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
            var scheduleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var before = _factory.Emails.Sent.Count;
            var entity = await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Schedules.SingleAsync(s => s.Id == scheduleId);
            (await scope.ServiceProvider.GetRequiredService<DeliveryRunner>().RunAsync(entity, CancellationToken.None)).ShouldBe(1);
            (await scope.ServiceProvider.GetRequiredService<DeliveryRunner>().RunAsync(entity, CancellationToken.None)).ShouldBe(0); // idempotent per period

            var mails = await NotificationTests.EventuallyAsync(
                () => Task.FromResult(_factory.Emails.Sent.Skip(before).Where(e => e.TemplateKey == "reporting.scheduled").ToList()), e => e.Count == 1);
            var mail = mails.Single();
            mail.To.ShouldBe(_w.Requester.UserName); // the delivery project's manager; the other projects have none
            var attachment = mail.Attachments.ShouldNotBeNull().Single();
            var pdfText = PdfText(attachment.Content);
            pdfText.ShouldContain(_seed.DeliveryNumber);
            pdfText.ShouldNotContain(_seed.DelayedNumber);
        }

        // An invalid recipient field (not a person) is refused.
        await using var check = TestAccess.SystemScope(_factory);
        var id = (await check.ServiceProvider.GetRequiredService<ReportingDbContext>().DossierTemplates.SingleAsync(t => t.Code == Template)).Id;
        var invalid = await _fullClient.PostAsJsonAsync("/api/v1/reporting/schedules", new
        {
            name = "Invalid",
            targetKind = "dossier",
            targetId = id,
            frequency = "monthly",
            time = "07:00",
            day = 1,
            periodRule = "previous-month",
            format = "pdf",
            active = true,
            recipients = Array.Empty<object>(),
            dossier = new { filters = Array.Empty<object>(), language = "en", recipientField = "a.number" },
        });
        await ReportingEngineTests.ShouldBeValidationAsync(invalid, "reporting.errors.dossierSchedule");
    }
}
