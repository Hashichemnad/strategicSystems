using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Kpi;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>
/// KPI → Settings → Responsibilities (SPEC-ADDENDUM §D2, ADR 0168): only the selected cards move (with their results), audited with the reason,
/// both people notified once, result approvals re-routed; per-system permission, scope, vendors and concurrency.
/// </summary>
public sealed class KpiResponsibilityTests(KpiFixture fx) : IClassFixture<KpiFixture>
{
    private const string System = KpiPermissions.SystemCode;
    private const string Reason = "Ownership moved to department B";

    [Fact]
    public async Task Only_the_selected_cards_move_with_their_results_audited_and_both_people_notified_once()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var c1 = await w.CreateCardAsync(KpiWorld.Card("Permits A", w.OwnerA, w.DepartmentA));
        var c2 = await w.CreateCardAsync(KpiWorld.Card("Licences A", w.OwnerA, w.DepartmentA));
        var c3 = await w.CreateCardAsync(KpiWorld.Card("Inspections A", w.OwnerA, w.DepartmentA));
        await w.CreateCardAsync(KpiWorld.Card("Audits B", w.OwnerB, w.DepartmentB));
        await w.StartYearAsync(2047, [KpiWorld.Item(c1, 10), KpiWorld.Item(c3, 10)]);
        var result = (await w.PlanAsync(c1.Id, 2047)).Periods[0].ResultId;
        var pmo = w.C(w.Pmo);

        // The kinds of the system and the list filtered by the current person.
        var kinds = (await pmo.GetFromJsonAsync<ResponsibilityKindsDto>(Responsibilities.Base(System)))!;
        kinds.Kinds.Select(k => k.Code).ShouldBe(["card-person", "card-unit"], ignoreOrder: true);
        var list = await Responsibilities.ListAsync(pmo, System, "card-person", $"&personId={w.OwnerA.EmployeeId}");
        list.Items.Select(r => r.Id).ShouldBe([c1.Id, c2.Id, c3.Id], ignoreOrder: true);
        list.Items.ShouldAllBe(r => r.Responsible!.Id == w.OwnerA.EmployeeId && r.Responsible.NameEn != null && r.Unit!.Id == w.DepartmentA);
        (await Responsibilities.ListAsync(pmo, System, "card-person", $"&personId={w.OwnerA.EmployeeId}&text=licences")).Items.ShouldHaveSingleItem().Id.ShouldBe(c2.Id);

        // Validation: a reason is required, the new person must exist.
        var selected = list.Items.Where(r => r.Id == c1.Id || r.Id == c2.Id).ToList();
        await (await pmo.PostAsJsonAsync($"{Responsibilities.Base(System)}/card-person/reassign", Responsibilities.Body(selected, w.OwnerB.EmployeeId!.Value, reason: " ")))
            .ShouldBeValidationProblemAsync("reason", "validation.notEmpty");
        await (await pmo.PostAsJsonAsync($"{Responsibilities.Base(System)}/card-person/preview", Responsibilities.Body(selected, Guid.CreateVersion7())))
            .ShouldBeValidationProblemAsync("newId", "validation.unknownReference");

        // Preview: both allowed, an unknown id is blocked without leaking anything.
        var unknown = Guid.CreateVersion7();
        var preview = await Responsibilities.PreviewAsync(pmo, System, "card-person", new
        {
            items = selected.Select(r => new { id = r.Id, version = r.Version }).Append(new { id = unknown, version = 1u }).ToList(),
            newId = w.OwnerB.EmployeeId,
            reason = Reason,
        });
        preview.Where(o => o.Id != unknown).ShouldAllBe(o => o.Outcome == ResponsibilityOutcomes.Allowed && o.Previous!.Id == w.OwnerA.EmployeeId);
        var blocked = preview.Single(o => o.Id == unknown);
        (blocked.Outcome, blocked.MessageKey, blocked.Number).ShouldBe((ResponsibilityOutcomes.Blocked, ResponsibilityMessages.NotFound, (string?)null));

