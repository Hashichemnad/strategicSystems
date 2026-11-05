using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Reporting;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Reporting;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>The volume seed is valid: the Portfolio and KPI metric sources compute the seeded counts (own host and database).</summary>
public class PortfolioKpiVolumeTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [Fact]
    public async Task A_small_seed_is_counted_by_the_portfolio_and_kpi_metric_sources()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var units = await UnitsAsync(factory, 3);
        await PortfolioKpiVolume.SeedAsync(factory, units, projects: 20, kpiCards: 40);

        var (portfolio, kpi) = await FactsAsync(factory);
        var mine = portfolio.Where(f => f.UnitId is { } u && units.Contains(u)).ToList();
        Sum(mine, PortfolioMetricCodes.Projects).ShouldBe(20);
        Sum(mine, PortfolioMetricCodes.Projects, (ReportingDimensions.Status, ProjectStatuses.Active)).ShouldBe(14); // 2 on hold, 2 cancelled, 2 completed
        Sum(mine, PortfolioMetricCodes.Projects, (ReportingDimensions.Status, ProjectStatuses.Completed)).ShouldBe(2);
        Sum(mine, PortfolioMetricCodes.Projects, (PortfolioMetricCodes.Delayed, "true")).ShouldBeGreaterThan(0);
        Sum(mine, PortfolioMetricCodes.Requests, (ReportingDimensions.Stage, RequestStatuses.Approved)).ShouldBe(20);
        Sum(mine, PortfolioMetricCodes.Budget, (PortfolioMetricCodes.Kind, PortfolioMetricCodes.KindPlanned)).ShouldBeGreaterThan(0);
        Sum(mine, PortfolioMetricCodes.Milestones).ShouldBeGreaterThan(0);
        mine.Where(f => f.Metric == PortfolioMetricCodes.Projects).Select(f => f.UnitId).Distinct().Count().ShouldBe(3);

        var cards = kpi.Where(f => f.UnitId is { } u && units.Contains(u)).ToList();
        Sum(cards, KpiMetricCodes.Cards).ShouldBe(40);
        Sum(cards, KpiMetricCodes.Results, (ReportingDimensions.Status, ResultStatuses.Approved)).ShouldBeGreaterThan(0);
        var score = cards.Where(f => f.Metric == KpiMetricCodes.Score).ToList();
        score.Sum(f => f.Weight).ShouldBeGreaterThan(0);
        score.Select(f => f.Dimensions[ReportingDimensions.Objective]).Distinct().Count().ShouldBe(6);
        cards.Where(f => f.Metric == KpiMetricCodes.Results).Select(f => f.Dimensions[KpiMetricCodes.Rag]).Distinct().Count().ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task The_full_volume_seeds_in_well_under_a_minute()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var units = await UnitsAsync(factory, 12);
        var watch = Stopwatch.StartNew();
        await PortfolioKpiVolume.SeedAsync(factory, units);
        watch.Stop();
        output.WriteLine($"Seeded 500 projects / 1 000 KPI cards in {watch.Elapsed.TotalSeconds:F1} s");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));

        var (portfolio, kpi) = await FactsAsync(factory);
        Sum(portfolio.Where(f => f.UnitId is { } u && units.Contains(u)), PortfolioMetricCodes.Projects).ShouldBe(500);
        Sum(kpi.Where(f => f.UnitId is { } u && units.Contains(u)), KpiMetricCodes.Cards).ShouldBe(1000);
    }

    private static async Task<List<Guid>> UnitsAsync(ApiFactory factory, int count)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..];
        await using var scope = TestAccess.SystemScope(factory);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var organisation = (await dispatcher.SendAsync(new CreateUnit($"VOL-{suffix}", "الجهة", $"Volume {suffix}", "organisation", null, null, null, null, null))).Value.Id;
        var units = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            units.Add((await dispatcher.SendAsync(new CreateUnit($"VD{i}-{suffix}", "إدارة", $"Department {i} {suffix}", "department", organisation, null, null, null, null)))
                .Value.Id);
        }

        return units;
    }

    private static async Task<(IReadOnlyList<MetricFact> Portfolio, IReadOnlyList<MetricFact> Kpi)> FactsAsync(ApiFactory factory)
    {
        await using var scope = TestAccess.SystemScope(factory);
        var today = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        var month = MetricComputation.MonthOf(today);
        var sources = scope.ServiceProvider.GetServices<IReportingMetricSource>().ToList();
        async Task<IReadOnlyList<MetricFact>> Compute(string system)
        {
            var source = sources.Single(s => s.SystemCode == system);
            return await source.ComputeAsync(new MetricComputation(source.Metrics.Select(m => m.Code).ToList(), new DateOnly(today.Year, 1, 1), month, month, today, null),
                CancellationToken.None);
        }

        return (await Compute("portfolio"), await Compute("kpi"));
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, params (string Key, string Value)[] dimensions) =>
        facts.Where(f => f.Metric == metric && dimensions.All(d => f.Dimensions.TryGetValue(d.Key, out var v) && v == d.Value)).Sum(f => f.Value);
}
