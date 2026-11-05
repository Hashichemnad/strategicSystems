using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Reporting;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>One host per test class with a travelling clock (tasks are created in the past so they can be overdue now).</summary>
public sealed class TaskReportingFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<IClock>(Clock));

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>Task reporting sources (docs/design/reporting.md §2.5, §6, §7, ADR 0039 / 0141): facts vs the dashboard, confidential partition, scope, list, dataset, card.</summary>
public sealed class TaskReportingTests(TaskReportingFixture fx) : IClassFixture<TaskReportingFixture>
{
    private const string Base = TasksWorld.Base;

    private sealed record Seeded(TasksWorld World, TestUser Admin, Dictionary<Guid, HttpClient> Clients, Guid Overdue, Guid Future, Guid Confidential, Guid Closed,
        DateOnly OverdueDue, DateOnly FutureDue);

    private static ClaimsPrincipal P(TestUser user) => Risk.RiskReportingTests.PrincipalOf(user);

    /// <summary>"Today" on the travelling clock.</summary>
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime);

    private MetricComputation Range(ClaimsPrincipal? user, params string[] metrics)
    {
        var month = MetricComputation.MonthOf(Today);
        return new MetricComputation(metrics, month.AddMonths(-3), month, month, Today, user);
    }

    private async Task<IReadOnlyList<MetricFact>> FactsAsync(ClaimsPrincipal? user)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(s => s.SystemCode == TaskPermissions.SystemCode);
        return await source.ComputeAsync(Range(user, [.. source.Metrics.Select(m => m.Code)]), CancellationToken.None);
    }

    /// <summary>The dashboard as <paramref name="user"/> sees it, through the handler (no sign-in on the travelling clock).</summary>
    private async Task<TaskDashboardDto> DashboardAsync(TestUser user, DateOnly? from = null, DateOnly? to = null)
    {
        await using var scope = await TaskTestHelpers.UserScopeAsync(fx.Factory, user);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new GetTaskDashboard(from, to), CancellationToken.None);
        return result.Value;
    }

    private static decimal Sum(IEnumerable<MetricFact> facts, string metric, Func<MetricFact, bool>? where = null) =>
        facts.Where(f => f.Metric == metric && (where?.Invoke(f) ?? true)).Sum(f => f.Value);

    private static async Task CloseAsync(HttpClient assignee, HttpClient creator, Guid id)
    {
        await TasksWorld.ActOkAsync(assignee, id, TaskActions.Accept);
        await TasksWorld.ActOkAsync(assignee, id, TaskActions.Submit, new { comment = "Done" });
        await TasksWorld.ActOkAsync(creator, id, TaskActions.Approve, new { feedbackPoint = 4, comment = "Thanks" });
    }

    /// <summary>
    /// Created now: Alpha (A, due in 5 days), Beta (B, due in 75 days), a confidential task of A for B (due in 10 days), Gamma (C) and a
    /// confidential one for B; then the clock moves 20 days on (Alpha and the confidential task are overdue) and Gamma and the second
    /// confidential task are closed.
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        fx.Clock.Offset = TimeSpan.Zero;
        var w = await TasksWorld.CreateAsync(fx.Factory);
        var admin = await fx.Factory.CreateUserAsync(admin: true, roles: "tasks.administrator");
        var created = Today;
        var overdueDue = created.AddDays(5);
        var futureDue = created.AddDays(75);
        object Body(string title, Guid? assignee, DateOnly? due, bool confidential = false) => new
        {
            titleEn = title,
            titleAr = "مهمة " + title,
            typeCode = "manager-assignment",
            sourceCode = "manager-assignment",
            priority = "high",
            confidential,
            dueDate = due,
            assigneeEmployeeIds = new[] { assignee },
        };

        var overdue = (await TasksWorld.CreateAsync(w.ManagerClient, Body("Alpha", w.A.EmployeeId, overdueDue))).Tasks.Single().Id;
        var future = (await TasksWorld.CreateAsync(w.ManagerClient, Body("Beta", w.B.EmployeeId, futureDue))).Tasks.Single().Id;
        var confidential = (await TasksWorld.CreateAsync(w.AClient, Body("Secret", w.B.EmployeeId, created.AddDays(10), confidential: true))).Tasks.Single().Id;
        var gamma = (await TasksWorld.CreateAsync(w.ManagerClient, Body("Gamma", w.C.EmployeeId, null))).Tasks.Single().Id;
        var secretClosed = (await TasksWorld.CreateAsync(w.AClient, Body("Secret closed", w.B.EmployeeId, null, confidential: true))).Tasks.Single().Id;

        fx.Clock.Offset = TimeSpan.FromDays(20);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { w.Manager, w.A, w.B, w.C, w.Outsider })
        {
            clients[user.Id] = await fx.Factory.SignInAsync(user);
        }

        await CloseAsync(clients[w.C.Id], clients[w.Manager.Id], gamma);
        await CloseAsync(clients[w.B.Id], clients[w.A.Id], secretClosed);
        return new Seeded(w, admin, clients, overdue, future, confidential, gamma, overdueDue, futureDue);
    }

    [Fact]
    public async Task Facts_match_the_dashboard_and_confidential_tasks_stay_in_their_partition()
    {
        var s = await SeedAsync();
        var w = s.World;

        // (a) Cache path: the public partition equals the dashboard of an all-scope administrator (who does not see confidential tasks).
        var all = await FactsAsync(null);
        var dashboard = await DashboardAsync(s.Admin);
        var pub = all.Where(f => f.Partition == ReportingPartitions.Public).ToList();
        Sum(pub, TaskMetricSource.Tasks).ShouldBe(dashboard.Open);
        Sum(pub, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Overdue] == "true").ShouldBe(dashboard.Overdue);
        foreach (var status in dashboard.ByStatus)
        {
            Sum(pub, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Status] == status.Key).ShouldBe(status.Open, status.Key);
        }

        foreach (var priority in dashboard.ByPriority)
        {
            Sum(pub, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Priority] == priority.Key).ShouldBe(priority.Open, priority.Key);
        }

        foreach (var source in dashboard.BySource)
        {
            Sum(pub, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Source] == source.Key).ShouldBe(source.Open, source.Key);
        }

        Sum(pub, TaskMetricSource.Completed).ShouldBe(dashboard.ClosedLast90Days);
        var onTime = pub.Where(f => f.Metric == TaskMetricSource.OnTime).ToList();
        if (dashboard.OnTimeRate is { } rate)
        {
            ((double)(onTime.Sum(f => f.Value) / onTime.Sum(f => f.Weight))).ShouldBe(rate, 0.0001);
        }

        var accept = pub.Where(f => f.Metric == TaskMetricSource.HoursToAccept).ToList();
        ((double)(accept.Sum(f => f.Value) / accept.Sum(f => f.Weight))).ShouldBe(dashboard.AverageHoursToAccept!.Value, 0.051);

        // (c) Confidential tasks: present in the cache only in the confidential partition, which no viewer reads in full.
        Sum(all, TaskMetricSource.Tasks, f => f.Partition == ReportingPartitions.Confidential && f.UnitId == w.B.UnitId).ShouldBe(1);
        Sum(all, TaskMetricSource.Completed, f => f.Partition == ReportingPartitions.Confidential && f.UnitId == w.B.UnitId).ShouldBe(1);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var source = scope.ServiceProvider.GetServices<IReportingMetricSource>().Single(x => x.SystemCode == TaskPermissions.SystemCode);
            (await source.VisiblePartitionsAsync(P(w.B), CancellationToken.None)).ShouldBeEmpty();
            (await source.VisiblePartitionsAsync(P(s.Admin), CancellationToken.None)).ShouldBeEmpty();
        }

        // (b) Live path: the manager (Team scope) counts the team's tasks but not the confidential one; B (a participant) counts it.
        var manager = await FactsAsync(P(w.Manager));
        manager.ShouldNotContain(f => f.Partition == ReportingPartitions.Confidential);
        Sum(manager, TaskMetricSource.Tasks).ShouldBe(2); // Alpha (A), Beta (B)
        Sum(manager, TaskMetricSource.Tasks, f => f.Dimensions[ReportingDimensions.Overdue] == "true").ShouldBe(1);
        Sum(manager, TaskMetricSource.Completed).ShouldBe(1); // Gamma
        var managerDashboard = await DashboardAsync(w.Manager);
        Sum(manager, TaskMetricSource.Tasks).ShouldBe(managerDashboard.Open);
        var b = await FactsAsync(P(w.B));
        Sum(b, TaskMetricSource.Tasks).ShouldBe(2); // Beta and the confidential task
        Sum(b, TaskMetricSource.Tasks, f => f.Partition == ReportingPartitions.Confidential).ShouldBe(1);
        Sum(await FactsAsync(P(w.Outsider)), TaskMetricSource.Tasks).ShouldBe(0);
        Sum(all, TaskMetricSource.Tasks).ShouldBeGreaterThan(Sum(manager, TaskMetricSource.Tasks));
    }

    [Fact]
    public async Task Dashboard_from_and_to_drive_open_tasks_by_due_date_and_closed_tasks_by_closing_month()
    {
        var s = await SeedAsync();
        var manager = s.Clients[s.World.Manager.Id];
        string Month(DateOnly d) => $"{d.Year:D4}-{d.Month:D2}-01";
        var future = (await manager.GetFromJsonAsync<TaskDashboardDto>($"{Base}/dashboard?from={Month(s.FutureDue)}&to={Month(s.FutureDue)}"))!;
        (future.Open, future.ClosedLast90Days).ShouldBe((1, 0));
        var thisMonth = Month(Today);
        var closed = (await manager.GetFromJsonAsync<TaskDashboardDto>($"{Base}/dashboard?from={thisMonth}&to={thisMonth}"))!;
        closed.ClosedLast90Days.ShouldBe(1);
        var overdue = (await manager.GetFromJsonAsync<TaskDashboardDto>($"{Base}/dashboard?from={Month(s.OverdueDue)}&to={Month(s.OverdueDue)}"))!;
        overdue.Overdue.ShouldBe(1);
        var everything = (await manager.GetFromJsonAsync<TaskDashboardDto>($"{Base}/dashboard"))!;
        (everything.Open, everything.ClosedLast90Days).ShouldBe((2, 1));
    }

    [Fact]
    public async Task Overdue_list_dataset_and_my_tasks_card_follow_the_visibility_rule()
    {
        var s = await SeedAsync();
        var w = s.World;
        await using var scope = TestAccess.SystemScope(fx.Factory);

        // (d) The overdue list: the manager sees Alpha with a working link; the outsider sees nothing and gets 404 on the link.
        var lists = scope.ServiceProvider.GetServices<IReportingListSource>().Single(x => x.SystemCode == TaskPermissions.SystemCode);
        var query = new ReportingListQuery(Today.AddMonths(-1), Today, Today, null, 10);
        var items = await lists.GetAsync(TaskListSource.OverdueList, query, P(w.Manager), CancellationToken.None);
        var alpha = items.Single();
        (alpha.Id, alpha.Link, alpha.Tone).ShouldBe((s.Overdue, $"/tasks/{s.Overdue:D}", "danger"));
        alpha.Value!.Value.ShouldBeGreaterThan(0m);
        (await s.Clients[w.Manager.Id].GetAsync($"/api/v1{alpha.Link}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Clients[w.Outsider.Id].GetAsync($"/api/v1{alpha.Link}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lists.GetAsync(TaskListSource.OverdueList, query, P(w.Outsider), CancellationToken.None)).ShouldBeEmpty();
        (await lists.GetAsync(TaskListSource.OverdueList, query, P(w.B), CancellationToken.None)).Select(i => i.Id).ShouldBe([s.Confidential]);

        // (e) Dataset: scoped rows (the confidential task only for its participants), filter, sort and group.
        var datasets = scope.ServiceProvider.GetServices<IReportingDatasetSource>().Single(x => x.SystemCode == TaskPermissions.SystemCode);
        var mine = new[] { w.A.EmployeeId!.Value.ToString(), w.B.EmployeeId!.Value.ToString(), w.C.EmployeeId!.Value.ToString() };
        var managerRows = await datasets.RunAsync(new ReportQuery(TaskDatasetSource.TasksDataset, ["number", "titleEn", "dueDate", "confidential"],
            [new ReportFilter("assignee", ReportOperators.In, mine)], [], [new ReportSort("titleEn", false)], 0, 50), P(w.Manager), CancellationToken.None);
        managerRows.Rows.Select(r => r[1]).ShouldBe(["Alpha", "Beta", "Gamma"]);
        var bRows = await datasets.RunAsync(new ReportQuery(TaskDatasetSource.TasksDataset, ["titleEn"],
            [new ReportFilter("confidential", ReportOperators.Equal, ["true"])], [], [], 0, 50), P(w.B), CancellationToken.None);
        bRows.Rows.Select(r => r[0]).ShouldBe(["Secret", "Secret closed"], ignoreOrder: true);
        var grouped = await datasets.RunAsync(new ReportQuery(TaskDatasetSource.TasksDataset, ["status"],
            [new ReportFilter("assignee", ReportOperators.In, mine)], ["status"], [], 0, 50), P(w.Manager), CancellationToken.None);
        grouped.Rows.Single(r => (string)r[0]! == TaskStatuses.Approved)[1].ShouldBe(1);
        grouped.Rows.Single(r => (string)r[0]! == TaskStatuses.Assigned)[1].ShouldBe(2);

        // (f) My tasks card: A is assigned Alpha (overdue) and created the confidential task for B; the outsider has no card.
        var cards = scope.ServiceProvider.GetServices<IMyWorkCardSource>().Single(x => x.SystemCode == TaskPermissions.SystemCode);
        var card = (await cards.GetCardsAsync(P(w.A), CancellationToken.None)).Single();
        (card.Code, card.TitleKey, card.Link).ShouldBe((TaskMyWorkCards.Code, "myWork.cards.myTasks", "/tasks"));
        card.Counts.ToDictionary(c => c.Key, c => c.Value).ShouldBe(new Dictionary<string, int>
        {
            ["assigned"] = 1,
            ["created"] = 1,
            ["followUp"] = 0,
            ["overdue"] = 1,
        });
        card.Items.ShouldContain(i => i.Id == s.Overdue && i.Overdue && i.Link == $"/tasks/{s.Overdue:D}");
        card.Items.Count.ShouldBeLessThanOrEqualTo(5);
        (await cards.GetCardsAsync(P(w.Outsider), CancellationToken.None)).ShouldBeEmpty();
    }
}
