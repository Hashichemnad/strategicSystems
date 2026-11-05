using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Reporting;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Requests.Application;
using Strategy.Platform.Reporting;
using static Strategy.IntegrationTests.Cap.ModuleReportingHelpers;

namespace Strategy.IntegrationTests.Bench;

/// <summary>Benchmarking reporting sources (Phase 17, docs/design/reporting.md §2.5): facts vs the dashboard, scope, the study dataset and card.</summary>
public sealed class BenchReportingTests(BenchFixture fx) : IClassFixture<BenchFixture>
{
    private const string Base = BenchWorld.Base;

    [Fact]
    public async Task Facts_match_the_dashboard_live_figures_are_scoped_and_the_dataset_and_card_follow_the_reader()
    {
        var w = await BenchWorld.CreateAsync(fx.Factory);
        await fx.Factory.GrantRoleAsync(w.Manager.Id, "bench.viewer");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = today.Year + 1;

        // s1 (Q1) in Search; s2 (Q3) through analysis with a development plan in CAP and a best practice; s3 (Q4) of department B;
        // s4 (Q2) cancelled; s5 (Q1) returned to the requester.
        var s1 = await w.ApprovedStudyAsync(year, 1, "Channels benchmarking");
        var s2 = await w.ToAnalysisAsync(await w.ApprovedStudyAsync(year, 3, "Analysis benchmarking"));
        s2.Stage.ShouldBe(StudyStages.Analysis);
        var data = new
        {
            titleAr = "مقارنة الإدارة ب",
            titleEn = "Department B benchmarking",
            responsibleUnit = w.DepartmentB,
            responsibleEmployee = w.Outsider.EmployeeId,
            targetYear = year,
            targetQuarter = 4,
            comparisonType = "functional",
            purpose = "process-improvement",
            scope = "Payroll processing",
            topics = new object[] { new { topic = "Payroll", owner = w.Outsider.EmployeeId } },
        };
        var draft3 = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Outsider).PostAsJsonAsync("/api/v1/requests", new { typeCode = BenchRequestTypes.Request, data }));
        var submitted3 = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Outsider).PostAsJsonAsync($"/api/v1/requests/{draft3.Id}/submit", new { version = draft3.Version }));
        await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Office).PostAsJsonAsync($"/api/v1/requests/{submitted3.Id}/decide", new { action = "approve" }));
        var s3 = await w.StudyOfRequestAsync(submitted3.Id);
        var s4 = await w.ApprovedStudyAsync(year, 2, "Cancelled benchmarking");
        await BenchWorld.OkAsync<BenchStudyDto>(w.C(w.Office).PostAsJsonAsync($"{Base}/studies/{s4.Id}/cancel", new { reason = "Merged" }));
        var draft5 = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Coordinator).PostAsJsonAsync("/api/v1/requests",
            new { typeCode = BenchRequestTypes.Request, data = w.PlanData(year, 1, "Returned benchmarking") }));
        var submitted5 = await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Coordinator).PostAsJsonAsync($"/api/v1/requests/{draft5.Id}/submit", new { version = draft5.Version }));
        await BenchWorld.OkAsync<RequestRecordDto>(w.C(w.Office).PostAsJsonAsync($"/api/v1/requests/{submitted5.Id}/decide", new { action = "return", comment = "Narrow it" }));
        var s5 = await w.StudyOfRequestAsync(submitted5.Id);
        s5.Returned.ShouldBeTrue();

        // (f) The coordinator's card: s2 waits for the analysis form, s5 for the resubmitted plan; the outsider has nothing to do.
        var card = (await CardsAsync(fx.Factory, BenchPermissions.SystemCode, w.Coordinator)).Single();
        card.TitleKey.ShouldBe("myWork.cards.benchStudies");
        (card.Count("benchStudiesToAct"), card.Count("benchPlansReturned")).ShouldBe((1, 1));
        card.Items.Select(i => i.Id).ShouldBe([s5.Id, s2.Id], ignoreOrder: true);
        card.Items.Single(i => i.Id == s2.Id).Link.ShouldBe($"/bench/studies/{s2.Id}");
        card.Items.Single(i => i.Id == s2.Id).MessageKey.ShouldBe("bench.myWork.analysis");
        card.Items.Single(i => i.Id == s5.Id).Link.ShouldBe($"/requests/{s5.RequestId}");
        (await CardsAsync(fx.Factory, BenchPermissions.SystemCode, w.Outsider)).ShouldBeEmpty();

        // Analysis approved → two proposed CAP actions and a best practice in the library.
        var due = today.AddDays(90);
        await BenchWorld.OkAsync<BenchAnalysisDto>(w.C(w.Coordinator).PutAsJsonAsync($"{Base}/studies/{s2.Id}/analysis", new
        {
            practices = new object[]
            {
                new { topicId = s2.Topics[0].Id, subject = "Single service app", ourPractice = "Five portals", theirPractice = "One app", category = "compared", applicable = true, addToLibrary = true },
            },
            indicators = Array.Empty<object>(),
            planItems = new object[]
            {
                new { titleAr = "إطلاق تطبيق موحد", titleEn = "Launch a single app", ownerEmployeeId = w.TopicOwner.EmployeeId, dueOn = due, priority = "high" },
                new { titleAr = "نشر التتبع المباشر", titleEn = "Publish live tracking", ownerEmployeeId = w.Coordinator.EmployeeId, dueOn = due, priority = "medium" },
            },
        }));
        await BenchWorld.OkAsync<BenchStudyDto>(w.C(w.Coordinator).PostAsJsonAsync($"{Base}/studies/{s2.Id}/analysis/submit", new { comment = "Ready" }));
        await w.DecideAsync(s2.Id, BenchProcesses.Analysis);
        (await w.GetAsync(s2.Id)).AnalysisApprovedAt.ShouldNotBeNull();

        // (a) The cache equals the office's dashboard.
        var dashboard = await BenchWorld.OkAsync<BenchDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard"));
        var facts = await FactsAsync(fx.Factory, BenchPermissions.SystemCode, today, null);
        facts.Sum(BenchMetrics.Studies).ShouldBe(dashboard.Studies);
        facts.Sum(BenchMetrics.Studies, f => f.Has(ReportingDimensions.Status, StudyStatuses.Active)).ShouldBe(dashboard.Active);
        facts.Sum(BenchMetrics.Studies, f => f.Has(ReportingDimensions.Status, StudyStatuses.Completed)).ShouldBe(dashboard.Completed);
        facts.Sum(BenchMetrics.Studies, f => f.Has(ReportingDimensions.Status, StudyStatuses.Cancelled)).ShouldBe(dashboard.Cancelled);
        foreach (var stage in dashboard.ByStage)
        {
            facts.Sum(BenchMetrics.Studies, f => f.Has(ReportingDimensions.Stage, stage.Code) && !f.Has(ReportingDimensions.Status, StudyStatuses.Cancelled))
                .ShouldBe(stage.Count, stage.Code);
        }

        foreach (var quarter in dashboard.ByQuarter)
        {
            facts.Sum(BenchMetrics.Studies, f => f.Has(BenchMetrics.Quarter, quarter.Code)).ShouldBe(quarter.Count, quarter.Code);
        }

        dashboard.Actions.ShouldBe(2);
        facts.Sum(BenchMetrics.Actions).ShouldBe(dashboard.Actions);
        facts.Sum(BenchMetrics.Actions, f => f.Has(ReportingDimensions.Status, "completed")).ShouldBe(dashboard.ActionsCompleted);
        facts.Sum(BenchMetrics.Actions, f => f.Has(ReportingDimensions.Status, "late")).ShouldBe(dashboard.ActionsLate);
        facts.Sum(BenchMetrics.Actions, f => f.Has(ReportingDimensions.Status, "not-started")).ShouldBe(2m);
        dashboard.PracticesAdopted.ShouldBe(1);
        facts.Sum(BenchMetrics.PracticesAdopted).ShouldBe(dashboard.PracticesAdopted);
        facts.Single(f => f.Metric == BenchMetrics.PracticesAdopted).Month.ShouldBe(MonthOf(today));
        facts.Where(f => f.Metric == BenchMetrics.Studies && f.UnitId == w.DepartmentB).Sum(f => f.Value).ShouldBe(1m, "unit = the responsible unit");
        (await FactsAsync(fx.Factory, BenchPermissions.SystemCode, today, null, today.AddMonths(-3), today.AddMonths(-1))).ShouldBeEmpty("no stock and no practice before");

        // (b) Live path: the viewer of department A (unit and below) counts A's studies only, as their dashboard; the outsider their own study.
        var managerDashboard = await BenchWorld.OkAsync<BenchDashboardDto>(w.C(w.Manager).GetAsync($"{Base}/dashboard"));
        var manager = await FactsAsync(fx.Factory, BenchPermissions.SystemCode, today, w.Manager);
        manager.Sum(BenchMetrics.Studies).ShouldBe(managerDashboard.Studies);
        manager.Sum(BenchMetrics.Studies).ShouldBe(4m);
        manager.Sum(BenchMetrics.Actions).ShouldBe(managerDashboard.Actions);
        manager.ShouldAllBe(f => f.UnitId == w.DepartmentA);
        var outsider = await FactsAsync(fx.Factory, BenchPermissions.SystemCode, today, w.Outsider);
        outsider.Sum(BenchMetrics.Studies).ShouldBe(1m);
        outsider.Sum(BenchMetrics.Actions).ShouldBe(0m);
        outsider.Sum(BenchMetrics.PracticesAdopted).ShouldBe(0m);
        (await PartitionsAsync(fx.Factory, BenchPermissions.SystemCode, w.Office)).ShouldBeEmpty();

        // (e) Report builder on the reader's scope.
        var office = await RunAsync(fx.Factory, BenchPermissions.SystemCode, w.Office, BenchDatasetSource.StudiesDataset, ["number", "stage", "unit"]);
        office.Total.ShouldBe(dashboard.Studies);
        var coordinator = await RunAsync(fx.Factory, BenchPermissions.SystemCode, w.Coordinator, BenchDatasetSource.StudiesDataset, ["number", "unit", "targetQuarter"],
            sort: [new ReportSort("targetQuarter", true), new ReportSort("number", false)]);
        coordinator.RowIds!.ShouldNotContain(s3.Id);
        coordinator.Rows.ShouldAllBe(r => (Guid)r[1]! == w.DepartmentA);
        coordinator.Rows.Select(r => (int)r[2]!).ShouldBeInOrder(SortDirection.Descending);
        (await RunAsync(fx.Factory, BenchPermissions.SystemCode, w.Manager, BenchDatasetSource.StudiesDataset, ["number"],
            [new ReportFilter("stage", ReportOperators.Equal, [StudyStages.Search]), new ReportFilter("status", ReportOperators.NotEqual, [StudyStatuses.Cancelled])]))
            .RowIds.ShouldBe([s1.Id], "s4 was cancelled in the search stage");
        var byStatus = await RunAsync(fx.Factory, BenchPermissions.SystemCode, w.Manager, BenchDatasetSource.StudiesDataset, ["status"], groupBy: ["status"]);
        byStatus.Rows.Select(r => ((string)r[0]!, (int)r[1]!)).ShouldBe([(StudyStatuses.Active, 3), (StudyStatuses.Cancelled, 1)], ignoreOrder: true);
        (await RunAsync(fx.Factory, BenchPermissions.SystemCode, w.Outsider, BenchDatasetSource.StudiesDataset, ["number"])).RowIds.ShouldBe([s3.Id]);

        // (g) Dashboard months: a study counts when its target quarter overlaps the months.
        async Task<int> StudiesAsync(string query) => (await BenchWorld.OkAsync<BenchDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?{query}"))).Studies;
        (await StudiesAsync($"from={year}-05-01&to={year}-05-31")).ShouldBe(1, "s4 (Q2)");
        (await StudiesAsync($"from={year}-08-01")).ShouldBe(2, "s2 (Q3) and s3 (Q4)");
        (await StudiesAsync($"to={year}-01-31")).ShouldBe(2, "s1 and s5 (Q1)");
        (await StudiesAsync($"from={year}-03-01&to={year}-04-30")).ShouldBe(3, "Q1 and Q2 overlap March–April");
        (await w.C(w.Office).GetAsync($"{Base}/dashboard?from={year}-05-01&to={year}-04-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Quarter_and_action_codes_follow_the_catalogue()
    {
        BenchMetrics.QuarterCode(3).ShouldBe("q3");
        BenchMetrics.ActionStatusCode("not_started").ShouldBe("not-started");
        BenchMetrics.ActionStatusCode("in_progress").ShouldBe("in-progress");
        BenchMetrics.ActionStatusCode("late").ShouldBe("late");
        CapActionProgress Action(Guid id, string status, string validation = "approved") =>
            new(id, "A", null, null, validation, status, 0m, false, null, null, null, "/cap/actions/" + id);
        var shared = Guid.CreateVersion7();
        var progress = new CapSourceProgress(Guid.CreateVersion7(), "S",
        [
            new(Guid.CreateVersion7(), "R1", null, null, null, "implementation", "implementation", null, null,
                [Action(shared, "late"), Action(Guid.CreateVersion7(), "cancelled"), Action(Guid.CreateVersion7(), "not_started", "rejected")], "/r1"),
            new(Guid.CreateVersion7(), "R2", null, null, null, "implementation", "implementation", null, null, [Action(shared, "late")], "/r2"),
        ], 1, 0, false, "/s");
        BenchMetrics.Counted(progress).Select(a => a.ActionId).ShouldBe([shared], "distinct, not cancelled, not rejected");
    }
}
