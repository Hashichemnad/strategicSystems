using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Reporting;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Reporting;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting;
using static Strategy.IntegrationTests.Cap.ModuleReportingHelpers;

namespace Strategy.IntegrationTests.Reporting.Volume;

/// <summary>One host and a small organisation (four departments, three employees) for the volume seeding tests.</summary>
public sealed class AuditCapVolumeFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public IReadOnlyList<Guid> Units { get; private set; } = [];

    public IReadOnlyList<Guid> Employees { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres);
        var units = new List<Guid>();
        await using (var scope = TestAccess.SystemScope(Factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var root = (await dispatcher.SendAsync(new CreateUnit("VOL", "الجهة", "Organisation", "organisation", null, null, null, null, null))).Value.Id;
            for (var i = 1; i <= 4; i++)
            {
                units.Add((await dispatcher.SendAsync(new CreateUnit($"VOL{i}", "إدارة " + i, "Department " + i, "department", root, null, null, null, null))).Value.Id);
            }
        }

        var employees = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            employees.Add((await Factory.CreateUserAsync(unitId: units[i])).EmployeeId!.Value);
        }

        Units = units;
        Employees = employees;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>The volume seed keeps the modules' invariants: their metric sources count exactly what was seeded (Phase 17 performance test data).</summary>
public sealed class AuditCapVolumeTests(AuditCapVolumeFixture fx, ITestOutputHelper output) : IClassFixture<AuditCapVolumeFixture>
{
    [Fact]
    public async Task A_small_seed_is_counted_exactly_by_the_audit_and_cap_metric_sources()
    {
        const int n = 120;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var auditBefore = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, null);
        var capBefore = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null);
        await AuditCapVolume.SeedAsync(fx.Factory, fx.Units, fx.Employees, n, n);
        var audit = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, null);
        var cap = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null);
        decimal Delta(IReadOnlyList<MetricFact> after, IReadOnlyList<MetricFact> before, string metric, Func<MetricFact, bool>? where = null) =>
            after.Sum(metric, where) - before.Sum(metric, where);

        // Audit: the replicated status rule of the seed (by i % 20), 1–3 recommendations each, every finding in an issued report.
        var ids = Enumerable.Range(1, n).ToList();
        string FindingStatus(int i) => (i % 20) switch
        {
            <= 7 => "open",
            <= 11 => "late",
            <= 15 => "closed",
            <= 17 => "risk_accepted",
            18 => "cancelled",
            _ => "open",
        };
        Delta(audit, auditBefore, AuditMetrics.Findings).ShouldBe(n);
        foreach (var status in new[] { "open", "late", "closed", "risk_accepted", "cancelled" })
        {
            Delta(audit, auditBefore, AuditMetrics.Findings, f => f.Has(ReportingDimensions.Status, status)).ShouldBe(ids.Count(i => FindingStatus(i) == status), status);
        }

        Delta(audit, auditBefore, AuditMetrics.Recommendations).ShouldBe(ids.Sum(i => 1 + (i % 3)));
        Delta(audit, auditBefore, AuditMetrics.Recommendations, f => f.Has(ReportingDimensions.Status, "late"))
            .ShouldBe(ids.Where(i => FindingStatus(i) == "late").Sum(i => 1 + (i % 3)));
        Delta(audit, auditBefore, AuditMetrics.Recommendations, f => f.Has(ReportingDimensions.Overdue, "true")).ShouldBe(
            ids.Where(i => FindingStatus(i) == "late").Sum(i => 1 + (i % 3)) + ids.Count(i => FindingStatus(i) == "open" && i % 7 == 0));
        audit.Where(f => f.Metric == AuditMetrics.Findings).ShouldAllBe(f => f.Partition == ReportingPartitions.Public);
        audit.Where(f => f.Metric == AuditMetrics.Findings).Select(f => f.Dimensions[ReportingDimensions.AgeBucket]).Distinct().Count().ShouldBe(6, "every age bucket and closed");
        audit.Where(f => f.Metric == AuditMetrics.Findings).Select(f => f.UnitId).Distinct().Count().ShouldBe(fx.Units.Count);

        // CAP: remarks by status (i % 10), 1–2 actions for planned remarks, completed actions half on time.
        string[] remarkStatuses = ["assigned", "planning", "validation", "implementation", "implementation", "verification", "completed", "completed", "rejected", "cancelled"];
        var planned = ids.Where(i => remarkStatuses[i % 10] is "planning" or "validation" or "implementation" or "verification" or "completed").ToList();
        var completedRemarks = planned.Where(i => remarkStatuses[i % 10] is "verification" or "completed").ToList();
        Delta(cap, capBefore, CapMetrics.Remarks).ShouldBe(n);
        Delta(cap, capBefore, CapMetrics.Remarks, f => f.Has(ReportingDimensions.Status, "completed")).ShouldBe(ids.Count(i => remarkStatuses[i % 10] == "completed"));
        Delta(cap, capBefore, CapMetrics.Remarks, f => f.Has(ReportingDimensions.Overdue, "true")).ShouldBeGreaterThan(0);
        Delta(cap, capBefore, CapMetrics.Actions).ShouldBe(planned.Sum(i => 1 + (i % 2)));
        Delta(cap, capBefore, CapMetrics.Actions, f => f.Has(ReportingDimensions.Status, "completed")).ShouldBe(completedRemarks.Sum(i => 1 + (i % 2)));
        Delta(cap, capBefore, CapMetrics.Actions, f => f.Has(ReportingDimensions.Status, "late")).ShouldBeGreaterThan(0);
        Delta(cap, capBefore, CapMetrics.Actions, f => f.Has(CapMetrics.Validated, "false")).ShouldBe(
            planned.Where(i => remarkStatuses[i % 10] is "planning" or "validation").Sum(i => 1 + (i % 2)));
        (cap.Sum(CapMetrics.Closure) - capBefore.Sum(CapMetrics.Closure)).ShouldBe(completedRemarks.Sum(i => 1 + (i % 2)));

        // Completions spread over past months: on time (k = 1) and late (k = 2) over the whole range.
        var flow = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null, today.AddMonths(-24), today, CapMetrics.ActionsOnTime);
        flow.Sum(f => f.Weight).ShouldBeGreaterThanOrEqualTo(completedRemarks.Count(i => DateOnly.FromDayNumber(today.DayNumber - ((i * 41) % 600) + 45) <= today));
        flow.Sum(f => f.Value).ShouldBeLessThan(flow.Sum(f => f.Weight));
        output.WriteLine($"audit facts {audit.Count}, cap facts {cap.Count}");
    }

    [Fact]
    public async Task The_full_volume_seeds_in_seconds()
    {
        var watch = Stopwatch.StartNew();
        await AuditCapVolume.SeedAsync(fx.Factory, fx.Units, fx.Employees);
        watch.Stop();
        output.WriteLine($"2 000 findings + 2 000 remarks seeded in {watch.Elapsed.TotalSeconds:0.0} s");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var compute = Stopwatch.StartNew();
        var audit = await FactsAsync(fx.Factory, AuditPermissions.SystemCode, today, null);
        var cap = await FactsAsync(fx.Factory, CapPermissions.SystemCode, today, null);
        compute.Stop();
        output.WriteLine($"audit + cap facts computed in {compute.Elapsed.TotalMilliseconds:0} ms");
        audit.Sum(AuditMetrics.Findings).ShouldBeGreaterThanOrEqualTo(2000m);
        cap.Sum(CapMetrics.Remarks).ShouldBeGreaterThanOrEqualTo(2000m);
    }
}
