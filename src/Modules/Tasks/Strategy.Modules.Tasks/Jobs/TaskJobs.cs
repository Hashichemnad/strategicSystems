using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Jobs;

/// <summary>
/// Reminders and escalation (SPEC §11.8, ADR 0037). Each reminder is sent once (stamp on the task), so runs are idempotent:
/// due soon → assignee; overdue → assignee and creator; escalation → the assignee's manager (else the creator); still Assigned → accept reminder.
/// </summary>
internal sealed class TaskReminders(TasksDbContext db, TaskNotifier notifier, IEmployeeDirectory employees, ISettingsStore settingsStore, IClock clock)
{
    private static readonly string[] Waiting = [TaskStatuses.Assigned, TaskStatuses.InProgress, TaskStatuses.Submitted, TaskStatuses.ToDo];

    public async Task<(int DueSoon, int Overdue, int Escalated, int Accept)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var settings = await settingsStore.GetAsync<TaskSettings>(ct).ConfigureAwait(false);
        var dueSoon = 0;
        var overdue = 0;
        var escalated = 0;
        var accept = 0;

        foreach (var task in await Batch(t => Waiting.Contains(t.Status) && t.DueSoonAt != null && t.DueSoonAt <= now && t.DueAt > now && t.ReminderSentAt == null, ct)
                     .ConfigureAwait(false))
        {
            await notifier.SendAsync(TaskNotifications.DueSoon, task, [task.AssigneeEmployeeId], null, null, db, ct).ConfigureAwait(false);
            task.MarkReminderSent(now);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.Reminded, task.Status, task.Status, null, null));
            dueSoon++;
        }

        foreach (var task in await Batch(t => Waiting.Contains(t.Status) && t.DueAt != null && t.DueAt <= now && t.OverdueNotifiedAt == null, ct).ConfigureAwait(false))
        {
            if (settings.OverdueNotice)
            {
                await notifier.SendAsync(TaskNotifications.Overdue, task, [task.AssigneeEmployeeId, task.CreatorEmployeeId], null, null, db, ct).ConfigureAwait(false);
            }

            task.MarkOverdueNotified(now);
            task.MarkReminderSent(task.ReminderSentAt ?? now);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.Overdue, task.Status, task.Status, null, null));
            overdue++;
        }

        foreach (var task in await Batch(t => Waiting.Contains(t.Status) && t.EscalateAt != null && t.EscalateAt <= now && t.EscalatedAt == null, ct).ConfigureAwait(false))
        {
            var manager = await employees.GetManagerAsync(task.AssigneeEmployeeId, null, ct).ConfigureAwait(false) ?? task.CreatorEmployeeId;
            await notifier.SendAsync(TaskNotifications.Escalated, task, [manager], await notifier.NameOfAsync(task.AssigneeEmployeeId, ct).ConfigureAwait(false), null, db, ct)
                .ConfigureAwait(false);
            task.MarkEscalated(now);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.Escalated, task.Status, task.Status, null, null, data: new { to = manager }));
            escalated++;
        }

        foreach (var task in await Batch(t => t.Status == TaskStatuses.Assigned && t.AcceptRemindAt != null && t.AcceptRemindAt <= now && t.AcceptRemindedAt == null, ct)
                     .ConfigureAwait(false))
        {
            await notifier.SendAsync(TaskNotifications.AcceptReminder, task, [task.AssigneeEmployeeId], null, null, db, ct).ConfigureAwait(false);
            task.MarkAcceptReminded(now);
            db.Events.Add(TaskEvent.Create(task.Id, now, TaskEvents.AcceptReminded, task.Status, task.Status, null, null));
            accept++;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (dueSoon, overdue, escalated, accept);
    }

    private Task<List<TaskItem>> Batch(System.Linq.Expressions.Expression<Func<TaskItem, bool>> predicate, CancellationToken ct) =>
        db.Tasks.Where(predicate).OrderBy(t => t.DueAt).Take(500).ToListAsync(ct);
}

internal sealed class TaskRemindersJob(RequestContext requestContext, TaskReminders reminders) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "tasks.reminders", "تذكيرات المهام وتصعيدها", "Task reminders and escalation", "0 0/15 * * * ?", typeof(TaskRemindersJob),
        "تذكير قبل الاستحقاق، إشعار التأخر، التصعيد إلى المدير، وتذكير القبول", "Due-soon reminders, overdue notices, escalation to the manager and accept reminders");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (dueSoon, overdue, escalated, accept) = await reminders.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"dueSoon={dueSoon} overdue={overdue} escalated={escalated} accept={accept}";
    }
}

