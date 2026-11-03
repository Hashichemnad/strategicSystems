using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Modules.Tasks.Jobs;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>
/// Due dates, overdue, reminders and escalation in working time (ADR 0037) with the travelling clock and the default business hours
/// (Monday–Friday 07:30–15:30, Asia/Dubai). Each test uses its own year so holidays never meet.
/// </summary>
public sealed class TaskDeadlineTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Dubai = TimeSpan.FromHours(4);
    private readonly TravellingClock _clock = new();
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<IClock>(_clock));

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Due_dates_move_off_weekends_and_holidays_and_reminder_and_escalation_count_working_days()
    {
        // Monday 3 March 2031, 09:00 Dubai. Monday 10 March is a holiday.
        _clock.Frozen = new DateTimeOffset(2031, 3, 3, 9, 0, 0, Dubai);
        await CreateHolidayAsync(new DateOnly(2031, 3, 10));
        var w = await TasksWorld.CreateAsync(_factory);

        // Due on Saturday 8 March → the next working day is Monday 10 (a holiday) → Tuesday 11 March, end of working time 15:30.
        var id = (await TasksWorld.CreateAsync(w.ManagerClient, new
        {
            titleEn = "Weekend due date",
            typeCode = "manager-assignment",
            sourceCode = "manager-assignment",
            dueDate = "2031-03-08",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
        })).Tasks.Single().Id;
        var task = await TasksWorld.GetAsync(w.ManagerClient, id);
        task.DueAt.ShouldBe(new DateTimeOffset(2031, 3, 11, 15, 30, 0, Dubai));

        // Reminder 2 working days before Tuesday 11: Monday 10 is a holiday → Friday 7 (1), Thursday 6 (2) at 07:30.
        task.DueSoonAt.ShouldBe(new DateTimeOffset(2031, 3, 6, 7, 30, 0, Dubai));
        task.Indicator.ShouldBe(TaskIndicators.OnTime);

        // Escalation 3 working days after Tuesday 11: Wednesday, Thursday, Friday 14 → end of Friday.
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var stored = await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Tasks.AsNoTracking().SingleAsync(t => t.Id == id);
            stored.EscalateAt.ShouldBe(new DateTimeOffset(2031, 3, 14, 15, 30, 0, Dubai));
            stored.AcceptRemindAt.ShouldBe(new DateTimeOffset(2031, 3, 4, 9, 0, 0, Dubai)); // 8 working hours after Monday 09:00 = Tuesday 09:00
        }

        // A due date on a working day ends at 15:30 that day.
        var plain = (await TasksWorld.CreateAsync(w.ManagerClient, new
        {
            titleEn = "Weekday due date",
            typeCode = "manager-assignment",
            sourceCode = "manager-assignment",
            dueDate = "2031-03-05",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
        })).Tasks.Single();
        plain.DueAt.ShouldBe(new DateTimeOffset(2031, 3, 5, 15, 30, 0, Dubai));
    }

    [Fact]
    public async Task Reminders_overdue_notice_escalation_and_accept_reminder_run_once_each_at_their_time()
    {
        _clock.Frozen = new DateTimeOffset(2032, 3, 1, 9, 0, 0, Dubai); // Monday
        await CreateHolidayAsync(new DateOnly(2032, 3, 4)); // Thursday holiday
        var w = await TasksWorld.CreateAsync(_factory);
        var id = (await TasksWorld.CreateAsync(w.ManagerClient, new
        {
            titleEn = "Deadline task",
            typeCode = "manager-assignment",
            sourceCode = "manager-assignment",
            dueDate = "2032-03-05", // Friday
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
        })).Tasks.Single().Id;

        // Monday 10:00: nothing is due yet.
        _clock.Frozen = new DateTimeOffset(2032, 3, 1, 10, 0, 0, Dubai);
        (await RunAsync()).ShouldBe((0, 0, 0, 0));

        // Tuesday 09:30: 8 working hours in Assigned → accept reminder; and the due-soon reminder, due since 07:30 that day (2 working days
        // before Friday 5, the Thursday holiday skipped: Wednesday 3 (1), Tuesday 2 (2)). Each runs once.
        _clock.Frozen = new DateTimeOffset(2032, 3, 2, 9, 30, 0, Dubai);
        (await RunAsync()).ShouldBe((1, 0, 0, 1));
        (await RunAsync()).ShouldBe((0, 0, 0, 0));

        // Friday 15:31: overdue → notice to assignee and creator (once).
        _clock.Frozen = new DateTimeOffset(2032, 3, 5, 15, 31, 0, Dubai);
        (await RunAsync()).ShouldBe((0, 1, 0, 0));
        (await RunAsync()).ShouldBe((0, 0, 0, 0));

        // Escalation after 3 working days overdue: Monday 8, Tuesday 9, Wednesday 10 → end of Wednesday 15:30. Tuesday evening: not yet.
        _clock.Frozen = new DateTimeOffset(2032, 3, 9, 20, 0, 0, Dubai);
        (await RunAsync()).ShouldBe((0, 0, 0, 0));
        _clock.Frozen = new DateTimeOffset(2032, 3, 10, 15, 31, 0, Dubai);
        (await RunAsync()).ShouldBe((0, 0, 1, 0));
        (await RunAsync()).ShouldBe((0, 0, 0, 0));

        await using var scope = TestAccess.SystemScope(_factory);
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var events = await db.Events.AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Select(e => new { e.Event, e.Data }).ToListAsync();
        events.Select(e => e.Event).ShouldBe([TaskEvents.Created, TaskEvents.Reminded, TaskEvents.AcceptReminded, TaskEvents.Overdue, TaskEvents.Escalated]);
        events.Last().Data!.ShouldContain(w.Manager.EmployeeId!.Value.ToString()); // escalated to the assignee's manager
        var stored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == id);
        TaskIndicators.Of(stored.Status, stored.DueAt, stored.DueSoonAt, _clock.UtcNow).ShouldBe(TaskIndicators.Overdue);
    }

    [Fact]
    public async Task Recurring_task_creates_its_next_occurrence_when_the_date_comes()
    {
        _clock.Frozen = new DateTimeOffset(2033, 1, 3, 9, 0, 0, Dubai); // Monday
        var w = await TasksWorld.CreateAsync(_factory);
        var first = (await TasksWorld.CreateAsync(w.ManagerClient, new
        {
            titleEn = "Weekly report",
            typeCode = "manager-assignment",
            sourceCode = "manager-assignment",
            plannedStart = "2033-01-03",
            dueDate = "2033-01-06",
            assigneeEmployeeIds = new[] { w.A.EmployeeId },
            recurrence = new { frequency = "weekly", interval = 1, count = 3 },
        })).Tasks.Single();

        (await RecurAsync()).ShouldBe(0); // next one on Monday 10
        _clock.Frozen = new DateTimeOffset(2033, 1, 10, 6, 0, 0, Dubai);
        (await RecurAsync()).ShouldBe(1);
        (await RecurAsync()).ShouldBe(0);
        _clock.Frozen = new DateTimeOffset(2033, 1, 24, 6, 0, 0, Dubai);
        (await RecurAsync()).ShouldBe(1); // occurrence 3 of 3
        _clock.Frozen = new DateTimeOffset(2033, 2, 1, 6, 0, 0, Dubai);
        (await RecurAsync()).ShouldBe(0); // the series has ended

        await using var scope = TestAccess.SystemScope(_factory);
        var occurrences = await scope.ServiceProvider.GetRequiredService<TasksDbContext>().Tasks.AsNoTracking()
            .Where(t => t.RecurrenceSourceId == first.Id).OrderBy(t => t.PlannedStart).ToListAsync();
        occurrences.Select(o => o.PlannedStart).ShouldBe([new DateOnly(2033, 1, 10), new DateOnly(2033, 1, 17)]);
        occurrences.Select(o => o.DueDate).ShouldBe([new DateOnly(2033, 1, 13), new DateOnly(2033, 1, 20)]);
        occurrences.ShouldAllBe(o => o.AssigneeEmployeeId == w.A.EmployeeId && o.Status == TaskStatuses.Assigned);
    }

    private async Task<(int, int, int, int)> RunAsync()
    {
        await using var scope = JobScope();
        return await scope.ServiceProvider.GetRequiredService<TaskReminders>().RunAsync(CancellationToken.None);
    }

    private async Task<int> RecurAsync()
    {
        await using var scope = JobScope();
        return await scope.ServiceProvider.GetRequiredService<TaskRecurrence>().RunAsync(CancellationToken.None);
    }

    private AsyncServiceScope JobScope()
    {
        var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return scope;
    }

    private async Task CreateHolidayAsync(DateOnly date)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var result = await scope.ServiceProvider.GetRequiredService<CalendarAdministration>().CreateHolidayAsync(
            new HolidayInput("عطلة", "Holiday", date, null, true, null, null, false, true, []), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error!.Code : null);
    }
}
