using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Risk;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>Risk → Settings → Responsibilities (ADR 0168): action owners move with their record roles, the review approval follows, risk owners move.</summary>
public sealed class RiskResponsibilityTests(RiskFixture fx) : IClassFixture<RiskFixture>
{
    private const string System = RiskPermissions.SystemCode;
    private const string Reason = "Action owner moved to another post";

    [Fact]
    public async Task Only_the_selected_action_moves_its_record_role_follows_and_the_review_approval_is_re_routed()
    {
        var w = await RiskWorld.CreateAsync(fx.Factory);
        var newOwner = await fx.Factory.CreateUserAsync(unitId: w.DepartmentA);
        var newOwnerClient = await fx.Factory.SignInAsync(newOwner);
        var c = w.C(w.CoordinatorA);
        var r1 = await w.CreateAsync(RiskWorld.Values("Outage", w.DepartmentA, w.OwnerA, 3, 3));
        var r2 = await w.CreateAsync(RiskWorld.Values("Fraud", w.DepartmentA, w.OwnerA, 3, 3));
        async Task<ActionDto> ActionAsync(RiskDto risk, string title) => await RiskWorld.OkAsync<ActionDto>(c.PostAsJsonAsync($"{RiskWorld.Base}/risks/{risk.Id}/actions",
            new { values = new { titleEn = title, ownerEmployeeId = w.ActionOwner.EmployeeId, plannedEnd = new DateOnly(2026, 12, 31) } }));
        var a1 = await ActionAsync(r1, "Backup site");
        var a3 = await ActionAsync(r2, "Segregate duties");

        // The review step is held by the action owners of the risk (record role): the action owner approves R1.
        var admin = await fx.Factory.CreateSignedInClientAsync(admin: true);
        var process = (await admin.GetFromJsonAsync<ProcessDetailDto>($"/api/v1/workflow/processes/{RiskProcesses.Review}"))!;
        (await admin.PutAsJsonAsync($"/api/v1/workflow/processes/{RiskProcesses.Review}", new SaveProcessInput(
            [new ApprovalStepDefinition("action-owners", "مسؤولو الإجراءات", "Action owners", new ApproverRule(ApproverRuleKinds.RecordRole, RoleCode: RiskPermissions.ActionOwnerRole))],
            "responsibility test", process.VersionNo))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await RiskWorld.OkAsync<RiskDto>(c.PostAsJsonAsync($"{RiskWorld.Base}/risks/{r1.Id}/submit", new { comment = "please review" }));
        (await w.InstanceAsync(r1.Id, RiskProcesses.Review, w.ActionOwner)).MyTask.ShouldNotBeNull();

        var office = w.C(w.Office);
        var rows = await Responsibilities.ListAsync(office, System, "action-owner", $"&personId={w.ActionOwner.EmployeeId}");
        rows.Items.Select(r => r.Id).ShouldBe([a1.Id, a3.Id], ignoreOrder: true);
        rows.Items.Single(r => r.Id == a1.Id).ContextEn.ShouldBe(r1.Number);
        var done = await Responsibilities.ReassignAsync(office, System, "action-owner",
            Responsibilities.Body(rows.Items.Where(r => r.Id == a1.Id), newOwner.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(1);

        // Only a1 moved; a3 (other risk) stays. Record roles followed: the previous owner no longer reads R1, the new one does.
        var after = await Responsibilities.ListAsync(office, System, "action-owner");
        after.Items.Single(r => r.Id == a1.Id).Responsible!.Id.ShouldBe(newOwner.EmployeeId!.Value);
        after.Items.Single(r => r.Id == a3.Id).Responsible!.Id.ShouldBe(w.ActionOwner.EmployeeId!.Value);
        (await newOwnerClient.GetAsync($"{RiskWorld.Base}/risks/{r1.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.ActionOwner).GetAsync($"{RiskWorld.Base}/risks/{r2.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The pending review step moved to the new action owner.
        var panel = await RiskWorld.OkAsync<SubjectApprovalsDto>(newOwnerClient.GetAsync($"/api/v1/workflow/subjects/{RiskRecord.EntityType}/{r1.Id}"));
        panel.Instances.First(i => i.ProcessKey == RiskProcesses.Review && i.Status == ApprovalInstanceStatuses.InProgress).MyTask.ShouldNotBeNull();
        var previous = await w.C(w.ActionOwner).GetAsync($"/api/v1/workflow/subjects/{RiskRecord.EntityType}/{r1.Id}");
        if (previous.StatusCode == HttpStatusCode.OK)
        {
            // A former approver keeps the history view, but no longer holds the step.
            (await previous.Content.ReadFromJsonAsync<SubjectApprovalsDto>())!.Instances.ShouldAllBe(i => i.MyTask == null);
        }

        await Responsibilities.ShouldBeAuditedAsync(admin, System, TreatmentAction.EntityType, a1.Id, Reason);
        await Responsibilities.ShouldBeNotifiedOnceAsync(newOwnerClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.ActionOwner), Responsibilities.RemovedTemplate);

        // Risk owner: closed risks are not offered; an open one moves.
        var owners = await Responsibilities.ListAsync(office, System, "risk-owner", $"&personId={w.OwnerA.EmployeeId}");
        var r2Row = owners.Items.Single(r => r.Id == r2.Id);
        (await Responsibilities.PreviewAsync(office, System, "risk-owner", Responsibilities.Body([r2Row], newOwner.EmployeeId!.Value)))
            .ShouldHaveSingleItem().Outcome.ShouldBe(ResponsibilityOutcomes.Allowed);
        (await Responsibilities.ReassignAsync(office, System, "risk-owner", Responsibilities.Body([r2Row], newOwner.EmployeeId!.Value, Reason))).Moved.ShouldBe(1);
        (await w.GetAsync(r2.Id)).Values.OwnerEmployeeId.ShouldBe(newOwner.EmployeeId);
        (await w.GetAsync(r1.Id)).Values.OwnerEmployeeId.ShouldBe(w.OwnerA.EmployeeId);
    }
}
