using System.Net;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Reporting;
using Strategy.Platform.Reporting;
using static Strategy.IntegrationTests.Cap.ModuleReportingHelpers;

namespace Strategy.IntegrationTests.AuditManagement;

/// <summary>
/// Audit Management reporting sources (Phase 17, docs/design/reporting.md §2.5): facts vs the audit dashboard, the office partition for findings
/// of draft reports, department users' live figures, lists, datasets and the follow-ups card.
/// </summary>
public sealed class AuditManagementReportingTests(AuditFixture fx) : IClassFixture<AuditFixture>
{
    private const string Base = AuditWorld.Base;

    [Fact]
    public async Task Facts_match_the_dashboard_draft_findings_stay_in_the_office_partition_and_department_users_count_only_their_units()
    {
        var w = await AuditWorld.CreateAsync(fx.Factory);
        await w.SettingsAsync(s => s with { DepartmentScope = DepartmentScopes.Section });
        var today = await w.TodayAsync();
        var issued = await w.ReportAsync("Revenue audit");

        // f1 (S1): old finding, recommendation past due → late; f2 (S2): completed → closed; f3 (E): follow-up due within the week.
        var f1 = await w.FindingAsync(issued, AuditWorld.Finding("Unreconciled collections", w.S1, today.AddDays(-100), AuditPriorities.Critical),
            AuditWorld.Recommendation("Reconcile monthly", today.AddDays(-5)));
        var f2 = await w.FindingAsync(issued, AuditWorld.Finding("Billing errors", w.S2, today, AuditPriorities.Medium),
            AuditWorld.Recommendation("Validate invoices", today.AddDays(60)));
        var f3 = await w.FindingAsync(issued, AuditWorld.Finding("Payroll overtime", w.E, today, AuditPriorities.Low),
            AuditWorld.Recommendation("Cap overtime", today.AddDays(90)));
        await w.IssueAsync(issued);
        await w.FollowUpAsync((await w.RecsAsync(f2.Id))[0].Id, 1m, today, null);
        var rec3 = (await w.RecsAsync(f3.Id))[0];
        await w.FollowUpAsync(rec3.Id, 0.2m, today, today.AddDays(3));
        await w.MonitorAsync();
        (await w.StatusesAsync(f1.Id)).ShouldBe((AuditFindingStatuses.Late, RecommendationStatuses.Late));
        (await w.GetAsync(f2.Id)).Status.ShouldBe(AuditFindingStatuses.Closed);

        // f4 (S1) in a draft report: the Audit Office's own.
        var draft = await w.ReportAsync("Draft procurement audit", ReportTypes.InternalAudit);
        var f4 = await w.FindingAsync(draft, AuditWorld.Finding("Single-source purchases", w.S1, today), AuditWorld.Recommendation("Tender", today.AddDays(60)));

        // (a) The cache equals the Audit Office's dashboard (which includes draft reports).
        var dashboard = await AuditWorld.OkAsync<AuditDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard"));
        var facts = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, null);
        facts.Sum(AuditMetrics.Findings).ShouldBe(dashboard.Findings);
        foreach (var (dimension, expected) in new[]
        {
            (ReportingDimensions.Status, dashboard.ByStatus), (ReportingDimensions.Priority, dashboard.ByPriority), (AuditMetrics.ReportType, dashboard.ByReportType),
            (AuditMetrics.Year, dashboard.ByYear),
        })
        {
            foreach (var count in expected)
            {
                facts.Sum(AuditMetrics.Findings, f => f.Has(dimension, count.Code)).ShouldBe(count.Count, $"{dimension} = {count.Code}");
            }
        }

