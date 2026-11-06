using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Reporting.Volume;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Settings;

namespace Strategy.IntegrationTests.Performance;

/// <summary>
/// Seeds a named, persistent database for the k6 load test (docs/performance.md, ADR 0158) with the Phase 17 volume — 1 050 projects
/// (at least 525 active), one of them with 30 milestones for the Gantt check, 1 000 KPI cards, 1 000 risks, 2 000 findings, 2 000 remarks,
/// 10 000 tasks — and writes the test users' personal access tokens and record ids to a JSON file. A no-op unless
/// <c>STRATEGY_PERF_DATABASE</c> (connection string of an empty database) and <c>STRATEGY_PERF_OUTPUT</c> (file) are set; <c>tests/perf/run.sh</c>
/// sets them. The volume seeders write with binary COPY (test set-up only, never a business path, see <see cref="PortfolioKpiVolume"/>).
/// </summary>
public sealed class PerformanceVolumeSeed
{
    private static readonly string[] ReadPermissions =
    [
        "portfolio.project.read", "portfolio.request.read", "kpi.card.read", "kpi.result.read", "risk.risk.read", "auditmgmt.finding.read", "cap.remark.read",
        "tasks.task.read", "tasks.task.create", "teams.team.read", "docreg.document.read", "ip.record.read", "bench.study.read", "requests.request.read",
    ];

    [Fact]
    public async Task Seed_the_performance_database_when_asked()
    {
        var connectionString = Environment.GetEnvironmentVariable("STRATEGY_PERF_DATABASE");
        var output = Environment.GetEnvironmentVariable("STRATEGY_PERF_OUTPUT");
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(output))
        {
            return; // not a load-test run
        }

        var smtpPort = int.Parse(Environment.GetEnvironmentVariable("STRATEGY_PERF_SMTP_PORT") ?? "1025", CultureInfo.InvariantCulture);
        await using var factory = new ApiFactory(connectionString);
        _ = factory.Server; // runs the migrations

        var units = new List<Guid>();
        var employees = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var person = await factory.CreateUserAsync();
            units.Add(person.UnitId!.Value);
            employees.Add(person.EmployeeId!.Value);
        }

        await PortfolioKpiVolume.SeedAsync(factory, units, projects: 1050, firstProjectMilestones: 30);
        await RiskTaskVolume.SeedAsync(factory, units, employees);
        await AuditCapVolume.SeedAsync(factory, units, employees);

        await using (var scope = TestAccess.SystemScope(factory))
        {
            var editor = scope.ServiceProvider.GetRequiredService<ISettingsEditor>();
            var doc = await editor.GetDocumentAsync<ReportingSettings>();
            (await editor.SaveAsync((doc.Value ?? new ReportingSettings()) with { DepartmentLevelCode = "organisation" }, doc.Version, _ => [])).IsSuccess
                .ShouldBeTrue();
            var month = ReportPeriod.MonthOf(await scope.ServiceProvider.GetRequiredService<ReportingClock>().TodayAsync(CancellationToken.None));
            await scope.ServiceProvider.GetRequiredService<FactRefresher>().RefreshAsync(null, month.AddMonths(-11), month, "perf", CancellationToken.None);

            // Notification latency check: e-mail goes to the Mailpit of the test run.
            var identity = scope.ServiceProvider.GetRequiredService<IdentityStoreDbContext>();
            var smtp = await identity.SmtpSettings.SingleAsync();
            smtp.Enabled = true;
            smtp.Host = "localhost";
            smtp.Port = smtpPort;
            smtp.Security = SmtpSecurityModes.None;
            smtp.FromAddress = "strategy@perf.test";
            smtp.PublicBaseUrl = "http://localhost:5186";
            await identity.SaveChangesAsync();
        }

        // A Director General reading everything (scope All) who may also create tasks, and a unit-scoped executive viewer.
        var allRoles = await Task.WhenAll(ReadPermissions.GroupBy(p => p[..p.IndexOf('.', StringComparison.Ordinal)])
            .Select(g => factory.CreateRoleAsync(g.Key, g.Select(p => (p, DataScope.All)).ToArray())));
        var unitRoles = await Task.WhenAll(ReadPermissions.Where(p => p is not "docreg.document.read").GroupBy(p => p[..p.IndexOf('.', StringComparison.Ordinal)])
            .Select(g => factory.CreateRoleAsync(g.Key, g.Select(p => (p, DataScope.UnitAndBelow)).ToArray())));
        var director = await factory.CreateUserAsync(withMfa: true, unitId: units[1], roles: ["reporting.executive-viewer", .. allRoles]);
        var unitViewer = await factory.CreateUserAsync(withMfa: true, unitId: units[0], roles: ["reporting.executive-viewer", .. unitRoles]);
        var assignee = await factory.CreateUserAsync(unitId: units[1]);

        string directorToken;
        string unitToken;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var tokens = scope.ServiceProvider.GetRequiredService<AccessTokenService>();
            directorToken = (await tokens.CreateAsync(director.Id, "k6", [TokenScopes.ApiRead, TokenScopes.ApiWrite], 30, CancellationToken.None)).Value.Token;
            unitToken = (await tokens.CreateAsync(unitViewer.Id, "k6", [TokenScopes.ApiRead], 30, CancellationToken.None)).Value.Token;
        }

        Guid ganttProject;
        List<Guid> activeProjects;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            ganttProject = await portfolio.Milestones.AsNoTracking().GroupBy(m => m.ProjectId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
            activeProjects = await portfolio.Projects.AsNoTracking().Where(p => p.Status == ProjectStatuses.Active).Select(p => p.Id).ToListAsync();
        }

        activeProjects.Count.ShouldBeGreaterThanOrEqualTo(500);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            directorToken,
            unitToken,
            ganttProject,
            activeProjects,
            assigneeEmployeeId = assignee.EmployeeId,
            assigneeEmail = assignee.UserName,
        }));
        NpgsqlConnection.ClearAllPools();
    }
}
