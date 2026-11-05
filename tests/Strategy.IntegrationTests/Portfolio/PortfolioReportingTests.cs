using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Portfolio.Reporting;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>
/// Portfolio reporting sources (docs/design/reporting.md §2.5, §6, §7): the cache facts equal the module dashboard, the live path counts only what the
/// user reads, budget figures stay in the cost partition, lists / datasets are scoped, My Work cards count, and the dashboard takes a period.
/// </summary>
public sealed class PortfolioReportingTests(PortfolioFixture fx) : IClassFixture<PortfolioFixture>
{
    private const string Base = PortfolioWorld.Base;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly DateOnly ThisMonth = MetricComputation.MonthOf(Today);

    private static readonly string[] AllMetrics =
        [PortfolioMetricCodes.Projects, PortfolioMetricCodes.Health, PortfolioMetricCodes.Budget, PortfolioMetricCodes.Requests, PortfolioMetricCodes.Milestones];

    [Fact]
    public async Task Cache_facts_equal_the_dashboard_and_the_live_path_counts_only_what_the_user_reads()
    {
        var (w, seed) = await SeedAsync();

        // (a) Every record (the cache) = the dashboard of an all-scope user, read at the same moment.
        var all = await FactsAsync(null, ThisMonth, ThisMonth, AllMetrics);
        var dashboard = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.PmOffice).GetAsync($"{Base}/dashboard"));
        Sum(all, PortfolioMetricCodes.Projects).ShouldBe(dashboard.Total);
        Sum(all, PortfolioMetricCodes.Projects, (ReportingDimensions.Status, ProjectStatuses.Active)).ShouldBe(dashboard.Active);
        Sum(all, PortfolioMetricCodes.Projects, (PortfolioMetricCodes.Delayed, "true")).ShouldBe(dashboard.Delayed);
        foreach (var stage in dashboard.ByStage.Where(s => ProjectStages.ProjectPath.Contains(s.Stage) && s.Stage != ProjectStages.Completed))
        {
            Sum(all, PortfolioMetricCodes.Projects, (ReportingDimensions.Status, ProjectStatuses.Active), (ReportingDimensions.Stage, stage.Stage)).ShouldBe(stage.Count);
        }

        Sum(all, PortfolioMetricCodes.Budget, (PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindPlanned)).ShouldBe(dashboard.Budget.Sum(b => b.Budget));
        Sum(all, PortfolioMetricCodes.Budget, (PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindActual)).ShouldBe(dashboard.Budget.Sum(b => b.Actual));
        all.Where(f => f.Metric == PortfolioMetricCodes.Budget).ShouldAllBe(f => f.Partition == ReportingPartitions.Costs);
        Sum(all, PortfolioMetricCodes.Requests).ShouldBe(dashboard.Funnel.Submitted);
        Sum(all, PortfolioMetricCodes.Requests, (ReportingDimensions.Stage, RequestStatuses.Approved)).ShouldBe(dashboard.Funnel.Approved);
        if (dashboard.HealthOn)
        {
            var heat = dashboard.Heat.Where(h => h.Dimension == HealthDimensions.Overall).ToList();
            var green = all.Where(f => f.Metric == PortfolioMetricCodes.Health).Sum(f => f.Value);
            green.ShouldBe(heat.Sum(h => h.Green));
            all.Where(f => f.Metric == PortfolioMetricCodes.Health).Sum(f => f.Weight).ShouldBe(heat.Sum(h => h.Green + h.Amber + h.Red));
        }

        // The facts carry the owning unit of the project (the unit column of portfolio.project.read).
        all.Where(f => f.Metric == PortfolioMetricCodes.Projects && f.UnitId == w.DepartmentId).Sum(f => f.Value).ShouldBe(2);
        all.Where(f => f.Metric == PortfolioMetricCodes.Projects && f.UnitId == w.OtherDepartmentId).Sum(f => f.Value).ShouldBe(1);
        all.Where(f => f.Metric is PortfolioMetricCodes.Projects or PortfolioMetricCodes.Requests).ShouldAllBe(f => f.Month == ThisMonth);

        // (b) The live path for the department viewer (unit scope) = their own dashboard, and narrower than the PM Office's.
        var viewer = await FactsAsync(w.DepartmentViewer, ThisMonth, ThisMonth, AllMetrics);
        var viewerDashboard = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard"));
        Sum(viewer, PortfolioMetricCodes.Projects).ShouldBe(viewerDashboard.Total);
        Sum(viewer, PortfolioMetricCodes.Projects).ShouldBe(2);
        Sum(viewer, PortfolioMetricCodes.Projects, (PortfolioMetricCodes.Delayed, "true")).ShouldBe(viewerDashboard.Delayed);
        Sum(viewer, PortfolioMetricCodes.Requests).ShouldBe(viewerDashboard.Funnel.Submitted);
        viewer.Where(f => f.Metric == PortfolioMetricCodes.Projects).ShouldAllBe(f => f.UnitId == w.DepartmentId);
        Sum(viewer, PortfolioMetricCodes.Projects).ShouldBeLessThan(Sum(all, PortfolioMetricCodes.Projects));

        // The other department's requester (own scope) counts only their own project.
        var other = await FactsAsync(w.OtherRequester, ThisMonth, ThisMonth, PortfolioMetricCodes.Projects);
        Sum(other, PortfolioMetricCodes.Projects).ShouldBe(1);
        other.ShouldAllBe(f => f.UnitId == w.OtherDepartmentId);

        // Flow: the milestone counts in the month of its planned end only.
        var milestoneMonth = MetricComputation.MonthOf(seed.MilestoneEnd);
        var flow = await FactsAsync(w.DepartmentViewer, milestoneMonth, milestoneMonth, PortfolioMetricCodes.Milestones);
        flow.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            f => f.Month.ShouldBe(milestoneMonth), f => f.Value.ShouldBe(1), f => f.UnitId.ShouldBe(w.DepartmentId),
            f => f.Dimensions[ReportingDimensions.Status].ShouldBe(PortfolioMetricCodes.MilestoneOpen), f => f.Dimensions[ReportingDimensions.OnTime].ShouldBe("true"));
        (await FactsAsync(w.DepartmentViewer, milestoneMonth.AddMonths(1), milestoneMonth.AddMonths(2), PortfolioMetricCodes.Milestones)).ShouldBeEmpty();

        // Stock metrics are stamped on the current month only when the range covers it.
        (await FactsAsync(w.DepartmentViewer, ThisMonth.AddMonths(-3), ThisMonth.AddMonths(-1), PortfolioMetricCodes.Projects)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Budget_facts_are_in_the_cost_partition_and_only_users_who_see_every_cost_read_it()
    {
        var (w, seed) = await SeedAsync();

        // The other department's requester also joins a department project as a team member: they read it, but not its costs (ADR 0098).
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, seed.Delivery, RecordPrincipalKinds.Employee,
                w.OtherRequester.EmployeeId!.Value, PortfolioPermissions.TeamMemberRole);
        }

        (await PartitionsAsync(w.DepartmentViewer)).ShouldContain(ReportingPartitions.Costs);
        (await PartitionsAsync(w.PmOffice)).ShouldContain(ReportingPartitions.Costs);
        (await PartitionsAsync(w.OtherRequester)).ShouldNotContain(ReportingPartitions.Costs);

        // Live: the member's budget figures only hold their own project (whose costs they see).
        var member = await FactsAsync(w.OtherRequester, ThisMonth, ThisMonth, PortfolioMetricCodes.Projects, PortfolioMetricCodes.Budget);
        Sum(member, PortfolioMetricCodes.Projects).ShouldBe(2);
        Sum(member, PortfolioMetricCodes.Budget, (PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindPlanned)).ShouldBe(120_000m);
        member.Where(f => f.Metric == PortfolioMetricCodes.Budget).ShouldAllBe(f => f.UnitId == w.OtherDepartmentId && f.Partition == ReportingPartitions.Costs);
        var dashboard = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.OtherRequester).GetAsync($"{Base}/dashboard"));
        dashboard.Budget.Sum(b => b.Budget).ShouldBe(120_000m);
        dashboard.CostsHidden.ShouldBe(1);

        // The report builder masks the hidden project's budget row by row; the visible one keeps it.
        var rows = await RunAsync(w.OtherRequester, new ReportQuery(PortfolioDatasetSource.ProjectsDataset, ["number", "budget"], [], [], [new ReportSort("number", false)], 0, 50));
        rows.Total.ShouldBe(2);
        rows.Rows.Single(r => (string)r[0]! == seed.DeliveryNumber)[1].ShouldBeNull();
        rows.Rows.Single(r => (string)r[0]! == seed.OtherNumber)[1].ShouldBe(120_000m);

        // Filtering on the masked field cannot reveal it.
        var filtered = await RunAsync(w.OtherRequester, new ReportQuery(PortfolioDatasetSource.ProjectsDataset, ["number"],
            [new ReportFilter("budget", ReportOperators.GreaterOrEqual, ["1"])], [], [], 0, 50));
        filtered.Rows.Select(r => (string)r[0]!).ShouldBe([seed.OtherNumber]);
    }

    [Fact]
    public async Task Lists_and_datasets_hold_only_the_users_records_with_working_links()
    {
        var (w, seed) = await SeedAsync();
        var query = new ReportingListQuery(ThisMonth, ThisMonth.AddMonths(1).AddDays(-1), Today, null, 10);

        var delayed = await ListAsync(w.DepartmentViewer, PortfolioListSource.DelayedProjects, query);
        delayed.Select(i => i.Id).ShouldBe([seed.Delayed]);
        delayed[0].ShouldSatisfyAllConditions(i => i.Link.ShouldBe($"/portfolio/projects/{seed.Delayed:D}"), i => i.Tone.ShouldBe("danger"),
            i => i.UnitId.ShouldBe(w.DepartmentId), i => i.ResourceType.ShouldBe(PortfolioPermissions.ProjectResource));
        (await ListAsync(w.OtherRequester, PortfolioListSource.DelayedProjects, query)).ShouldNotContain(i => i.Id == seed.Delayed);
        (await ListAsync(w.DepartmentViewer, PortfolioListSource.DelayedProjects, query with { UnitIds = [w.OtherDepartmentId] })).ShouldBeEmpty();

        var upcoming = await ListAsync(w.DepartmentViewer, PortfolioListSource.UpcomingMilestones, query);
        upcoming.ShouldContain(i => i.Id == seed.Milestone && i.Link == $"/portfolio/projects/{seed.Delivery:D}?tab=milestones" && i.Date == seed.MilestoneEnd);
        (await ListAsync(w.OtherRequester, PortfolioListSource.UpcomingMilestones, query)).ShouldNotContain(i => i.Id == seed.Milestone);

        // Every list link is an existing SPA route that opens for the user.
        (await w.C(w.DepartmentViewer).GetAsync($"{Base}/projects/{delayed[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Datasets: scoped rows, filter, sort and grouping in the database query.
        var projects = await RunAsync(w.DepartmentViewer, new ReportQuery(PortfolioDatasetSource.ProjectsDataset, ["number", "progressStatus", "unit", "budget"], [],
            [], [new ReportSort("number", true)], 0, 50));
        projects.Total.ShouldBe(2);
        projects.Rows.Select(r => (string)r[0]!).ShouldBe(new[] { seed.DeliveryNumber, seed.DelayedNumber }.OrderByDescending(n => n, StringComparer.Ordinal).ToList());
        projects.RowIds!.ShouldBe([seed.Delivery, seed.Delayed], ignoreOrder: true);
        projects.Rows.ShouldAllBe(r => (Guid?)r[2] == w.DepartmentId && r[3] != null);
        var onlyDelayed = await RunAsync(w.DepartmentViewer, new ReportQuery(PortfolioDatasetSource.ProjectsDataset, ["number"],
            [new ReportFilter("progressStatus", ReportOperators.Equal, [ProgressCalculator.Delayed])], [], [], 0, 50));
        onlyDelayed.Rows.Select(r => (string)r[0]!).ShouldBe([seed.DelayedNumber]);
        var grouped = await RunAsync(w.DepartmentViewer, new ReportQuery(PortfolioDatasetSource.ProjectsDataset, ["progressStatus", "budget"], [], ["progressStatus"], [], 0, 50));
        grouped.Columns.ShouldBe(["progressStatus", "count", "budget"]);
        grouped.Rows.Sum(r => (int)r[1]!).ShouldBe(2);
        grouped.Rows.Sum(r => (decimal)r[2]!).ShouldBe(240_000m + 240_000m);

        var milestones = await RunAsync(w.DepartmentViewer, new ReportQuery(PortfolioDatasetSource.MilestonesDataset, ["projectNumber", "code", "plannedEnd"], [], [], [], 0, 50));
        milestones.Rows.ShouldHaveSingleItem()[2].ShouldBe(seed.MilestoneEnd);
        milestones.RowIds!.ShouldBe([seed.Delivery]);
        (await RunAsync(w.OtherRequester, new ReportQuery(PortfolioDatasetSource.MilestonesDataset, ["code"], [], [], [], 0, 50))).Total.ShouldBe(0);

        var requests = await RunAsync(w.DepartmentViewer, new ReportQuery(PortfolioDatasetSource.RequestsDataset, ["number", "unit"], [], [], [], 0, 50));
        requests.Rows.ShouldAllBe(r => (Guid?)r[1] == w.DepartmentId);
        requests.Total.ShouldBe(2);
    }

    [Fact]
    public async Task My_work_cards_count_the_pms_projects_and_milestones_and_the_dashboard_takes_a_period()
    {
        var (w, seed) = await SeedAsync();

        // The requester manages the delivery project: one project, one milestone due within 30 days.
        var cards = await CardsAsync(w.Requester);
        var card = cards.Single(c => c.Code == PortfolioMyWorkCards.ProjectsCard);
        card.TitleKey.ShouldBe("myWork.cards.portfolio-projects");
        card.Counts.Single(c => c.Key == "projects").Value.ShouldBe(1);
        card.Counts.Single(c => c.Key == "milestonesDue").Value.ShouldBe(1);
        card.Counts.Single(c => c.Key == "milestonesOverdue").Value.ShouldBe(0);
        card.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(i => i.Id.ShouldBe(seed.Milestone),
            i => i.Link.ShouldBe($"/portfolio/projects/{seed.Delivery:D}?tab=milestones"), i => i.DueDate.ShouldBe(seed.MilestoneEnd));
        cards.ShouldNotContain(c => c.Code == PortfolioMyWorkCards.WaselCard); // no vendor submission waits
        (await CardsAsync(w.DepartmentViewer)).ShouldBeEmpty();

        // (g) The dashboard period: projects whose planned window overlaps it (seeded: next January to the June after).
        var year = Today.Year;
        var inside = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(
            w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard?from={year + 1}-01-01&to={year + 1}-12-31"));
        inside.Total.ShouldBe(2);
        var after = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(
            w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard?from={year + 3}-01-01&to={year + 3}-03-31"));
        after.Total.ShouldBe(0);
        var before = await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard?to={year}-06-30"));
        before.Total.ShouldBe(0);
        (await PortfolioWorld.OkAsync<PortfolioDashboardDto>(w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard"))).Total.ShouldBe(2);
        (await w.C(w.DepartmentViewer).GetAsync($"{Base}/dashboard?from={year + 1}-02-01&to={year + 1}-01-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    private sealed record Seed(
        Guid Delivery, string DeliveryNumber, Guid Delayed, string DelayedNumber, Guid Other, string OtherNumber, Guid Milestone, DateOnly MilestoneEnd);

    /// <summary>
    /// Two active projects in the department (requested by the requester; the delivery project managed by them, with one milestone due in ten days;
    /// the other delayed) and one in the other department. Test set-up only: projects are created from their requests through the domain.
    /// </summary>
    private async Task<(PortfolioWorld World, Seed Seed)> SeedAsync()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var r1 = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Delivery", budget: 240_000m)));
        var r2 = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Delayed", budget: 240_000m)));
        var r3 = await w.SubmitAsync(await w.CreateWithCostFileAsync(PortfolioWorld.Valid("Other", budget: 120_000m), w.OtherRequester), w.OtherRequester);
        var end = Today.AddDays(10);
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var requests = await db.Requests.Where(r => r.Id == r1.Id || r.Id == r2.Id || r.Id == r3.Id).ToDictionaryAsync(r => r.Id);
        var delivery = Project.FromRequest($"RPT-{w.Suffix}-1", requests[r1.Id], w.Director.EmployeeId);
        delivery.SetPm(w.Requester.EmployeeId!.Value, DateTimeOffset.UtcNow);
        var delayed = Project.FromRequest($"RPT-{w.Suffix}-2", requests[r2.Id], w.Director.EmployeeId);
        delayed.SetProgress(0.1m, 0.5m, ProgressCalculator.Delayed);
        var other = Project.FromRequest($"RPT-{w.Suffix}-3", requests[r3.Id], null);
        db.Projects.AddRange(delivery, delayed, other);
        var milestone = Milestone.Create(delivery.Id, $"{delivery.Number}-1", new MilestoneValues(
            "تسليم", "Delivery", null, false, false, Today.AddDays(-20), end, 1m, 10_000m, true, null, null, null, false, null));
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IRecordRoleService>().GrantAsync(PortfolioPermissions.ProjectResource, delivery.Id, RecordPrincipalKinds.Employee,
            w.Requester.EmployeeId!.Value, PortfolioPermissions.PmRole);
        return (w, new Seed(delivery.Id, delivery.Number, delayed.Id, delayed.Number, other.Id, other.Number, milestone.Id, end));
    }

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(TestUser? user, DateOnly from, DateOnly to, params string[] metrics)
    {
        await using var scope = user is null ? TestAccess.SystemScope(fx.Factory) : await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var principal = user is null ? null : scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == PortfolioPermissions.SystemCode);
        source.Metrics.Select(m => m.Code).ShouldBe(AllMetrics, ignoreOrder: true);
        return await source.ComputeAsync(new MetricComputation(metrics, from, to, ThisMonth, Today, principal), CancellationToken.None);
    }

    private async Task<IReadOnlyCollection<string>> PartitionsAsync(TestUser user)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == PortfolioPermissions.SystemCode);
        return await source.VisiblePartitionsAsync(scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ReportingListItem>> ListAsync(TestUser user, string code, ReportingListQuery query)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingListSource>().Single(s => s.SystemCode == PortfolioPermissions.SystemCode);
        return await source.GetAsync(code, query, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private async Task<ReportResult> RunAsync(TestUser user, ReportQuery query)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == PortfolioPermissions.SystemCode);
        return await source.RunAsync(query, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private async Task<IReadOnlyList<MyWorkCard>> CardsAsync(TestUser user)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == PortfolioPermissions.SystemCode);
        return await source.GetCardsAsync(scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, params (string Key, string Value)[] dimensions) =>
        facts.Where(f => f.Metric == metric && dimensions.All(d => f.Dimensions.TryGetValue(d.Key, out var v) && v == d.Value)).Sum(f => f.Value);
}