        facts.Sum(AuditMetrics.Findings, f => f.Has(ReportingDimensions.Overdue, "true")).ShouldBe(dashboard.Late);
        facts.Sum(AuditMetrics.Findings, f => f.Has(AuditMetrics.Repeat, "true")).ShouldBe(dashboard.Repeat);
        facts.Sum(AuditMetrics.Recommendations, f => f.Has(ReportingDimensions.Status, RecommendationStatuses.Late)).ShouldBe(dashboard.OverdueRecommendations);
        Math.Round(facts.Sum(AuditMetrics.Closure) / facts.Weight(AuditMetrics.Closure), 4).ShouldBe(dashboard.ClosureRate);
        facts.Single(f => f.Metric == AuditMetrics.Findings && f.Has(ReportingDimensions.Status, AuditFindingStatuses.Late)).Dimensions[ReportingDimensions.AgeBucket]
            .ShouldBe("91-180");
        facts.Single(f => f.Metric == AuditMetrics.Findings && f.Has(ReportingDimensions.Status, AuditFindingStatuses.Closed)).Dimensions[ReportingDimensions.AgeBucket]
            .ShouldBe("closed");

        // (c) Office partition: exactly the draft report's finding and recommendation; the unit is the responsible unit.
        facts.Where(f => f.Partition == ReportingPartitions.Office && f.Metric == AuditMetrics.Findings).Sum(f => f.Value).ShouldBe(1m);
        facts.Single(f => f.Partition == ReportingPartitions.Office && f.Metric == AuditMetrics.Findings).UnitId.ShouldBe(w.S1);
        facts.Where(f => f.Partition == ReportingPartitions.Office && f.Metric == AuditMetrics.Recommendations).Sum(f => f.Value).ShouldBe(1m);
        (await PartitionsAsync(fx.Factory, AuditPermissions.SystemCode, w.Office)).ShouldBe([ReportingPartitions.Office]);
        (await PartitionsAsync(fx.Factory, AuditPermissions.SystemCode, w.Director)).ShouldBeEmpty();
        (await PartitionsAsync(fx.Factory, AuditPermissions.SystemCode, w.Committee)).ShouldBeEmpty("All readers outside the office never see draft reports");

