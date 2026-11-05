using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.DocumentRegistry.Application;
using Strategy.Modules.DocumentRegistry.Reporting;
using Strategy.Modules.IpRegistry.Application;
using Strategy.Modules.IpRegistry.Reporting;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Reporting;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Requests;

/// <summary>
/// Reporting sources of the Request Framework, the Document Registry and the IP Registry (docs/design/reporting.md §2.5, §6, §7, ADR 0141):
/// facts vs the dashboards, scope, sensitivity partitions, lists, datasets, the My requests card and dashboard from / to.
/// </summary>
public sealed class RequestReportingTests(RequestsFixture fixture) : IClassFixture<RequestsFixture>
{
    private const string Docs = "/api/v1/docreg";
    private const string Ip = "/api/v1/ip";

    private static ClaimsPrincipal P(TestUser user) => Risk.RiskReportingTests.PrincipalOf(user);

    private DateOnly Today => DateOnly.FromDateTime(fixture.Clock.UtcNow.UtcDateTime);

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(string system, ClaimsPrincipal? user)
    {
        await using var scope = TestAccess.SystemScope(fixture.Factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == system);
        var month = MetricComputation.MonthOf(Today);
        return await source.ComputeAsync(new MetricComputation([.. source.Metrics.Select(m => m.Code)], month, month, month, Today, user), CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, Func<MetricFact, bool>? where = null) =>
        facts.Where(f => f.Metric == metric && (where?.Invoke(f) ?? true)).Sum(f => f.Value);

    private static string Month(DateOnly d) => $"{d.Year:D4}-{d.Month:D2}-01";

    private static async Task<RequestRecordDto> SubmitIpAsync(RequestWorld w, TestUser requester, string name)
    {
        var draft = await w.DraftAsync(requester, "ip.registration", new { nameEn = name });
        var saved = await RequestWorld.OkAsync<RequestRecordDto>(w.C(requester).PutAsJsonAsync($"{RequestWorld.Base}/{draft.Id}", new
        {
            version = draft.Version,
            data = new
            {
                nameAr = "منهجية " + name,
                nameEn = name,
                description = $"A methodology called {name}.",
                contributors = new object[] { new { employee = requester.EmployeeId, emiratesId = "784-1985-7654321-9", role = "author", share = 100 } },
                declaration = true,
            },
        }));
        return await w.SubmitAsync(requester, saved);
    }

    [Fact]
    public async Task Request_facts_live_scope_dataset_and_my_requests_card()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory, "docreg.office", "ip.office");
        var (_, published) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeA, "Travel policy", "internal");
        var ipRequest = await SubmitIpAsync(w, w.EmployeeB, "Service blueprint");
        var draft = await w.DraftAsync(w.EmployeeA, "docreg.document", new { nameAr = "مسودة", nameEn = "Unsent draft" });

        // (a) Cache path: per status, the same totals as the office's queue (every visible request of that status).
        var all = await FactsAsync(RequestPermissions.SystemCode, null);
        foreach (var status in new[] { "completed", "in_review", "in_progress" })
        {
            var queue = await RequestWorld.OkAsync<JsonElement>(w.C(w.Office).GetAsync($"{RequestWorld.Base}/queue?status={status}&pageSize=1"));
            Sum(all, RequestMetricSource.Requests, f => f.Dimensions[ReportingDimensions.Status] == status).ShouldBe(queue.GetProperty("totalCount").GetInt32(), status);
        }

        all.ShouldNotContain(f => f.Dimensions.ContainsKey(ReportingDimensions.Status) && f.Dimensions[ReportingDimensions.Status] == "draft");
        Sum(all, RequestMetricSource.Requests, f => f.UnitId == w.DepartmentA && f.Dimensions[ReportingDimensions.Type] == "docreg.document").ShouldBe(1);
        var cycle = all.Where(f => f.Metric == RequestMetricSource.CycleDays && f.UnitId == w.DepartmentA).ToList();
        cycle.Sum(f => f.Weight).ShouldBe(1);
        cycle.Sum(f => f.Value).ShouldBeGreaterThanOrEqualTo(0m);

        // (b) Live path: each employee counts only their own requests; the office counts every one.
        var a = await FactsAsync(RequestPermissions.SystemCode, P(w.EmployeeA));
        Sum(a, RequestMetricSource.Requests).ShouldBe(1);
        var b = await FactsAsync(RequestPermissions.SystemCode, P(w.EmployeeB));
        Sum(b, RequestMetricSource.Requests).ShouldBe(1);
        b.Single(f => f.Metric == RequestMetricSource.Requests).Dimensions[ReportingDimensions.Type].ShouldBe("ip.registration");
        Sum(await FactsAsync(RequestPermissions.SystemCode, P(w.Office)), RequestMetricSource.Requests).ShouldBe(Sum(all, RequestMetricSource.Requests));
        Sum(await FactsAsync(RequestPermissions.SystemCode, P(w.Outsider)), RequestMetricSource.Requests).ShouldBe(0);

        await using var scope = TestAccess.SystemScope(fixture.Factory);

        // (e) Dataset: own rows only for an employee; filter, sort and group for the office.
        var datasets = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == RequestPermissions.SystemCode);
        var rowsA = await datasets.RunAsync(new ReportQuery(RequestDatasetSource.RequestsDataset, ["number", "status", "type"], [], [], [], 0, 50), P(w.EmployeeA),
            CancellationToken.None);
        rowsA.RowIds!.ShouldBe([published.Id, draft.Id], ignoreOrder: true);
        var units = new[] { w.DepartmentA.ToString(), w.DepartmentB.ToString() };
        var office = await datasets.RunAsync(new ReportQuery(RequestDatasetSource.RequestsDataset, ["number", "type", "submittedAt"],
            [new ReportFilter("unit", ReportOperators.In, units), new ReportFilter("status", ReportOperators.NotEqual, ["draft"])], [],
            [new ReportSort("type", false)], 0, 50), P(w.Office), CancellationToken.None);
        office.Rows.Select(r => r[1]).ShouldBe(["docreg.document", "ip.registration"]);
        var grouped = await datasets.RunAsync(new ReportQuery(RequestDatasetSource.RequestsDataset, ["status"],
            [new ReportFilter("unit", ReportOperators.In, units)], ["status"], [], 0, 50), P(w.Office), CancellationToken.None);
        grouped.Rows.Single(r => (string)r[0]! == "completed")[1].ShouldBe(1);

        // (f) My requests card: B's IP request is in review; A has a draft; the outsider has no card.
        var cards = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == RequestPermissions.SystemCode);
        var cardB = (await cards.GetCardsAsync(P(w.EmployeeB), CancellationToken.None)).Single();
        (cardB.Code, cardB.TitleKey, cardB.Link).ShouldBe((RequestMyWorkCards.Code, "myWork.cards.myRequests", "/requests/mine"));
        var item = cardB.Items.Single();
        (item.Id, item.MessageKey, item.Link).ShouldBe((ipRequest.Id, "requests.status." + ipRequest.Status, $"/requests/{ipRequest.Id:D}"));
        cardB.Counts.Sum(c => c.Value).ShouldBe(1);
        var cardA = (await cards.GetCardsAsync(P(w.EmployeeA), CancellationToken.None)).Single();
        cardA.Counts.Single(c => c.Key == "drafts").Value.ShouldBe(1);
        (await w.C(w.EmployeeA).GetAsync($"/api/v1{cardA.Items.Single().Link}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cards.GetCardsAsync(P(w.Outsider), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Document_facts_match_the_dashboard_and_restricted_levels_stay_in_their_partitions()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory, "docreg.office", "ip.office");
        var soon = Today.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var (open, _) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeA, "Open handbook", "internal", type: "manual", expiryDate: soon);
        var (confidential, _) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeA, "Board pay", "confidential", expiryDate: soon);
        var (secret, _) = await DocumentRegistryTests.PublishAsync(w, w.EmployeeB, "Merger plan", "secret");

        // (a) Cache path vs the office's dashboard (the office sees every document).
        var all = await FactsAsync(DocRegPermissions.SystemCode, null);
        var dashboard = await RequestWorld.OkAsync<DashboardDto>(w.C(w.Office).GetAsync($"{Docs}/dashboard"));
        Sum(all, DocRegMetricSource.Documents).ShouldBe(dashboard.Published + dashboard.Expired);
        Sum(all, DocRegMetricSource.Documents, f => f.Dimensions[ReportingDimensions.Status] == "published").ShouldBe(dashboard.Published);
        foreach (var level in dashboard.ByLevel)
        {
            Sum(all, DocRegMetricSource.Documents, f => f.Dimensions[DocRegMetricSource.Level] == level.Code).ShouldBe(level.Count, level.Code);
        }

        foreach (var type in dashboard.ByType)
        {
            Sum(all, DocRegMetricSource.Documents, f => f.Dimensions[ReportingDimensions.Type] == type.Code).ShouldBe(type.Count, type.Code);
        }

        Sum(all, DocRegMetricSource.Documents, f => f.Dimensions[DocRegMetricSource.Expiring] == "true").ShouldBe(dashboard.ExpiringSoon);

        // (c) Partitions: internal → public; confidential and secret → their sensitivity partitions.
        Sum(all, DocRegMetricSource.Documents, f => f.UnitId == w.DepartmentA && f.Partition == ReportingPartitions.Public).ShouldBe(1);
        Sum(all, DocRegMetricSource.Documents, f => f.UnitId == w.DepartmentA && f.Partition == ReportingPartitions.Sensitivity("confidential")).ShouldBe(1);
        Sum(all, DocRegMetricSource.Documents, f => f.UnitId == w.DepartmentB && f.Partition == ReportingPartitions.Sensitivity("secret")).ShouldBe(1);
        await using var scope = TestAccess.SystemScope(fixture.Factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == DocRegPermissions.SystemCode);
        (await source.VisiblePartitionsAsync(P(w.EmployeeB), CancellationToken.None)).ShouldBeEmpty();
        (await source.VisiblePartitionsAsync(P(w.Office), CancellationToken.None)).ShouldBe(
            [ReportingPartitions.Sensitivity("confidential"), ReportingPartitions.Sensitivity("secret"), ReportingPartitions.Sensitivity("none")], ignoreOrder: true);
        (await source.VisiblePartitionsAsync(P(w.Outsider), CancellationToken.None)).ShouldNotContain(ReportingPartitions.Sensitivity("secret"));

        // (b) Live path: B sees the internal document and their own secret one, never A's confidential one; the office sees all three.
        var b = (await FactsAsync(DocRegPermissions.SystemCode, P(w.EmployeeB))).Where(f => f.UnitId == w.DepartmentA || f.UnitId == w.DepartmentB).ToList();
        Sum(b, DocRegMetricSource.Documents).ShouldBe(2);
        b.ShouldNotContain(f => f.Partition == ReportingPartitions.Sensitivity("confidential"));
        var office = (await FactsAsync(DocRegPermissions.SystemCode, P(w.Office))).Where(f => f.UnitId == w.DepartmentA || f.UnitId == w.DepartmentB).ToList();
        Sum(office, DocRegMetricSource.Documents).ShouldBe(3);

        // (d) Expiring list: B sees the open handbook only; links open for the reader and 404 for the uncleared.
        var lists = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == DocRegPermissions.SystemCode);
        var query = new ReportingListQuery(Today, Today, Today, [w.DepartmentA, w.DepartmentB], 10);
        var bList = await lists.GetAsync(DocRegListSource.ExpiringList, query, P(w.EmployeeB), CancellationToken.None);
        bList.Select(i => i.Id).ShouldBe([open]);
        bList[0].Link.ShouldBe($"/documents/{open:D}");
        bList[0].Value.ShouldBe(30m);
        (await w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{open}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var officeList = await lists.GetAsync(DocRegListSource.ExpiringList, query, P(w.Office), CancellationToken.None);
        officeList.Select(i => i.Id).ShouldBe([open, confidential], ignoreOrder: true);
        (await w.C(w.EmployeeB).GetAsync($"{Docs}/documents/{confidential}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // (e) Dataset: B's rows exclude the confidential document; group by sensitivity for the office.
        var datasets = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == DocRegPermissions.SystemCode);
        var units = new[] { w.DepartmentA.ToString(), w.DepartmentB.ToString() };
        var bRows = await datasets.RunAsync(new ReportQuery(DocRegDatasetSource.DocumentsDataset, ["number", "nameEn", "sensitivity"],
            [new ReportFilter("unit", ReportOperators.In, units)], [], [new ReportSort("nameEn", false)], 0, 50), P(w.EmployeeB), CancellationToken.None);
        bRows.Rows.Select(r => r[1]).ShouldBe(["Merger plan", "Open handbook"]);
        var grouped = await datasets.RunAsync(new ReportQuery(DocRegDatasetSource.DocumentsDataset, ["sensitivity"],
            [new ReportFilter("unit", ReportOperators.In, units)], ["sensitivity"], [new ReportSort("sensitivity", false)], 0, 50), P(w.Office), CancellationToken.None);
        grouped.Rows.Select(r => (r[0], r[1])).ShouldBe([((object?)"confidential", (object?)1), ("internal", 1), ("secret", 1)]);

        // (g) Dashboard from / to: publication month for the library figures, expiry month for "expiring".
        var thisMonth = await RequestWorld.OkAsync<DashboardDto>(w.C(w.Office).GetAsync($"{Docs}/dashboard?from={Month(Today)}&to={Month(Today)}"));
        thisMonth.Published.ShouldBe(dashboard.Published);
        var nextYear = Today.AddYears(1);
        var later = await RequestWorld.OkAsync<DashboardDto>(w.C(w.Office).GetAsync($"{Docs}/dashboard?from={Month(nextYear)}&to={Month(nextYear)}"));
        (later.Published, later.ExpiringSoon).ShouldBe((0, 0));
        var expiryMonth = Today.AddDays(30);
        var expiring = await RequestWorld.OkAsync<DashboardDto>(w.C(w.Office).GetAsync($"{Docs}/dashboard?from={Month(expiryMonth)}&to={Month(expiryMonth)}"));
        expiring.Expiring.Select(d => d.Id).ShouldContain(open);
        officeList.ShouldNotContain(i => i.Id == secret);
    }

    [Fact]
    public async Task Ip_facts_match_the_dashboard_and_follow_the_requester_scope()
    {
        var w = await RequestWorld.CreateAsync(fixture.Factory, "docreg.office", "ip.office");
        var a = await SubmitIpAsync(w, w.EmployeeA, "Customer journey method");
        await SubmitIpAsync(w, w.EmployeeB, "Kiosk design");

        // (a) Cache path vs the office's IP dashboard.
        var all = await FactsAsync(IpPermissions.SystemCode, null);
        var dashboard = await RequestWorld.OkAsync<IpDashboardDto>(w.C(w.Office).GetAsync($"{Ip}/dashboard"));
        Sum(all, IpMetricSource.Records).ShouldBe(dashboard.Total);
        Sum(all, IpMetricSource.Records, f => f.Dimensions[ReportingDimensions.Status] == "registered" || f.Dimensions[ReportingDimensions.Status] == "renewed")
            .ShouldBe(dashboard.Registered);
        Sum(all, IpMetricSource.Records, f => f.Dimensions[IpMetricSource.Expiring] == "true").ShouldBe(dashboard.ExpiringSoon);
        foreach (var year in dashboard.ByYear)
        {
            Sum(all, IpMetricSource.Records, f => f.Dimensions[IpMetricSource.Year] == year.Code).ShouldBe(year.Count, year.Code);
        }

        // (b) Live path: a requester counts their own record; the office counts every record.
        var mine = await FactsAsync(IpPermissions.SystemCode, P(w.EmployeeA));
        Sum(mine, IpMetricSource.Records).ShouldBe(1);
        mine.Single().UnitId.ShouldBe(w.DepartmentA);
        Sum(await FactsAsync(IpPermissions.SystemCode, P(w.Office)), IpMetricSource.Records).ShouldBe(dashboard.Total);

        await using var scope = TestAccess.SystemScope(fixture.Factory);

        // (d) / (e) List and dataset: nothing outside the visibility; A's dataset row links to the record page.
        var lists = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == IpPermissions.SystemCode);
        (await lists.GetAsync(IpListSource.ExpiringList, new ReportingListQuery(Today, Today, Today, null, 10), P(w.Outsider), CancellationToken.None)).ShouldBeEmpty();
        var datasets = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == IpPermissions.SystemCode);
        var rows = await datasets.RunAsync(new ReportQuery(IpDatasetSource.RecordsDataset, ["number", "nameEn", "status", "unit"], [], [], [], 0, 50), P(w.EmployeeA),
            CancellationToken.None);
        rows.RowIds!.ShouldBe([a.SubjectId!.Value]);
        datasets.Datasets.Single().Link.ShouldBe("/ip/records/{id}");
        datasets.Datasets.Single().Fields.ShouldNotContain(f => f.Code.Contains("emirates", StringComparison.OrdinalIgnoreCase));
        (await w.C(w.EmployeeA).GetAsync($"{Ip}/records/{a.SubjectId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.EmployeeB).GetAsync($"{Ip}/records/{a.SubjectId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var grouped = await datasets.RunAsync(new ReportQuery(IpDatasetSource.RecordsDataset, ["unit"],
            [new ReportFilter("unit", ReportOperators.In, [w.DepartmentA.ToString(), w.DepartmentB.ToString()])], ["unit"], [], 0, 50), P(w.Office), CancellationToken.None);
        grouped.Rows.Count.ShouldBe(2);

        // (g) Dashboard from / to: records not filed yet count by their creation month.
        (await RequestWorld.OkAsync<IpDashboardDto>(w.C(w.Office).GetAsync($"{Ip}/dashboard?from={Month(Today)}&to={Month(Today)}"))).Total.ShouldBe(dashboard.Total);
        var nextMonth = Today.AddMonths(1);
        (await RequestWorld.OkAsync<IpDashboardDto>(w.C(w.Office).GetAsync($"{Ip}/dashboard?from={Month(nextMonth)}"))).Total.ShouldBe(0);
    }
}
