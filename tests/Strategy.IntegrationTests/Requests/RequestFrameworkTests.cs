using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Requests.Application;

namespace Strategy.IntegrationTests.Requests;

/// <summary>The Request Framework with a custom type built in the form builder (no code): SPEC §5, docs/design/requests.md §2.</summary>
public sealed class RequestFrameworkTests(RequestsFixture fixture) : IClassFixture<RequestsFixture>
{
    private const string Base = RequestWorld.Base;

    private static object TrainingType(string code) => new
    {
        code,
        nameAr = "طلب تدريب",
        nameEn = "Training request",
        descriptionEn = "Ask for a training course.",
        category = "services",
        icon = "school",
        numberPrefix = "TRN",
        submitters = new { everyone = true },
        form = new
        {
            sections = new object[]
            {
                new
                {
                    code = "course",
                    titleAr = "الدورة",
                    titleEn = "Course",
                    fields = new object[]
                    {
                        new { code = "courseName", type = "text", labelAr = "اسم الدورة", labelEn = "Course name", required = true, maxLength = 120, titleOf = "en" },
                        new { code = "cost", type = "currency", labelAr = "التكلفة", labelEn = "Cost", required = true, min = 0, max = 100000 },
                        new { code = "startDate", type = "date", labelAr = "تاريخ البدء", labelEn = "Start date", required = true },
                        new { code = "external", type = "checkbox", labelAr = "جهة خارجية", labelEn = "External provider" },
                        new
                        {
                            code = "provider", type = "text", labelAr = "الجهة المقدمة", labelEn = "Provider",
                            visibleWhen = new { field = "external", op = "eq", value = true },
                            requiredWhen = new { field = "external", op = "eq", value = true },
                        },
                        new
                        {
                            code = "attendees", type = "table", labelAr = "المشاركون", labelEn = "Attendees", minRows = 1, maxRows = 5, required = true,
                            columns = new object[]
                            {
                                new { code = "attendee", type = "employee", labelAr = "الموظف", labelEn = "Employee", required = true },
                                new { code = "days", type = "number", labelAr = "الأيام", labelEn = "Days", decimals = 0, min = 1, max = 30 },
                            },
                        },
                    },
                },
            },
        },
        stages = new
        {
            approval = true,
            processing = true,
            processingNameEn = "HR processing",
            processorRoleKeys = new[] { "requests.office" },
            slaHours = 16,
            completionForm = new
            {
                sections = new object[]
                {
                    new { code = "done", fields = new object[] { new { code = "bookingRef", type = "text", labelAr = "رقم الحجز", labelEn = "Booking reference", required = true } } },
                },
            },
        },
        templates = new object[]
        {
            new { code = "summary", nameEn = "Summary", audience = "requester", htmlAr = "<h1>{{request.number}}</h1><p>{{field.courseName}}</p>{{#rows attendees}}<p>{{row.index}} {{attendee}}</p>{{/rows}}", htmlEn = "<h1>{{request.number}}</h1><p>{{field.courseName}}</p>" },
        },
        active = true,
        showInCatalogue = true,
        embeddable = true,
    };