        // (b) Live path: the staff of S1 counts f1 only (not the draft f4); the director D's sections; nobody outside the office sees the partition.
        var staff = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, w.StaffS1);
        staff.Sum(AuditMetrics.Findings).ShouldBe(1m);
        staff.ShouldAllBe(f => f.UnitId == w.S1 && f.Partition == ReportingPartitions.Public);
        var director = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, w.Director);
        director.Sum(AuditMetrics.Findings).ShouldBe(2m);
        director.Sum(AuditMetrics.Recommendations).ShouldBe(2m);
        director.ShouldAllBe(f => f.Partition == ReportingPartitions.Public);
        var committeeDashboard = await AuditWorld.OkAsync<AuditDashboardDto>(w.C(w.Committee).GetAsync($"{Base}/dashboard"));
        var committee = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, w.Committee);
        committee.Sum(AuditMetrics.Findings).ShouldBe(committeeDashboard.Findings);
        committee.Sum(AuditMetrics.Findings).ShouldBe(dashboard.Findings - 1);
        var office = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, w.Office);
        office.Sum(AuditMetrics.Findings).ShouldBe(dashboard.Findings);

        // (d) Lists: late findings and overdue recommendations, scoped, linking to pages the viewer may open.
        var late = await ListAsync(fx.Factory, AuditPermissions.SystemCode, AuditListSource.LateFindings, w.StaffS1, today);
        late.Select(i => i.Id).ShouldBe([f1.Id]);
        late[0].Link.ShouldBe($"/audit-management/findings/{f1.Id}");
        (await w.C(w.StaffS1).GetAsync($"{Base}/findings/{f1.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(fx.Factory, AuditPermissions.SystemCode, AuditListSource.LateFindings, w.Outsider, today)).ShouldBeEmpty();
        var overdue = await ListAsync(fx.Factory, AuditPermissions.SystemCode, AuditListSource.OverdueRecommendations, w.Office, today);
        var rec1 = (await w.RecsAsync(f1.Id))[0];
        overdue.Select(i => i.Id).ShouldBe([rec1.Id]);
        overdue[0].Link.ShouldBe($"/audit-management/recommendations/{rec1.Id}");
        overdue[0].Value.ShouldBe(5m);
        overdue[0].UnitId.ShouldBe(w.S1);
        (await ListAsync(fx.Factory, AuditPermissions.SystemCode, AuditListSource.OverdueRecommendations, w.StaffS2, today)).ShouldBeEmpty();

        // (e) Report builder: department users never see draft findings; filters, sorting and grouping run on the reader's query.
        var staffRows = await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.StaffS1, AuditDatasetSource.FindingsDataset, ["number", "status", "responsibleUnit"]);
        staffRows.RowIds.ShouldBe([f1.Id]);
        var officeRows = await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.Office, AuditDatasetSource.FindingsDataset, ["number", "reportStatus"],
            sort: [new ReportSort("number", false)]);
        officeRows.Total.ShouldBe(dashboard.Findings);
        officeRows.RowIds!.ShouldContain(f4.Id);
        officeRows.Rows.ShouldContain(r => (string)r[1]! == ReportStatuses.Draft);
        var critical = await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.Office, AuditDatasetSource.FindingsDataset, ["number"],
            [new ReportFilter("priority", ReportOperators.In, [AuditPriorities.Critical, AuditPriorities.Low])]);
        critical.RowIds.ShouldBe([f1.Id, f3.Id], ignoreOrder: true);
        var byStatus = await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.Director, AuditDatasetSource.FindingsDataset, ["status"], groupBy: ["status"]);
        byStatus.Rows.Select(r => ((string)r[0]!, (int)r[1]!)).ShouldBe([(AuditFindingStatuses.Late, 1), (AuditFindingStatuses.Closed, 1)], ignoreOrder: true);
        var recRows = await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.Director, AuditDatasetSource.RecommendationsDataset, ["number", "overdue", "findingNumber"],
            [new ReportFilter("overdue", ReportOperators.Equal, ["true"])]);
        recRows.RowIds.ShouldBe([rec1.Id]);
        (await RunAsync(fx.Factory, AuditPermissions.SystemCode, w.Outsider, AuditDatasetSource.RecommendationsDataset, ["number"])).RowIds.ShouldBe([rec3.Id]);

        // (f) My Work card for the auditor (lead auditor of the reports): one follow-up due, one late recommendation; none for department staff.
        var card = (await CardsAsync(fx.Factory, AuditPermissions.SystemCode, w.Office)).Single();
        card.TitleKey.ShouldBe("myWork.cards.auditFollowUps");
        (card.Count("auditFollowUpsDue"), card.Count("auditRecommendationsLate")).ShouldBe((1, 1));
        card.Items.Select(i => i.Id).ShouldBe([rec1.Id, rec3.Id]);
        card.Items[0].Overdue.ShouldBeTrue();
        card.Items[0].Link.ShouldBe($"/audit-management/findings/{f1.Id}?recommendation={rec1.Id}");
        (await CardsAsync(fx.Factory, AuditPermissions.SystemCode, w.StaffS1)).ShouldBeEmpty();
        (await CardsAsync(fx.Factory, AuditPermissions.SystemCode, w.Committee)).ShouldBeEmpty();

        // (g) Dashboard months on the finding date.
        var thisMonth = MonthOf(today);
        var oldMonth = MonthOf(today.AddDays(-100));
        (await AuditWorld.OkAsync<AuditDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?from={thisMonth:yyyy-MM-dd}&to={thisMonth:yyyy-MM-dd}"))).Findings
            .ShouldBe(dashboard.Findings - 1);
        (await AuditWorld.OkAsync<AuditDashboardDto>(w.C(w.Office).GetAsync($"{Base}/dashboard?from={oldMonth:yyyy-MM-dd}&to={oldMonth:yyyy-MM-dd}"))).Findings
            .ShouldBe(1);
        (await w.C(w.Office).GetAsync($"{Base}/dashboard?from={thisMonth:yyyy-MM-dd}&to={oldMonth:yyyy-MM-dd}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
