using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>"Reassign open tasks of an employee" (ADR 0040): preview, to one employee or back to the creators, access, notifications, audit, the "Left" offer.</summary>
public sealed class TaskReassignmentTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Base = TasksWorld.Base;

    /// <summary>A task administrator (All scope) with MFA, signed in.</summary>
    private async Task<(TestUser User, HttpClient Client)> AdminAsync()
    {
        var user = await api.Factory.CreateUserAsync(admin: true, roles: "tasks.administrator");
        return (user, await api.Factory.SignInAsync(user));
    }

    /// <summary>A's open tasks in every status, one of them confidential, one created by A.</summary>
    private static async Task<Dictionary<string, Guid>> ArrangeAsync(TasksWorld w)
    {
        async Task<Guid> New(HttpClient client, string title, bool confidential = false, string type = "manager-assignment") =>
            (await TasksWorld.CreateAsync(client, new
            {
                titleEn = title,
                typeCode = type,
                sourceCode = "manager-assignment",
                confidential,
                dueDate = "2031-06-30",
                assigneeEmployeeIds = new[] { w.A.EmployeeId },
            })).Tasks.Single().Id;

        var tasks = new Dictionary<string, Guid>
        {
            ["assigned"] = await New(w.ManagerClient, "Assigned"),
            ["working"] = await New(w.ManagerClient, "Working"),
            ["onHold"] = await New(w.ManagerClient, "On hold"),
            ["submitted"] = await New(w.ManagerClient, "Submitted"),
            ["rejected"] = await New(w.ManagerClient, "Rejected"),
            ["confidential"] = await New(w.BClient, "Secret salary review", confidential: true),
            ["own"] = await New(w.AClient, "Personal note", type: "personal"),
        };
        await TasksWorld.ActOkAsync(w.AClient, tasks["working"], TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, tasks["onHold"], TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, tasks["onHold"], TaskActions.Pause, new { reason = "waiting" });
        await TasksWorld.ActOkAsync(w.AClient, tasks["submitted"], TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, tasks["submitted"], TaskActions.Submit);
        await TasksWorld.ActOkAsync(w.AClient, tasks["rejected"], TaskActions.Reject, new { reason = "not mine" });
        return tasks;
    }

    [Fact]
    public async Task Open_tasks_move_to_a_new_assignee_with_a_preview_notifications_record_roles_and_audit()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var tasks = await ArrangeAsync(w);
        var done = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Done already", w.A.EmployeeId))).Tasks.Single().Id;
        await TasksWorld.ActOkAsync(w.AClient, done, TaskActions.Accept);
        await TasksWorld.ActOkAsync(w.AClient, done, TaskActions.Submit);
        await TasksWorld.ActOkAsync(w.ManagerClient, done, TaskActions.Approve, new { feedbackPoint = 3 });
        var (_, admin) = await AdminAsync();

        // Preview: every open task (closed ones left out); the confidential one without its title or number.
        var preview = (await admin.GetFromJsonAsync<ReassignmentPreviewDto>($"{Base}/reassignment/{w.A.EmployeeId}"))!;
        preview.Tasks.Select(t => t.Id).ShouldBe(tasks.Values, ignoreOrder: true);
        var secret = preview.Tasks.Single(t => t.Id == tasks["confidential"]);
        secret.Readable.ShouldBeFalse();
        secret.TitleEn.ShouldBeNull();
        secret.Number.ShouldBeNull();
        preview.Tasks.Single(t => t.Id == tasks["own"]).CreatedByLeaver.ShouldBeTrue();

        // A reason is required; then everything goes to C.
        (await admin.PostAsJsonAsync($"{Base}/reassignment", new { fromEmployeeId = w.A.EmployeeId, mode = "to-employee", toEmployeeId = w.C.EmployeeId }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var response = await admin.PostAsJsonAsync($"{Base}/reassignment",
            new { fromEmployeeId = w.A.EmployeeId, mode = "to-employee", toEmployeeId = w.C.EmployeeId, reason = "A left the organisation" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<ReassignmentResultDto>())!;
        result.Reassigned.ShouldBe(7);
        result.ReturnedToCreators.ShouldBe(0);

        // Statuses: work in progress / on hold / rejected restart as Assigned for C; submitted stays submitted; personal stays To Do.
        async Task<TaskDto> AsC(string key) => await TasksWorld.GetAsync(w.CClient, tasks[key]);
        (await AsC("assigned")).Status.ShouldBe(TaskStatuses.Assigned);
        (await AsC("working")).Status.ShouldBe(TaskStatuses.Assigned);
        (await AsC("onHold")).Status.ShouldBe(TaskStatuses.Assigned);
        (await AsC("rejected")).Status.ShouldBe(TaskStatuses.Assigned);
        (await AsC("submitted")).Status.ShouldBe(TaskStatuses.Submitted);
        var own = await AsC("own");
        own.Status.ShouldBe(TaskStatuses.ToDo);
        own.Creator.EmployeeId.ShouldBe(w.C.EmployeeId!.Value); // the leaver's own task: C takes over as creator too
        (await AsC("confidential")).Assignee.EmployeeId.ShouldBe(w.C.EmployeeId!.Value); // C is now a participant
        foreach (var key in tasks.Keys)
        {
            (await AsC(key)).Assignee.EmployeeId.ShouldBe(w.C.EmployeeId!.Value);
        }

        // The running and paused periods were closed; the timeline records the transfer with the reason; C can act, A no longer.
        var timeline = (await w.CClient.GetFromJsonAsync<TimelineDto>($"{Base}/{tasks["working"]}/timeline"))!;
        timeline.Timesheet.Running.ShouldBeFalse();
        timeline.Events[^1].Event.ShouldBe(TaskEvents.Transferred);
        timeline.Events[^1].Comment.ShouldBe("A left the organisation");
        await TasksWorld.ActOkAsync(w.CClient, tasks["working"], TaskActions.Accept);
        (await w.AClient.GetAsync($"{Base}/{tasks["assigned"]}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TasksWorld.GetAsync(w.ManagerClient, tasks["submitted"])).Actions.ShouldContain(TaskActions.Approve);

        // Everyone involved hears about it: the new assignee and the creators.
        await ApprovalFlowTests.WaitForNotificationAsync(w.CClient, TaskNotifications.ReassignedToYou.Key);
        await ApprovalFlowTests.WaitForNotificationAsync(w.ManagerClient, TaskNotifications.AssigneeChanged.Key);
        await ApprovalFlowTests.WaitForNotificationAsync(w.BClient, TaskNotifications.AssigneeChanged.Key);

        // One audit event for the action (on the employee), and field changes on each task.
        var adminAudit = await api.AdminClientAsync();
        var events = (await adminAudit.GetFromJsonAsync<PagedResult<JsonElement>>(
            $"/api/v1/audit/entries?action=tasks.task.bulk-reassign&recordId={w.A.EmployeeId}&pageSize=10"))!;
        events.Items.ShouldHaveSingleItem().GetProperty("reason").GetString().ShouldBe("A left the organisation");
        var history = (await w.CClient.GetFromJsonAsync<PagedResult<JsonElement>>($"{Base}/{tasks["assigned"]}/audit?pageSize=100"))!;
        history.Items.ShouldContain(e => e.GetProperty("field").GetString() == "AssigneeEmployeeId");

        // Nothing left to move.
        (await admin.PostAsJsonAsync($"{Base}/reassignment",
            new { fromEmployeeId = w.A.EmployeeId, mode = "to-employee", toEmployeeId = w.C.EmployeeId, reason = "again" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Creators_decide_when_tasks_go_back_to_them_and_the_leavers_own_tasks_need_a_fallback()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var tasks = await ArrangeAsync(w);
        var (_, admin) = await AdminAsync();
        var body = new { fromEmployeeId = w.A.EmployeeId, mode = "to-creators", reason = "A moved to another department" };

        // A created one of the tasks: someone must take it.
        await (await admin.PostAsJsonAsync($"{Base}/reassignment", body)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var response = await admin.PostAsJsonAsync($"{Base}/reassignment", new
        {
            fromEmployeeId = w.A.EmployeeId,
            mode = "to-creators",
            fallbackEmployeeId = w.C.EmployeeId,
            reason = "A moved to another department",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<ReassignmentResultDto>())!;
        result.Reassigned.ShouldBe(1);           // the leaver's own task → C
        result.ReturnedToCreators.ShouldBe(6);

        // Work not yet submitted comes back as Rejected with the reason: the creator reassigns or cancels through the lifecycle.
        var working = await TasksWorld.GetAsync(w.ManagerClient, tasks["working"]);
        working.Status.ShouldBe(TaskStatuses.Rejected);
        working.RejectionReason.ShouldBe("A moved to another department");
        working.Actions.ShouldBe([TaskActions.Reassign, TaskActions.Cancel], ignoreOrder: true);
        (await TasksWorld.GetAsync(w.ManagerClient, tasks["submitted"])).Status.ShouldBe(TaskStatuses.Submitted);
        await TasksWorld.ActOkAsync(w.ManagerClient, tasks["working"], TaskActions.Reassign, new { assigneeEmployeeId = w.B.EmployeeId });
        (await TasksWorld.GetAsync(w.BClient, tasks["working"])).Status.ShouldBe(TaskStatuses.Assigned);
        (await TasksWorld.GetAsync(w.CClient, tasks["own"])).Creator.EmployeeId.ShouldBe(w.C.EmployeeId!.Value);

        await ApprovalFlowTests.WaitForNotificationAsync(w.ManagerClient, TaskNotifications.DecisionNeeded.Key);
        await ApprovalFlowTests.WaitForNotificationAsync(w.BClient, TaskNotifications.DecisionNeeded.Key);
    }

    [Fact]
    public async Task Only_holders_of_the_permission_within_their_scope_may_reassign()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Open", w.A.EmployeeId));
        var body = new { fromEmployeeId = w.A.EmployeeId, mode = "to-employee", toEmployeeId = w.C.EmployeeId, reason = "x" };

        // Without the permission: refused at the endpoint (the manager included).
        (await w.ManagerClient.GetAsync($"{Base}/reassignment/{w.A.EmployeeId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.ManagerClient.PostAsJsonAsync($"{Base}/reassignment", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // With the permission for another unit only: the employee is outside the scope → 404.
        var role = await api.Factory.CreateRoleAsync("tasks", (TaskPermissions.ReassignmentManage, DataScope.Unit), (TaskPermissions.Read, DataScope.Own));
        var other = await api.Factory.CreateUserAsync(roles: role);
        var otherClient = await api.Factory.SignInAsync(other);
        (await otherClient.GetAsync($"{Base}/reassignment/{w.A.EmployeeId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherClient.PostAsJsonAsync($"{Base}/reassignment", body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The same permission in the employee's own unit is enough.
        var inUnit = await api.Factory.CreateUserAsync(unitId: w.Manager.UnitId, roles: role);
        var inUnitClient = await api.Factory.SignInAsync(inUnit);
        (await inUnitClient.GetAsync($"{Base}/reassignment/{w.A.EmployeeId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await inUnitClient.PostAsJsonAsync($"{Base}/reassignment", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Marking_an_employee_as_left_offers_the_reassignment_to_task_administrators_and_their_manager()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Still open", w.A.EmployeeId));
        var (_, admin) = await AdminAsync();
        var org = await api.AdminClientAsync();
        var employee = await org.GetFromJsonAsync<JsonElement>($"/api/v1/org/employees/{w.A.EmployeeId}");
        var changed = await org.PostAsJsonAsync($"/api/v1/org/employees/{w.A.EmployeeId}/change-status",
            new { status = "left", version = employee.GetProperty("version").GetUInt32() });
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        var offer = await ApprovalFlowTests.WaitForNotificationAsync(admin, TaskNotifications.EmployeeLeftWithTasks.Key);
        offer.Link.ShouldBe($"/settings/tasks/reassign?employee={w.A.EmployeeId:D}");
        await ApprovalFlowTests.WaitForNotificationAsync(w.ManagerClient, TaskNotifications.EmployeeLeftWithTasks.Key);

        // Nothing moved by itself.
        (await TasksWorld.ListAsync(w.ManagerClient, "view=created")).Items.Single().Assignee.EmployeeId.ShouldBe(w.A.EmployeeId!.Value);
        var preview = (await admin.GetFromJsonAsync<ReassignmentPreviewDto>($"{Base}/reassignment/{w.A.EmployeeId}"))!;
        preview.EmployeeStatus.ShouldBe("left");
        preview.Tasks.Count.ShouldBe(1);
    }
}
