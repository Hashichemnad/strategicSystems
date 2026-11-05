using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Reporting.Volume;
using Strategy.Platform.Access;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Reporting;

/// <summary>
/// SPEC §0.4 / BUILD_PLAN Phase 17: the executive dashboard loads in under 3 s on the seeded volume (500 projects, 1 000 KPIs, 1 000 risks,
/// 2 000 findings, 2 000 remarks, 10 000 tasks) — it reads cached facts only; lists are short scoped module queries. A unit-scoped viewer of the
/// same dashboard sees different numbers.
/// </summary>
public sealed class ExecutivePerformanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateAsync(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task The_executive_dashboard_loads_in_under_three_seconds_on_the_seeded_volume_and_scopes_its_figures()
    {
        // Twelve departments (each test user gets its own unit), one employee each.
        var units = new List<Guid>();
        var employees = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var person = await _factory.CreateUserAsync();
            units.Add(person.UnitId!.Value);
            employees.Add(person.EmployeeId!.Value);
        }

        var seed = Stopwatch.StartNew();
        await PortfolioKpiVolume.SeedAsync(_factory, units);
        await RiskTaskVolume.SeedAsync(_factory, units, employees);
        await AuditCapVolume.SeedAsync(_factory, units, employees);
        seed.Stop();

        await using (var scope = TestAccess.SystemScope(_factory))
        {
            // The test units sit at the "organisation" level: the scorecard rows are those units.
            var editor = scope.ServiceProvider.GetRequiredService<ISettingsEditor>();
            var doc = await editor.GetDocumentAsync<ReportingSettings>();
            (await editor.SaveAsync((doc.Value ?? new ReportingSettings()) with { DepartmentLevelCode = "organisation" }, doc.Version, _ => [])).IsSuccess.ShouldBeTrue();
        }

        var refresh = Stopwatch.StartNew();
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var month = ReportPeriod.MonthOf(await scope.ServiceProvider.GetRequiredService<ReportingClock>().TodayAsync(CancellationToken.None));
            await scope.ServiceProvider.GetRequiredService<FactRefresher>().RefreshAsync(null, month.AddMonths(-11), month, "test", CancellationToken.None);
        }

        refresh.Stop();

        // A Director General (scope All in every system) and a unit-scoped executive viewer.
        var allRole = await AllReadersRoleAsync();
        var director = await _factory.SignInAsync(await _factory.CreateUserAsync(withMfa: true, roles: ["reporting.executive-viewer", .. allRole]));
        var unitRoles = await UnitReadersRoleAsync();
        var unitViewer = await _factory.SignInAsync(await _factory.CreateUserAsync(withMfa: true, unitId: units[0], roles: ["reporting.executive-viewer", .. unitRoles]));

        (await director.GetAsync("/api/v1/reporting/dashboards/executive/data?kind=year")).StatusCode.ShouldBe(HttpStatusCode.OK); // warm-up
        var timings = new List<long>();
        JsonElement data = default;
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await director.GetAsync("/api/v1/reporting/dashboards/executive/data?kind=year");
            data = await response.Content.ReadFromJsonAsync<JsonElement>();
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            timings.Add(watch.ElapsedMilliseconds);
        }

        var median = timings.Order().ElementAt(1);
        TestContext.Current.SendDiagnosticMessage($"seed {seed.ElapsedMilliseconds} ms, refresh {refresh.ElapsedMilliseconds} ms, executive {string.Join("/", timings)} ms");
        median.ShouldBeLessThan(3000, $"executive dashboard took {string.Join("/", timings)} ms");

        decimal? Tile(JsonElement d, string id) =>
            d.GetProperty("data").EnumerateArray().Single(w => w.GetProperty("id").GetString() == id) is var w && w.GetProperty("value").ValueKind == JsonValueKind.Number
                ? w.GetProperty("value").GetDecimal() : null;
        Tile(data, "org-score").ShouldNotBeNull();
        Tile(data, "active-projects").ShouldNotBeNull().ShouldBeGreaterThan(100);
        Tile(data, "late-findings").ShouldNotBeNull().ShouldBeGreaterThan(100);
        var scorecard = data.GetProperty("data").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "scorecard").GetProperty("scorecard");
        scorecard.GetProperty("rows").GetArrayLength().ShouldBeGreaterThanOrEqualTo(12);

        var unitData = await unitViewer.GetFromJsonAsync<JsonElement>("/api/v1/reporting/dashboards/executive/data?kind=year");
        Tile(unitData, "active-projects").ShouldNotBeNull().ShouldBeLessThan(Tile(data, "active-projects")!.Value);
        unitData.GetProperty("data").EnumerateArray().Single(w => w.GetProperty("id").GetString() == "scorecard").GetProperty("scorecard").GetProperty("rows")
            .GetArrayLength().ShouldBe(1);
    }

    private static readonly string[] ReadPermissions =
    [
        "portfolio.project.read", "portfolio.request.read", "kpi.card.read", "kpi.result.read", "risk.risk.read", "auditmgmt.finding.read", "cap.remark.read",
        "tasks.task.read", "teams.team.read", "docreg.document.read", "ip.record.read", "bench.study.read", "requests.request.read",
    ];

    private async Task<string[]> AllReadersRoleAsync() =>
        await Task.WhenAll(ReadPermissions.GroupBy(p => p[..p.IndexOf('.', StringComparison.Ordinal)])
            .Select(g => _factory.CreateRoleAsync(g.Key, g.Select(p => (p, DataScope.All)).ToArray())));

    private async Task<string[]> UnitReadersRoleAsync() =>
        await Task.WhenAll(ReadPermissions.Where(p => p is not "docreg.document.read").GroupBy(p => p[..p.IndexOf('.', StringComparison.Ordinal)])
            .Select(g => _factory.CreateRoleAsync(g.Key, g.Select(p => (p, DataScope.UnitAndBelow)).ToArray())));
}
