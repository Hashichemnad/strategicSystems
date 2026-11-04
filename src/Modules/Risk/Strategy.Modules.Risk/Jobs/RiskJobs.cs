using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.Risk.Jobs;

/// <summary>
/// Monitoring (SPEC R2.2, R3.1, design §5.2 / §5.3): review reminders N working days before the due date and an overdue notice after it; action
/// reminders before the planned end and escalation to the risk owner and coordinator M working days after it. Each step once (stamps).
/// </summary>
internal sealed class MonitorRunner(RiskDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public async Task<(int ReviewReminders, int ReviewOverdue, int ActionReminders, int ActionEscalations)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        int reviewReminders = 0, reviewOverdue = 0, actionReminders = 0, actionEscalations = 0;
        var riskIds = await db.Risks.AsNoTracking()
            .Where(r => r.Status == RiskStatuses.Active && r.NextReviewDueAt != null && (r.ReviewRemindedAt == null || r.ReviewOverdueNotifiedAt == null))
            .Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in riskIds)
        {
            await using var scope = RiskJobScope.Create(scopes);
            var scoped = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
            var deadlines = scope.ServiceProvider.GetRequiredService<RiskDeadlines>();
            var notifier = scope.ServiceProvider.GetRequiredService<RiskNotifier>();
            var settings = await scope.ServiceProvider.GetRequiredService<RiskReader>().SettingsAsync(ct).ConfigureAwait(false);
            var risk = await scoped.Risks.FirstAsync(r => r.Id == id, ct).ConfigureAwait(false);
            var due = risk.NextReviewDueAt!.Value;
            var link = $"/risk/risks/{risk.Id:D}?tab=reviews";
            var values = RiskNotifier.Values(risk, RiskDeadlines.Date(risk.NextReviewOn));
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var changed = false;
            if (now <= due && risk.ReviewRemindedAt is null
                && now >= await deadlines.ShiftWorkingDaysAsync(risk.UnitId, due, -settings.ReviewReminderWorkingDays, ct).ConfigureAwait(false))
            {
                risk.MarkReviewReminded(now);
                await notifier.SendAsync(RiskNotifications.ReviewDue, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId, risk.FollowUpEmployeeId], values, link, scoped, ct)
                    .ConfigureAwait(false);
                reviewReminders++;
                changed = true;
            }

            if (now > due && risk.ReviewOverdueNotifiedAt is null)
            {
                risk.MarkReviewOverdue(now);
                risk.MarkReviewReminded(risk.ReviewRemindedAt ?? now);
                await notifier.SendAsync(RiskNotifications.ReviewOverdue, [risk.OwnerEmployeeId, risk.CoordinatorEmployeeId, risk.FollowUpEmployeeId], values, link, scoped, ct)
                    .ConfigureAwait(false);
                reviewOverdue++;
                changed = true;
            }

            if (changed)
            {
                await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        var actionIds = await db.Actions.AsNoTracking()
            .Where(a => (a.Status == ActionStatuses.NotStarted || a.Status == ActionStatuses.InProgress) && a.PlannedEnd != null && (a.RemindedAt == null || a.EscalatedAt == null))
            .Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in actionIds)
        {
            await using var scope = RiskJobScope.Create(scopes);
            var scoped = scope.ServiceProvider.GetRequiredService<RiskDbContext>();
            var deadlines = scope.ServiceProvider.GetRequiredService<RiskDeadlines>();
            var notifier = scope.ServiceProvider.GetRequiredService<RiskNotifier>();
            var settings = await scope.ServiceProvider.GetRequiredService<RiskReader>().SettingsAsync(ct).ConfigureAwait(false);
            var action = await scoped.Actions.FirstAsync(a => a.Id == id, ct).ConfigureAwait(false);
            var risk = await scoped.Risks.AsNoTracking().FirstAsync(r => r.Id == action.RiskId, ct).ConfigureAwait(false);
            if (risk.Status is RiskStatuses.Closed or RiskStatuses.Rejected)
            {
                continue;
            }

            var due = await deadlines.EndOfDayAsync(risk.UnitId, action.PlannedEnd!.Value, ct).ConfigureAwait(false);
            var values = RiskNotifier.Values(action, RiskDeadlines.Date(action.PlannedEnd));
            var link = $"/risk/my-actions?action={action.Id:D}";
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var changed = false;
            if (now <= due && action.RemindedAt is null
                && now >= await deadlines.ShiftWorkingDaysAsync(risk.UnitId, due.AddTicks(-1), -settings.ActionReminderWorkingDays, ct).ConfigureAwait(false))
            {
                action.MarkReminded(now);
                await notifier.SendAsync(RiskNotifications.ActionDue, [action.OwnerEmployeeId], values, link, scoped, ct).ConfigureAwait(false);
                actionReminders++;
                changed = true;
            }

            if (now > due && action.EscalatedAt is null
                && now >= await deadlines.ShiftWorkingDaysAsync(risk.UnitId, due.AddTicks(-1), settings.ActionEscalationWorkingDays, ct).ConfigureAwait(false))
            {
                action.MarkEscalated(now);
                action.MarkReminded(action.RemindedAt ?? now);
                await notifier.SendAsync(RiskNotifications.ActionOverdue, [action.OwnerEmployeeId, risk.OwnerEmployeeId, risk.CoordinatorEmployeeId], values, link, scoped, ct)
                    .ConfigureAwait(false);
                actionEscalations++;
                changed = true;
            }

            if (changed)
            {
                await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        return (reviewReminders, reviewOverdue, actionReminders, actionEscalations);
    }
}

[DisallowConcurrentExecution]
internal sealed class RiskMonitorJob(RequestContext requestContext, MonitorRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "risk.monitor", "متابعة المخاطر", "Risk monitoring", "0 20 * * * ?", typeof(RiskMonitorJob),
        "يرسل تذكيرات مراجعة المخاطر وإشعارات التأخر، وتذكيرات إجراءات المعالجة وتصعيد المتأخر منها.",
        "Sends risk review reminders and overdue notices, treatment action reminders and escalations of late actions.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (reminders, overdue, actionReminders, escalations) = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"reviewReminders={reminders} reviewOverdue={overdue} actionReminders={actionReminders} actionEscalations={escalations}";
    }
}

/// <summary>A child scope that runs as the job (system principal, channel job).</summary>
internal static class RiskJobScope
{
    public static AsyncServiceScope Create(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
