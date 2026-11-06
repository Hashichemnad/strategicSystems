using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Reporting;
using Strategy.Platform.Reporting;
using static Strategy.IntegrationTests.Cap.ModuleReportingHelpers;

namespace Strategy.IntegrationTests.Cap;

/// <summary>CAP reporting sources (Phase 17, docs/design/reporting.md §2.5): facts vs the CAP dashboard, scope, lists, datasets, My Work card.</summary>
public sealed class CapReportingTests(CapFixture fx) : IClassFixture<CapFixture>
{
    private const string Base = CapWorld.Base;

    [Fact]
    public async Task Facts_match_the_dashboard_live_figures_are_scoped_and_lists_datasets_and_cards_follow_the_reader()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        DateOnly today;
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            today = await scope.ServiceProvider.GetRequiredService<CapDeadlines>().TodayAsync(CancellationToken.None);
        }

        var source = await w.SourceAsync();

        // r1: planned, validated and completed on time with evidence; r2: waiting for the owner; r3: department B, owned by the outsider;
        // r4: validated with an end date already past (late action); r5: submitted, waiting for the PM Office.
        var r1 = await w.RemarkAsync(source, "Publish the service charter");
        var a1 = await PlanAsync(w, r1.Id, w.CapActionValues("Publish the service charter", end: today.AddDays(20)));
        var validated = await w.ValidateAsync(a1.Id);
        var progressed = await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{a1.Id}/progress",
            new { pct = 1m, note = "Published", actualEnd = today, version = validated.Version }));
        progressed.AchievedPct.ShouldBe(1m);
        await w.UploadEvidenceAsync(a1.Id, w.Assignee);
        await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{a1.Id}/request-completion", new { note = "Done" }));
        await w.DecideAsync(a1.Id, CapProcesses.Completion);
        (await w.ActionAsync(a1.Id)).Status.ShouldBe(ActionStatuses.Completed);
        var r2 = await w.RemarkAsync(source, "Signage in the service hall", CapPriorities.Low);
        var r3 = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/sources/{source.Id}/remarks", new
        {
            textAr = "ملاحظة الإدارة ب",
            textEn = "Department B remark",
            typeCode = CapRemarkTypes.Observation,
            priority = CapPriorities.Medium,
            departmentUnitId = w.DepartmentB,
            ownerEmployeeId = w.Outsider.EmployeeId,
        }));
        var r4 = await w.RemarkAsync(source, "Complaints are not answered in time");
        var a4 = await PlanAsync(w, r4.Id, w.CapActionValues("Answer the complaints", budgetRequired: true, budgetAvailable: true, cost: 5000m, end: today.AddDays(-1)));
        (await w.ValidateAsync(a4.Id)).Status.ShouldBe(ActionStatuses.Late);
        var r5 = await w.RemarkAsync(source, "The intranet page is outdated");
        await PlanAsync(w, r5.Id, w.CapActionValues("Update the intranet page", end: today.AddDays(40)));
        (await w.GetAsync(r5.Id)).Status.ShouldBe(CapRemarkStatuses.Validation);

        // (a) The cache (every record) equals the PM Office's dashboard (scope All) on the same data.
        var dashboard = await CapWorld.OkAsync<CapDashboardDto>(w.C(w.Pmo).GetAsync($"{Base}/dashboard"));
        var facts = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null);
        facts.Sum(CapMetrics.Remarks).ShouldBe(dashboard.Remarks);
        facts.Sum(CapMetrics.Remarks, f => f.Has(CapMetrics.Open, "true")).ShouldBe(dashboard.OpenRemarks);
        facts.Sum(CapMetrics.Remarks, f => f.Has(ReportingDimensions.Overdue, "true")).ShouldBe(dashboard.OverdueRemarks);
        foreach (var (dimension, expected) in new[]
        {
            (ReportingDimensions.Status, dashboard.ByStatus), (ReportingDimensions.Priority, dashboard.ByPriority), (ReportingDimensions.Source, dashboard.BySourceType),
        })
        {
            foreach (var count in expected)
            {
                facts.Sum(CapMetrics.Remarks, f => f.Has(dimension, count.Code)).ShouldBe(count.Count, $"{dimension} = {count.Code}");
            }
        }

        facts.Sum(CapMetrics.Actions).ShouldBe(dashboard.Actions);
        facts.Sum(CapMetrics.Actions, f => f.Has(ReportingDimensions.Status, "late")).ShouldBe(dashboard.LateActions);
        facts.Sum(CapMetrics.Actions, f => f.Has(ReportingDimensions.Status, "completed")).ShouldBe(dashboard.CompletedActions);
        facts.Sum(CapMetrics.Actions, f => f.Has(CapMetrics.Budget, "true")).ShouldBe(dashboard.BudgetActions);
        foreach (var count in dashboard.ActionsByStatus)
        {
            facts.Sum(CapMetrics.Actions, f => f.Has(CapMetrics.Validated, "true") && f.Has(ReportingDimensions.Status, CapMetrics.ActionStatusCode(count.Code)))
                .ShouldBe(count.Count);
        }

        facts.Sum(CapMetrics.Closure).ShouldBe(dashboard.CompletedActions);
        facts.Weight(CapMetrics.Closure).ShouldBe(dashboard.Actions);
        facts.Where(f => f.Metric == CapMetrics.ActionsOnTime).ShouldAllBe(f => f.Month == MonthOf(today));
        (facts.Sum(CapMetrics.ActionsOnTime), facts.Weight(CapMetrics.ActionsOnTime)).ShouldBe((1m, 1m), "a1 was completed by its end date");
        facts.Where(f => f.Metric == CapMetrics.Remarks).ShouldAllBe(f => f.Partition == ReportingPartitions.Public);
        facts.Single(f => f.Metric == CapMetrics.Remarks && f.UnitId == w.DepartmentB).Value.ShouldBe(1m, "unit = the remark's department");

        // (b) Live path: the department manager (unit and below) counts department A only, as their dashboard; the outsider their own remark.
        var managerDashboard = await CapWorld.OkAsync<CapDashboardDto>(w.C(w.DepartmentManager).GetAsync($"{Base}/dashboard"));
        var managerFacts = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, w.DepartmentManager);
        managerFacts.Sum(CapMetrics.Remarks).ShouldBe(managerDashboard.Remarks);
        managerFacts.Sum(CapMetrics.Remarks).ShouldBe(4m);
        managerFacts.Sum(CapMetrics.Actions).ShouldBe(managerDashboard.Actions);
        managerFacts.ShouldAllBe(f => f.UnitId == w.DepartmentA);
        var outsiderFacts = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, w.Outsider);
        outsiderFacts.Sum(CapMetrics.Remarks).ShouldBe(1m);
        outsiderFacts.Where(f => f.Metric == CapMetrics.Remarks).ShouldAllBe(f => f.UnitId == w.DepartmentB);
        outsiderFacts.Sum(CapMetrics.Actions).ShouldBe(0m);
        (await PartitionsAsync(fx.Factory, CapPermissions.SystemCode, w.Pmo)).ShouldBeEmpty();

        // A past range holds no stock and no flow.
        var past = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null, today.AddMonths(-3), today.AddMonths(-1));
        past.ShouldBeEmpty();

        // (e) Report builder: scoped rows, filter, sort and grouping on the reader's query.
        var all = await RunAsync(fx.Factory, CapPermissions.SystemCode, w.Pmo, CapDatasetSource.RemarksDataset, ["number", "status", "department"]);
        all.Total.ShouldBe(dashboard.Remarks);
        var mine = await RunAsync(fx.Factory, CapPermissions.SystemCode, w.DepartmentManager, CapDatasetSource.RemarksDataset, ["number", "priority", "department"],
            sort: [new ReportSort("number", true)]);
        mine.Total.ShouldBe(4);
        mine.Rows.ShouldAllBe(r => (Guid)r[2]! == w.DepartmentA);
        mine.Rows.Select(r => (string)r[0]!).ShouldBe(mine.Rows.Select(r => (string)r[0]!).OrderByDescending(n => n, StringComparer.Ordinal).ToList());
        mine.RowIds!.ShouldNotContain(r3.Id);
        var low = await RunAsync(fx.Factory, CapPermissions.SystemCode, w.DepartmentManager, CapDatasetSource.RemarksDataset, ["number"],
            [new ReportFilter("priority", ReportOperators.Equal, [CapPriorities.Low])]);
        low.RowIds.ShouldBe([r2.Id]);
        var grouped = await RunAsync(fx.Factory, CapPermissions.SystemCode, w.DepartmentManager, CapDatasetSource.RemarksDataset, ["status"], groupBy: ["status"]);
        grouped.Columns.ShouldBe(["status", "count"]);
        grouped.Rows.Sum(r => (int)r[1]!).ShouldBe(4);
        grouped.Rows.Single(r => (string)r[0]! == CapRemarkStatuses.Assigned)[1].ShouldBe(1);
        var assigneeActions = await RunAsync(fx.Factory, CapPermissions.SystemCode, w.Assignee, CapDatasetSource.ActionsDataset,
            ["number", "assignee", "department", "status", "estimatedCost", "achieved"]);
        assigneeActions.Total.ShouldBe(3);
        assigneeActions.Rows.ShouldAllBe(r => (Guid)r[1]! == w.Assignee.EmployeeId!.Value);
        assigneeActions.Rows.Single(r => (string)r[3]! == ActionStatuses.Late)[4].ShouldBe(5000m);
        (await RunAsync(fx.Factory, CapPermissions.SystemCode, w.Outsider, CapDatasetSource.ActionsDataset, ["number"])).Total.ShouldBe(0);

        // (f) My Work card counts.
        var ownerCard = (await CardsAsync(fx.Factory, CapPermissions.SystemCode, w.SectionManager)).Single();
        ownerCard.TitleKey.ShouldBe("myWork.cards.capRemarksActions");
        ownerCard.Count("capRemarksToRespond").ShouldBe(1);
        ownerCard.Items.ShouldContain(i => i.Id == r2.Id && i.Link == $"/cap/remarks/{r2.Id}" && i.MessageKey == "cap.myWork.toAccept");
        var assigneeCard = (await CardsAsync(fx.Factory, CapPermissions.SystemCode, w.Assignee)).Single();
        assigneeCard.Count("capActionsLate").ShouldBe(1);
        assigneeCard.Count("capActionsDue").ShouldBe(0);
        assigneeCard.Items[0].Id.ShouldBe(a4.Id);
        assigneeCard.Items[0].Overdue.ShouldBeTrue();
        (await CardsAsync(fx.Factory, CapPermissions.SystemCode, w.Pmo)).Single().Count("capActionsToValidate").ShouldBe(1);
        (await CardsAsync(fx.Factory, CapPermissions.SystemCode, w.Outsider)).Single().Count("capRemarksToRespond").ShouldBe(1);
        (await CardsAsync(fx.Factory, CapPermissions.SystemCode, w.DepartmentManager)).ShouldBeEmpty("nothing waits for the department manager");
        foreach (var card in new[] { ownerCard, assigneeCard })
        {
            card.Items.Count.ShouldBeLessThanOrEqualTo(5);
        }

        // (g) Dashboard months: the assignment month includes everything, a later month nothing, a reversed range is refused.
        var month = $"{today:yyyy-MM}-01";
        var next = $"{MonthOf(today).AddMonths(1):yyyy-MM-dd}";
        (await CapWorld.OkAsync<CapDashboardDto>(w.C(w.Pmo).GetAsync($"{Base}/dashboard?from={month}&to={month}"))).Remarks.ShouldBe(dashboard.Remarks);
        (await CapWorld.OkAsync<CapDashboardDto>(w.C(w.Pmo).GetAsync($"{Base}/dashboard?from={next}"))).Remarks.ShouldBe(0);
        (await CapWorld.OkAsync<CapDashboardDto>(w.C(w.Pmo).GetAsync($"{Base}/dashboard?to={MonthOf(today).AddMonths(-1):yyyy-MM-dd}"))).Remarks.ShouldBe(0);
        (await w.C(w.Pmo).GetAsync($"{Base}/dashboard?from={next}&to={month}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // (d) Overdue remarks list, a year later: scoped, ordered by due date, links to the remark page (opened once the clock is back: the
        // travelled clock expires the HTTP sessions).
        IReadOnlyList<ReportingListItem> managerList;
        fx.Clock.Offset = TimeSpan.FromDays(400);
        try
        {
            var later = today.AddDays(400);
            var pmoList = await ListAsync(fx.Factory, CapPermissions.SystemCode, CapListSource.OverdueRemarks, w.Pmo, later);
            var overdueFacts = await FactsAsync(fx.Factory, CapPermissions.SystemCode, later, null, CapMetrics.Remarks);
            pmoList.Count.ShouldBe((int)overdueFacts.Sum(CapMetrics.Remarks, f => f.Has(ReportingDimensions.Overdue, "true")));
            pmoList.Select(i => i.Id).ShouldContain(r3.Id);
            pmoList.ShouldAllBe(i => i.Link == $"/cap/remarks/{i.Id}" && i.ResourceType == Remark.EntityType && i.Value > 0);
            pmoList.Select(i => i.Date).ShouldBeInOrder();
            managerList = await ListAsync(fx.Factory, CapPermissions.SystemCode, CapListSource.OverdueRemarks, w.DepartmentManager, later);
            managerList.Select(i => i.Id).ShouldContain(r2.Id);
            managerList.Select(i => i.Id).ShouldNotContain(r3.Id);
            managerList.ShouldAllBe(i => i.UnitId == w.DepartmentA);
            (await ListAsync(fx.Factory, CapPermissions.SystemCode, CapListSource.OverdueRemarks, w.Pmo, later, take: 1)).Count.ShouldBe(1);
        }
        finally
        {
            fx.Clock.Offset = TimeSpan.Zero;
        }

        foreach (var item in managerList)
        {
            (await w.C(w.DepartmentManager).GetAsync(item.Link.Replace("/cap/", $"{Base}/", StringComparison.Ordinal))).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await w.C(w.DepartmentManager).GetAsync($"{Base}/remarks/{r3.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Action_statuses_ages_and_on_time_follow_the_catalogue()
    {
        CapMetrics.ActionStatusCode(ActionStatuses.NotStarted).ShouldBe("not-started");
        CapMetrics.ActionStatusCode(ActionStatuses.InProgress).ShouldBe("in-progress");
        CapMetrics.ActionStatusCode(ActionStatuses.Late).ShouldBe("late");
        CapMetrics.ActionStatusCode(ActionStatuses.Completed).ShouldBe("completed");
        var today = new DateOnly(2026, 11, 5);
        CapMetrics.AgeOf(true, today, today).ShouldBe("0-30");
        CapMetrics.AgeOf(true, today.AddDays(-31), today).ShouldBe("31-90");
        CapMetrics.AgeOf(true, today.AddDays(-400), today).ShouldBe("365+");
        CapMetrics.AgeOf(false, today.AddDays(-400), today).ShouldBe("closed");
        var at = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        CapMetrics.OnTime(today, at, today).ShouldBeTrue();
        CapMetrics.OnTime(today.AddDays(1), at, today).ShouldBeFalse();
        CapMetrics.OnTime(null, at, today.AddDays(-1)).ShouldBeFalse("without an achieved date the completion date counts");
        CapMetrics.OnTime(null, at, null).ShouldBeTrue();
    }

    /// <summary>The owner accepts, adds one action and submits it for validation.</summary>
    private static async Task<CapActionDto> PlanAsync(CapWorld w, Guid remarkId, object action)
    {
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsync($"{Base}/remarks/{remarkId}/accept", null));
        var added = await w.AddActionAsync(remarkId, action);
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{remarkId}/submit", new { comment = "Ready" }));
        return added;
    }
}
