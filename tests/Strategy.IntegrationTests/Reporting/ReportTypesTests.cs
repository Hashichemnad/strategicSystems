using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.Platform.Access;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// Reporting 2.0 (SPEC-ADDENDUM E1 / E2, ADRs 0180–0183) on the template module and Tasks: multi-object reports return only the rows and
/// related rows the runner may see (each object through its own module's scope), inner vs outer joins, hidden fields neither offered nor returned
/// nor usable, subtotals / summaries / chart on a worked example, exports, report types administration, My reports and folders.
/// </summary>
public sealed class ReportTypesTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private ApiFactory Factory => api.Factory;

    private sealed record World(
        string Suffix, TestUser EditorA, HttpClient EditorAClient, TestUser EditorB, HttpClient EditorBClient, TestUser Assignee, HttpClient AssigneeClient,
        TestUser UnitReader, HttpClient UnitClient, HttpClient AdminClient, string InnerType, string OuterType, Guid ItemWithTasks, IReadOnlyList<Guid> ItemsA,
        IReadOnlyList<Guid> ItemsB, Guid TaskOfEditor, Guid TaskOfAssignee);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ApiFixture, World> Worlds = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Unit A: items A0 (amount 100), A1 (200), A2 (300) by editor A; unit B: B0 (50), B1 (70) by editor B. Editor A creates two tasks linked to A0: one
    /// assigned to themself, one to the assignee (a sample viewer). The unit reader reads unit A's items only. Two report types (items with tasks,
    /// inner and outer) are created by a reporting administrator.
    /// </summary>
    private async Task<World> WorldAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (Worlds.TryGetValue(api, out var known))
            {
                return known;
            }

            var suffix = Guid.CreateVersion7().ToString("N")[^6..];
            var editorA = await Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
            var editorB = await Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
            var assignee = await Factory.CreateUserAsync(unitId: editorA.UnitId, roles: "sample.viewer");
            var unitRole = await Factory.CreateRoleAsync("sample", ("sample.item.read", DataScope.UnitAndBelow));
            var unitReader = await Factory.CreateUserAsync(unitId: editorA.UnitId, roles: unitRole);
            var admin = await Factory.CreateUserAsync(admin: true, roles: ["reporting.administrator", "sample.viewer"]);
            var editorAClient = await Factory.SignInAsync(editorA);
            var editorBClient = await Factory.SignInAsync(editorB);

            async Task<Guid> Item(HttpClient client, string name, decimal amount)
            {
                var response = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = name + "-" + suffix, amount });
                response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            }

            var itemsA = new List<Guid> { await Item(editorAClient, "A0", 100), await Item(editorAClient, "A1", 200), await Item(editorAClient, "A2", 300) };
            var itemsB = new List<Guid> { await Item(editorBClient, "B0", 50), await Item(editorBClient, "B1", 70) };
            async Task<Guid> LinkedTask(string title, Guid? assigneeEmployee)
            {
                var created = await TasksWorld.CreateAsync(editorAClient, new
                {
                    titleEn = title,
                    titleAr = "مهمة " + title,
                    typeCode = "project",
                    sourceCode = "project",
                    assigneeEmployeeIds = new[] { assigneeEmployee },
                    linkedType = "sample.item",
                    linkedId = itemsA[0],
                });
                return created.Tasks.Single().Id;
            }

            var ofEditor = await LinkedTask("Scan " + suffix, editorA.EmployeeId);
            var ofAssignee = await LinkedTask("Index " + suffix, assignee.EmployeeId);

            var adminClient = await Factory.SignInAsync(admin);
            async Task<string> Type(string join)
            {
                var code = $"test.items-tasks-{join}-{suffix}";
                var response = await adminClient.PostAsJsonAsync("/api/v1/reporting/admin/report-types", new
                {
                    code,
                    systemCode = "sample",
                    labelAr = "البنود مع المهام",
                    labelEn = "Items with tasks " + join,
                    active = true,
                    definition = new
                    {
                        nodes = new object[]
                        {
                            new { alias = "a", dataset = "sample.items" },
                            new { alias = "b", dataset = "tasks.tasks", parent = "a", relationship = "sample.item-tasks", join },
                        },
                    },
                });
                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                return code;
            }

            var world = new World(suffix, editorA, editorAClient, editorB, editorBClient, assignee, await Factory.SignInAsync(assignee), unitReader,
                await Factory.SignInAsync(unitReader), adminClient, await Type("inner"), await Type("outer"), itemsA[0], itemsA, itemsB, ofEditor, ofAssignee);
            Worlds.Add(api, world);
            return world;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static object Def(string type, string[] columns, object[]? filters = null, string[]? groupBy = null, object[]? sort = null, object[]? summaries = null,
        object? chart = null, object[]? highlights = null) => new
        {
            reportType = type,
            columns,
            filters = filters ?? [],
            groupBy = groupBy ?? [],
            sort = sort ?? [],
            summaries = summaries ?? [],
            chart,
            highlights = highlights ?? [],
        };

    private static async Task<JsonElement> QueryAsync(HttpClient client, object definition, string language = "en")
    {
        var response = await client.PostAsJsonAsync("/api/v1/reporting/reports/query", new { definition, language, pageSize = 200 });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<List<string?>> Cells(JsonElement run) =>
        run.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList()).ToList();

    private static object Mine(World w) => new { field = "a.nameEn", @operator = "contains", values = new[] { "-" + w.Suffix } };

    [Fact]
    public async Task A_multi_level_report_returns_only_rows_and_related_rows_the_runner_may_see_and_inner_and_outer_joins_differ()
    {
        var w = await WorldAsync();
        string Name(string n) => n + "-" + w.Suffix;
        var columns = new[] { "a.nameEn", "b.titleEn" };
        var sort = new object[] { new { field = "a.nameEn", descending = false }, new { field = "b.titleEn", descending = false } };

        // Editor A (creator of both tasks, editor of the item): both related rows; inner keeps only A0, outer every item they read.
        var editorInner = Cells(await QueryAsync(w.EditorAClient, Def(w.InnerType, columns, [Mine(w)], sort: sort)));
        editorInner.ShouldBe([[Name("A0"), "Index " + w.Suffix], [Name("A0"), "Scan " + w.Suffix]]);
        var editorOuter = Cells(await QueryAsync(w.EditorAClient, Def(w.OuterType, columns, [Mine(w)], sort: sort)));
        editorOuter.Count.ShouldBe(2 + 4); // A0 twice, then A1, A2, B0, B1 without tasks (editors read every item)
        editorOuter.Where(r => r[1] is null).Select(r => r[0]).ShouldBe([Name("A1"), Name("A2"), Name("B0"), Name("B1")]);

        // The assignee reads every item (viewer) but only their own task: the related level is filtered by Tasks' own scope.
        var assigneeInner = Cells(await QueryAsync(w.AssigneeClient, Def(w.InnerType, columns, [Mine(w)], sort: sort)));
        assigneeInner.ShouldBe([[Name("A0"), "Index " + w.Suffix]]);
        var assigneeTasks = await TasksWorld.ListAsync(w.AssigneeClient, $"linkedType=sample.item&linkedId={w.ItemWithTasks}");
        assigneeTasks.Items.Select(t => t.Id).ShouldBe([w.TaskOfAssignee]); // the same rows as the Tasks screens

        // The unit reader reads unit A's items only and no task: inner → nothing; outer → unit A's three items, task columns empty.
        Cells(await QueryAsync(w.UnitClient, Def(w.InnerType, columns, [Mine(w)]))).ShouldBeEmpty();
        var unitOuter = Cells(await QueryAsync(w.UnitClient, Def(w.OuterType, columns, [Mine(w)], sort: sort)));
        unitOuter.Select(r => r[0]).ShouldBe([Name("A0"), Name("A1"), Name("A2")]);
        unitOuter.ShouldAllBe(r => r[1] == null);

        // Filters on the related object filter related rows: with "with", a parent whose tasks are all filtered out disappears.
        var filtered = Cells(await QueryAsync(w.EditorAClient, Def(w.InnerType, columns,
            [Mine(w), new { field = "b.titleEn", @operator = "contains", values = new[] { "Scan" } }])));
        filtered.ShouldBe([[Name("A0"), "Scan " + w.Suffix]]);
        Cells(await QueryAsync(w.EditorAClient, Def(w.InnerType, columns,
            [Mine(w), new { field = "b.titleEn", @operator = "contains", values = new[] { "nothing-matches" } }]))).ShouldBeEmpty();

        // Rows link to each object's record.
        var run = await QueryAsync(w.EditorAClient, Def(w.InnerType, columns, [Mine(w)], sort: sort));
        var links = run.GetProperty("rows")[0].GetProperty("links");
        links.GetProperty("a").GetString().ShouldBe($"/sample/{w.ItemWithTasks:D}");
        links.GetProperty("b").GetString()!.ShouldStartWith("/tasks/");

        // Someone who cannot read the primary object gets 404 for the type.
        var outsider = await Factory.SignInAsync(await Factory.CreateUserAsync());
        (await outsider.PostAsJsonAsync("/api/v1/reporting/reports/query", new { definition = Def(w.InnerType, columns) })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetFromJsonAsync<JsonElement>("/api/v1/reporting/report-types")).EnumerateArray()
            .ShouldNotContain(t => t.GetProperty("code").GetString() == w.InnerType);
    }

    [Fact]
    public async Task A_field_the_runner_may_not_see_is_not_offered_not_returned_and_not_usable()
    {
        var w = await WorldAsync();

        // The amount needs sample.item.update: the unit reader is not offered it …
        var type = await w.UnitClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/report-types/{w.OuterType}");
        var offered = type.GetProperty("objects").EnumerateArray().SelectMany(o => o.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("code").GetString())).ToList();
        offered.ShouldContain("a.nameEn");
        offered.ShouldNotContain("a.amount");
        offered.ShouldNotContain("a.id"); // relationship keys are never columns
        var editorType = await w.EditorAClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/report-types/{w.OuterType}");
        editorType.GetProperty("objects")[0].GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("code").GetString()).ShouldContain("a.amount");

        // … a saved column on it is dropped (never returned) …
        var run = await QueryAsync(w.UnitClient, Def(w.OuterType, ["a.nameEn", "a.amount"], [Mine(w)]));
        run.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ShouldBe(["a.nameEn"]);
        run.GetProperty("droppedColumns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["a.amount"]);
        run.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("cells").GetArrayLength() == 1);

        // … and filtering, grouping, sorting, summarising, charting or highlighting on it is refused (it would leak by inference).
        async Task Refused(object definition)
        {
            var response = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports/query", new { definition });
            await ReportingEngineTests.ShouldBeValidationAsync(response, "reporting.errors.fieldNotAllowed");
        }

        await Refused(Def(w.OuterType, ["a.nameEn"], [new { field = "a.amount", @operator = "gte", values = new[] { "150" } }]));
        await Refused(Def(w.OuterType, ["a.nameEn"], sort: [new { field = "a.amount", descending = true }]));
        await Refused(Def(w.OuterType, ["a.nameEn"], summaries: [new { column = "a.amount", functions = new[] { "sum" } }]));
        await Refused(Def(w.OuterType, ["a.nameEn"], chart: new { type = "bar", category = "a.unit", measure = "a.amount", aggregate = "sum" }));
        await Refused(Def(w.OuterType, ["a.nameEn"], highlights: [new { column = "a.amount", @operator = "gte", values = new[] { "1" }, tone = "bad" }]));

        // Unknown fields are a different error, and a related object's key is never usable.
        var unknown = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports/query", new { definition = Def(w.OuterType, ["a.nameEn", "b.id"]) });
        await ReportingEngineTests.ShouldBeValidationAsync(unknown, "reporting.errors.fieldUnknown");
    }

    [Fact]
    public async Task Subtotals_summaries_chart_and_highlights_are_correct_on_a_worked_example_and_exports_carry_them()
    {
        var w = await WorldAsync();

        // Editor A reads every item with amounts: unit A 100 + 200 + 300, unit B 50 + 70 (outer join: A0 appears twice, once per task).
        var definition = Def(w.OuterType, ["a.nameEn", "a.amount", "b.titleEn"], [Mine(w)], ["a.unit"],
            summaries: [new { column = "a.amount", functions = new[] { "sum", "avg", "min", "max" } }],
            chart: new { type = "bar", category = "a.unit", measure = "a.amount", aggregate = "sum" },
            highlights: [new { column = "a.amount", @operator = "gte", values = new[] { "200" }, tone = "bad" }]);
        var run = await QueryAsync(w.EditorAClient, definition);
        run.GetProperty("total").GetInt32().ShouldBe(6);
        var groups = run.GetProperty("groups").EnumerateArray().ToList();
        groups.Count.ShouldBe(2);
        decimal Raw(JsonElement g, string fn) => g.GetProperty("aggregates").EnumerateArray().Single(a => a.GetProperty("function").GetString() == fn).GetProperty("raw").GetDecimal();
        var unitA = groups.Single(g => g.GetProperty("keys")[0].GetString() == w.EditorA.UnitId!.Value.ToString("D"));
        var unitB = groups.Single(g => g.GetProperty("keys")[0].GetString() == w.EditorB.UnitId!.Value.ToString("D"));
        unitA.GetProperty("count").GetInt32().ShouldBe(4);
        Raw(unitA, "sum").ShouldBe(700m); // 100 (twice: two tasks) + 200 + 300
        Raw(unitA, "avg").ShouldBe(175m);
        Raw(unitA, "min").ShouldBe(100m);
        Raw(unitA, "max").ShouldBe(300m);
        unitB.GetProperty("count").GetInt32().ShouldBe(2);
        Raw(unitB, "sum").ShouldBe(120m);
        Raw(run.GetProperty("grandTotal"), "sum").ShouldBe(820m);
        unitA.GetProperty("label").GetString().ShouldNotBe(unitA.GetProperty("keys")[0].GetString()); // unit name, not id

        var chart = run.GetProperty("chart");
        chart.GetProperty("type").GetString().ShouldBe("bar");
        var categories = chart.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ToList();
        var values = chart.GetProperty("series")[0].GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ToList();
        values.Sum().ShouldBe(820m);
        values.ShouldContain(700m);
        values.ShouldContain(120m);
        categories.Count.ShouldBe(2);

        var tones = run.GetProperty("rows").EnumerateArray().Select(r => (Amount: r.GetProperty("cells")[1].GetString(), Tone: r.GetProperty("tones")[1].GetString())).ToList();
        tones.Where(t => t.Amount is "200.00" or "300.00").ShouldAllBe(t => t.Tone == "bad");
        tones.Where(t => t.Amount is "100.00" or "50.00" or "70.00").ShouldAllBe(t => t.Tone == null);

        // Grouped, without details: subtotals only.
        var summary = await QueryAsync(w.EditorAClient, new
        {
            reportType = w.OuterType,
            columns = new[] { "a.nameEn", "a.amount" },
            filters = new[] { Mine(w) },
            groupBy = new[] { "a.unit" },
            sort = Array.Empty<object>(),
            summaries = new[] { new { column = "a.amount", functions = new[] { "sum" } } },
            highlights = Array.Empty<object>(),
            showDetails = false,
        });
        summary.GetProperty("rows").GetArrayLength().ShouldBe(0);
        summary.GetProperty("groups").GetArrayLength().ShouldBe(2);

        // Exports: Excel (RTL for Arabic, group lines with subtotals, chart data sheet), CSV (BOM, Arabic headers).
        var xlsx = await w.EditorAClient.PostAsJsonAsync("/api/v1/reporting/reports/export", new { definition, format = "xlsx", language = "ar", title = "Items" });
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync());
        using (var book = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        {
            var sheet = book.Worksheets.First();
            sheet.RightToLeft.ShouldBeTrue();
            sheet.Cell(1, 2).GetString().ShouldBe("القيمة");
            sheet.RowsUsed().Count().ShouldBe(1 + 2 + 6 + 1); // header, 2 group lines, 6 rows, grand total
            sheet.Column(2).CellsUsed().Select(c => c.GetString()).ShouldContain(s => s.Contains("700.00", StringComparison.Ordinal));
            book.Worksheets.Count.ShouldBe(2);
        }

        var csv = await w.EditorAClient.PostAsJsonAsync("/api/v1/reporting/reports/export", new { definition, format = "csv", language = "ar", title = "Items" });
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        text.ShouldStartWith("﻿\"الاسم بالإنجليزية\"");
        text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(10);
    }

    [Fact]
    public async Task Report_types_are_administered_only_by_reporting_administrators_and_validated_against_the_metadata()
    {
        var w = await WorldAsync();
        var types = await w.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/admin/report-types");
        var codes = types.EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("portfolio.projects-milestones");
        codes.ShouldContain("risk.risks-controls-actions");
        codes.ShouldContain("docreg.documents-ip");
        codes.ShouldContain(w.InnerType);
        types.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "portfolio.projects-milestones").GetProperty("seeded").GetBoolean().ShouldBeTrue();

        var metadata = await w.AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/admin/reportable");
        var relationships = metadata.GetProperty("relationships").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        foreach (var crossSystem in new[] { "portfolio.project-team-members", "portfolio.project-kpi-results", "bench.study-cap-remarks", "auditmgmt.finding-risk-risks",
                     "risk.kri-results", "ip.document-records" })
        {
            relationships.ShouldContain(crossSystem);
        }

        // An invalid definition (relationship from the wrong parent) is refused with its message key; an editor may not administer.
        var invalid = await w.AdminClient.PostAsJsonAsync("/api/v1/reporting/admin/report-types", new
        {
            code = "test.invalid-" + w.Suffix,
            systemCode = "sample",
            labelAr = "خطأ",
            labelEn = "Invalid",
            active = true,
            definition = new { nodes = new object[] { new { alias = "a", dataset = "tasks.tasks" }, new { alias = "b", dataset = "tasks.tasks", parent = "a", relationship = "sample.item-tasks", join = "inner" } } },
        });
        await ReportingEngineTests.ShouldBeValidationAsync(invalid, "reporting.errors.reportType.relationship");
        (await w.EditorAClient.GetAsync("/api/v1/reporting/admin/report-types")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Deactivated types disappear from the builder.
        var stored = types.EnumerateArray().Single(t => t.GetProperty("code").GetString() == w.OuterType);
        var copy = "test.copy-" + w.Suffix;
        var created = await w.AdminClient.PostAsJsonAsync("/api/v1/reporting/admin/report-types", new
        {
            code = copy,
            systemCode = "sample",
            labelAr = "نسخة",
            labelEn = "Copy",
            active = false,
            definition = stored.GetProperty("definition"),
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        (await w.EditorAClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/report-types")).EnumerateArray()
            .Select(t => t.GetProperty("code").GetString()).ShouldNotContain(copy);
    }

    [Fact]
    public async Task Every_user_keeps_my_reports_and_private_folders_while_publishing_to_a_shared_folder_needs_report_author()
    {
        var w = await WorldAsync();
        var definition = Def(w.OuterType, ["a.nameEn"], [Mine(w)]);

        // A user without any reporting role: private folder and report.
        var folder = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/folders", new { name = "Mine", kind = "private" });
        folder.StatusCode.ShouldBe(HttpStatusCode.OK, await folder.Content.ReadAsStringAsync());
        var folderId = (await folder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports", new { name = "My items", definition, folderId });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var reportId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await w.UnitClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports")).EnumerateArray()
            .ShouldContain(r => r.GetProperty("id").GetGuid() == reportId && r.GetProperty("folderId").GetGuid() == folderId);
        (await w.EditorBClient.GetAsync($"/api/v1/reporting/reports/{reportId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // … but no shared folder and no sharing.
        (await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/folders", new { name = "Team", kind = "shared" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.UnitClient.PutAsJsonAsync($"/api/v1/reporting/reports/{reportId}/shares", new[] { new { principalKind = "user", principalRef = w.EditorB.Id.ToString("D") } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A Report author publishes into a shared folder shared with editor B: B sees the report (and runs it with B's own rights); others do not.
        var author = await Factory.CreateUserAsync(roles: ["reporting.report-author", "sample.viewer"]);
        var authorClient = await Factory.SignInAsync(author);
        var shared = await authorClient.PostAsJsonAsync("/api/v1/reporting/folders", new { name = "Published", kind = "shared" });
        shared.StatusCode.ShouldBe(HttpStatusCode.OK, await shared.Content.ReadAsStringAsync());
        var sharedId = (await shared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await authorClient.PutAsJsonAsync($"/api/v1/reporting/folders/{sharedId}/shares", new[] { new { principalKind = "user", principalRef = w.EditorB.Id.ToString("D") } }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var published = await authorClient.PostAsJsonAsync("/api/v1/reporting/reports", new { name = "Published items", definition, folderId = sharedId });
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        var publishedId = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await w.EditorBClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/folders")).EnumerateArray().ShouldContain(f => f.GetProperty("id").GetGuid() == sharedId);
        var forB = await w.EditorBClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/reports/{publishedId}");
        forB.GetProperty("shared").GetBoolean().ShouldBeTrue();
        (await w.UnitClient.GetAsync($"/api/v1/reporting/reports/{publishedId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The unit reader cannot move their report into someone else's (or a shared) folder.
        (await w.UnitClient.PutAsJsonAsync($"/api/v1/reporting/reports/{reportId}/folder", new { folderId = sharedId })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