/// <summary>Creates the next occurrence of recurring tasks whose date has come (ADR 0038). Idempotent: <c>next_occurrence_on</c> moves forward.</summary>
internal sealed class TaskRecurrence(TasksDbContext db, TaskCreator factory, ISettingsStore settingsStore, IEmployeeDirectory employees, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = await TodayAsync(ct).ConfigureAwait(false);
        var settings = await settingsStore.GetAsync<TaskSettings>(ct).ConfigureAwait(false);
        var ids = await db.Tasks.AsNoTracking().Where(t => t.NextOccurrenceOn != null && t.NextOccurrenceOn <= today && t.Status != TaskStatuses.Cancelled)
            .Select(t => t.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
        var created = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var series = await db.Tasks.FirstAsync(t => t.Id == id, ct).ConfigureAwait(false);
            var rule = RecurrenceRule.FromJson(series.Recurrence);
            var anchor = series.PlannedStart ?? series.DueDate;
            if (rule is null || anchor is null || series.NextOccurrenceOn is not { } date || date > today)
            {
                continue;
            }

            var assignee = await employees.GetAsync(series.AssigneeEmployeeId, ct).ConfigureAwait(false);
            if (assignee is not null && assignee.Status != "left")
            {
                var shift = date.DayNumber - anchor.Value.DayNumber;
                var content = new TaskContent(series.TitleAr, series.TitleEn, series.Description, series.TypeCode, series.Template, series.SourceCode, series.CategoryCode,
                    series.Priority, series.Confidential, series.PlannedStart?.AddDays(shift), series.DueDate?.AddDays(shift), series.EstimatedHours,
                    series.LinkedType, series.LinkedId);
                var checklist = await db.Checklist.AsNoTracking().Where(c => c.TaskId == series.Id && c.Active).OrderBy(c => c.Sort).Select(c => c.Text)
                    .ToListAsync(ct).ConfigureAwait(false);
                await factory.CreateAsync(new NewTaskSpec(content, series.CreatorEmployeeId, series.CreatorUserId, series.EnteredByEmployeeId, assignee.Id, assignee.UnitId,
                    false, Checklist: checklist, RecurrenceSourceId: series.Id), settings, null, null, ct).ConfigureAwait(false);
                created++;
            }

            series.AdvanceRecurrence(rule.Next(anchor.Value, series.OccurrenceCount + 2));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return created;
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var zone = TaskDeadlines.Zone((await settingsStore.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }
}

internal sealed class TaskRecurrenceJob(RequestContext requestContext, TaskRecurrence recurrence) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "tasks.recurrence", "إنشاء المهام المتكررة", "Create recurring tasks", "0 30 0 * * ?", typeof(TaskRecurrenceJob),
        "ينشئ التكرار التالي للمهام المتكررة عند حلول موعده", "Creates the next occurrence of recurring tasks when it is due");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"created={await recurrence.RunAsync(cancellationToken).ConfigureAwait(false)}";
}

/// <summary>Daily / weekly digest of open tasks (SPEC §11.8), when switched on in Settings → Tasks.</summary>
internal sealed class TaskDigest(TasksDbContext db, INotificationService notifications, IUserDirectory users, ISettingsStore settingsStore, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync<TaskSettings>(ct).ConfigureAwait(false);
        var zone = TaskDeadlines.Zone((await settingsStore.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone);
        var today = TimeZoneInfo.ConvertTime(clock.UtcNow, zone);
        var weekday = today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)today.DayOfWeek;
        if (settings.Digest == DigestModes.Off || (settings.Digest == DigestModes.Weekly && weekday != settings.DigestWeekday))
        {
            return 0;
        }

        var now = clock.UtcNow;
        var open = db.Tasks.AsNoTracking().Where(t => !TaskStatuses.Closed.Contains(t.Status) && t.Status != TaskStatuses.Draft);
        var assigned = await open.GroupBy(t => t.AssigneeEmployeeId)
            .Select(g => new
            {
                Employee = g.Key,
                Open = g.Count(),
                Overdue = g.Count(t => t.DueAt != null && t.DueAt < now),
                DueSoon = g.Count(t => t.DueSoonAt != null && t.DueSoonAt <= now && t.DueAt >= now),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var approvals = await open.Where(t => t.Status == TaskStatuses.Submitted).GroupBy(t => t.CreatorEmployeeId)
            .Select(g => new { Employee = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Employee, x => x.Count, ct).ConfigureAwait(false);
        var employeeIds = assigned.Select(a => a.Employee).Union(approvals.Keys).ToList();
        var userByEmployee = (await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active && u.EmployeeId is not null)
            .GroupBy(u => u.EmployeeId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
        var sent = 0;
        foreach (var employee in employeeIds)
        {
            if (!userByEmployee.TryGetValue(employee, out var userId))
            {
                continue;
            }

            var row = assigned.FirstOrDefault(a => a.Employee == employee);
            await notifications.SendAsync(new NotificationRequest(TaskNotifications.Digest.Key, [NotificationRecipient.User(userId)],
                new Dictionary<string, string>
                {
                    ["open"] = (row?.Open ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["overdue"] = (row?.Overdue ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["dueSoon"] = (row?.DueSoon ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["approvals"] = approvals.GetValueOrDefault(employee).ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                "/tasks", NotificationChannels.Email), cancellationToken: ct).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }
}

internal sealed class TaskDigestJob(RequestContext requestContext, TaskDigest digest) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "tasks.digest", "ملخص المهام بالبريد", "Task digest e-mail", "0 45 6 * * ?", typeof(TaskDigestJob),
        "ملخص يومي أو أسبوعي للمهام المفتوحة عند تفعيله", "Daily or weekly summary of open tasks when switched on");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken) =>
        $"sent={await digest.RunAsync(cancellationToken).ConfigureAwait(false)}";
}