        var done = await Responsibilities.ReassignAsync(pmo, System, "card-person", Responsibilities.Body(selected, w.OwnerB.EmployeeId!.Value, Reason));
        (done.Moved, done.Skipped).ShouldBe((2, 0));
        done.New.NameEn.ShouldNotBeNull();

        // Only the selected cards (and the current / future results with them) moved; the third card stays with A.
        var after = await Responsibilities.ListAsync(pmo, System, "card-person");
        after.Items.Single(r => r.Id == c1.Id).Responsible!.Id.ShouldBe(w.OwnerB.EmployeeId!.Value);
        after.Items.Single(r => r.Id == c2.Id).Responsible!.Id.ShouldBe(w.OwnerB.EmployeeId!.Value);
        after.Items.Single(r => r.Id == c3.Id).Responsible!.Id.ShouldBe(w.OwnerA.EmployeeId!.Value);
        (await w.C(w.OwnerB).GetAsync($"{KpiWorld.Base}/results/{result}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.OwnerA).GetAsync($"{KpiWorld.Base}/results/{result}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.OwnerA).GetAsync($"{KpiWorld.Base}/cards/{c3.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Audit (field changes with the reason + one summary event) and the two notices.
        await Responsibilities.ShouldBeAuditedAsync(await fx.Factory.CreateSignedInClientAsync(admin: true), System, "kpi.card", c1.Id, Reason);
        var assigned = await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.OwnerB), Responsibilities.AssignedTemplate);
        assigned.TitleEn.ShouldContain("KPI card responsible person");
        assigned.TitleAr.ShouldContain("الموظف المسؤول عن بطاقة المؤشر");
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.OwnerA), Responsibilities.RemovedTemplate);

        // Concurrency: the versions the page showed before are stale now — skipped, never overwritten.
        var stale = await Responsibilities.ReassignAsync(pmo, System, "card-person", Responsibilities.Body(selected, w.OwnerA.EmployeeId!.Value, "Back again"));
        (stale.Moved, stale.Skipped).ShouldBe((0, 2));
        stale.Items.ShouldAllBe(o => o.MessageKey == ResponsibilityMessages.ChangedMeanwhile);
        (await Responsibilities.ListAsync(pmo, System, "card-person")).Items.Single(r => r.Id == c1.Id).Responsible!.Id.ShouldBe(w.OwnerB.EmployeeId!.Value);
    }

    [Fact]
    public async Task Moving_the_responsible_unit_re_routes_the_result_approval_in_progress()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var headB = await fx.Factory.CreateUserAsync(unitId: w.DepartmentB);
        await ApprovalWorld.SetHeadAsync(fx.Factory, w.DepartmentB, headB.EmployeeId);
        var headBClient = await fx.Factory.SignInAsync(headB);
        var admin = await fx.Factory.CreateSignedInClientAsync(admin: true);
        var process = (await admin.GetFromJsonAsync<ProcessDetailDto>("/api/v1/workflow/processes/kpi.result"))!;
        (await admin.PutAsJsonAsync("/api/v1/workflow/processes/kpi.result", new SaveProcessInput(
            [new ApprovalStepDefinition("head", "رئيس الوحدة", "Head of the unit", new ApproverRule(ApproverRuleKinds.HeadOfUnit, Of: ApproverRuleSubjects.Record))],
            "responsibility test", process.VersionNo))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = await w.CreateCardAsync(KpiWorld.Card("Visits", w.OwnerA, w.DepartmentA));
        await w.StartYearAsync(2048, [KpiWorld.Item(card, 10)]);
        var result = (await w.PlanAsync(card.Id, 2048)).Periods[0].ResultId;
        await w.SubmitAsync(w.OwnerA, result, KpiWorld.Entry(3));
        await KpiWorld.MyTaskAsync(w.C(w.ManagerA), KpiResult.EntityType, result);

        var row = (await Responsibilities.ListAsync(w.C(w.Pmo), System, "card-unit", $"&personId={w.DepartmentA}")).Items.Single(r => r.Id == card.Id);
        var done = await Responsibilities.ReassignAsync(w.C(w.Pmo), System, "card-unit", Responsibilities.Body([row], w.DepartmentB, Reason));
        done.Moved.ShouldBe(1);

        // The head of B now holds the step; the head of A no longer.
        await KpiWorld.MyTaskAsync(headBClient, KpiResult.EntityType, result);
        var panel = (await w.C(w.ManagerA).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/{KpiResult.EntityType}/{result}"));
        panel?.Instances.ShouldAllBe(i => i.MyTask == null);
        await Responsibilities.ShouldBeNotifiedOnceAsync(headBClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeNotifiedOnceAsync(w.C(w.ManagerA), Responsibilities.RemovedTemplate);
    }

    [Fact]
    public async Task The_permission_is_per_system_scoped_and_refused_to_vendors()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var a = await w.CreateCardAsync(KpiWorld.Card("Scoped A", w.OwnerA, w.DepartmentA));
        var b = await w.CreateCardAsync(KpiWorld.Card("Scoped B", w.OwnerB, w.DepartmentB));

        // A Risk system administrator cannot reassign KPI responsibilities (403), only Risk ones.
        var riskOffice = await fx.Factory.SignInAsync(await fx.Factory.CreateUserAsync(withMfa: true, roles: "risk.risk-office"));
        (await riskOffice.GetAsync(Responsibilities.Base(System))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await riskOffice.PostAsJsonAsync($"{Responsibilities.Base(System)}/card-person/reassign",
            new { items = new[] { new { id = a.Id, version = 1u } }, newId = w.OwnerB.EmployeeId, reason = Reason })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await riskOffice.GetAsync(Responsibilities.Base("risk"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.C(w.OwnerA).GetAsync(Responsibilities.Base(System))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await (await fx.Factory.CreateSpaClientAsync()).GetAsync(Responsibilities.Base(System))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await w.C(w.Pmo).GetAsync(Responsibilities.Base("no-such-system"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Pmo).GetAsync($"{Responsibilities.Base(System)}/no-such-kind")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A delegated administrator holding only the permission, for department A: lists A's cards; B's card is out of scope → not moved.
        var role = await fx.Factory.CreateRoleAsync(System, (ResponsibilityPermissions.Manage(System), DataScope.Unit));
        var delegated = await fx.Factory.CreateUserAsync(unitId: w.DepartmentA, roles: role);
        var client = await fx.Factory.SignInAsync(delegated);
        var visible = await Responsibilities.ListAsync(client, System, "card-person");
        visible.Items.Select(r => r.Id).ShouldContain(a.Id);
        visible.Items.Select(r => r.Id).ShouldNotContain(b.Id);
        var cardB = (await Responsibilities.ListAsync(w.C(w.Pmo), System, "card-person")).Items.Single(r => r.Id == b.Id);
        var refused = await Responsibilities.ReassignAsync(client, System, "card-person", Responsibilities.Body([cardB], w.OwnerA.EmployeeId!.Value, Reason));
        refused.Items.ShouldHaveSingleItem().ShouldBe(new ResponsibilityOutcomeDto(b.Id, ResponsibilityOutcomes.Skipped, ResponsibilityMessages.NotFound, null, null, null,
            null, null));
        (await Responsibilities.ListAsync(w.C(w.Pmo), System, "card-person")).Items.Single(r => r.Id == b.Id).Responsible!.Id.ShouldBe(w.OwnerB.EmployeeId!.Value);
        var cardA = visible.Items.Single(r => r.Id == a.Id);
        (await Responsibilities.ReassignAsync(client, System, "card-person", Responsibilities.Body([cardA], w.ManagerA.EmployeeId!.Value, Reason))).Moved.ShouldBe(1);

        // A vendor (Wasel) principal is refused by the service whatever reaches it.
        var vendor = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()), new Claim(StrategyClaims.PrincipalType, StrategyClaims.VendorPrincipal)], "wasel"));
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Wasel, vendor);
        var service = scope.ServiceProvider.GetRequiredService<ResponsibilityService>();
        (await service.KindsAsync(System, CancellationToken.None)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await service.ReassignAsync(System, "card-person", new ResponsibilityChangeInput([new(a.Id, 1)], w.OwnerB.EmployeeId, Reason), CancellationToken.None))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }
}
