using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Reporting;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Kpi;

/// <summary>
/// KPI reporting sources (docs/design/reporting.md §2.5, §6, §7): cache facts equal the KPI dashboard (only approved results, capped performance with
/// card weights), the live path counts only the user's KPIs, lists / datasets are scoped, the My Work card counts, the dashboard takes a period.
/// </summary>
public sealed class KpiReportingTests(KpiFixture fx) : IClassFixture<KpiFixture>
{
    private const string Base = KpiWorld.Base;

    private static readonly string[] AllMetrics =
        [KpiMetricCodes.Score, KpiMetricCodes.Results, KpiMetricCodes.SubmissionsOnTime, KpiMetricCodes.Cards, KpiMetricCodes.Pending];

    [Fact]
    public async Task Cache_facts_equal_the_dashboard_and_the_live_path_counts_only_the_users_kpis()
    {
        const int year = 2061;
        var (w, s) = await SeedAsync(year);
        var range = (From: new DateOnly(year, 1, 1), To: new DateOnly(year, 12, 1));

        // (a) The cache (every record) = the dashboard of the Strategy Office (scope All) for the year.
        var all = await FactsAsync(w, null, range.From, range.To, AllMetrics);
        var dashboard = (await w.C(w.Pmo).GetFromJsonAsync<KpiDashboardDto>($"{Base}/dashboard?year={year}"))!;
        var thisMonth = MetricComputation.MonthOf(Today);
        var stock = await FactsAsync(w, null, thisMonth, thisMonth, KpiMetricCodes.Cards, KpiMetricCodes.Pending);
        all.ShouldNotContain(f => f.Metric == KpiMetricCodes.Cards || f.Metric == KpiMetricCodes.Pending); // stock: only when the range covers this month
        Sum(all, KpiMetricCodes.Results, (ReportingDimensions.Status, ResultStatuses.Approved)).ShouldBe(dashboard.ApprovedResults);
        Sum(all, KpiMetricCodes.Results, (ReportingDimensions.Status, ResultStatuses.PendingApproval)).ShouldBe(dashboard.ApprovalBacklog);
        Sum(stock, KpiMetricCodes.Pending, (ReportingDimensions.Status, ResultStatuses.PendingApproval), (KpiMetricCodes.Year, Y(year))).ShouldBe(dashboard.ApprovalBacklog);
        (Sum(stock, KpiMetricCodes.Pending, (ReportingDimensions.Status, ResultStatuses.Open), (KpiMetricCodes.Year, Y(year)))
            + Sum(stock, KpiMetricCodes.Pending, (ReportingDimensions.Status, ResultStatuses.Returned), (KpiMetricCodes.Year, Y(year)))).ShouldBe(dashboard.PendingUpdates);
        Sum(stock, KpiMetricCodes.Cards).ShouldBe(dashboard.TotalCards);
        Sum(stock, KpiMetricCodes.Cards, (ReportingDimensions.Status, CardStatuses.Active)).ShouldBe(dashboard.ActiveCards);
        Sum(stock, KpiMetricCodes.Cards, (ReportingDimensions.Status, CardStatuses.Active), (ReportingDimensions.Type, "strategic"))
            .ShouldBe(dashboard.ByType.Single(t => t.Key == "strategic").Count);

        // kpi.score: Σ weight · capped ÷ Σ weight over approved results = the dashboard's objective score (2 × 100 % capped + 1 × 80 %) ÷ 3.
        var objective = dashboard.ByObjective.Single(o => o.Key == s.ObjectiveCode);
        var score = all.Where(f => f.Metric == KpiMetricCodes.Score && f.Dimensions[ReportingDimensions.Objective] == s.ObjectiveCode).ToList();
        score.ShouldAllBe(f => f.Dimensions[ReportingDimensions.Perspective] == s.PerspectiveCode && f.Month == new DateOnly(year, 12, 1));
        Math.Round(score.Sum(f => f.Value) / score.Sum(f => f.Weight), 4).ShouldBe(objective.Score!.Value);
        objective.Score.ShouldBe(0.9333m);
        score.Sum(f => f.Weight).ShouldBe(3m); // the pending and open results do not count

        // RAG of the results: A1 green (≥ 95 %), B1 amber (≥ 80 %), A2 (pending) and A3 (open) not reported.
        Sum(all, KpiMetricCodes.Results, (KpiMetricCodes.Rag, KpiMetricCodes.NotReported)).ShouldBe(2);
        Sum(all, KpiMetricCodes.Results, (KpiMetricCodes.Rag, RagCodes.Green)).ShouldBe(1);
        Sum(all, KpiMetricCodes.Results, (KpiMetricCodes.Rag, RagCodes.Amber)).ShouldBe(1);

        // (b) The department A manager (unit scope) counts only department A: their dashboard figures, fewer than the cache.
        var manager = await FactsAsync(w, w.ManagerA, range.From, range.To, AllMetrics);
        var managerDashboard = (await w.C(w.ManagerA).GetFromJsonAsync<KpiDashboardDto>($"{Base}/dashboard?year={year}"))!;
        Sum(manager, KpiMetricCodes.Results, (ReportingDimensions.Status, ResultStatuses.Approved)).ShouldBe(managerDashboard.ApprovedResults);
        Sum(manager, KpiMetricCodes.Results, (ReportingDimensions.Status, ResultStatuses.Approved)).ShouldBe(1);
        var managerStock = await FactsAsync(w, w.ManagerA, thisMonth, thisMonth, AllMetrics);
        Sum(managerStock, KpiMetricCodes.Cards).ShouldBe(managerDashboard.TotalCards);
        Sum(managerStock, KpiMetricCodes.Cards).ShouldBe(3);
        Sum(managerStock, KpiMetricCodes.Pending, (KpiMetricCodes.Year, Y(year))).ShouldBe(2); // A2 pending approval, A3 open
        manager.Concat(managerStock).ShouldAllBe(f => f.UnitId == w.DepartmentA);
        manager.Where(f => f.Metric == KpiMetricCodes.Score).Sum(f => f.Value).ShouldBe(2m);
        Sum(manager, KpiMetricCodes.Results).ShouldBeLessThan(Sum(all, KpiMetricCodes.Results));
        var ownerB = await FactsAsync(w, w.OwnerB, range.From, range.To, KpiMetricCodes.Results);
        Sum(await FactsAsync(w, w.OwnerB, thisMonth, thisMonth, KpiMetricCodes.Cards), KpiMetricCodes.Cards).ShouldBe(1);
        Sum(ownerB, KpiMetricCodes.Results).ShouldBe(1);

        // On-time submissions by due month: both department A results were submitted before their deadline.
        var onTime = await FactsAsync(w, w.ManagerA, thisMonth, thisMonth.AddMonths(1), KpiMetricCodes.SubmissionsOnTime);
        onTime.Sum(f => f.Value).ShouldBe(2m);
        onTime.Sum(f => f.Weight).ShouldBe(2m);

        // Outside the year: no flow facts.
        (await FactsAsync(w, null, new DateOnly(year + 1, 1, 1), new DateOnly(year + 1, 12, 1), KpiMetricCodes.Score, KpiMetricCodes.Results))
            .ShouldNotContain(f => f.Dimensions.GetValueOrDefault(ReportingDimensions.Objective) == s.ObjectiveCode);
    }

