using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Reporting;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Risk;

/// <summary>Risk reporting sources (docs/design/reporting.md §2.5, §6, §7): facts vs the dashboard, scope, lists, datasets, My Work card, dashboard from / to.</summary>
public sealed class RiskReportingTests(RiskFixture fx) : IClassFixture<RiskFixture>
{
    private const string Base = RiskWorld.Base;

    internal static ClaimsPrincipal PrincipalOf(TestUser user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(StrategyClaims.PrincipalType, StrategyClaims.InternalPrincipal),
        new Claim(StrategyClaims.EmployeeId, user.EmployeeId!.Value.ToString()),
        new Claim(StrategyClaims.AuthMethod, "mfa"),
    ], "test"));

    internal static MetricComputation Now(IReadOnlyCollection<string> metrics, ClaimsPrincipal? user)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = MetricComputation.MonthOf(today);
        return new MetricComputation(metrics, month, month, month, today, user);
    }

    private static async Task<IReadOnlyList<MetricFact>> FactsAsync(ApiFactory factory, ClaimsPrincipal? user)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == RiskPermissions.SystemCode);
        return await source.ComputeAsync(Now(source.Metrics.Select(m => m.Code).ToList(), user), CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, Func<MetricFact, bool>? where = null) =>
        facts.Where(f => f.Metric == metric && (where?.Invoke(f) ?? true)).Sum(f => f.Value);

    private static async Task<RiskDto> ApproveAsync(RiskWorld w, RiskDto risk, TestUser submitter)
    {
        await RiskWorld.OkAsync<RiskDto>(w.C(submitter).PostAsJsonAsync($"{Base}/risks/{risk.Id}/submit", new { comment = "please review" }));
        await w.ActAsync(risk.Id, RiskProcesses.Review, w.Office, "risk-office");
        return await w.GetAsync(risk.Id);
    }

    private static async Task<RiskWorld> SeedAsync(RiskFixture fx)
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var critical = await ApproveAsync(w, await w.CreateAsync(RiskWorld.Values("Payment outage", w.DepartmentA, w.OwnerA, 5, 5, rl: 5, ri: 4)), w.CoordinatorA);
        await w.CreateAsync(RiskWorld.Values("Draft A", w.DepartmentA, w.OwnerA, 2, 3, category: "strategic"));
        await w.CreateAsync(RiskWorld.Values("Old A", w.DepartmentA, w.OwnerA, 1, 2, identified: new DateOnly(2025, 6, 10)));
        await ApproveAsync(w, await w.CreateAsync(RiskWorld.Values("Supplier B", w.DepartmentB, w.OwnerA, 3, 3), w.CoordinatorB), w.CoordinatorB);
        await RiskWorld.OkAsync<ActionDto>(w.C(w.CoordinatorA).PostAsJsonAsync($"{Base}/risks/{critical.Id}/actions", new
        {
            values = new { titleEn = "Failover site", ownerEmployeeId = w.ActionOwner.EmployeeId, plannedEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20) },
        }));
        return w;
    }

    [Fact]
    public async Task Facts_for_everyone_match_the_dashboard_and_the_live_path_counts_only_the_viewers_risks()
    {
        var w = await SeedAsync(fx);

        // (a) Cache path (no user): the same figures as the dashboard of an all-scope user over the same data.
        var all = await FactsAsync(fx.Factory, null);
        var dashboard = await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?includeProjectRisks=false"));
        Sum(all, RiskMetricSource.Risks).ShouldBe(dashboard.Total);
        Sum(all, RiskMetricSource.Risks, f => f.Dimensions[ReportingDimensions.Status] == RiskStatuses.Active).ShouldBe(dashboard.Active);
        Sum(all, RiskMetricSource.Risks, f => f.Dimensions[ReportingDimensions.Status] == RiskStatuses.Draft).ShouldBe(dashboard.Draft);
        foreach (var band in dashboard.ByResidualBand)
        {
            Sum(all, RiskMetricSource.Risks, f => f.Dimensions[ReportingDimensions.Band] == band.Code).ShouldBe(band.Count, band.Code);
        }

        foreach (var category in dashboard.ByCategory)
        {
            Sum(all, RiskMetricSource.Risks, f => f.Dimensions[ReportingDimensions.Category] == category.Code).ShouldBe(category.Count, category.Code);
        }

        foreach (var cell in dashboard.Inherent.Where(c => c.Enterprise > 0))
        {
            Sum(all, RiskMetricSource.Heat, f => f.Dimensions[RiskMetricSource.Kind] == "inherent" && f.Dimensions[RiskMetricSource.Likelihood] == $"{cell.Likelihood}"
                && f.Dimensions[RiskMetricSource.Impact] == $"{cell.Impact}").ShouldBe(cell.Enterprise);
        }

        Sum(all, RiskMetricSource.Heat, f => f.Dimensions[RiskMetricSource.Kind] == "residual").ShouldBe(dashboard.Residual.Sum(c => c.Enterprise));
        Sum(all, RiskMetricSource.Actions, f => f.Dimensions[ReportingDimensions.Overdue] == "true").ShouldBe(dashboard.OverdueActionsCount);
        Sum(all, RiskMetricSource.ReviewsOverdue).ShouldBe(dashboard.OverdueReviewsCount);
        Sum(all, RiskMetricSource.Risks, f => f.Dimensions[RiskMetricSource.Appetite] == RiskMetricSource.Outside && f.Dimensions[ReportingDimensions.Status] == RiskStatuses.Active)
            .ShouldBe(dashboard.OutsideAppetiteCount);
        all.ShouldAllBe(f => f.Partition == ReportingPartitions.Public && f.Month == MetricComputation.MonthOf(DateOnly.FromDateTime(DateTime.UtcNow)));

        // Severe = the framework's two highest bands (critical, high by default); the critical risk's unit carries it.
        Sum(all, RiskMetricSource.Risks, f => f.UnitId == w.DepartmentA && f.Dimensions[ReportingDimensions.Severe] == "true").ShouldBe(1);
        all.Where(f => f.Metric == RiskMetricSource.Risks && f.UnitId == w.DepartmentA).Sum(f => f.Value).ShouldBe(3);

        // (b) Live path: coordinator B (unit-and-below of B) counts only B's risk, like their own dashboard; the Risk Office sees more.
        var b = await FactsAsync(fx.Factory, PrincipalOf(w.CoordinatorB));
        var bDashboard = await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.CoordinatorB).GetAsync($"{Base}/dashboard?includeProjectRisks=false"));
        Sum(b, RiskMetricSource.Risks).ShouldBe(bDashboard.Total);
        Sum(b, RiskMetricSource.Risks).ShouldBe(1);
        b.Where(f => f.Metric == RiskMetricSource.Risks).ShouldAllBe(f => f.UnitId == w.DepartmentB);
        Sum(b, RiskMetricSource.Actions).ShouldBe(0);
        var office = await FactsAsync(fx.Factory, PrincipalOf(w.Office));
        Sum(office, RiskMetricSource.Risks).ShouldBe(Sum(all, RiskMetricSource.Risks));
        Sum(office, RiskMetricSource.Risks).ShouldBeGreaterThan(Sum(b, RiskMetricSource.Risks));

        // The outsider (risk owner role only, owns nothing) gets no facts; the action owner reads the critical risk through the record role.
        (await FactsAsync(fx.Factory, PrincipalOf(w.Outsider))).ShouldBeEmpty();
        Sum(await FactsAsync(fx.Factory, PrincipalOf(w.ActionOwner)), RiskMetricSource.Actions).ShouldBe(1);
    }

    [Fact]
    public async Task Dashboard_from_and_to_filter_on_the_identification_month()
    {
        var w = await SeedAsync(fx);
        var unit = $"unitId={w.DepartmentA}&includeProjectRisks=false";
        (await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?{unit}"))).Total.ShouldBe(3);
        (await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?{unit}&from=2026-01-01&to=2026-12-31"))).Total.ShouldBe(2);
        (await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?{unit}&from=2025-06-01&to=2025-06-01"))).Total.ShouldBe(1);
        (await RiskWorld.OkAsync<RiskDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?{unit}&to=2025-12-31"))).Total.ShouldBe(1);
        var heat = await RiskWorld.OkAsync<HeatCellRisksDto>(w.C(w.Office).GetAsync($"{Base}/heatmap?kind=inherent&likelihood=1&impact=2&{unit}&from=2026-01-01"));
        heat.Risks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lists_datasets_and_the_my_work_card_are_scoped_with_working_links()
    {
        var w = await SeedAsync(fx);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var lists = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == RiskPermissions.SystemCode);
        var query = new ReportingListQuery(today.AddMonths(-1), today, today, [w.DepartmentA, w.DepartmentB], 10);

        // (d) Lists: the Risk Office sees the critical risk of A; coordinator B does not; every link opens for the viewer.
        var critical = await lists.GetAsync(RiskListSource.CriticalList, query, PrincipalOf(w.Office), CancellationToken.None);
        critical.Single().TitleEn.ShouldBe("Payment outage");
        critical.Single().Tone.ShouldBe("danger");
        critical.Single().Link.ShouldBe($"/risk/risks/{critical.Single().Id:D}");
        (await w.C(w.Office).GetAsync($"/api/v1{critical.Single().Link}")).IsSuccessStatusCode.ShouldBeTrue();
        (await lists.GetAsync(RiskListSource.CriticalList, query, PrincipalOf(w.CoordinatorB), CancellationToken.None)).ShouldBeEmpty();
        (await w.C(w.CoordinatorB).GetAsync($"/api/v1{critical.Single().Link}")).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        var top = await lists.GetAsync(RiskListSource.Top10List, query, PrincipalOf(w.CoordinatorB), CancellationToken.None);
        top.Single().TitleEn.ShouldBe("Supplier B");
        (await lists.GetAsync(RiskListSource.Top10List, query with { UnitIds = [w.DepartmentA] }, PrincipalOf(w.Office), CancellationToken.None))
            .ShouldAllBe(i => i.UnitId == w.DepartmentA);

        // (e) Datasets: scoped rows, filter / sort / group.
        var datasets = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == RiskPermissions.SystemCode);
        var units = new[] { w.DepartmentA.ToString(), w.DepartmentB.ToString() };
        var rows = await datasets.RunAsync(new ReportQuery(RiskDatasetSource.RisksDataset, ["number", "titleEn", "residualScore", "unit"],
            [new ReportFilter("unit", ReportOperators.In, units)], [], [new ReportSort("residualScore", true)], 0, 50), PrincipalOf(w.Office), CancellationToken.None);
        rows.Total.ShouldBe(4);
        rows.Rows[0][1].ShouldBe("Payment outage");
        rows.RowIds!.Count.ShouldBe(4);
        var grouped = await datasets.RunAsync(new ReportQuery(RiskDatasetSource.RisksDataset, ["unit", "residualScore"],
            [new ReportFilter("unit", ReportOperators.In, units)], ["unit"], [], 0, 50), PrincipalOf(w.Office), CancellationToken.None);
        grouped.Columns.ShouldBe(["unit", "count", "residualScore"]);
        grouped.Rows.Single(r => (Guid)r[0]! == w.DepartmentA)[1].ShouldBe(3);
        var bRows = await datasets.RunAsync(new ReportQuery(RiskDatasetSource.RisksDataset, ["number", "unit"], [], [], [], 0, 50), PrincipalOf(w.CoordinatorB),
            CancellationToken.None);
        bRows.Rows.ShouldAllBe(r => (Guid)r[1]! == w.DepartmentB);
        var actions = await datasets.RunAsync(new ReportQuery(RiskDatasetSource.ActionsDataset, ["number", "unit", "plannedEnd"],
            [new ReportFilter("unit", ReportOperators.Equal, [w.DepartmentA.ToString()])], [], [], 0, 50), PrincipalOf(w.Office), CancellationToken.None);
        actions.Total.ShouldBe(1);
        (await datasets.RunAsync(new ReportQuery(RiskDatasetSource.ActionsDataset, ["number"], [], [], [], 0, 50), PrincipalOf(w.CoordinatorB), CancellationToken.None))
            .Total.ShouldBe(0);

        // (f) My Work card: the action owner has one overdue action; the outsider has no card.
        var cards = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == RiskPermissions.SystemCode);
        var card = (await cards.GetCardsAsync(PrincipalOf(w.ActionOwner), CancellationToken.None)).Single();
        (card.Code, card.TitleKey).ShouldBe((RiskMyWorkCards.Code, "myWork.cards.myRisks"));
        card.Counts.Single(c => c.Key == "myWork.counts.actionsOverdue").Value.ShouldBe(1);
        card.Items.Single().Overdue.ShouldBeTrue();
        card.Items.Single().Link.ShouldStartWith("/risk/my-actions?action=");
        (await cards.GetCardsAsync(PrincipalOf(w.Outsider), CancellationToken.None)).ShouldBeEmpty();
        var owner = (await cards.GetCardsAsync(PrincipalOf(w.OwnerA), CancellationToken.None)).SingleOrDefault();
        owner?.Items.Count.ShouldBeLessThanOrEqualTo(5);
    }
}
