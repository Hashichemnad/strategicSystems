using System.Net;
using Strategy.IntegrationTests.Cap;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Domain;

namespace Strategy.IntegrationTests.Access;

/// <summary>CAP → Settings → Responsibilities (ADR 0168): remark owners and action assignees move with their record roles, audited and notified.</summary>
public sealed class CapResponsibilityTests(CapFixture fx) : IClassFixture<CapFixture>
{
    private const string System = CapPermissions.SystemCode;
    private const string Reason = "Section reorganised";

    [Fact]
    public async Task Only_the_selected_remarks_and_actions_move_to_the_new_person()
    {
        var w = await CapWorld.CreateAsync(fx.Factory, fx.Clock);
        var successor = await fx.Factory.CreateUserAsync(unitId: w.SectionA1);
        var successorClient = await fx.Factory.SignInAsync(successor);
        var source = await w.SourceAsync();
        var r1 = await w.RemarkAsync(source, "Service charter outdated");
        var r2 = await w.RemarkAsync(source, "Complaints answered late");
        var r3 = await w.RemarkAsync(source, "Forms not bilingual");
        var pmo = w.C(w.Pmo);

        var rows = await Responsibilities.ListAsync(pmo, System, "remark-owner", $"&personId={w.SectionManager.EmployeeId}");
        rows.Items.Select(r => r.Id).ShouldBe([r1.Id, r2.Id, r3.Id], ignoreOrder: true);
        var done = await Responsibilities.ReassignAsync(pmo, System, "remark-owner",
            Responsibilities.Body(rows.Items.Where(r => r.Id == r1.Id || r.Id == r2.Id), successor.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(2);
        (await w.GetAsync(r1.Id)).Owner!.Id.ShouldBe(successor.EmployeeId!.Value);
        (await w.GetAsync(r3.Id)).Owner!.Id.ShouldBe(w.SectionManager.EmployeeId!.Value);
        (await successorClient.GetAsync($"{CapWorld.Base}/remarks/{r1.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await successorClient.GetAsync($"{CapWorld.Base}/remarks/{r3.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, Remark.EntityType, r1.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(successorClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.SectionManager), Responsibilities.RemovedTemplate);

        // Action assignee of R3: moves, the remark's assignee record role follows (the new assignee reads the action).
        await w.AcceptAndPlanAsync(r3, w.CapActionValues("Translate the forms"), w.CapActionValues("Publish the forms"));
        var actions = await Responsibilities.ListAsync(pmo, System, "action-assignee", $"&personId={w.Assignee.EmployeeId}");
        actions.Items.Count.ShouldBe(2);
        var chosen = actions.Items[0];
        (await Responsibilities.ReassignAsync(pmo, System, "action-assignee", Responsibilities.Body([chosen], successor.EmployeeId!.Value, Reason))).Moved.ShouldBe(1);
        (await w.ActionAsync(chosen.Id)).Assignee!.Id.ShouldBe(successor.EmployeeId!.Value);
        (await w.ActionAsync(actions.Items[1].Id)).Assignee!.Id.ShouldBe(w.Assignee.EmployeeId!.Value);
        (await successorClient.GetAsync($"{CapWorld.Base}/actions/{chosen.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, CapAction.EntityType, chosen.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.Assignee), Responsibilities.RemovedTemplate);
    }
}
