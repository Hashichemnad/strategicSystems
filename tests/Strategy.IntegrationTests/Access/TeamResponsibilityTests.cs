using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Teams;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;

namespace Strategy.IntegrationTests.Access;

/// <summary>Teams → Settings → Responsibilities (ADR 0168): a new leader replaces the leader member (history kept), record roles follow.</summary>
public sealed class TeamResponsibilityTests(TeamsFixture fx) : IClassFixture<TeamsFixture>
{
    private const string System = TeamPermissions.SystemCode;
    private const string Reason = "President appointed elsewhere";

    [Fact]
    public async Task Only_the_selected_team_gets_the_new_leader_and_the_member_history_is_kept()
    {
        var w = await TeamsWorld.CreateAsync(fx.Factory);
        var t1 = await w.FormAsync(w.Request("Pricing Committee " + Guid.CreateVersion7().ToString("N")[^5..], withExternal: false));
        var t2 = await w.FormAsync(w.Request("Quality Committee " + Guid.CreateVersion7().ToString("N")[^5..], withExternal: false));
        var successor = await fx.Factory.CreateUserAsync(unitId: w.DepartmentId);
        var successorClient = await fx.Factory.SignInAsync(successor);
        var hr = w.C(w.Hr);

        var rows = await Responsibilities.ListAsync(hr, System, "team-leader", $"&personId={w.President.EmployeeId}");
        rows.Items.Select(r => r.Id).ShouldContain(t1.Id);
        rows.Items.Select(r => r.Id).ShouldContain(t2.Id);

        // A current member cannot be made leader this way (the member list would hold them twice).
        (await Responsibilities.PreviewAsync(hr, System, "team-leader", Responsibilities.Body(rows.Items.Where(r => r.Id == t1.Id), w.MemberA.EmployeeId!.Value)))
            .ShouldHaveSingleItem().MessageKey.ShouldBe("teams.errors.duplicateMember");

        var done = await Responsibilities.ReassignAsync(hr, System, "team-leader", Responsibilities.Body(rows.Items.Where(r => r.Id == t1.Id), successor.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(1);
        (await w.GetAsync(t1.Id)).Leader!.Id.ShouldBe(successor.EmployeeId!.Value);
        (await w.GetAsync(t2.Id)).Leader!.Id.ShouldBe(w.President.EmployeeId!.Value);
        var history = (await hr.GetFromJsonAsync<List<MemberDto>>($"{TeamsWorld.Base}/{t1.Id}/members?history=true"))!;
        history.ShouldContain(m => m.Employee != null && m.Employee.Id == w.President.EmployeeId && m.Status == MemberStatuses.Replaced);
        history.ShouldContain(m => m.Employee != null && m.Employee.Id == successor.EmployeeId && m.Status == MemberStatuses.Active && m.RoleCode == "president");
        (await successorClient.GetAsync($"{TeamsWorld.Base}/{t1.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, Team.EntityType, t1.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(successorClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.President), Responsibilities.RemovedTemplate);
    }
}
