using System.Net;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Tasks;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;

namespace Strategy.IntegrationTests.Access;

/// <summary>
/// Tasks → Settings → Responsibilities (ADR 0168): a thin adapter over "Reassign open tasks of an employee" (ADR 0040) — only the selected tasks
/// move, with the command's own notices and audit; the platform notices are not sent twice.
/// </summary>
public sealed class TaskResponsibilityTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string System = TaskPermissions.SystemCode;
    private const string Reason = "A moved to another department";

    [Fact]
    public async Task Only_the_selected_open_tasks_move_through_the_existing_reassignment_command()
    {
        var w = await TasksWorld.CreateAsync(api.Factory);
        var t1 = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Prepare the budget", w.A.EmployeeId))).Tasks.Single().Id;
        var t2 = (await TasksWorld.CreateAsync(w.ManagerClient, TasksWorld.NewTask("Review the contract", w.A.EmployeeId))).Tasks.Single().Id;
        var admin = await api.Factory.SignInAsync(await api.Factory.CreateUserAsync(admin: true, roles: "tasks.administrator"));

        var rows = await Responsibilities.ListAsync(admin, System, "task-assignee", $"&personId={w.A.EmployeeId}");
        rows.Items.Select(r => r.Id).ShouldBe([t1, t2], ignoreOrder: true);
        var done = await Responsibilities.ReassignAsync(admin, System, "task-assignee", Responsibilities.Body(rows.Items.Where(r => r.Id == t1), w.C.EmployeeId!.Value, Reason));
        done.Moved.ShouldBe(1);

        (await TasksWorld.GetAsync(w.CClient, t1)).Assignee.EmployeeId.ShouldBe(w.C.EmployeeId!.Value);
        (await TasksWorld.GetAsync(w.AClient, t2)).Assignee.EmployeeId.ShouldBe(w.A.EmployeeId!.Value);
        (await w.AClient.GetAsync($"{TasksWorld.Base}/{t1}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var timeline = (await TasksWorld.GetAsync(w.CClient, t1)).Status;
        timeline.ShouldBe(TaskStatuses.Assigned);

        // The command's own notices (once each), not the platform ones; the field change carries the reason.
        await ApprovalFlowTests.WaitForNotificationAsync(w.CClient, TaskNotifications.ReassignedToYou.Key);
        await ApprovalFlowTests.WaitForNotificationAsync(w.AClient, TaskNotifications.YourTasksReassigned.Key);
        await Responsibilities.ShouldNotBeNotifiedAsync(w.CClient, Responsibilities.AssignedTemplate);
        await Responsibilities.ShouldBeAuditedAsync(await api.AdminClientAsync(), System, TaskPermissions.ResourceType, t1, Reason);
    }
}
