using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Calendar;

namespace Strategy.Modules.Tasks.Application;

/// <summary>The deadlines of a task (ADR 0037), computed through the shared working-time calculator.</summary>
public sealed record TaskDeadlineSet(Guid BusinessHoursId, DateTimeOffset DueAt, DateTimeOffset DueSoonAt, DateTimeOffset EscalateAt);

/// <summary>
/// Due dates, reminders and escalation in working time (ADR 0037, ADR 0027): the business hours of the assignee's unit, holidays included.
/// A "working day" is a day with working time in that record.
/// </summary>
internal sealed class TaskDeadlines(IBusinessHoursCalculator calculator)
{
    private const int MaxDaysScanned = 400;

    /// <summary>Sets <c>due_at</c>, <c>due_soon_at</c> and <c>escalate_at</c> from the task's due date and the settings, or clears them.</summary>
    public async Task ApplyAsync(TaskItem task, TaskSettings settings, CancellationToken ct)
    {
        if (task.DueDate is not { } due)
        {
            task.SetDeadlines(null, null, null, null);
            return;
        }

        var set = await ComputeAsync(task.UnitId, due, settings, ct).ConfigureAwait(false);
        task.SetDeadlines(set.BusinessHoursId, set.DueAt, set.DueSoonAt, set.EscalateAt);
    }

    /// <summary>The accept reminder: assignment + N working hours (null when switched off).</summary>
    public async Task ApplyAcceptReminderAsync(TaskItem task, TaskSettings settings, CancellationToken ct)
    {
        if (settings.AcceptReminderHours <= 0 || task.AssignedAt is not { } assigned || task.Status != TaskStatuses.Assigned)
        {
            task.SetAcceptReminder(null);
            return;
        }

        var hours = await calculator.ResolveForUnitAsync(task.UnitId, ct).ConfigureAwait(false);
        task.SetAcceptReminder(await calculator.AddWorkingTimeAsync(hours.Id, assigned, TimeSpan.FromHours(settings.AcceptReminderHours), ct).ConfigureAwait(false));
    }

    public async Task<TaskDeadlineSet> ComputeAsync(Guid? unitId, DateOnly dueDate, TaskSettings settings, CancellationToken ct)
    {
        var hours = await calculator.ResolveForUnitAsync(unitId, ct).ConfigureAwait(false);
        var zone = Zone(hours.TimeZone);

        var dueDay = await NextWorkingDayAsync(hours.Id, zone, dueDate, ct).ConfigureAwait(false);
        var dueAt = await EndOfWorkingDayAsync(hours.Id, zone, dueDay, ct).ConfigureAwait(false);

        var soonDay = await StepWorkingDaysAsync(hours.Id, zone, dueDay, -settings.ReminderDaysBeforeDue, ct).ConfigureAwait(false);
        var dueSoonAt = await StartOfWorkingDayAsync(hours.Id, zone, soonDay, ct).ConfigureAwait(false);

        var escalateDay = await StepWorkingDaysAsync(hours.Id, zone, dueDay, settings.EscalateAfterOverdueDays, ct).ConfigureAwait(false);
        var escalateAt = await EndOfWorkingDayAsync(hours.Id, zone, escalateDay, ct).ConfigureAwait(false);
        return new TaskDeadlineSet(hours.Id, dueAt, dueSoonAt, escalateAt);
    }

    /// <summary>Working time on a calendar day in the record's zone.</summary>
    private async Task<TimeSpan> WorkingTimeOnAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct) =>
        await calculator.WorkingTimeBetweenAsync(hoursId, Midnight(zone, day), Midnight(zone, day.AddDays(1)), ct).ConfigureAwait(false);

    /// <summary>The day itself when it has working time, else the next one that has.</summary>
    private async Task<DateOnly> NextWorkingDayAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct)
    {
        for (var i = 0; i < MaxDaysScanned; i++, day = day.AddDays(1))
        {
            if (await WorkingTimeOnAsync(hoursId, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                return day;
            }
        }

        return day;
    }

    /// <summary>Moves <paramref name="count"/> working days forward (positive) or back (negative) from a working day.</summary>
    private async Task<DateOnly> StepWorkingDaysAsync(Guid hoursId, TimeZoneInfo zone, DateOnly from, int count, CancellationToken ct)
    {
        var step = count < 0 ? -1 : 1;
        var remaining = Math.Abs(count);
        var day = from;
        for (var i = 0; remaining > 0 && i < MaxDaysScanned; i++)
        {
            day = day.AddDays(step);
            if (await WorkingTimeOnAsync(hoursId, zone, day, ct).ConfigureAwait(false) > TimeSpan.Zero)
            {
                remaining--;
            }
        }

        return day;
    }

    private async Task<DateTimeOffset> EndOfWorkingDayAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct)
    {
        var start = Midnight(zone, day);
        var working = await WorkingTimeOnAsync(hoursId, zone, day, ct).ConfigureAwait(false);
        return working > TimeSpan.Zero ? await calculator.AddWorkingTimeAsync(hoursId, start, working, ct).ConfigureAwait(false) : start.AddDays(1);
    }

    private async Task<DateTimeOffset> StartOfWorkingDayAsync(Guid hoursId, TimeZoneInfo zone, DateOnly day, CancellationToken ct)
    {
        var start = Midnight(zone, day);
        if (await WorkingTimeOnAsync(hoursId, zone, day, ct).ConfigureAwait(false) <= TimeSpan.Zero)
        {
            return start;
        }

        var oneMinute = TimeSpan.FromMinutes(1);
        return (await calculator.AddWorkingTimeAsync(hoursId, start, oneMinute, ct).ConfigureAwait(false)) - oneMinute;
    }

    internal static DateTimeOffset Midnight(TimeZoneInfo zone, DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    internal static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
