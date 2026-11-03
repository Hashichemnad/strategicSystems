using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Workflow;

/// <summary>The seeded sample chain (section manager when Q-P4 applies → department director → item approver when amount &gt; 100 000).</summary>
public sealed class ApprovalFlowTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Item_follows_the_chain_and_skips_the_steps_whose_condition_is_false()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var item = await world.CreateItemAsync(5000m);
        item.ApprovalStatus.ShouldBe("draft");

        var submitted = await world.SubmitOkAsync(item, "please approve");
        submitted.ApprovalStatus.ShouldBe("pending");

        // Q-P4: the section has its own manager, distinct from the director and the submitter → section manager first.
        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        ApprovalWorld.CurrentStep(panel).ShouldBe("section-manager");
        panel.Instances[0].MyTask.ShouldBeNull(); // the submitter is not an approver
        var step1 = await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, item.Id);
        step1.Actions.Select(a => a.Code).ShouldBe(["approve", "reject", "return"]);

        var first = await ApprovalWorld.ActOkAsync(world.SectionManagerClient, step1.TaskId, "approve");
        first.InstanceStatus.ShouldBe(ApprovalInstanceStatuses.InProgress);
        first.NextStepNameEn.ShouldBe("Department director");

        var step2 = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        var last = await ApprovalWorld.ActOkAsync(world.DirectorClient, step2.TaskId, "approve", "fine");
        last.InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Approved); // amount 5 000 ≤ 100 000: the item approver step is skipped

        (await world.ItemAsync(item.Id)).ApprovalStatus.ShouldBe("approved");
        panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        panel.Instances[0].Steps.Select(s => (s.Code, s.State)).ShouldBe(
            [("section-manager", StepStates.Done), ("department-director", StepStates.Done), ("item-approver", StepStates.Skipped)]);
        panel.Instances[0].History.Select(h => h.Event).Reverse().ShouldBe(
            ["submitted", "step-entered", "approved", "step-entered", "approved", "step-skipped", "completed"]);
        panel.Instances[0].Steps[1].ActedBy!.UserId.ShouldBe(world.Director.Id);
        panel.Instances[0].Steps[1].Comment.ShouldBe("fine");
    }

    [Fact]
    public async Task Section_manager_step_is_skipped_when_q_p4_does_not_apply()
    {
        // Submitter placed directly in the department: no section on the path → straight to the department director.
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync(200_000m);
        await world.SubmitOkAsync(item);

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        ApprovalWorld.CurrentStep(panel).ShouldBe("department-director");
        panel.Instances[0].Steps[0].State.ShouldBe(StepStates.Skipped);
        var skipped = panel.Instances[0].History.Single(h => h.Event == "step-skipped");
        skipped.Data!.Value.GetProperty("reason").GetString().ShouldBe("condition");
    }

    [Fact]
    public async Task Section_manager_submitting_skips_their_own_step()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        await TestAccess.GrantRoleAsync(world.Factory, world.SectionManager.Id, TestIdentity.SampleEditor);
        var managerClient = await world.Factory.SignInAsync(world.SectionManager);
        var item = await world.CreateItemAsync(10m, managerClient);
        await world.SubmitAsync(item, client: managerClient);

        var panel = await ApprovalWorld.PanelAsync(managerClient, item.Id);
        ApprovalWorld.CurrentStep(panel).ShouldBe("department-director");
    }

    [Fact]
    public async Task Return_for_correction_and_resubmission_link_the_attempts()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync(100m);
        await world.SubmitOkAsync(item);
        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);

        // Return needs a comment.
        await (await ApprovalWorld.ActAsync(world.DirectorClient, task.TaskId, "return")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var returned = await ApprovalWorld.ActOkAsync(world.DirectorClient, task.TaskId, "return", "add the cost breakdown");
        returned.InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Returned);
        var corrected = await world.ItemAsync(item.Id);
        corrected.ApprovalStatus.ShouldBe("returned");

        // My Work of the submitter lists the item to correct; status says "returned".
        var work = (await world.SubmitterClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == item.Id && i.MessageKey == "sample.myWork.returned");
        var status = (await world.SubmitterClient.GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/sample.item/{item.Id}"))!;
        status.MessageKey.ShouldBe("sample.currentStatus.returned");
        status.NextActions.Single().Code.ShouldBe("resubmit");

        // Correct and resubmit: a new attempt linked to the returned one.
        var updated = await world.SubmitterClient.PutAsJsonAsync($"{ApprovalWorld.Items}/{item.Id}",
            new { nameAr = "بند مصحح", nameEn = "Corrected item", amount = 120m, version = corrected.Version });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        await world.SubmitOkAsync((await updated.Content.ReadFromJsonAsync<SampleItemDto>())!, "corrected");

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        panel.Instances.Count.ShouldBe(2);
        panel.Instances[0].Attempt.ShouldBe(2);
        panel.Instances[1].Status.ShouldBe(ApprovalInstanceStatuses.Returned);
        panel.Instances[1].Steps[1].State.ShouldBe(StepStates.Returned);
        panel.Instances[0].History[^1].Event.ShouldBe("resubmitted");
        panel.Instances[0].History[^1].Data!.Value.GetProperty("previousInstanceId").GetGuid().ShouldBe(panel.Instances[1].Id);

        var second = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        (await ApprovalWorld.ActOkAsync(world.DirectorClient, second.TaskId, "approve")).InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Approved);
        (await world.ItemAsync(item.Id)).ApprovalStatus.ShouldBe("approved");
    }

    [Fact]
    public async Task Rejection_needs_a_comment_and_is_final()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);

        var missing = await ApprovalWorld.ActAsync(world.DirectorClient, task.TaskId, "reject", "  ");
        var problem = await missing.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        problem.GetProperty("errors")[0].GetProperty("messageKey").GetString().ShouldBe("workflow.errors.commentRequired");
        (await ApprovalWorld.ActAsync(world.DirectorClient, task.TaskId, "escalate", "x"))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await ApprovalWorld.ActOkAsync(world.DirectorClient, task.TaskId, "reject", "out of scope")).InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Rejected);
        var rejected = await world.ItemAsync(item.Id);
        rejected.ApprovalStatus.ShouldBe("rejected");
        await (await world.SubmitAsync(rejected)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "sample.item.notSubmittable");

        // A decided task cannot be decided again.
        await (await ApprovalWorld.ActAsync(world.DirectorClient, task.TaskId, "approve")).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "workflow.task.closed");
    }

    [Fact]
    public async Task Someone_outside_the_chain_cannot_act()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);

        // Can read the item (sample viewer) but is not an approver → 403.
        var reader = await api.Factory.CreateSignedInClientAsync(roles: "sample.viewer");
        await (await ApprovalWorld.ActAsync(reader, task.TaskId, "approve")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "workflow.notAnApprover");
        (await ApprovalWorld.PanelAsync(reader, item.Id)).Instances[0].MyTask.ShouldBeNull();

        // Cannot even see the item → 404, for the task and for the approval panel.
        var outsider = await api.Factory.CreateSignedInClientAsync();
        (await ApprovalWorld.ActAsync(outsider, task.TaskId, "approve")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/workflow/subjects/sample.item/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.GetAsync($"/api/v1/workflow/status/sample.item/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The submitter cannot approve their own request either.
        await (await ApprovalWorld.ActAsync(world.SubmitterClient, task.TaskId, "approve")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "workflow.notAnApprover");
        (await world.ItemAsync(item.Id)).ApprovalStatus.ShouldBe("pending");
    }

    [Fact]
    public async Task Approver_gets_read_rights_on_the_record_while_the_task_is_open()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync();
        await TestAccess.GrantSystemAccessAsync(world.Factory, "sample", "user", world.Director.Id);
        var directorClient = await world.Factory.SignInAsync(world.Director);
        // System access but no read permission: refused before the approval starts.
        (await directorClient.GetAsync($"{ApprovalWorld.Items}/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await world.SubmitOkAsync(item);
        (await directorClient.GetAsync($"{ApprovalWorld.Items}/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var status = (await directorClient.GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/sample.item/{item.Id}"))!;
        status.MessageKey.ShouldBe("workflow.currentStatus.waitingStep");
        status.NextActions.Select(a => a.Code).ShouldBe(["approve", "reject", "return"]);
        status.Actors.ShouldContain(a => a.UserId == world.Director.Id);

        var task = await ApprovalWorld.MyTaskAsync(directorClient, item.Id);
        await ApprovalWorld.ActOkAsync(directorClient, task.TaskId, "approve");
        (await directorClient.GetAsync($"{ApprovalWorld.Items}/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // rights ended with the task
    }

    [Fact]
    public async Task Delegate_acts_on_behalf_and_both_names_are_recorded()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var deputy = await world.Factory.CreateUserAsync(unitId: world.DepartmentId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var delegation = await world.DirectorClient.PostAsJsonAsync("/api/v1/org/delegations",
            new { delegateEmployeeId = deputy.EmployeeId, fromDate = today, toDate = today.AddDays(5), scope = new[] { "sample" }, reason = "leave" });
        delegation.IsSuccessStatusCode.ShouldBeTrue(await delegation.Content.ReadAsStringAsync());
        var deputyClient = await world.Factory.SignInAsync(deputy);

        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);

        var work = (await deputyClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        var pending = work.Approvals.Single(a => a.ResourceId == item.Id);
        pending.OnBehalfOf!.UserId.ShouldBe(world.Director.Id);
        work.Counts.Delegated.ShouldBeGreaterThanOrEqualTo(1);

        var task = await ApprovalWorld.MyTaskAsync(deputyClient, item.Id);
        task.SelfAssigned.ShouldBeFalse();
        task.OnBehalfOf.Single().UserId.ShouldBe(world.Director.Id);
        await ApprovalWorld.ActOkAsync(deputyClient, task.TaskId, "approve", "approved during leave");

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        var step = panel.Instances[0].Steps.Single(s => s.Code == "department-director");
        step.ActedBy!.UserId.ShouldBe(deputy.Id);
        step.OnBehalfOf!.UserId.ShouldBe(world.Director.Id);
        var approved = panel.Instances[0].History.Single(h => h.Event == "approved");
        approved.Actor!.UserId.ShouldBe(deputy.Id);
        approved.OnBehalfOf!.UserId.ShouldBe(world.Director.Id);

        // The audit trail carries "on behalf of" on the task change, the approval event and the item's own change.
        await using var scope = TestAccess.SystemScope(world.Factory);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
        var taskEntries = await audit.HistoryAsync("workflow.task", task.TaskId.ToString("D"), new PageRequest(1, 200));
        taskEntries.Items.ShouldContain(e => e.Field == "Status" && e.NewValue == "completed" && e.UserId == deputy.Id && e.OnBehalfOfId == world.Director.Id);
        var itemEntries = await audit.HistoryAsync("sample.item", item.Id.ToString("D"), new PageRequest(1, 200));
        itemEntries.Items.ShouldContain(e => e.Field == "ApprovalStatus" && e.NewValue == "approved" && e.OnBehalfOfId == world.Director.Id);
        var events = await audit.SearchAsync(new AuditFilter(Action: "workflow.task.approve", ExactAction: true, RecordId: panel.Instances[0].Id.ToString("D")),
            new PageRequest(1, 10));
        events.Items.Single().OnBehalfOfId.ShouldBe(world.Director.Id);

        // The delegator is told someone acted for them.
        var notified = await WaitForNotificationAsync(world.DirectorClient, "workflow.actedOnBehalf");
        notified.Link.ShouldBe($"/sample/items/{item.Id}");
    }

    [Fact]
    public async Task A_submitter_cannot_approve_their_own_request_as_a_delegate()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await world.DirectorClient.PostAsJsonAsync("/api/v1/org/delegations",
            new { delegateEmployeeId = world.Submitter.EmployeeId, fromDate = today, toDate = today.AddDays(2), reason = "leave" }))
            .IsSuccessStatusCode.ShouldBeTrue();
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);

        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        (await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id)).Instances[0].MyTask.ShouldBeNull();
        await (await ApprovalWorld.ActAsync(world.SubmitterClient, task.TaskId, "approve", onBehalfOf: world.Director.Id))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "workflow.notAnApprover");
        var work = (await world.SubmitterClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.Approvals.ShouldNotContain(a => a.ResourceId == item.Id);
    }

    [Fact]
    public async Task Every_action_is_audited()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        await ApprovalWorld.ActOkAsync(world.DirectorClient, task.TaskId, "return", "fix it");
        var returned = await world.ItemAsync(item.Id);
        await world.SubmitOkAsync(returned);
        var again = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        await ApprovalWorld.ActOkAsync(world.DirectorClient, again.TaskId, "approve");

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        await using var scope = TestAccess.SystemScope(world.Factory);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
        async Task<IReadOnlyList<AuditEntryDto>> EventsAsync(Guid instanceId) =>
            (await audit.SearchAsync(new AuditFilter(Action: "workflow", RecordId: instanceId.ToString("D"), Kind: AuditKinds.Event), new PageRequest(1, 50))).Items;

        (await EventsAsync(panel.Instances[1].Id)).Select(e => e.Action).Order().ShouldBe(["workflow.instance.submit", "workflow.task.return"]);
        (await EventsAsync(panel.Instances[0].Id)).Select(e => e.Action).Order().ShouldBe(["workflow.instance.resubmit", "workflow.task.approve"]);
        var returnEvent = (await EventsAsync(panel.Instances[1].Id)).Single(e => e.Action == "workflow.task.return");
        returnEvent.UserId.ShouldBe(world.Director.Id);
        returnEvent.Reason.ShouldBe("fix it");
        returnEvent.Channel.ShouldBe("ui");

        // Field-level changes of the instance (status) and the task (decision) are in the trail too.
        var instanceChanges = await audit.HistoryAsync("workflow.instance", panel.Instances[0].Id.ToString("D"), new PageRequest(1, 100));
        instanceChanges.Items.ShouldContain(e => e.Field == "Status" && e.NewValue == ApprovalInstanceStatuses.Approved);
    }

    [Fact]
    public async Task Repeating_a_decision_with_the_same_idempotency_key_does_not_approve_twice()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var item = await world.CreateItemAsync();
        var key = Guid.CreateVersion7().ToString("N");
        var first = await world.SubmitAsync(item, key: key);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayedSubmit = await world.SubmitAsync(item, key: key);
        replayedSubmit.StatusCode.ShouldBe(HttpStatusCode.OK);
        replayedSubmit.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await replayedSubmit.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());

        var task = await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, item.Id);
        var approveKey = Guid.CreateVersion7().ToString("N");
        var approved = await ApprovalWorld.ActAsync(world.SectionManagerClient, task.TaskId, "approve", key: approveKey);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await ApprovalWorld.ActAsync(world.SectionManagerClient, task.TaskId, "approve", key: approveKey);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.Headers.Contains("Idempotent-Replayed").ShouldBeTrue();

        // Same key, different request → refused, nothing runs.
        await (await ApprovalWorld.ActAsync(world.SectionManagerClient, task.TaskId, "reject", "no", key: approveKey))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "idempotency.keyReused");

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        panel.Instances.Count.ShouldBe(1);
        panel.Instances[0].History.Count(h => h.Event == "approved").ShouldBe(1);
        panel.Instances[0].History.Count(h => h.Event == "submitted").ShouldBe(1);
        ApprovalWorld.CurrentStep(panel).ShouldBe("department-director");

        // A failed request is not remembered: the same key may run once the request is valid.
        var other = await world.CreateItemAsync();
        var badKey = Guid.CreateVersion7().ToString("N");
        (await world.SubmitAsync(other with { Version = other.Version + 7 }, key: badKey)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await world.SubmitAsync(other, key: badKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Approvers_are_notified_and_my_work_lists_the_pending_approval_with_a_deep_link()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory, withSection: false);
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);

        var notification = await WaitForNotificationAsync(world.DirectorClient, "workflow.taskAssigned");
        notification.Link.ShouldBe($"/sample/items/{item.Id}");
        notification.TitleEn.ShouldContain("Sample item approval");
        notification.TitleAr.ShouldContain("اعتماد البند التجريبي");

        var work = (await world.DirectorClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        var entry = work.Approvals.Single(a => a.ResourceId == item.Id);
        entry.Link.ShouldBe($"/sample/items/{item.Id}");
        entry.StepNameEn.ShouldBe("Department director");
        entry.SystemCode.ShouldBe("sample");
        entry.Overdue.ShouldBeFalse();
        entry.OnBehalfOf.ShouldBeNull();
        work.Counts.PendingApprovals.ShouldBe(work.Approvals.Count);

        var mine = (await world.SubmitterClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        mine.Submissions.ShouldContain(s => s.InstanceId != Guid.Empty && s.Link == $"/sample/items/{item.Id}" && s.StepNameEn == "Department director");

        var task = await ApprovalWorld.MyTaskAsync(world.DirectorClient, item.Id);
        await ApprovalWorld.ActOkAsync(world.DirectorClient, task.TaskId, "return", "please fix");
        var back = await WaitForNotificationAsync(world.SubmitterClient, "workflow.returned");
        back.Link.ShouldBe($"/sample/items/{item.Id}");
    }

    internal static async Task<NotificationDto> WaitForNotificationAsync(HttpClient client, string templateKey)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var page = await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications/mine?pageSize=50");
            if (page!.Items.FirstOrDefault(n => n.TemplateKey == templateKey) is { } found)
            {
                return found;
            }

            await Task.Delay(250);
        }

        throw new ShouldAssertException($"No in-app notification '{templateKey}' arrived.");
    }
}
