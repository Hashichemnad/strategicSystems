using System.Net;
using Strategy.IntegrationTests.Bench;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;

namespace Strategy.IntegrationTests.Access;

/// <summary>Benchmarking → Settings → Responsibilities (ADR 0168): the study coordinator moves with the coordinator record role.</summary>
public sealed class BenchResponsibilityTests(BenchFixture fx) : IClassFixture<BenchFixture>
{
    private const string System = BenchPermissions.SystemCode;
    private const string Reason = "Coordinator on long leave";

    [Fact]
    public async Task Only_the_selected_study_gets_the_new_coordinator()
    {
        var w = await BenchWorld.CreateAsync(fx.Factory);
        var s1 = await w.ApprovedStudyAsync(DateTime.UtcNow.Year + 1, 1, "Licensing benchmarking");
        var s2 = await w.ApprovedStudyAsync(DateTime.UtcNow.Year + 1, 2, "Inspection benchmarking");
        var successor = await fx.Factory.CreateUserAsync(unitId: w.DepartmentA);
        var successorClient = await fx.Factory.SignInAsync(successor);
        var office = w.C(w.Office);

        var rows = await Responsibilities.ListAsync(office, System, "study-coordinator", $"&personId={w.Coordinator.EmployeeId}");
        rows.Items.Select(r => r.Id).ShouldBe([s1.Id, s2.Id], ignoreOrder: true);
        var done = await Responsibilities.ReassignAsync(office, System, "study-coordinator",
            Responsibilities.Body(rows.Items.Where(r => r.Id == s1.Id), successor.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(1);
        (await w.GetAsync(s1.Id)).ResponsibleEmployee!.Id.ShouldBe(successor.EmployeeId!.Value);
        (await w.GetAsync(s2.Id)).ResponsibleEmployee!.Id.ShouldBe(w.Coordinator.EmployeeId!.Value);
        (await successorClient.GetAsync($"{BenchWorld.Base}/studies/{s1.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await successorClient.GetAsync($"{BenchWorld.Base}/studies/{s2.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, Study.EntityType, s1.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(successorClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.Coordinator), Responsibilities.RemovedTemplate);
    }
}
