using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.Cap.Jobs;

public sealed record CapMonitorResult(int Late, int LateNotices, int Escalations, int RemarkOverdue, int MonthlyReminders, int VerificationNotices, int Mirrored);

/// <summary>
/// Monitoring (SPEC §9.4 C3, ADR 0122): late actions (owner at once, PM Office after N working days), remark overdue notices, monthly reminders on
/// the configured day, effectiveness verification due, project-progress mirroring (when on). Each step once (stamps); idempotent.
/// </summary>
internal sealed class CapMonitorRunner(CapDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public async Task<CapMonitorResult> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        int late = 0, lateNotices = 0, escalations = 0, overdue = 0, monthly = 0, verification = 0, mirrored = 0;
        var actionIds = await db.Actions.AsNoTracking()
            .Where(a => a.ValidationStatus == ValidationStatuses.Approved && a.Status != ActionStatuses.Completed && a.Status != ActionStatuses.Cancelled)
            .Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in actionIds)
        {
            await using var scope = CapJobScope.Create(scopes);
            var sp = scope.ServiceProvider;
            var scoped = sp.GetRequiredService<CapDbContext>();
            var deadlines = sp.GetRequiredService<CapDeadlines>();
            var notifier = sp.GetRequiredService<CapNotifier>();
            var service = sp.GetRequiredService<CapService>();
            var settings = await sp.GetRequiredService<CapReader>().SettingsAsync(ct).ConfigureAwait(false);
            var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
            var action = await scoped.Actions.FirstAsync(a => a.Id == id, ct).ConfigureAwait(false);
            var home = await scoped.Remarks.AsNoTracking().FirstAsync(r => r.Id == action.HomeRemarkId, ct).ConfigureAwait(false);
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (settings.MirrorProjectProgress && action.ProgramRequestId is { } requestId && sp.GetService<IProgramRequestDrafts>() is { } drafts
                && await drafts.GetProgressAsync(requestId, ct).ConfigureAwait(false) is { ProjectProgress: { } progress } && progress != action.AchievedPct)
            {
                action.Progress(Math.Min(progress, 1m), null, null, today);
                scoped.Updates.Add(AchievementUpdate.Create(action.Id, action.AchievedPct, "project-progress", null, null, now, null));
                mirrored++;
            }

            if (action.RefreshLate(today) && action.Status == ActionStatuses.Late)
            {
                late++;
            }

            var link = CapService.Link(action);
            if (action.Status == ActionStatuses.Late && action.EffectiveEnd is { } end)
            {
                var unit = home.SectionUnitId ?? home.DepartmentUnitId;
                if (action.LateNotifiedAt is null)
                {
                    action.MarkLateNotified(now);
                    await notifier.SendAsync(CapNotifications.ActionLate, [action.AssignedEmployeeId, home.OwnerEmployeeId], CapNotifier.Values(action), link, scoped, ct)
                        .ConfigureAwait(false);
                    lateNotices++;
                }

                var dueEnd = await deadlines.EndOfDayAsync(unit, end, ct).ConfigureAwait(false);
                if (action.PmoEscalatedAt is null
                    && now >= await deadlines.ShiftWorkingDaysAsync(unit, dueEnd.AddTicks(-1), settings.EscalatePmoWorkingDays, ct).ConfigureAwait(false))
                {
                    action.MarkPmoEscalated(now);
                    await notifier.SendAsync(CapNotifications.ActionEscalated, [home.OwnerEmployeeId], CapNotifier.Values(action), link, scoped, ct,
                        [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
                    escalations++;
                }
            }

            await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _ = service;
        }

        await using (var scope = CapJobScope.Create(scopes))
        {
            var sp = scope.ServiceProvider;
            var scoped = sp.GetRequiredService<CapDbContext>();
            var notifier = sp.GetRequiredService<CapNotifier>();
            var deadlines = sp.GetRequiredService<CapDeadlines>();
            var settings = await sp.GetRequiredService<CapReader>().SettingsAsync(ct).ConfigureAwait(false);
            var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Remarks past their due date (once per due date).
            var overdueRemarks = await scoped.Remarks
                .Where(r => r.DueAt < now && r.OverdueNotifiedAt == null && r.Status != CapRemarkStatuses.Completed && r.Status != CapRemarkStatuses.Rejected
                    && r.Status != CapRemarkStatuses.Cancelled && r.Status != CapRemarkStatuses.Verification)
                .Take(500).ToListAsync(ct).ConfigureAwait(false);
            foreach (var remark in overdueRemarks)
            {
                remark.MarkOverdueNotified(now);
                await notifier.SendAsync(CapNotifications.RemarkOverdue, [remark.OwnerEmployeeId], CapNotifier.Values(remark), CapService.Link(remark), scoped, ct,
                    [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
                overdue++;
            }

            // Effectiveness verification due (once).
            var verify = await scoped.Remarks.Where(r => r.Status == CapRemarkStatuses.Verification && r.VerificationNotifiedAt == null && r.VerificationDueOn != null
                    && r.VerificationDueOn <= today)
                .Take(500).ToListAsync(ct).ConfigureAwait(false);
            foreach (var remark in verify)
            {
                remark.MarkVerificationNotified(now);
                await notifier.SendAsync(CapNotifications.VerificationDue, [], CapNotifier.Values(remark, CapDeadlines.Date(remark.VerificationDueOn)), CapService.Link(remark),
                    scoped, ct, [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
                verification++;
            }

            // Monthly reminders on the configured day (once per month per action, one notification per assignee).
            if (today.Day >= settings.MonthlyReminderDay)
            {
                var month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var due = await scoped.Actions.Where(a => a.ValidationStatus == ValidationStatuses.Approved && a.AssignedEmployeeId != null
                        && (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress || a.Status == ActionStatuses.Late)
                        && (a.MonthlyRemindedFor == null || a.MonthlyRemindedFor != month))
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var group in due.GroupBy(a => a.AssignedEmployeeId))
                {
                    var counts = string.Join(" · ", group.GroupBy(a => a.Status).Select(g => $"{Label(g.Key)}: {g.Count()}"));
                    foreach (var action in group)
                    {
                        action.MarkMonthly(month);
                    }

                    await notifier.SendAsync(CapNotifications.MonthlyReminder, [group.Key], new Dictionary<string, string>
                    {
                        ["number"] = string.Empty, ["titleAr"] = string.Empty, ["titleEn"] = string.Empty, ["date"] = month, ["actor"] = string.Empty, ["text"] = counts,
                        ["count"] = group.Count().ToString(CultureInfo.InvariantCulture),
                    }, "/cap/my-actions", scoped, ct).ConfigureAwait(false);
                    monthly++;
                }
            }

            await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return new CapMonitorResult(late, lateNotices, escalations, overdue, monthly, verification, mirrored);
    }

    private static string Label(string status) => status switch
    {
        ActionStatuses.NotStarted => "Not started / لم يبدأ",
        ActionStatuses.Late => "Late / متأخر",
        _ => "In progress / قيد التنفيذ",
    };
}

[DisallowConcurrentExecution]
internal sealed class CapMonitorJob(RequestContext requestContext, CapMonitorRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "cap.monitor", "متابعة خطة العمل التصحيحية", "Corrective action plan monitoring", "0 25 * * * ?", typeof(CapMonitorJob),
        "يحدد الإجراءات المتأخرة ويصعدها، وينبه بتأخر الملاحظات، ويرسل التذكيرات الشهرية وإشعارات التحقق من الفعالية.",
        "Flags and escalates late actions, notices overdue remarks, sends the monthly reminders and effectiveness verification notices.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var r = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"late={r.Late} lateNotices={r.LateNotices} escalations={r.Escalations} remarkOverdue={r.RemarkOverdue} monthly={r.MonthlyReminders} " +
            $"verification={r.VerificationNotices} mirrored={r.Mirrored}";
    }
}

/// <summary>A child scope that runs as the job (system principal, channel job).</summary>
internal static class CapJobScope
{
    public static AsyncServiceScope Create(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
