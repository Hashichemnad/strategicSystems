using System.Net.Http.Json;
using Strategy.IntegrationTests.AuditManagement;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>Audit Management → Settings → Responsibilities (ADR 0168): the responsible auditor moves only to Audit Office members; departments stay.</summary>
public sealed class AuditResponsibilityTests(AuditFixture fx) : IClassFixture<AuditFixture>
{
    private const string System = AuditPermissions.SystemCode;
    private const string Reason = "Audit portfolio rebalanced";

    [Fact]
    public async Task Findings_move_only_to_audit_office_members_and_their_departments_stay()
    {
        var w = await AuditWorld.CreateAsync(fx.Factory);
        var today = await w.TodayAsync();
        var report = await w.ReportAsync();
        var f1 = await w.FindingAsync(report, AuditWorld.Finding("Unbilled fees", w.S1, today), AuditWorld.Recommendation("Bill monthly", today.AddDays(60)));
        var f2 = await w.FindingAsync(report, AuditWorld.Finding("Late deposits", w.S2, today), AuditWorld.Recommendation("Deposit daily", today.AddDays(60)));
        var f3 = await w.FindingAsync(report, AuditWorld.Finding("Missing receipts", w.S1, today), AuditWorld.Recommendation("Archive receipts", today.AddDays(60)));
        var office = w.C(w.Office);

        // The candidates are the Audit Office members only.
        var candidates = (await office.GetFromJsonAsync<List<ResponsibilityNameDto>>($"{Responsibilities.Base(System)}/finding-auditor/candidates"))!;
        candidates.Select(c => c.Id).ShouldContain(w.Office.EmployeeId!.Value);
        candidates.Select(c => c.Id).ShouldContain(w.Chief.EmployeeId!.Value);
        candidates.Select(c => c.Id).ShouldNotContain(w.Director.EmployeeId!.Value);

        var rows = await Responsibilities.ListAsync(office, System, "finding-auditor", $"&personId={w.Office.EmployeeId}");
        var selected = rows.Items.Where(r => r.Id == f1.Id || r.Id == f2.Id).ToList();
        selected.Count.ShouldBe(2);

        // A person outside the Audit Office is refused on the server (validation), nothing moves.
        await (await office.PostAsJsonAsync($"{Responsibilities.Base(System)}/finding-auditor/reassign", Responsibilities.Body(selected, w.Director.EmployeeId!.Value, Reason)))
            .ShouldBeValidationProblemAsync("newId", ResponsibilityMessages.NotEligible);
        (await w.GetAsync(f1.Id)).Auditor!.Id.ShouldBe(w.Office.EmployeeId!.Value);

        var done = await Responsibilities.ReassignAsync(office, System, "finding-auditor", Responsibilities.Body(selected, w.Chief.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(2);
        var moved = await w.GetAsync(f1.Id);
        moved.Auditor!.Id.ShouldBe(w.Chief.EmployeeId!.Value);
        moved.ResponsibleUnit!.Id.ShouldBe(w.S1);
        (await w.GetAsync(f3.Id)).Auditor!.Id.ShouldBe(w.Office.EmployeeId!.Value);
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, Finding.EntityType, f1.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.Chief), Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(office, Responsibilities.RemovedTemplate);

        // The report's lead auditor follows the same rule.
        var reports = await Responsibilities.ListAsync(office, System, "report-lead-auditor", $"&personId={w.Office.EmployeeId}");
        var row = reports.Items.Single(r => r.Id == report.Id);
        (await Responsibilities.PreviewAsync(office, System, "report-lead-auditor", Responsibilities.Body([row], w.Chief.EmployeeId!.Value)))
            .ShouldHaveSingleItem().Outcome.ShouldBe(ResponsibilityOutcomes.Allowed);
    }
}
