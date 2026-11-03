using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.AI;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>Task Management end to end (docs/design/tasks.md §14) on a real database.</summary>
public sealed class TasksApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Base = TasksWorld.Base;

    [Fact]
    public async Task Multi_assign_to_three_people_creates_three_independent_tasks_and_one_group()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var created = await TasksWorld.CreateAsync(w.ManagerClient,
            TasksWorld.NewTask("Prepare the quarterly report", w.A.EmployeeId, w.B.EmployeeId, w.C.EmployeeId));

        created.Tasks.Count.ShouldBe(3);
        created.GroupId.ShouldNotBeNull();
        created.Tasks.Select(t => t.Number).Distinct().Count().ShouldBe(3);
        created.Tasks.ShouldAllBe(t => t.GroupId == created.GroupId && t.Status == TaskStatuses.Assigned && t.Template == TaskTemplates.Full);
        created.Tasks.Select(t => t.Assignee.EmployeeId).ShouldBe([w.A.EmployeeId!.Value, w.B.EmployeeId!.Value, w.C.EmployeeId!.Value], ignoreOrder: true);

        Guid TaskOf(TestUser user) => created.Tasks.Single(t => t.Assignee.EmployeeId == user.EmployeeId).Id;

        // Each assignee sees only their own task and acts independently.
        (await TasksWorld.ListAsync(w.AClient, "view=assigned")).Items.Select(t => t.Id).ShouldBe([TaskOf(w.A)]);
        (await w.AClient.GetAsync($"{Base}/{TaskOf(w.B)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await TasksWorld.ActOkAsync(w.AClient, TaskOf(w.A), TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.BClient, TaskOf(w.B), TaskActions.Reject, new { reason = "Not my area" });
        await TasksWorld.ActOkAsync(w.CClient, TaskOf(w.C), TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, TaskOf(w.A), TaskActions.Submit, new { comment = "Done, see annex" });
        await TasksWorld.ActOkAsync(w.ManagerClient, TaskOf(w.A), TaskActions.Approve, new { feedbackPoint = 5, comment = "Great" });

        var group = (await w.ManagerClient.GetFromJsonAsync<GroupDto>($"{Base}/groups/{created.GroupId}"))!;
        group.Total.ShouldBe(3);
        group.Finished.ShouldBe(1); // "1 of 3 approved"
        group.Complete.ShouldBeFalse();
        group.CanManage.ShouldBeTrue();
        group.Members.Single(m => m.Task.Id == TaskOf(w.B)).Task.Status.ShouldBe(TaskStatuses.Rejected);

        // Group actions: message all, extend the due date of open tasks, cancel all open tasks.
        (await w.ManagerClient.PostAsJsonAsync($"{Base}/groups/{created.GroupId}/message", new { text = "Deadline is firm." })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var comments = (await w.CClient.GetFromJsonAsync<List<CommentDto>>($"{Base}/{TaskOf(w.C)}/comments"))!;
        comments.ShouldContain(c => c.Kind == CommentKinds.GroupMessage && c.Text == "Deadline is firm.");
        var extended = await w.ManagerClient.PostAsJsonAsync($"{Base}/groups/{created.GroupId}/extend-due", new { dueDate = "2027-01-31" });
        extended.StatusCode.ShouldBe(HttpStatusCode.OK, await extended.Content.ReadAsStringAsync());
        (await TasksWorld.GetAsync(w.CClient, TaskOf(w.C))).DueDate.ShouldBe(new DateOnly(2027, 1, 31));
        (await TasksWorld.GetAsync(w.AClient, TaskOf(w.A))).DueDate.ShouldBeNull(); // approved: not moved

        // An assignee cannot manage the group; then the creator cancels the rest.
        (await w.CClient.PostAsJsonAsync($"{Base}/groups/{created.GroupId}/cancel", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var cancelled = await w.ManagerClient.PostAsJsonAsync($"{Base}/groups/{created.GroupId}/cancel", new { reason = "Scope changed" });
        var after = (await cancelled.Content.ReadFromJsonAsync<GroupDto>())!;
        after.Cancelled.ShouldBe(2);
        after.Complete.ShouldBeTrue(); // every non-cancelled member approved
        await ApprovalFlowTests.WaitForNotificationAsync(w.CClient, TaskNotifications.Cancelled.Key);
    }

    [Fact]
    public async Task Full_lifecycle_with_pause_restart_return_and_creator_approval_with_feedback()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var id = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Draft the policy", w.A.EmployeeId))).Tasks.Single().Id;
        await ApprovalFlowTests.WaitForNotificationAsync(w.AClient, TaskNotifications.Assigned.Key);

        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Accept);
        var paused = await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Pause, new { reason = "Waiting for legal input" });
        paused.Status.ShouldBe(TaskStatuses.OnHold);
        paused.Indicator.ShouldBe(TaskIndicators.OnHold);
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Start);
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Submit, new { comment = "First version" });
        (await TasksWorld.ActAsync(w.ManagerClient, await TasksWorld.GetAsync(w.ManagerClient, id), TaskActions.Return, new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // a comment is required
        await TasksWorld.ActOkAsync(w.ManagerClient, id, TaskActions.Return, new { comment = "Add the annex" });
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Submit, new { comment = "With annex" });

        // The assignee cannot approve their own task.
        (await TasksWorld.ActAsync(w.AClient, await TasksWorld.GetAsync(w.AClient, id), TaskActions.Approve, new { feedbackPoint = 5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var approved = await TasksWorld.ActOkAsync(w.ManagerClient, id, TaskActions.Approve, new { feedbackPoint = 4, comment = "Good work" });
        approved.Status.ShouldBe(TaskStatuses.Approved);
        approved.FeedbackPoint.ShouldBe(4);
        approved.ApprovedAt.ShouldNotBeNull();
        approved.Actions.ShouldBeEmpty();

        var timeline = (await w.ManagerClient.GetFromJsonAsync<TimelineDto>($"{Base}/{id}/timeline"))!;
        timeline.Events.Select(e => e.Event).ShouldBe(
            [TaskEvents.Created, TaskActions.Accept, TaskActions.Pause, TaskActions.Start, TaskActions.Submit, TaskActions.Return, TaskActions.Submit, TaskActions.Approve]);
        timeline.Events.Single(e => e.Event == TaskActions.Pause).Comment.ShouldBe("Waiting for legal input");
        timeline.Events.Skip(1).ShouldAllBe(e => e.SecondsSincePrevious != null);
        timeline.Timesheet.Periods.Count.ShouldBe(3);
        timeline.Timesheet.Periods[0].HoldReason.ShouldBe("Waiting for legal input");
        timeline.Timesheet.Periods.ShouldAllBe(p => p.Number!.StartsWith("TS-") && p.EndedAt != null);
        timeline.Timesheet.Running.ShouldBeFalse();
        timeline.Timesheet.TotalSeconds.ShouldBe(timeline.Timesheet.Periods.Sum(p => p.DurationSeconds));

        await ApprovalFlowTests.WaitForNotificationAsync(w.AClient, TaskNotifications.Approved.Key);
        await ApprovalFlowTests.WaitForNotificationAsync(w.ManagerClient, TaskNotifications.Submitted.Key);

        // Audited: the approval event and the field changes, readable on the History tab.
        var history = (await w.ManagerClient.GetFromJsonAsync<PagedResult<JsonElement>>($"{Base}/{id}/audit?pageSize=200"))!;
        history.Items.ShouldContain(e => e.GetProperty("action").GetString() == "tasks.task.approve");
        history.Items.ShouldContain(e => e.GetProperty("field").GetString() == "FeedbackPoint");

        // Performance input for the employee: visible to them and their manager, not to an outsider.
        var performance = (await w.ManagerClient.GetFromJsonAsync<PerformanceDto>($"{Base}/performance/{w.A.EmployeeId}"))!;
        performance.Completed.ShouldBe(1);
        performance.AverageFeedback.ShouldBe(4);
        (await w.OutsiderClient.GetAsync($"{Base}/performance/{w.A.EmployeeId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_creator_approves_even_an_administrator_with_every_task_permission_is_refused()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var adminUser = await api.Factory.CreateUserAsync(admin: true, roles: "tasks.administrator");
        var admin = await api.Factory.SignInAsync(adminUser);
        var id = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Approve me", w.A.EmployeeId))).Tasks.Single().Id;
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Submit);

        var asAdmin = await TasksWorld.GetAsync(admin, id); // the administrator sees it (All scope)…
        asAdmin.Actions.ShouldBeEmpty();
        (await TasksWorld.ActAsync(admin, asAdmin, TaskActions.Approve, new { feedbackPoint = 5 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // …but cannot approve
        (await TasksWorld.ActAsync(w.BClient, asAdmin, TaskActions.Approve, new { feedbackPoint = 5 })).StatusCode.ShouldBe(HttpStatusCode.NotFound); // a colleague cannot see it

        // The manager (Team scope) of the assignee sees the task in the team view but may not act on it either when someone else created it.
        var other = await api.Factory.CreateUserAsync(unitId: w.Manager.UnitId);
        var otherClient = await api.Factory.SignInAsync(other);
        var byOther = (await TasksWorld.CreateAsync(otherClient, TasksWorld.NewTask("Peer request", w.A.EmployeeId))).Tasks.Single().Id;
        (await TasksWorld.ListAsync(w.ManagerClient, "view=team")).Items.ShouldContain(t => t.Id == byOther);
        (await TasksWorld.ActAsync(w.ManagerClient, await TasksWorld.GetAsync(w.ManagerClient, byOther), TaskActions.Cancel, new { reason = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Feedback outside the scale is refused; the creator approves.
        (await TasksWorld.ActAsync(w.ManagerClient, asAdmin, TaskActions.Approve, new { feedbackPoint = 9 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await TasksWorld.ActOkAsync(w.ManagerClient, id, TaskActions.Approve, new { feedbackPoint = 5 });
    }

    [Fact]
    public async Task Sub_tasks_only_while_the_parent_is_in_progress_or_on_hold_and_they_block_approval()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var parent = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Organise the workshop", w.A.EmployeeId))).Tasks.Single().Id;
        object Sub(string title) => new { titleEn = title, assigneeEmployeeId = w.B.EmployeeId };

        // Assigned: not yet.
        (await w.AClient.PostAsJsonAsync($"{Base}/{parent}/subtasks", Sub("Book the room"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await TasksWorld.ActOkAsync(w.AClient, parent, TaskActions.Accept);

        // In progress: the assignee may; the creator may not (only the assignee creates sub-tasks).
        (await w.ManagerClient.PostAsJsonAsync($"{Base}/{parent}/subtasks", Sub("x"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var response = await w.AClient.PostAsJsonAsync($"{Base}/{parent}/subtasks", Sub("Book the room"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sub = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        sub.Parent!.Id.ShouldBe(parent);
        sub.Creator.EmployeeId.ShouldBe(w.A.EmployeeId!.Value);
        sub.Assignee.EmployeeId.ShouldBe(w.B.EmployeeId!.Value);

        // On hold: still allowed.
        await TasksWorld.ActOkAsync(w.AClient, parent, TaskActions.Pause, new { reason = "Waiting for the venue" });
        (await w.AClient.PostAsJsonAsync($"{Base}/{parent}/subtasks", Sub("Order catering"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await TasksWorld.ActOkAsync(w.AClient, parent, TaskActions.Start);
        await TasksWorld.ActOkAsync(w.AClient, parent, TaskActions.Submit);

        // Submitted: no more sub-tasks; approval blocked while sub-tasks are open.
        (await w.AClient.PostAsJsonAsync($"{Base}/{parent}/subtasks", Sub("Late"))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var blocked = await TasksWorld.ActAsync(w.ManagerClient, await TasksWorld.GetAsync(w.ManagerClient, parent), TaskActions.Approve, new { feedbackPoint = 4 });
        await blocked.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "tasks.task.openSubtasks");

        // The parent's creator sees the sub-tasks on the parent; the sub-task's creator (A) approves it.
        var detail = await TasksWorld.GetAsync(w.AClient, parent);
        detail.Subtasks.Count.ShouldBe(2);
        foreach (var s in detail.Subtasks)
        {
            await TasksWorld.ActOkAsync(w.BClient, s.Id, TaskActions.Accept);
            await TasksWorld.ActOkAsync(w.BClient, s.Id, TaskActions.Submit);
            await TasksWorld.ActOkAsync(w.AClient, s.Id, TaskActions.Approve, new { feedbackPoint = 3 });
        }

        await TasksWorld.ActOkAsync(w.ManagerClient, parent, TaskActions.Approve, new { feedbackPoint = 4 });
    }

    [Fact]
    public async Task Follow_up_employee_comments_and_uploads_but_cannot_submit_and_secretaries_add_follow_ups_in_their_unit()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var id = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Collect evidence", w.A.EmployeeId))).Tasks.Single().Id;
        await TasksWorld.ActOkAsync(w.AClient, id, TaskActions.Accept);

        // Only the assignee (or a secretary of the unit) adds follow-ups: B may not.
        (await w.BClient.PostAsJsonAsync($"{Base}/{id}/follow-ups", new { employeeId = w.C.EmployeeId })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var added = await w.AClient.PostAsJsonAsync($"{Base}/{id}/follow-ups", new { employeeId = w.B.EmployeeId, canUpload = true });
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        await ApprovalFlowTests.WaitForNotificationAsync(w.BClient, TaskNotifications.FollowUpAdded.Key);

        // B now reads, comments (mentioning A) and uploads.
        var task = await TasksWorld.GetAsync(w.BClient, id);
        task.Capabilities.CanComment.ShouldBeTrue();
        task.Capabilities.CanUpload.ShouldBeTrue();
        task.Actions.ShouldBeEmpty();
        (await TasksWorld.ListAsync(w.BClient, "view=follow-up")).Items.Select(t => t.Id).ShouldBe([id]);
        var comment = await w.BClient.PostAsJsonAsync($"{Base}/{id}/comments", new { text = "@A the figures are attached", mentions = new[] { w.A.EmployeeId } });
        comment.StatusCode.ShouldBe(HttpStatusCode.OK, await comment.Content.ReadAsStringAsync());
        await ApprovalFlowTests.WaitForNotificationAsync(w.AClient, TaskNotifications.Mentioned.Key);
        (await w.BClient.PostAsJsonAsync($"{Base}/{id}/comments", new { text = "hi", mentions = new[] { w.Outsider.EmployeeId } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest); // mentions are limited to participants
        var upload = await UploadAsync(w.BClient, id);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        // …but cannot submit, pause or approve.
        (await TasksWorld.ActAsync(w.BClient, task, TaskActions.Submit)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TasksWorld.ActAsync(w.BClient, task, TaskActions.Pause, new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A department secretary of the unit adds a follow-up employee (comment only) without being a participant.
        var secretary = await api.Factory.CreateUserAsync(unitId: w.Manager.UnitId, roles: "tasks.secretary");
        var secretaryClient = await api.Factory.SignInAsync(secretary);
        var bySecretary = await secretaryClient.PostAsJsonAsync($"{Base}/{id}/follow-ups", new { employeeId = w.C.EmployeeId, canUpload = false });
        bySecretary.StatusCode.ShouldBe(HttpStatusCode.OK, await bySecretary.Content.ReadAsStringAsync());
        var asC = await TasksWorld.GetAsync(w.CClient, id);
        asC.Capabilities.CanComment.ShouldBeTrue();
        asC.Capabilities.CanUpload.ShouldBeFalse();
        (await UploadAsync(w.CClient, id)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Removing B ends their access.
        (await w.AClient.PostAsync($"{Base}/{id}/follow-ups/{w.B.EmployeeId}/remove", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await w.BClient.GetAsync($"{Base}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Secretary_creates_on_behalf_of_the_manager_who_then_approves()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        await SetUnitHeadAsync(w.Manager);
        var secretary = await api.Factory.CreateUserAsync(unitId: w.Manager.UnitId, roles: "tasks.secretary");
        var secretaryClient = await api.Factory.SignInAsync(secretary);
        var body = new
        {
            titleEn = "Prepare the board pack",
            typeCode = "manager-assignment",
            sourceCode = "dg-office",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
            onBehalfOfEmployeeId = w.Manager.EmployeeId,
        };
        var created = (await TasksWorld.CreateAsync(secretaryClient, body)).Tasks.Single();
        created.Creator.EmployeeId.ShouldBe(w.Manager.EmployeeId!.Value);
        (await TasksWorld.GetAsync(secretaryClient, created.Id)).EnteredBy!.EmployeeId.ShouldBe(secretary.EmployeeId!.Value);

        // Without the role, on-behalf is refused.
        var plain = await w.BClient.PostAsJsonAsync(Base, body);
        plain.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await TasksWorld.ActOkAsync(w.AClient, created.Id, TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, created.Id, TaskActions.Submit);
        (await TasksWorld.ActAsync(secretaryClient, await TasksWorld.GetAsync(secretaryClient, created.Id), TaskActions.Approve, new { feedbackPoint = 5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await TasksWorld.ActOkAsync(w.ManagerClient, created.Id, TaskActions.Approve, new { feedbackPoint = 5 });
    }

    [Fact]
    public async Task Confidential_task_is_hidden_from_everyone_but_its_participants_including_search_ask_and_mcp()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var adminUser = await api.Factory.CreateUserAsync(admin: true, roles: "tasks.administrator");
        var admin = await api.Factory.SignInAsync(adminUser);
        var created = await TasksWorld.CreateAsync(w.AClient, new
        {
            titleEn = "Zephyr salary review",
            typeCode = "manager-assignment",
            sourceCode = "other",
            confidential = true,
            assigneeEmployeeIds = new[] { w.B.EmployeeId },
        });
        var id = created.Tasks.Single().Id;
        var open = await TasksWorld.CreateAsync(w.AClient, TasksWorld.NewTask("Zephyr public note", w.B.EmployeeId));

        // Participants see it.
        (await TasksWorld.GetAsync(w.AClient, id)).Confidential.ShouldBeTrue();
        (await TasksWorld.GetAsync(w.BClient, id)).Id.ShouldBe(id);

        // The manager (Team scope over A and B) and an administrator (All scope) do not: not by id, not in lists, not in counts or the dashboard.
        foreach (var client in new[] { w.ManagerClient, admin })
        {
            (await client.GetAsync($"{Base}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"{Base}/{id}/timeline")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync($"{Base}/{id}/audit")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var list = await TasksWorld.ListAsync(client, "view=all&search=Zephyr&pageSize=50");
            list.Items.ShouldNotContain(t => t.Id == id);
            list.Items.ShouldContain(t => t.Id == open.Tasks[0].Id);
            (await client.GetAsync($"/api/v1/files/owners/tasks.task/{id}")).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        }

        // Search, Ask (record source) and MCP tools run through the same rule.
        await using (var scope = await UserScopeAsync(w.Manager))
        {
            var search = scope.ServiceProvider.GetServices<IRecordSearchSource>().Single(s => s.ResourceType == TaskItem.EntityType);
            var principal = scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
            (await search.SearchAsync("Zephyr", principal, 20, CancellationToken.None)).Select(h => h.Id).ShouldNotContain(id);
            (await search.SimilarAsync("Zephyr salary review", null, principal, 20, CancellationToken.None)).Select(h => h.Id).ShouldNotContain(id);
            var record = scope.ServiceProvider.GetServices<IAiRecordSource>().Single(s => s.ResourceType == TaskItem.EntityType);
            (await record.GetAsync(id, principal, CancellationToken.None)).ShouldBeNull();
            var tool = scope.ServiceProvider.GetServices<Strategy.Platform.AI.Tools.IAppTool>().Single(t => t.Definition.Name == "my_tasks");
            var result = await tool.InvokeAsync(JsonDocument.Parse("""{"view":"team","openOnly":true}""").RootElement, new(principal, "mcp", "en"), CancellationToken.None);
            JsonSerializer.Serialize(result.Data).ShouldNotContain(id.ToString());
            JsonSerializer.Serialize(result.Data).ShouldContain(open.Tasks[0].Id.ToString());
            var status = scope.ServiceProvider.GetServices<ICurrentStatusProvider>().Single(p => p.ResourceType == TaskItem.EntityType);
            (await status.GetAsync(id, CancellationToken.None)).ShouldBeNull();
        }

        await using (var scope = await UserScopeAsync(w.B))
        {
            var principal = scope.ServiceProvider.GetRequiredService<IRequestContext>().User;
            var search = scope.ServiceProvider.GetServices<IRecordSearchSource>().Single(s => s.ResourceType == TaskItem.EntityType);
            (await search.SearchAsync("Zephyr", principal, 20, CancellationToken.None)).Select(h => h.Id).ShouldContain(id);
        }
    }

    [Fact]
    public async Task Simple_workflow_for_personal_tasks_and_validation_and_assign_rules()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var personal = (await TasksWorld.CreateAsync(w.AClient, new
        {
            titleAr = "قراءة التقرير",
            typeCode = "personal",
            sourceCode = "other",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
            checklist = new[] { "الفصل الأول", "الفصل الثاني" },
        })).Tasks.Single();
        personal.Template.ShouldBe(TaskTemplates.Simple);
        personal.Status.ShouldBe(TaskStatuses.ToDo);
        await TasksWorld.ActOkAsync(w.AClient, personal.Id, TaskActions.Start);
        var detail = await TasksWorld.GetAsync(w.AClient, personal.Id);
        detail.Checklist.Count.ShouldBe(2);
        var tick = await w.AClient.PutAsJsonAsync($"{Base}/{personal.Id}/checklist/{detail.Checklist[0].Id}", new { done = true });
        (await tick.Content.ReadFromJsonAsync<TaskDto>())!.Checklist[0].Done.ShouldBeTrue();
        (await TasksWorld.ActOkAsync(w.AClient, personal.Id, TaskActions.Complete)).Status.ShouldBe(TaskStatuses.Done);

        // Validation: a title, known codes, at least one assignee.
        var invalid = await w.AClient.PostAsJsonAsync(Base, new { typeCode = "nope", sourceCode = "other", assigneeEmployeeIds = Array.Empty<Guid>() });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unknownType = await w.AClient.PostAsJsonAsync(Base, new { titleEn = "x", typeCode = "nope", sourceCode = "other", assigneeEmployeeIds = new[] { w.A.EmployeeId } });
        unknownType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Assign rule "direct reports": the manager may assign A, A may not assign B.
        await SaveSettingsAsync(s => s with { AssignScope = AssignScopes.DirectReports });
        try
        {
            await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Allowed", w.A.EmployeeId));
            (await w.AClient.PostAsJsonAsync(Base, TasksWorld.NewTask("Not allowed", w.B.EmployeeId))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            await TasksWorld.CreateAsync(w.AClient, TasksWorld.NewTask("Self is always fine", w.A.EmployeeId));
        }
        finally
        {
            await SaveSettingsAsync(s => s with { AssignScope = AssignScopes.Anyone });
        }
    }

    [Fact]
    public async Task Sample_item_shows_its_tasks_through_the_links_registry_and_outsiders_see_a_restricted_badge()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var owner = await api.Factory.CreateUserAsync(roles: TestIdentity.SampleEditor);
        var ownerClient = await api.Factory.SignInAsync(owner);
        var item = await ownerClient.PostAsJsonAsync("/api/v1/sample/items", new { nameEn = "Digital archive" });
        var itemId = (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The owner creates two tasks from the item (multi-assign); a person who cannot read the item cannot link to it.
        var created = await TasksWorld.CreateAsync(ownerClient, new
        {
            titleEn = "Scan the archive",
            typeCode = "project",
            sourceCode = "project",
            assigneeEmployeeIds = new[] { w.A.EmployeeId, w.B.EmployeeId },
            linkedType = "sample.item",
            linkedId = itemId,
        });
        (await w.OutsiderClient.PostAsJsonAsync(Base, new
        {
            titleEn = "Sneaky",
            typeCode = "project",
            sourceCode = "project",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
            linkedType = "sample.item",
            linkedId = Guid.CreateVersion7(),
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The item shows its tasks (the owner follows its linked items as an editor of the item) and the task shows the item's badge.
        var linked = await TasksWorld.ListAsync(ownerClient, $"linkedType=sample.item&linkedId={itemId}");
        linked.Items.Select(t => t.Id).ShouldBe(created.Tasks.Select(t => t.Id), ignoreOrder: true);
        var badge = (await TasksWorld.GetAsync(ownerClient, created.Tasks[0].Id)).Link!;
        badge.Visibility.ShouldBe("visible");
        badge.TitleEn.ShouldBe("Digital archive");
        badge.StatusCode.ShouldBe("draft");
        badge.Link.ShouldBe($"/sample/items/{itemId:D}");

        // A colleague of the assignees who cannot read the item sees only their own linked task, and the badge is restricted.
        (await TasksWorld.ListAsync(w.BClient, $"linkedType=sample.item&linkedId={itemId}")).Items.Count.ShouldBe(1);
        var restricted = (await TasksWorld.GetAsync(w.BClient, created.Tasks.Single(t => t.Assignee.EmployeeId == w.B.EmployeeId).Id)).Link!;
        restricted.Visibility.ShouldBe("restricted");
        restricted.TitleEn.ShouldBeNull();
        restricted.Link.ShouldBeNull();

        // The registry stores the links; the item's "linked to me" list shows the tasks the caller may read.
        var to = (await ownerClient.GetFromJsonAsync<JsonElement>($"/api/v1/links/to/sample.item/{itemId}?kind=task-able"))!;
        to.GetProperty("links").GetArrayLength().ShouldBe(2);
        (await w.OutsiderClient.GetAsync($"/api/v1/links/to/sample.item/{itemId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.OutsiderClient.GetAsync($"/api/v1/links/resolve?type=sample.item&id={itemId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var types = (await w.AClient.GetFromJsonAsync<List<JsonElement>>("/api/v1/links/types?capability=task-able"))!;
        types.ShouldContain(t => t.GetProperty("resourceType").GetString() == "sample.item");
    }

    [Fact]
    public async Task Mcp_task_tools_are_registered_with_write_tools_off_and_approve_never_on_by_default()
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetServices<Strategy.Platform.AI.Tools.IAppTool>().Select(t => t.Definition).Where(d => d.SystemCode == "tasks").ToList();
        tools.Select(t => t.Name).ShouldBe(["my_tasks", "create_task", "update_task_status", "add_task_comment", "approve_task"], ignoreOrder: true);
        tools.Single(t => t.Name == "my_tasks").DefaultEnabled.ShouldBeTrue();
        tools.Where(t => t.Kind != Strategy.Platform.AI.Tools.ToolKind.Read).ShouldAllBe(t => !t.DefaultEnabled);
        tools.Single(t => t.Name == "approve_task").Kind.ShouldBe(Strategy.Platform.AI.Tools.ToolKind.Approve);
    }

    [Fact]
    public async Task My_work_shows_tasks_to_accept_awaiting_approval_and_the_dashboard_counts_the_team()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var first = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Respond to audit", w.A.EmployeeId))).Tasks.Single().Id;
        var second = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Update the register", w.A.EmployeeId))).Tasks.Single().Id;
        await TasksWorld.ActOkAsync(w.AClient, second, TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, second, TaskActions.Submit);

        var aWork = (await w.AClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        aWork.ActionItems.ShouldContain(i => i.ResourceId == first && i.MessageKey == "tasks.myWork.toAccept");
        var managerWork = (await w.ManagerClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        managerWork.ActionItems.ShouldContain(i => i.ResourceId == second && i.MessageKey == "tasks.myWork.awaitingApproval");

        var counts = (await w.ManagerClient.GetFromJsonAsync<TaskCountsDto>($"{Base}/counts"))!;
        counts.AwaitingMyApproval.ShouldBe(1);
        counts.CreatedOpen.ShouldBe(2);
        var aCounts = (await w.AClient.GetFromJsonAsync<TaskCountsDto>($"{Base}/counts"))!;
        aCounts.AssignedOpen.ShouldBe(2);

        var status = (await w.AClient.GetFromJsonAsync<CurrentStatus>($"/api/v1/workflow/status/{TaskItem.EntityType}/{first}"))!;
        status.MessageKey.ShouldBe("tasks.currentStatus.assigned");
        status.NextActions.Select(a => a.Code).ShouldBe([TaskActions.Accept, TaskActions.Reject], ignoreOrder: true);

        var dashboard = (await w.ManagerClient.GetFromJsonAsync<TaskDashboardDto>($"{Base}/dashboard"))!;
        dashboard.Open.ShouldBeGreaterThanOrEqualTo(2);
        dashboard.Workload.ShouldContain(r => r.Employee.EmployeeId == w.A.EmployeeId && r.Open == 2);
        dashboard.ByPriority.ShouldContain(c => c.Key == TaskPriorities.High);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid taskId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("evidence"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "evidence.txt");
        return await client.PostAsync($"/api/v1/files/owners/tasks.task/{taskId}", form);
    }

    private async Task SaveSettingsAsync(Func<TaskSettings, TaskSettings> change)
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        var dispatcher = scope.ServiceProvider.GetRequiredService<Strategy.Platform.Kernel.Messaging.IDispatcher>();
        var current = await dispatcher.SendAsync(new GetTaskSettings());
        var saved = await dispatcher.SendAsync(new SaveTaskSettings(change(current.Value.Value), current.Value.Version));
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Error!.Code : null);
    }

    private Task SetUnitHeadAsync(TestUser head) => ApprovalWorld.SetHeadAsync(api.Factory, head.UnitId!.Value, head.EmployeeId);

    /// <summary>A service scope acting as <paramref name="user"/> (claims as the sign-in issues them).</summary>
    private async Task<AsyncServiceScope> UserScopeAsync(TestUser user) => await TaskTestHelpers.UserScopeAsync(api.Factory, user);
}
