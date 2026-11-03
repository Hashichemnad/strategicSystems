using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Workflow;

/// <summary>A clock tests can move forward (business code reads time only through <see cref="IClock"/>).</summary>
public sealed class TravellingClock : IClock
{
    public TimeSpan Offset { get; set; }

    /// <summary>When set, time stands still at this moment (business-hours tests need exact instants).</summary>
    public DateTimeOffset? Frozen { get; set; }

    public DateTimeOffset UtcNow => Frozen?.ToUniversalTime() ?? DateTimeOffset.UtcNow + Offset;
}

/// <summary>SLA escalation with a fake clock (design note §6).</summary>
public sealed class SlaEscalationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly TravellingClock _clock = new();
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<IClock>(_clock));

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Runs the job's work as the scheduler does: channel <c>job</c>, system principal.</summary>
    private async Task<int> RunJobAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<SlaEscalation>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Overdue_step_reminds_its_approvers_and_escalates_once()
    {
        var world = await ApprovalWorld.CreateAsync(_factory);
        var escalationTarget = await _factory.CreateUserAsync(unitId: world.DepartmentId);
        var admin = await _factory.CreateSignedInClientAsync(admin: true);
        await ApprovalWorld.SaveChainAsync(admin,
            new ApprovalStepDefinition("review", "مراجعة", "Review", new ApproverRule(ApproverRuleKinds.SubmitterManager), SlaHours: 24,
                SlaMode: SlaModes.Calendar, Escalation: new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: escalationTarget.EmployeeId)));
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        var task = (await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id)).Instances[0].Steps.Single();
        task.DueAt.ShouldNotBeNull();

        // Not due yet: nothing happens.
        (await RunJobAsync()).ShouldBe(0);

        _clock.Offset = TimeSpan.FromHours(25);
        (await RunJobAsync()).ShouldBe(1);
        (await RunJobAsync()).ShouldBe(0); // idempotent: escalated once

        // Sessions follow the clock too: sign in again after the time jump.
        var submitterClient = await _factory.SignInAsync(world.Submitter);
        var managerClient = await _factory.SignInAsync(world.SectionManager);
        var panel = await ApprovalWorld.PanelAsync(submitterClient, item.Id);
        var step = panel.Instances[0].Steps.Single();
        step.Overdue.ShouldBeTrue();
        step.Approvers.Select(a => a.UserId!.Value).Order().ShouldBe(new[] { world.SectionManager.Id, escalationTarget.Id }.Order());
        panel.Instances[0].History.Select(h => h.Event).ShouldContain("reminded");
        panel.Instances[0].History.Single(h => h.Event == "escalated").Data!.Value.GetProperty("added")[0].GetString()!.ShouldStartWith("Test employee");

        // The escalation target may decide now; My Work flags the task as overdue.
        var targetClient = await _factory.SignInAsync(escalationTarget);
        var work = (await targetClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.Approvals.Single(a => a.ResourceId == item.Id).Overdue.ShouldBeTrue();
        work.Counts.Overdue.ShouldBe(1);
        await ApprovalFlowTests.WaitForNotificationAsync(targetClient, "workflow.taskEscalated");
        await ApprovalFlowTests.WaitForNotificationAsync(managerClient, "workflow.taskOverdue");
        var mine = await ApprovalWorld.MyTaskAsync(targetClient, item.Id);
        (await ApprovalWorld.ActOkAsync(targetClient, mine.TaskId, "approve")).InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Approved);

        await using var scope = TestAccess.SystemScope(_factory);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
        var escalated = await audit.SearchAsync(new AuditFilter(Action: "workflow.task.escalated", ExactAction: true, RecordId: panel.Instances[0].Id.ToString("D")),
            new PageRequest(1, 10));
        escalated.Items.Single().Channel.ShouldBe("job");
    }
}

/// <summary>Bulk role transfer re-routes pending approvals to the new role holders (SPEC §0.2, completes Phase 2).</summary>
public sealed class RoleTransferReroutingTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Bulk_transfer_moves_pending_tasks_to_the_new_holder_with_history_and_audit()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var oldApprover = await api.Factory.CreateUserAsync(roles: "sample.approver");
        var newApprover = await api.Factory.CreateUserAsync();
        var admin = await api.AdminClientAsync();
        await ApprovalWorld.SaveChainAsync(admin,
            new ApprovalStepDefinition("approvers", "المعتمدون", "Item approvers", new ApproverRule(ApproverRuleKinds.Role, RoleKey: "sample.approver")));
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        var before = (await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id)).Instances[0].Steps.Single();
        before.Approvers.Select(a => a.UserId).ShouldContain(oldApprover.Id);
        before.Approvers.Select(a => a.UserId).ShouldNotContain(newApprover.Id);

        Guid roleId;
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var roles = await scope.ServiceProvider.GetRequiredService<AccessAdministration>().ListRolesAsync("sample", CancellationToken.None);
            roleId = roles.Single(r => r.Code == "approver").Id;
        }

        var transfer = await admin.PostAsJsonAsync("/api/v1/access/transfers",
            new TransferInput([roleId], [oldApprover.Id], null, newApprover.Id, null, null, "Approver moved to another department"));
        transfer.StatusCode.ShouldBe(HttpStatusCode.OK, await transfer.Content.ReadAsStringAsync());

        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        var after = panel.Instances[0].Steps.Single();
        after.Approvers.Select(a => a.UserId).ShouldContain(newApprover.Id);
        after.Approvers.Select(a => a.UserId).ShouldNotContain(oldApprover.Id);
        var rerouted = panel.Instances[0].History.Single(h => h.Event == "rerouted");
        rerouted.Comment.ShouldBe("Approver moved to another department");

        // The old holder can no longer act; the new one can.
        var oldClient = await api.Factory.SignInAsync(oldApprover);
        var newClient = await api.Factory.SignInAsync(newApprover);
        var task = await ApprovalWorld.MyTaskAsync(newClient, item.Id);
        await (await ApprovalWorld.ActAsync(oldClient, task.TaskId, "approve")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "workflow.notAnApprover");
        await ApprovalFlowTests.WaitForNotificationAsync(newClient, "workflow.taskAssigned");

        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditQuery>();
            var events = await audit.SearchAsync(new AuditFilter(Action: "workflow.task.rerouted", ExactAction: true, RecordId: panel.Instances[0].Id.ToString("D")),
                new PageRequest(1, 10));
            var entry = events.Items.Single();
            entry.Reason.ShouldBe("Approver moved to another department");
            entry.Data!.ShouldContain(newApprover.Id.ToString("D"));
            var assignees = await audit.HistoryAsync("workflow.task", task.TaskId.ToString("D"), new PageRequest(1, 100));
            assignees.Items.ShouldContain(e => e.EntityType == "workflow.task-assignee" && e.Field == "Active" && e.NewValue == "false");
        }

        (await ApprovalWorld.ActOkAsync(newClient, task.TaskId, "approve")).InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Approved);
    }
}
