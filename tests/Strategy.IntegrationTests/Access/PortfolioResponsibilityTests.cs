using System.Net.Http.Json;
using Strategy.IntegrationTests.Portfolio;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Access;

namespace Strategy.IntegrationTests.Access;

/// <summary>
/// Portfolio → Settings → Responsibilities (ADR 0168): the project manager changes only through an approved Ownership change request (SPEC §1.9),
/// so the bulk action creates and submits one change request per selected project and moves nothing directly.
/// </summary>
public sealed class PortfolioResponsibilityTests(ProjectFixture fx) : IClassFixture<ProjectFixture>
{
    private const string System = PortfolioPermissions.SystemCode;
    private const string Reason = "PM moves to another programme";

    [Fact]
    public async Task A_pm_change_follows_the_change_request_rule_for_the_selected_projects_only()
    {
        var w = await PortfolioWorld.CreateAsync(fx.Factory);
        var p1 = await ProjectLifecycleTests.ApprovedProjectAsync(w, "Responsibility platform");
        var p2 = await ProjectLifecycleTests.ApprovedProjectAsync(w, "Responsibility portal");
        foreach (var project in new[] { p1, p2 })
        {
            await PortfolioWorld.OkAsync<ProjectFullDto>(w.C(w.Requester).PostAsJsonAsync($"{PortfolioWorld.Base}/projects/{project.Id}/take-ownership",
                new { version = (await ProjectLifecycleTests.Get(w, project.Id)).Version }));
        }

        var office = w.C(w.PmOffice);
        var kinds = (await office.GetFromJsonAsync<Strategy.Platform.Access.Application.ResponsibilityKindsDto>(Responsibilities.Base(System)))!;
        kinds.Kinds.Single(k => k.Code == "project-manager").NoteKey.ShouldBe("responsibilities.notes.changeRequest");
        var rows = await Responsibilities.ListAsync(office, System, "project-manager", $"&personId={w.Requester.EmployeeId}");
        var selected = rows.Items.Where(r => r.Id == p1.Id).ToList();
        selected.ShouldHaveSingleItem();
        rows.Items.Select(r => r.Id).ShouldContain(p2.Id);

        // The preview says a change request is needed; the reassignment creates and submits it, nothing moves yet.
        var preview = (await Responsibilities.PreviewAsync(office, System, "project-manager", Responsibilities.Body(selected, w.DepartmentViewer.EmployeeId!.Value)))
            .ShouldHaveSingleItem();
        (preview.Outcome, preview.MessageKey).ShouldBe((ResponsibilityOutcomes.ChangeRequest, ResponsibilityMessages.NeedsChangeRequest));
        var done = await Responsibilities.ReassignAsync(office, System, "project-manager", Responsibilities.Body(selected, w.DepartmentViewer.EmployeeId!.Value, Reason));
        (done.Moved, done.ChangeRequests, done.Skipped).ShouldBe((0, 1, 0));
        var item = done.Items.ShouldHaveSingleItem();
        item.Reference.ShouldBe($"{p1.Number}-CR-1");
        item.MessageKey.ShouldBe(ResponsibilityMessages.ChangeRequestSubmitted);

        (await ProjectLifecycleTests.Get(w, p1.Id)).Pm!.Id.ShouldBe(w.Requester.EmployeeId!.Value);
        var requests = (await office.GetFromJsonAsync<List<ChangeRequestDto>>($"{PortfolioWorld.Base}/projects/{p1.Id}/change-requests"))!;
        var cr = requests.ShouldHaveSingleItem();
        (cr.Type, cr.Status, cr.NewPm!.Id, cr.Reason).ShouldBe((ChangeTypes.Ownership, ChangeStatuses.UnderReview, w.DepartmentViewer.EmployeeId!.Value, Reason));
        (await office.GetFromJsonAsync<List<ChangeRequestDto>>($"{PortfolioWorld.Base}/projects/{p2.Id}/change-requests"))!.ShouldBeEmpty();

        // A second attempt while that change request is under review is blocked (one approval at a time).
        var again = await Responsibilities.ListAsync(office, System, "project-manager", $"&personId={w.Requester.EmployeeId}");
        var blocked = (await Responsibilities.PreviewAsync(office, System, "project-manager",
            Responsibilities.Body(again.Items.Where(r => r.Id == p1.Id), w.DepartmentViewer.EmployeeId!.Value))).ShouldHaveSingleItem();
        (blocked.Outcome, blocked.MessageKey).ShouldBe((ResponsibilityOutcomes.Blocked, "responsibilities.outcome.approvalRunning"));
    }
}
