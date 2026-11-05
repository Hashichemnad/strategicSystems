using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Reporting;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Reporting;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>The volume seeder writes data the module metric sources read back exactly (and fast enough for the performance test).</summary>
public sealed class RiskTaskVolumeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(List<Guid> Units, List<Guid> Employees)> PeopleAsync(int count)
    {
        var units = new List<Guid>();
        var employees = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var user = await _factory.CreateUserAsync();
            units.Add(user.UnitId!.Value);
            employees.Add(user.EmployeeId!.Value);
        }

        return (units, employees);
    }

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(string system)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == system);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month = MetricComputation.MonthOf(today);
        return await source.ComputeAsync(new MetricComputation([.. source.Metrics.Select(m => m.Code)], month.AddMonths(-3), month, month, today, null),
            CancellationToken.None);
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, Func<MetricFact, bool>? where = null) =>
        facts.Where(f => f.Metric == metric && (where?.Invoke(f) ?? true)).Sum(f => f.Value);

    [Fact]
    public async Task Seeded_risks_and_tasks_come_back_through_the_metric_sources()
    {
        var (units, employees) = await PeopleAsync(4);
        await RiskTaskVolume.SeedAsync(_factory, units, employees, risks: 50, tasks: 500);

        // Risks: i % 10 == 9 closed, 7 draft, 8 under review, else active; reviews overdue when i % 10 == 0; overdue actions when i % 12 == 0;
        // every fifth active risk re-assessed this month.
        var risk = await FactsAsync(RiskPermissions.SystemCode);
        Sum(risk, RiskMetricSource.Risks).ShouldBe(45);
        Sum(risk, RiskMetricSource.Risks, f => f.Dimensions[ReportingDimensions.Status] == "active").ShouldBe(35);
        Sum(risk, RiskMetricSource.Heat, f => f.Dimensions[RiskMetricSource.Kind] == "inherent").ShouldBe(45);
        Sum(risk, RiskMetricSource.ReviewsOverdue).ShouldBe(5);
        Sum(risk, RiskMetricSource.Actions, f => f.Dimensions[ReportingDimensions.Overdue] == "true").ShouldBe(5);
        Sum(risk, RiskMetricSource.Movement).ShouldBe(10);
        Sum(risk, RiskMetricSource.Movement, f => f.Dimensions[RiskMetricSource.Direction] == RiskMovement.Unchanged).ShouldBe(0);
        Sum(risk, RiskMetricSource.Risks, f => f.Dimensions[RiskMetricSource.Appetite] == RiskMetricSource.Outside).ShouldBe(10);
        risk.Where(f => f.Metric == RiskMetricSource.Risks).Select(f => f.UnitId!.Value).Distinct().ShouldBe(units, ignoreOrder: true);

        // Tasks: per 50, 10 closed (two thirds on time), 1 cancelled, 5 overdue; every 47th confidential.
        var tasks = await FactsAsync(TaskPermissions.SystemCode);
        var open = Enumerable.Range(0, 500).Where(j => j % 50 > 10).ToList();
        var closed = Enumerable.Range(0, 500).Where(j => j % 50 < 10).ToList();
        Sum(tasks, TaskMetricSource.Tasks).ShouldBe(open.Count);
        Sum(tasks, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Overdue] == "true").ShouldBe(50);
        Sum(tasks, TaskMetricSource.Tasks, f => f.Partition == ReportingPartitions.Confidential).ShouldBe(open.Count(j => j % 47 == 0));
        Sum(tasks, TaskMetricSource.Completed).ShouldBe(closed.Count);
        var onTime = tasks.Where(f => f.Metric == TaskMetricSource.OnTime).ToList();
        (onTime.Sum(f => f.Value), onTime.Sum(f => f.Weight)).ShouldBe((closed.Count(j => j % 3 != 0), closed.Count));
        tasks.Where(f => f.Metric == TaskMetricSource.HoursToAccept).Sum(f => f.Weight).ShouldBe(closed.Count);
    }

    [Fact]
    public async Task Full_volume_is_seeded_well_under_a_minute()
    {
        var (units, employees) = await PeopleAsync(8);
        var clock = Stopwatch.StartNew();
        await RiskTaskVolume.SeedAsync(_factory, units, employees);
        clock.Stop();
        TestContext.Current.SendDiagnosticMessage($"Seeded 1 000 risks and 10 000 tasks in {clock.Elapsed.TotalSeconds:0.0} s");
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        Sum(await FactsAsync(RiskPermissions.SystemCode), RiskMetricSource.Risks).ShouldBe(900);
        Sum(await FactsAsync(TaskPermissions.SystemCode), TaskMetricSource.Tasks).ShouldBe(7800);
    }
}