    [Fact]
    public async Task Lists_datasets_my_work_card_and_dashboard_period_are_scoped()
    {
        const int year = 2062;
        var (w, s) = await SeedAsync(year);
        var query = new ReportingListQuery(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), Today, null, 10);

        // (d) Lists: the performers of the year, scoped and with card links.
        var top = await ListAsync(w, w.Pmo, KpiListSource.TopPerformers, query);
        top.ShouldContain(i => i.Id == s.A1 && i.Link == $"/kpi/cards/{s.A1:D}");
        (await ListAsync(w, w.Pmo, KpiListSource.TopPerformers, query with { UnitIds = [w.DepartmentB] })).Select(i => i.Id).ShouldBe([s.B1]);
        var bottom = await ListAsync(w, w.ManagerA, KpiListSource.BottomPerformers, query);
        bottom.Select(i => i.Id).ShouldBe([s.A1]); // A2 / A3 have no approved result, B1 is another department
        (await w.C(w.ManagerA).GetAsync($"{Base}/cards/{bottom[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // (e) Datasets on the reader's scope: filter, sort, group.
        var results = await RunAsync(w, w.ManagerA, new ReportQuery(KpiDatasetSource.ResultsDataset, ["cardCode", "status", "cappedPerformance", "unit"],
            [new ReportFilter("year", ReportOperators.Equal, [Y(year)])], [], [new ReportSort("cardCode", false)], 0, 50));
        results.Total.ShouldBe(3);
        results.Rows.ShouldAllBe(r => (Guid?)r[3] == w.DepartmentA);
        results.RowIds!.ShouldBe([s.A1Result, s.A2Result, s.A3Result], ignoreOrder: true);
        var grouped = await RunAsync(w, w.Pmo, new ReportQuery(KpiDatasetSource.ResultsDataset, ["status"],
            [new ReportFilter("year", ReportOperators.Equal, [Y(year)])], ["status"], [], 0, 50));
        grouped.Rows.Single(r => (string)r[0]! == ResultStatuses.Approved)[1].ShouldBe(2);
        grouped.Rows.Single(r => (string)r[0]! == ResultStatuses.PendingApproval)[1].ShouldBe(1);
        var cards = await RunAsync(w, w.OwnerB, new ReportQuery(KpiDatasetSource.CardsDataset, ["code", "unit", "owner"], [], [], [], 0, 50));
        cards.RowIds!.ShouldBe([s.B1]);
        cards.Rows.Single()[2].ShouldBe(w.OwnerB.EmployeeId);

        // (f) My Work: owner A has A3 to submit, then A2 comes back for correction too.
        var before = (await CardsAsync(w, w.OwnerA)).ShouldHaveSingleItem();
        before.Counts.Single(x => x.Key == "toSubmit").Value.ShouldBe(1);
        before.Items.ShouldHaveSingleItem().Id.ShouldBe(s.A3Result);
        await w.DecideAsync(s.A2Result, "return", "Add the evidence");
        var card = (await CardsAsync(w, w.OwnerA)).ShouldHaveSingleItem();
        card.ShouldSatisfyAllConditions(c => c.Code.ShouldBe(KpiMyWorkCards.ResultsCard), c => c.TitleKey.ShouldBe("myWork.cards.kpi-results"),
            c => c.Counts.Single(x => x.Key == "toSubmit").Value.ShouldBe(2), c => c.Counts.Single(x => x.Key == "returned").Value.ShouldBe(1),
            c => c.Counts.Single(x => x.Key == "overdue").Value.ShouldBe(0), c => c.Items.Count.ShouldBe(2),
            c => c.Items.ShouldContain(i => i.Id == s.A2Result && i.MessageKey == "kpi.myWork.returned" && i.Link == $"/kpi/results/{s.A2Result:D}"));
        (await CardsAsync(w, w.ManagerA)).ShouldBeEmpty();

        // (g) The dashboard period: results whose period ends in the range.
        var inYear = (await w.C(w.Pmo).GetFromJsonAsync<KpiDashboardDto>($"{Base}/dashboard?from={year}-01-01&to={year}-12-31"))!;
        inYear.Year.ShouldBe(year);
        inYear.ApprovedResults.ShouldBe(2);
        var firstHalf = (await w.C(w.Pmo).GetFromJsonAsync<KpiDashboardDto>($"{Base}/dashboard?year={year}&from={year}-01-01&to={year}-06-30"))!;
        firstHalf.ApprovedResults.ShouldBe(0); // annual results end on 31 December
        (await w.C(w.Pmo).GetAsync($"{Base}/dashboard?from={year}-12-01&to={year}-01-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    private DateOnly Today => DateOnly.FromDateTime(fx.Factory.Services.GetRequiredService<Strategy.Platform.Kernel.Time.IClock>().UtcNow.UtcDateTime);

    private sealed record Seed(string ObjectiveCode, string PerspectiveCode, Guid A1, Guid B1, Guid A1Result, Guid A2Result, Guid A3Result);

    /// <summary>
    /// A vision → perspective → objective; annual KPIs A1 (weight 2), A2 and A3 (weight 1) in department A, B1 (weight 1) in department B. A1 (120 %
    /// of target) and B1 (80 %) are approved, A2 is submitted and waits for approval, A3 is open.
    /// </summary>
    private static async Task<(KpiWorld World, Seed Seed)> SeedAsync(ApiFactory factory, int year)
    {
        var w = await KpiWorld.CreateAsync(factory);
        var suffix = Guid.CreateVersion7().ToString("N")[^6..];
        var vision = await Element(w, "vision", $"RV-{suffix}", null);
        var perspective = await Element(w, "perspective", $"RP-{suffix}", vision);
        var objective = await Element(w, "objective", $"RO-{suffix}", perspective);
        var a1 = await w.CreateCardAsync(KpiWorld.Card("Report A1 " + suffix, w.OwnerA, w.DepartmentA, period: "annual", objectiveId: objective, weight: 2));
        var a2 = await w.CreateCardAsync(KpiWorld.Card("Report A2 " + suffix, w.OwnerA, w.DepartmentA, period: "annual", objectiveId: objective, weight: 1));
        var a3 = await w.CreateCardAsync(KpiWorld.Card("Report A3 " + suffix, w.OwnerA, w.DepartmentA, period: "annual", objectiveId: objective, weight: 1));
        var b1 = await w.CreateCardAsync(KpiWorld.Card("Report B1 " + suffix, w.OwnerB, w.DepartmentB, period: "annual", objectiveId: objective, weight: 1));
        await w.StartYearAsync(year, [KpiWorld.Item(a1, 100), KpiWorld.Item(a2, 100), KpiWorld.Item(a3, 100), KpiWorld.Item(b1, 100)]);
        var a1Result = (await w.PlanAsync(a1.Id, year)).Periods.Single().ResultId;
        var a2Result = (await w.PlanAsync(a2.Id, year)).Periods.Single().ResultId;
        var b1Result = (await w.PlanAsync(b1.Id, year)).Periods.Single().ResultId;
        await w.SubmitAsync(w.OwnerA, a1Result, KpiWorld.Entry(120));
        await w.SubmitAsync(w.OwnerA, a2Result, KpiWorld.Entry(50, "Budget cut", "Request budget"));
        await w.SubmitAsync(w.OwnerB, b1Result, KpiWorld.Entry(80, "Vendor late", "Escalate"));
        await w.DecideAsync(a1Result);
        await w.DecideAsync(b1Result);
        var a3Result = (await w.PlanAsync(a3.Id, year)).Periods.Single().ResultId;
        return (w, new Seed($"RO-{suffix}", $"RP-{suffix}", a1.Id, b1.Id, a1Result, a2Result, a3Result));
    }

    private Task<(KpiWorld World, Seed Seed)> SeedAsync(int year) => SeedAsync(fx.Factory, year);

    private static async Task<Guid> Element(KpiWorld w, string kind, string code, Guid? parent) =>
        (await KpiWorld.OkAsync<KpiElementDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/strategy/elements",
            new { kind, code, nameAr = "عنصر " + code, nameEn = "Element " + code, parentId = parent, sort = 1, active = true }))).Id;

    private static string Y(int year) => year.ToString(CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(KpiWorld w, TestUser? user, DateOnly from, DateOnly to, params string[] metrics)
    {
        await using var scope = user is null ? TestAccess.SystemScope(w.Factory) : await TaskTestHelpers.UserScopeAsync(w.Factory, user);
        var principal = user is null ? null : scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == KpiPermissions.SystemCode);
        source.Metrics.Select(m => m.Code).ShouldBe(AllMetrics, ignoreOrder: true);
        (await source.VisiblePartitionsAsync(principal ?? KpiWorld.System, CancellationToken.None)).ShouldBeEmpty();
        return await source.ComputeAsync(new MetricComputation(metrics, from, to, MetricComputation.MonthOf(Today), Today, principal), CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ReportingListItem>> ListAsync(KpiWorld w, TestUser user, string code, ReportingListQuery query)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(w.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == KpiPermissions.SystemCode);
        return await source.GetAsync(code, query, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static async Task<ReportResult> RunAsync(KpiWorld w, TestUser user, ReportQuery query)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(w.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == KpiPermissions.SystemCode);
        return await source.RunAsync(query, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<MyWorkCard>> CardsAsync(KpiWorld w, TestUser user)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(w.Factory, user);
        var source = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == KpiPermissions.SystemCode);
        return await source.GetCardsAsync(scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, params (string Key, string Value)[] dimensions) =>
        facts.Where(f => f.Metric == metric && dimensions.All(d => f.Dimensions.TryGetValue(d.Key, out var v) && v == d.Value)).Sum(f => f.Value);
}
