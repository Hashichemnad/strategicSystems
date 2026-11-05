using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Notifications;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// The reporting engine (Phase 17, ADRs 0140–0149) on the template module's sources: scope-correct figures from the cache and the live path,
/// restricted partitions, refresh after a relevant change, dashboard builder security, report builder field security, scheduled delivery as
/// each recipient in their language, drill-down links the viewer may open.
/// </summary>
public class ReportingEngineTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private sealed record World(
        TestUser Admin, HttpClient AdminClient, TestUser EditorA, HttpClient EditorAClient, TestUser EditorB, HttpClient EditorBClient,
        TestUser AllReader, HttpClient AllClient, TestUser UnitReader, HttpClient UnitClient, TestUser OwnReader, HttpClient OwnClient,
        Guid UnitA, Guid UnitB, string UnitRole, string OwnRole, IReadOnlyList<Guid> ItemsA, IReadOnlyList<Guid> ItemsB, Guid OwnItem);

    private World? _world;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ApiFactory Factory => api.Factory;

    /// <summary>
    /// Unit A: 3 items of 100 by editor A. Unit B: 2 items of 50 by editor B, plus 1 item of 10 by the "own" reader. Readers: all (sample viewer),
    /// unit A and below (custom role), own (custom role; live path).
    /// </summary>
    private async Task<World> WorldAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_world is not null)
            {
                return _world;
            }

            var admin = await Factory.CreateUserAsync(admin: true, roles: ["reporting.administrator", "reporting.executive-viewer", "sample.viewer"]);
            var adminClient = await Factory.SignInAsync(admin);
            var editorA = await Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
            var editorB = await Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
            var editorAClient = await Factory.SignInAsync(editorA);
            var editorBClient = await Factory.SignInAsync(editorB);
            var unitRole = await Factory.CreateRoleAsync("sample", (SamplePermissionRead, DataScope.UnitAndBelow));
            var ownRole = await Factory.CreateRoleAsync("sample", (SamplePermissionRead, DataScope.Own), ("sample.item.create", DataScope.All));
            var allReader = await Factory.CreateUserAsync(roles: "sample.viewer");
            var unitReader = await Factory.CreateUserAsync(unitId: editorA.UnitId, roles: unitRole);
            var ownReader = await Factory.CreateUserAsync(unitId: editorB.UnitId, roles: ownRole);
            var ownClient = await Factory.SignInAsync(ownReader);

            async Task<Guid> Item(HttpClient client, string name, decimal amount)
            {
                var response = await client.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = name, amount });
                response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
                return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            }

            var itemsA = new List<Guid>();
            for (var i = 0; i < 3; i++)
            {
                itemsA.Add(await Item(editorAClient, "A" + i, 100));
            }

            var itemsB = new List<Guid> { await Item(editorBClient, "B0", 50), await Item(editorBClient, "B1", 50) };
            var own = await Item(ownClient, "Own", 10);
            await RefreshAsync();
            _world = new World(admin, adminClient, editorA, editorAClient, editorB, editorBClient, allReader, await Factory.SignInAsync(allReader), unitReader,
                await Factory.SignInAsync(unitReader), ownReader, ownClient, editorA.UnitId!.Value, editorB.UnitId!.Value, unitRole, ownRole, itemsA, itemsB, own);
            return _world;
        }
        finally
        {
            _gate.Release();
        }
    }

    private const string SamplePermissionRead = "sample.item.read";

    private async Task RefreshAsync()
    {
        await using var scope = TestAccess.SystemScope(Factory);
        var clock = scope.ServiceProvider.GetRequiredService<ReportingClock>();
        var month = ReportPeriod.MonthOf(await clock.TodayAsync(CancellationToken.None));
        await scope.ServiceProvider.GetRequiredService<FactRefresher>().RefreshAsync(["sample"], month.AddMonths(-2), month, "test", CancellationToken.None);
    }

    private static async Task<Guid> SampleDashboardAsync(World w, params string[] roles)
    {
        var widgets = new object[]
        {
            new { id = "items", type = "tile", metric = "sample.items", x = 0, y = 0, w = 3, h = 2 },
            new { id = "amount", type = "tile", metric = "sample.amount", x = 3, y = 0, w = 3, h = 2 },
            new { id = "share", type = "gauge", metric = "sample.approved-share", x = 6, y = 0, w = 3, h = 2 },
            new { id = "by-unit", type = "bar", metric = "sample.items", dimension = "unit", x = 0, y = 2, w = 6, h = 3 },
            new { id = "largest", type = "list", list = "sample.largest", top = 10, x = 6, y = 2, w = 6, h = 3 },
        };
        var created = await w.AdminClient.PostAsJsonAsync("/api/v1/reporting/dashboards", new
        {
            titleAr = "لوحة البنود", titleEn = "Items dashboard " + Guid.CreateVersion7().ToString("N")[^6..], level = "role", definition = new { widgets },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await w.AdminClient.PostAsync($"/api/v1/reporting/dashboards/{id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var put = await w.AdminClient.PutAsJsonAsync($"/api/v1/reporting/dashboards/{id}/roles", roles.Select((r, i) => new { roleKey = r, sort = i, isDefault = false }));
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());
        return id;
    }

    private static async Task<JsonElement> DataAsync(HttpClient client, Guid dashboard)
    {
        var response = await client.GetAsync($"/api/v1/reporting/dashboards/{dashboard}/data?kind=month");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Widget(JsonElement data, string id) =>
        data.GetProperty("data").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id);

    private static decimal? Value(JsonElement widget) =>
        widget.GetProperty("value").ValueKind == JsonValueKind.Null ? null : widget.GetProperty("value").GetDecimal();

    [Fact]
    public async Task Two_users_with_different_scopes_see_different_numbers_on_the_same_dashboard_from_the_cache_and_the_live_path()
    {
        var w = await WorldAsync();
        var dashboard = await SampleDashboardAsync(w, "sample.viewer", w.UnitRole, w.OwnRole);

        var all = await DataAsync(w.AllClient, dashboard);
        var unit = await DataAsync(w.UnitClient, dashboard);
        var own = await DataAsync(w.OwnClient, dashboard);

        // Facts are per test database; the template module holds only this class's items.
        Value(Widget(all, "items")).ShouldBe(6);
        Widget(all, "items").GetProperty("live").GetBoolean().ShouldBeFalse();
        Value(Widget(unit, "items")).ShouldBe(3);
        Widget(unit, "items").GetProperty("live").GetBoolean().ShouldBeFalse();
        Value(Widget(own, "items")).ShouldBe(1);
        Widget(own, "items").GetProperty("live").GetBoolean().ShouldBeTrue("own scope is never served from the cache");

        // Breakdown by unit: the unit reader sees only unit A.
        Widget(unit, "by-unit").GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("key").GetProperty("unit").GetString())
            .ShouldBe([w.UnitA.ToString("D")]);
        Widget(all, "by-unit").GetProperty("rows").GetArrayLength().ShouldBe(2);
        unit.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ShouldContain(w.UnitA);

        // Ratio recombines from additive parts: no item approved yet → 0, not an average of averages.
        Value(Widget(all, "share")).ShouldBe(0);

        // Lists are scoped by the module.
        Widget(unit, "largest").GetProperty("items").GetArrayLength().ShouldBe(3);
        Widget(own, "largest").GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(w.OwnItem);
    }

    [Fact]
    public async Task Restricted_partitions_are_counted_only_for_users_who_may_read_them_and_a_metric_without_access_is_unavailable_not_zero()
    {
        var w = await WorldAsync();
        var dashboard = await SampleDashboardAsync(w, "sample.viewer", w.UnitRole, TestIdentity.SampleEditor);

        // Amounts are in the "costs" partition: only users who may edit items read them.
        Value(Widget(await DataAsync(w.AllClient, dashboard), "amount")).ShouldBe(0);
        var editorView = await DataAsync(w.EditorAClient, dashboard);
        Value(Widget(editorView, "amount")).ShouldBe(410);

        // A user with no access to the module: 404 on the role dashboard, "unavailable" on a custom one built by someone who may see it.
        var outsider = await Factory.SignInAsync(await Factory.CreateUserAsync());
        (await outsider.GetAsync($"/api/v1/reporting/dashboards/{dashboard}/data")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await using (var scope = TestAccess.SystemScope(Factory))
        {
            var evaluator = scope.ServiceProvider.GetRequiredService<MetricEvaluator>();
            var principal = await PrincipalAsync(Factory, (await Factory.CreateUserAsync()).Id);
            var month = ReportPeriod.MonthOf(DateOnly.FromDateTime(DateTime.UtcNow));
            (await evaluator.FactsAsync(principal, "sample.items", month, month, CancellationToken.None)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task Drill_down_links_land_on_records_the_viewer_may_open_and_not_on_others()
    {
        var w = await WorldAsync();
        var dashboard = await SampleDashboardAsync(w, w.UnitRole, w.OwnRole);
        var unitItems = Widget(await DataAsync(w.UnitClient, dashboard), "largest").GetProperty("items").EnumerateArray()
            .Select(i => (Id: i.GetProperty("id").GetGuid(), Link: i.GetProperty("link").GetString()!)).ToList();
        unitItems.Count.ShouldBe(3);
        foreach (var (id, link) in unitItems)
        {
            link.ShouldBe($"/sample/{id:D}");
            (await w.UnitClient.GetAsync($"/api/v1/sample/items/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await w.OwnClient.GetAsync($"/api/v1/sample/items/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task The_builder_offers_and_accepts_only_metrics_the_builder_may_see_and_admins_publish_role_dashboards_without_code()
    {
        var w = await WorldAsync();

        // A report author without access to the template module: the catalogue hides its metrics and saving a widget on them is refused.
        var author = await Factory.SignInAsync(await Factory.CreateUserAsync(roles: "reporting.report-author"));
        var catalogue = await author.GetFromJsonAsync<JsonElement>("/api/v1/reporting/catalogue");
        catalogue.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("code").GetString()).ShouldNotContain("sample.items");
        var refused = await author.PostAsJsonAsync("/api/v1/reporting/dashboards", new
        {
            titleEn = "Mine", level = "custom", definition = new { widgets = new[] { new { id = "x", type = "tile", metric = "sample.items", x = 0, y = 0, w = 3, h = 2 } } },
        });
        await ShouldBeValidationAsync(refused, "reporting.errors.metricNotAllowed");

        // Only administrators create role dashboards.
        var roleRefused = await author.PostAsJsonAsync("/api/v1/reporting/dashboards", new { titleEn = "Role", level = "role", definition = new { widgets = Array.Empty<object>() } });
        roleRefused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Admin builds and publishes a dashboard for the unit role; a holder sees it among their dashboards, others do not.
        var dashboard = await SampleDashboardAsync(w, w.UnitRole);
        var list = await w.UnitClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/dashboards");
        list.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ShouldContain(dashboard);
        var others = await w.OwnClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/dashboards");
        others.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ShouldNotContain(dashboard);
        (await w.OwnClient.GetAsync($"/api/v1/reporting/dashboards/{dashboard}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Pins and saved filters are per user.
        (await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/pins", new { kind = "dashboard", refId = dashboard })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.OwnClient.PostAsJsonAsync("/api/v1/reporting/pins", new { kind = "dashboard", refId = dashboard })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var saved = await w.UnitClient.PostAsJsonAsync($"/api/v1/reporting/dashboards/{dashboard}/filters", new { name = "Q", filters = new { kind = "quarter" }, isDefault = true });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_report_builder_never_exposes_fields_or_records_the_runner_may_not_see()
    {
        var w = await WorldAsync();
        object Definition(params string[] columns) => new { dataset = "sample.items", columns, filters = Array.Empty<object>(), groupBy = Array.Empty<string>(), sort = Array.Empty<object>() };

        // The unit reader: 3 rows, the amount column (needs the edit permission) is dropped, and filtering / grouping / sorting on it is refused.
        var run = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports/run", new { definition = Definition("number", "nameEn", "amount"), language = "en" });
        run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        var result = await run.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("total").GetInt32().ShouldBe(3);
        result.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ShouldBe(["number", "nameEn"]);
        result.GetProperty("droppedColumns").EnumerateArray().Select(c => c.GetString()).ShouldBe(["amount"]);
        var datasets = await w.UnitClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/datasets");
        datasets.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "sample.items").GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("code").GetString()).ShouldNotContain("amount");

        var filtered = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports/run", new
        {
            definition = new
            {
                dataset = "sample.items", columns = new[] { "number" }, filters = new[] { new { field = "amount", @operator = "gte", values = new[] { "60" } } },
                groupBy = Array.Empty<string>(), sort = Array.Empty<object>(),
            },
        });
        await ShouldBeValidationAsync(filtered, "reporting.errors.fieldNotAllowed");
        var sorted = await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/reports/run", new
        {
            definition = new { dataset = "sample.items", columns = new[] { "number" }, filters = Array.Empty<object>(), groupBy = Array.Empty<string>(), sort = new[] { new { field = "amount", descending = true } } },
        });
        await ShouldBeValidationAsync(sorted, "reporting.errors.fieldNotAllowed");

        // The editor sees amounts; grouping by unit gives one row per unit with counts and sums.
        var grouped = await w.EditorAClient.PostAsJsonAsync("/api/v1/reporting/reports/run", new
        {
            definition = new { dataset = "sample.items", columns = new[] { "unit", "amount" }, filters = Array.Empty<object>(), groupBy = new[] { "unit" }, sort = Array.Empty<object>() },
            language = "en",
        });
        grouped.StatusCode.ShouldBe(HttpStatusCode.OK, await grouped.Content.ReadAsStringAsync());
        var groups = await grouped.Content.ReadFromJsonAsync<JsonElement>();
        groups.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ShouldBe(["unit", "count", "amount"]);
        groups.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(2);

        // A report shared with the own reader runs with the own reader's rights.
        var save = await w.EditorAClient.PostAsJsonAsync("/api/v1/reporting/reports", new { name = "All items", definition = Definition("number", "nameEn") });
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden); // editors may run reports but not build them
        var author = await Factory.CreateUserAsync(roles: ["reporting.report-author", "sample.viewer"]);
        var authorClient = await Factory.SignInAsync(author);
        var saved = await authorClient.PostAsJsonAsync("/api/v1/reporting/reports", new { name = "All items", definition = Definition("number", "nameEn") });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var reportId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await w.OwnClient.GetAsync($"/api/v1/reporting/reports/{reportId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await authorClient.PutAsJsonAsync($"/api/v1/reporting/reports/{reportId}/shares", new[] { new { principalKind = "user", principalRef = w.OwnReader.Id.ToString("D") } }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var report = await w.OwnClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/reports/{reportId}");
        var ownRun = await w.OwnClient.PostAsJsonAsync("/api/v1/reporting/reports/run", new { definition = report.GetProperty("definition") });
        (await ownRun.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_scheduled_report_is_delivered_to_each_recipient_in_their_language_with_their_permissions_at_send_time()
    {
        var w = await WorldAsync();
        var author = await Factory.CreateUserAsync(roles: ["reporting.report-author", "sample.viewer"]);
        var authorClient = await Factory.SignInAsync(author);
        var saved = await authorClient.PostAsJsonAsync("/api/v1/reporting/reports", new
        {
            name = "Items",
            definition = new { dataset = "sample.items", columns = new[] { "number", "nameEn", "unit" }, filters = Array.Empty<object>(), groupBy = Array.Empty<string>(), sort = Array.Empty<object>() },
        });
        var reportId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await authorClient.PutAsJsonAsync($"/api/v1/reporting/reports/{reportId}/shares", new[]
        {
            new { principalKind = "user", principalRef = w.UnitReader.Id.ToString("D") }, new { principalKind = "user", principalRef = w.AllReader.Id.ToString("D") },
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Recipients' languages: the unit reader Arabic, the all reader English.
        (await w.UnitClient.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "ar" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await w.AllClient.PutAsJsonAsync("/api/v1/identity/me/preferences", new { language = "en" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var schedule = await authorClient.PostAsJsonAsync("/api/v1/reporting/schedules", new
        {
            name = "Weekly items", targetKind = "report", targetId = reportId, frequency = "weekly", time = "07:00", day = 0, periodRule = "current-month",
            format = "xlsx", active = true,
            recipients = new[] { new { kind = "user", @ref = w.UnitReader.Id.ToString("D") }, new { kind = "user", @ref = w.AllReader.Id.ToString("D") } },
        });
        schedule.StatusCode.ShouldBe(HttpStatusCode.OK, await schedule.Content.ReadAsStringAsync());
        var scheduleId = (await schedule.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var before = Factory.Emails.Sent.Count;
        await using (var scope = TestAccess.SystemScope(Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            var entity = await db.Schedules.SingleAsync(s => s.Id == scheduleId);
            (await scope.ServiceProvider.GetRequiredService<DeliveryRunner>().RunAsync(entity, CancellationToken.None)).ShouldBe(2);

            // Idempotent for the same period.
            (await scope.ServiceProvider.GetRequiredService<DeliveryRunner>().RunAsync(entity, CancellationToken.None)).ShouldBe(0);
        }

        var emails = await NotificationTests.EventuallyAsync(
            () => Task.FromResult(Factory.Emails.Sent.Skip(before).Where(e => e.TemplateKey == "reporting.scheduled").ToList()), e => e.Count == 2);
        var unitMail = emails.Single(e => e.To == w.UnitReader.UserName);
        var allMail = emails.Single(e => e.To == w.AllReader.UserName);
        foreach (var mail in emails)
        {
            mail.Subject.ShouldContain("Items");
            mail.Attachments.ShouldNotBeNull().Count.ShouldBe(1);
        }

        using (var arabic = new XLWorkbook(new MemoryStream(unitMail.Attachments![0].Content)))
        {
            var sheet = arabic.Worksheets.First();
            sheet.RightToLeft.ShouldBeTrue();
            sheet.Cell(1, 1).GetString().ShouldBe("الرقم");
            sheet.RowsUsed().Count().ShouldBe(1 + 3); // header + unit A's items only
        }

        using (var english = new XLWorkbook(new MemoryStream(allMail.Attachments![0].Content)))
        {
            var sheet = english.Worksheets.First();
            sheet.RightToLeft.ShouldBeFalse();
            sheet.Cell(1, 1).GetString().ShouldBe("Number");
            sheet.RowsUsed().Count().ShouldBeGreaterThanOrEqualTo(1 + 6);
        }

        var deliveries = await authorClient.GetFromJsonAsync<JsonElement>($"/api/v1/reporting/schedules/{scheduleId}/deliveries");
        deliveries.EnumerateArray().Select(d => d.GetProperty("status").GetString()).ShouldAllBe(s => s == "sent");
    }

    [Fact]
    public async Task My_work_cards_follow_system_access_and_the_users_layout()
    {
        var w = await WorldAsync();
        var dashboard = await SampleDashboardAsync(w, w.UnitRole);
        (await w.UnitClient.PostAsJsonAsync("/api/v1/reporting/pins", new { kind = "dashboard", refId = dashboard })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var cards = await w.UnitClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/my-work/cards");
        var codes = cards.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("code").GetString()!).ToList();
        codes.ShouldContain("notifications.latest");
        codes.ShouldContain("reporting.pins");

        var layout = new[] { new { code = "reporting.pins", hidden = false }, new { code = "notifications.latest", hidden = true } };
        (await w.UnitClient.PutAsJsonAsync("/api/v1/reporting/my-work/layout", layout)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await w.UnitClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/my-work/cards")).GetProperty("cards").EnumerateArray().ToList();
        after[0].GetProperty("code").GetString().ShouldBe("reporting.pins");
        after.Single(c => c.GetProperty("code").GetString() == "notifications.latest").GetProperty("hidden").GetBoolean().ShouldBeTrue();
    }

    internal static async Task ShouldBeValidationAsync(HttpResponseMessage response, string messageKey)
    {
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("messageKey").GetString()).ShouldContain(messageKey);
    }

    internal static async Task<System.Security.Claims.ClaimsPrincipal> PrincipalAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var principal = await scope.ServiceProvider.GetRequiredService<Strategy.Platform.Identity.Authentication.TokenPrincipalFactory>()
            .CreateAsync(userId, "test", "test", [], null);
        return principal.ShouldNotBeNull();
    }
}

/// <summary>Refresh after a relevant change (ADR 0142), on its own host so the counts of other classes are not disturbed.</summary>
public class ReportingRefreshTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Aggregates_refresh_after_a_relevant_change_through_the_dirty_mark()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var editor = await factory.SignInAsync(await factory.CreateUserAsync(roles: TestIdentity.SampleEditor));
        var tracker = factory.Services.GetRequiredService<ReportingChangeTracker>();
        async Task<decimal?> ItemsAsync()
        {
            await using var scope = TestAccess.SystemScope(factory);
            var month = ReportPeriod.MonthOf(await scope.ServiceProvider.GetRequiredService<ReportingClock>().TodayAsync(CancellationToken.None));
            var facts = await scope.ServiceProvider.GetRequiredService<MetricEvaluator>()
                .FactsAsync(StrategyClaims.CreateSystemPrincipal(), "sample.items", month, month, CancellationToken.None);
            return facts!.Facts.Sum(f => f.Value);
        }

        async Task RefreshDirtyAsync()
        {
            var dirty = tracker.Take(TimeSpan.Zero);
            dirty.ShouldContain("sample");
            await using var scope = TestAccess.SystemScope(factory);
            var month = ReportPeriod.MonthOf(await scope.ServiceProvider.GetRequiredService<ReportingClock>().TodayAsync(CancellationToken.None));
            await scope.ServiceProvider.GetRequiredService<FactRefresher>().RefreshAsync(dirty, month, month, "dirty", CancellationToken.None);
        }

        (await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "first" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        await RefreshDirtyAsync();
        (await ItemsAsync()).ShouldBe(1);

        tracker.Take(TimeSpan.Zero);
        (await editor.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "second" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        tracker.HasDirty.ShouldBeTrue("saving a business record marks its system dirty");
        (await ItemsAsync()).ShouldBe(1, "until the refresh runs the cache keeps its last figure");
        await RefreshDirtyAsync();
        (await ItemsAsync()).ShouldBe(2);

        await using (var scope = TestAccess.SystemScope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            (await db.RefreshLogs.CountAsync(l => l.SystemCode == "sample" && l.Kind == "dirty" && l.Error == null)).ShouldBe(2);
        }
    }
}