    [Fact]
    public async Task An_admin_builds_a_type_without_code_and_it_runs_from_catalogue_to_completion()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory);
        var code = "training-" + Guid.CreateVersion7().ToString("N")[^6..];

        // Only type administrators reach the builder.
        (await RequestWorld.StatusAsync(w.C(w.EmployeeA).PostAsJsonAsync($"{Base}/admin/types", TrainingType(code)))).ShouldBe(HttpStatusCode.Forbidden);
        var created = await RequestWorld.OkAsync<TypeDetailDto>(w.C(w.Admin).PostAsJsonAsync($"{Base}/admin/types", TrainingType(code)));
        created.Stages.Select(s => s.Code).ShouldBe(["review", "processing"]);
        created.Stages[0].ProcessKey.ShouldBe($"requests.type.{code}");

        // A bad schema is refused (duplicate code, condition on a later field).
        var bad = new
        {
            code = code + "x",
            nameEn = "Bad",
            form = new
            {
                sections = new object[]
                {
                    new
                    {
                        code = "s",
                        fields = new object[]
                        {
                            new { code = "a", type = "text", labelEn = "A", visibleWhen = new { field = "b", op = "filled" } },
                            new { code = "b", type = "text", labelEn = "B" },
                            new { code = "b", type = "text", labelEn = "B again" },
                        },
                    },
                },
            },
        };
        (await RequestWorld.StatusAsync(w.C(w.Admin).PostAsJsonAsync($"{Base}/admin/types", bad))).ShouldBe(HttpStatusCode.BadRequest);

        // The type is in the catalogue; its process appears in the Approval Settings centre with one office step.
        var catalogue = await RequestWorld.OkAsync<CatalogueDto>(w.C(w.EmployeeA).GetAsync($"{Base}/catalogue"));
        catalogue.Types.ShouldContain(t => t.Code == code);

        // Server-side validation: wrong types, unknown keys, required-when.
        var draft = await w.DraftAsync(w.EmployeeA, code, new { courseName = "Leadership" });
        draft.Number.ShouldStartWith("TRN-");
        var invalid = await w.C(w.EmployeeA).PutAsJsonAsync($"{Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = new { courseName = "Leadership", cost = "abc", startDate = "31/12/2030", hacker = "x" },
        });
        var problem = await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var fields = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        fields.ShouldContain("fields.cost");
        fields.ShouldContain("fields.startDate");
        fields.ShouldContain("fields.hacker");

        var saved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = new
            {
                courseName = "Leadership",
                cost = 2500.5,
                startDate = "2030-03-01",
                external = true,
                attendees = new object[] { new { attendee = w.EmployeeA.EmployeeId, days = 3 } },
            },
        }));

        // Submitting checks everything: the provider is required because "external" is ticked.
        var refused = await w.C(w.EmployeeA).PostAsJsonAsync($"{Base}/{draft.Id}/submit", new { version = saved.Version });
        (await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ShouldContain("fields.provider");

        saved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.EmployeeA).PutAsJsonAsync($"{Base}/{draft.Id}", new
        {
            version = saved.Version,
            data = new
            {
                courseName = "Leadership",
                cost = 2500.5,
                startDate = "2030-03-01",
                external = true,
                provider = "Academy",
                attendees = new object[] { new { attendee = w.EmployeeA.EmployeeId, days = 3 } },
            },
        }));
        saved.TitleEn.ShouldBe("Leadership");
        var submitted = await w.SubmitAsync(w.EmployeeA, saved);
        submitted.Status.ShouldBe("in_review");
        submitted.Stage.ShouldBe("review");

        // Outsiders do not see it (404); the requester sees it but cannot decide (403).
        (await RequestWorld.StatusAsync(w.C(w.Outsider).GetAsync($"{Base}/{draft.Id}"))).ShouldBe(HttpStatusCode.NotFound);
        (await RequestWorld.StatusAsync(w.C(w.Outsider).PostAsJsonAsync($"{Base}/{draft.Id}/decide", new { action = "approve" })))
            .ShouldBe(HttpStatusCode.NotFound);
        (await RequestWorld.StatusAsync(w.C(w.EmployeeA).PostAsJsonAsync($"{Base}/{draft.Id}/decide", new { action = "approve" })))
            .ShouldBe(HttpStatusCode.Forbidden);

        // The default single office step approves → processing stage with an SLA.
        var office = await w.GetAsync(w.Office, draft.Id);
        office.Can.Decide.ShouldBeTrue();
        var approved = await w.DecideAsync(w.Office, draft.Id, "approve");
        approved.Status.ShouldBe("in_progress");
        approved.Stage.ShouldBe("processing");
        approved.StageDueAt.ShouldNotBeNull();
        approved.Can.Actions.Select(a => a.Code).ShouldBe(["complete", "return", "reject"]);

        // The office's queue shows it; the completion mini-form is validated.
        var queue = await RequestWorld.OkAsync<JsonObject>(w.C(w.Office).GetAsync($"{Base}/queue?type={code}"));
        queue["items"]!.AsArray().Select(i => i!["id"]!.GetValue<string>()).ShouldContain(draft.Id.ToString());
        var current = await w.GetAsync(w.Office, draft.Id);
        (await RequestWorld.StatusAsync(w.C(w.Office).PostAsJsonAsync($"{Base}/{draft.Id}/actions/complete", new { version = current.Version, form = new { } })))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await RequestWorld.StatusAsync(w.C(w.EmployeeA).PostAsJsonAsync($"{Base}/{draft.Id}/actions/complete", new { version = current.Version, form = new { bookingRef = "B-1" } })))
            .ShouldBe(HttpStatusCode.Forbidden);
        var done = await w.ActAsync(w.Office, draft.Id, "complete", new { bookingRef = "B-77" });
        done.Status.ShouldBe("completed");
        done.ProcessData["processing"]!["bookingRef"]!.GetValue<string>().ShouldBe("B-77");
        done.Events.Select(e => e.Action).ShouldBe(["created", "submitted", "approved", "complete"]);

        // My requests and the requester's PDF template.
        var mine = await RequestWorld.OkAsync<JsonObject>(w.C(w.EmployeeA).GetAsync($"{Base}/mine"));
        mine["items"]!.AsArray().Select(i => i!["status"]!.GetValue<string>()).ShouldContain("completed");
        var pdf = await w.C(w.EmployeeA).GetAsync($"{Base}/{draft.Id}/pdf/summary?language=ar");
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        fixture.Pdf.Html.ShouldContain(h => h.Contains(done.Number, StringComparison.Ordinal) && h.Contains("Leadership", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Return_from_processing_and_resubmit_goes_back_to_the_same_stage_and_cancel_rules()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory);
        var code = "simple-" + Guid.CreateVersion7().ToString("N")[^6..];
        await RequestWorld.OkAsync<TypeDetailDto>(w.C(w.Admin).PostAsJsonAsync($"{Base}/admin/types", new
        {
            code,
            nameEn = "Simple",
            numberPrefix = "SIM",
            form = new { sections = new object[] { new { code = "s", fields = new object[] { new { code = "note", type = "textarea", labelEn = "Note", required = true } } } } },
            stages = new { approval = false, processing = true },
        }));

        var draft = await w.DraftAsync(w.EmployeeB, code, new { note = "Please" });
        var submitted = await w.SubmitAsync(w.EmployeeB, draft);
        submitted.Status.ShouldBe("in_progress");

        (await RequestWorld.StatusAsync(w.C(w.Office).PostAsJsonAsync($"{Base}/{draft.Id}/actions/return", new { version = submitted.Version })))
            .ShouldBe(HttpStatusCode.BadRequest); // comment required (validation)
        var returned = await w.ActAsync(w.Office, draft.Id, "return", comment: "Add details");
        returned.Status.ShouldBe("returned");
        var mine = await w.GetAsync(w.EmployeeB, draft.Id);
        mine.Can.Edit.ShouldBeTrue();
        var again = await w.SubmitAsync(w.EmployeeB, mine);
        (again.Status, again.Stage).ShouldBe(("in_progress", "processing"));

        // The requester cannot cancel at a processing stage; the office (manage) can.
        (await RequestWorld.StatusAsync(w.C(w.EmployeeB).PostAsJsonAsync($"{Base}/{draft.Id}/cancel", new { reason = "x", version = again.Version })))
            .ShouldBe(HttpStatusCode.Forbidden);
        var cancelled = await RequestWorld.OkAsync<RequestRecordDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/{draft.Id}/cancel", new { reason = "Duplicate", version = again.Version }));
        cancelled.Status.ShouldBe("cancelled");
    }
}
