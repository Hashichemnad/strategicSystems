using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Modules.Teams.Reporting;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Teams;

/// <summary>
/// Teams reporting sources (docs/design/reporting.md §2.5, §6, §7): the cache facts equal the Teams dashboard, the live path counts only the user's
/// teams (scores only where they hold the results permission), datasets are scoped, the My Work card counts, the dashboard takes a period.
/// </summary>
public sealed class TeamsReportingTests(TeamsFixture fx) : IClassFixture<TeamsFixture>
{
    private const string Base = TeamsWorld.Base;

    private static readonly string[] AllMetrics = [TeamsMetricCodes.Teams, TeamsMetricCodes.Evaluations, TeamsMetricCodes.Score];

    [Fact]
    public async Task Cache_facts_equal_the_dashboard_and_the_live_path_counts_only_the_users_teams()
    {
        var (w, s) = await SeedAsync();

        // (a) The cache = the dashboard of HR (read and results on every team).
        var all = await FactsAsync(null, AllMetrics);
        var dashboard = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard"))!;
        Sum(all, TeamsMetricCodes.Teams).ShouldBe(dashboard.Teams);
        Sum(all, TeamsMetricCodes.Teams, (TeamsMetricCodes.Operating, "true"), (TeamsMetricCodes.Committee, "true")).ShouldBe(dashboard.ActiveCommittees);
        foreach (var type in dashboard.ByType)
        {
            Sum(all, TeamsMetricCodes.Teams, (ReportingDimensions.Type, type.Code)).ShouldBe(type.Count);
        }

        foreach (var status in dashboard.ByStatus)
        {
            Sum(all, TeamsMetricCodes.Teams, (ReportingDimensions.Status, status.Code)).ShouldBe(status.Count);
        }

        foreach (var status in dashboard.EvaluationProgress)
        {
            Sum(all, TeamsMetricCodes.Evaluations, (ReportingDimensions.Status, status.Code)).ShouldBe(status.Count);
        }

        all.Where(f => f.Metric == TeamsMetricCodes.Score).Sum(f => f.Weight).ShouldBe(dashboard.ScoreDistribution!.Sum(d => d.Count));
        all.Where(f => f.Metric == TeamsMetricCodes.Teams && f.UnitId == w.DepartmentId).Sum(f => f.Value).ShouldBe(2);
        var mine = all.Where(f => f.Metric == TeamsMetricCodes.Score && f.UnitId == w.DepartmentId).ToList();
        mine.Sum(f => f.Value).ShouldBe(0.8m); // 80 points = 80 %
        mine.Sum(f => f.Weight).ShouldBe(1m);

        // (b) A member reads their two teams but no evaluation results; the leader (results viewer) reads the score; the outsider nothing.
        var member = await FactsAsync(w.MemberA, AllMetrics);
        Sum(member, TeamsMetricCodes.Teams).ShouldBe(2);
        Sum(member, TeamsMetricCodes.Evaluations).ShouldBe(2);
        member.ShouldNotContain(f => f.Metric == TeamsMetricCodes.Score);
        member.ShouldAllBe(f => f.UnitId == w.DepartmentId);
        Sum(member, TeamsMetricCodes.Teams).ShouldBeLessThanOrEqualTo(Sum(all, TeamsMetricCodes.Teams));
        var leader = await FactsAsync(w.President, AllMetrics);
        leader.Where(f => f.Metric == TeamsMetricCodes.Score).Sum(f => f.Weight).ShouldBe(1m);
        (await FactsAsync(w.Outsider, AllMetrics)).ShouldBeEmpty();
        s.ShouldNotBeNull();

        // Stock metrics only when the range covers the current month.
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var source = Source(scope.ServiceProvider);
            var today = Today;
            (await source.ComputeAsync(new MetricComputation(AllMetrics, MetricComputation.MonthOf(today).AddMonths(-2), MetricComputation.MonthOf(today).AddMonths(-1),
                MetricComputation.MonthOf(today), today, null), CancellationToken.None)).ShouldBeEmpty();
            (await source.VisiblePartitionsAsync(Strategy.Platform.Access.StrategyClaims.CreateSystemPrincipal(), CancellationToken.None)).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Datasets_my_work_card_and_dashboard_period_are_scoped()
    {
        var (w, s) = await SeedAsync();

        // (e) Datasets on the reader's scope: rows, filter, sort, group.
        var teams = await RunAsync(w.MemberA, new ReportQuery(TeamsDatasetSource.TeamsDataset, ["number", "type", "unit"], [], [], [new ReportSort("type", false)], 0, 50));
        teams.RowIds!.ShouldBe([s.Standing, s.Temporary], ignoreOrder: true);
        teams.Rows.Select(r => (string)r[1]!).ShouldBe(["standing-committee", "temporary-committee"]);
        teams.Rows.ShouldAllBe(r => (Guid?)r[2] == w.DepartmentId);
        var standing = await RunAsync(w.MemberA, new ReportQuery(TeamsDatasetSource.TeamsDataset, ["number"],
            [new ReportFilter("type", ReportOperators.Equal, ["standing-committee"])], [], [], 0, 50));
        standing.RowIds!.ShouldBe([s.Standing]);
        (await RunAsync(w.Outsider, new ReportQuery(TeamsDatasetSource.TeamsDataset, ["number"], [], [], [], 0, 50))).Total.ShouldBe(0);
        var members = await RunAsync(w.MemberA, new ReportQuery(TeamsDatasetSource.MembersDataset, ["teamNumber", "employee", "role"],
            [new ReportFilter("role", ReportOperators.Equal, ["president"])], [], [], 0, 50));
        members.Total.ShouldBe(2);
        members.Rows.ShouldAllBe(r => (Guid?)r[1] == w.President.EmployeeId);
        members.RowIds!.ShouldBe([s.Standing, s.Temporary], ignoreOrder: true);
        var byRole = await RunAsync(w.MemberA, new ReportQuery(TeamsDatasetSource.MembersDataset, ["role"], [], ["role"], [], 0, 50));
        byRole.Rows.Single(r => (string)r[0]! == "member")[1].ShouldBe(2);

        // (f) My Work: the leader evaluates the open cycle; the member sees their teams; the outsider no card.
        var leader = (await CardsAsync(w.President)).ShouldHaveSingleItem();
        leader.ShouldSatisfyAllConditions(c => c.Code.ShouldBe(TeamsMyWorkCards.TeamsCard), c => c.TitleKey.ShouldBe("myWork.cards.teams-teams"),
            c => c.Counts.Single(x => x.Key == "teams").Value.ShouldBe(2), c => c.Counts.Single(x => x.Key == "evaluations").Value.ShouldBe(1),
            c => c.Items[0].Link.ShouldBe($"/teams/evaluations/{s.OpenEvaluation:D}"), c => c.Items.Count.ShouldBe(3));
        var member = (await CardsAsync(w.MemberA)).ShouldHaveSingleItem();
        member.Counts.Single(x => x.Key == "teams").Value.ShouldBe(2);
        member.Counts.Single(x => x.Key == "evaluations").Value.ShouldBe(0);
        member.Items.ShouldAllBe(i => i.MessageKey == "teams.myWork.member" && i.Link.StartsWith("/teams/", StringComparison.Ordinal));
        (await CardsAsync(w.Outsider)).ShouldBeEmpty();

        // (g) The dashboard period: teams running in it (formed-on to end) and cycles ending in it.
        var year = Today.Year;
        var unfiltered = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard"))!;
        var thisYear = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard?from={year}-01-01&to={year}-12-31"))!;
        thisYear.Teams.ShouldBe(unfiltered.Teams);
        thisYear.EvaluationProgress.Sum(e => e.Count).ShouldBe(unfiltered.EvaluationProgress.Sum(e => e.Count));
        var past = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard?from={year - 5}-01-01&to={year - 5}-12-31"))!;
        past.Teams.ShouldBe(0);
        past.EvaluationProgress.ShouldBeEmpty();
        var nextYear = (await w.C(w.Hr).GetFromJsonAsync<TeamDashboardDto>($"{Base}/dashboard?from={year + 1}-01-01&to={year + 1}-12-31"))!;
        nextYear.EvaluationProgress.ShouldBeEmpty(); // this year's cycles end this year
        nextYear.Teams.ShouldBeLessThan(unfiltered.Teams); // the temporary committee has ended by then
        (await w.C(w.Hr).GetAsync($"{Base}/dashboard?from={year}-12-01&to={year}-01-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------

    private DateOnly Today => DateOnly.FromDateTime(fx.Factory.Services.GetRequiredService<Strategy.Platform.Kernel.Time.IClock>().UtcNow.UtcDateTime);

    private sealed record Seed(Guid Standing, Guid Temporary, Guid ApprovedEvaluation, Guid OpenEvaluation);

    /// <summary>
    /// A standing committee and a temporary committee (ending in two weeks) of the department, both with the President, member A and the secretary.
    /// HR opens this year's cycle on both; the standing one is approved with 80 points (test set-up through the domain), the other stays open.
    /// </summary>
    private async Task<(TeamsWorld World, Seed Seed)> SeedAsync()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var suffix = Guid.CreateVersion7().ToString("N")[^5..];
        var today = Today;
        var standing = await w.FormAsync(w.Request("Reporting standing " + suffix, start: today, withExternal: false));
        var temporary = await w.FormAsync(w.Request("Reporting temporary " + suffix, type: "temporary-committee", permanent: false, start: today, end: today.AddDays(14),
            withExternal: false));
        var approved = await TeamsWorld.OkAsync<EvaluationSummaryDto>(w.C(w.Hr).PostAsJsonAsync($"{Base}/{standing.Id}/evaluations", new { year = today.Year }));
        var open = await TeamsWorld.OkAsync<EvaluationSummaryDto>(w.C(w.Hr).PostAsJsonAsync($"{Base}/{temporary.Id}/evaluations", new { year = today.Year }));
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<TeamsDbContext>();
            var evaluation = await db.Evaluations.SingleAsync(e => e.Id == approved.Id);
            evaluation.SaveDraft(evaluation.ReadCriteria(), 80m, evaluation.Workload, [], []).IsSuccess.ShouldBeTrue();
            evaluation.Apply(EvaluationActions.Submit, DateTimeOffset.UtcNow, w.President.EmployeeId).IsSuccess.ShouldBeTrue();
            evaluation.Apply(EvaluationActions.Approve, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        return (w, new Seed(standing.Id, temporary.Id, approved.Id, open.Id));
    }

    private static IReportingMetricSource Source(IServiceProvider services) =>
        services.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == TeamPermissions.SystemCode);

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(TestUser? user, params string[] metrics)
    {
        await using var scope = user is null ? TestAccess.SystemScope(fx.Factory) : await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var principal = user is null ? null : scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
        var source = Source(scope.ServiceProvider);
        source.Metrics.Select(m => m.Code).ShouldBe(AllMetrics, ignoreOrder: true);
        var month = MetricComputation.MonthOf(Today);
        var facts = await source.ComputeAsync(new MetricComputation(metrics, month, month, month, Today, principal), CancellationToken.None);
        facts.ShouldAllBe(f => f.Month == month && f.Partition == ReportingPartitions.Public);
        return facts;
    }

    private async Task<ReportResult> RunAsync(TestUser user, ReportQuery query)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(s => s.SystemCode == TeamPermissions.SystemCode);
        return await source.RunAsync(query, scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private async Task<IReadOnlyList<MyWorkCard>> CardsAsync(TestUser user)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var source = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(s => s.SystemCode == TeamPermissions.SystemCode);
        return await source.GetCardsAsync(scope.ServiceProvider.GetRequiredService<IRequestContext>().User, CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, params (string Key, string Value)[] dimensions) =>
        facts.Where(f => f.Metric == metric && dimensions.All(d => f.Dimensions.TryGetValue(d.Key, out var v) && v == d.Value)).Sum(f => f.Value);
}
