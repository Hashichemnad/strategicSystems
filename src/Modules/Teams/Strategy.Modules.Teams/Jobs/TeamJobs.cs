using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Jobs;

/// <summary>
/// Evaluation triggers (SPEC §4.3): annual for permanent teams on the type's date (previous year), end date for temporary teams; plus the
/// evaluator reminder. Idempotent: one cycle per team and cycle code (unique index), reminder stamped.
/// </summary>
internal sealed class EvaluationTriggerRunner(TeamsDbContext db, EvaluationOpener opener, TeamNotifier notifier, TeamDeadlines deadlines, ISettingsStore settings, IClock clock)
{
    public async Task<(int Annual, int EndDate, int Reminders)> RunAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var types = await db.Types.AsNoTracking().ToDictionaryAsync(t => t.Code, ct).ConfigureAwait(false);
        var candidates = await db.Teams.AsNoTracking().Where(t => t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive).Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        int annual = 0, endDate = 0;
        foreach (var id in candidates)
        {
            db.ChangeTracker.Clear();
            var team = await db.Teams.FirstAsync(t => t.Id == id, ct).ConfigureAwait(false);
            if (!types.TryGetValue(team.TypeCode, out var type))
            {
                continue;
            }

            var trigger = Domain.EvaluationTriggers.Effective(type.EvaluationTrigger, team.Permanent);
            int? year = null;
            var open = false;
            if (trigger == Domain.EvaluationTriggers.Annual && TeamCalendar.AnnualCycleDue(today, type.AnnualMonth, type.AnnualDay) is { } due
                && (team.StartDate is null || team.StartDate.Value.Year <= due)
                && !await db.Evaluations.AnyAsync(e => e.TeamId == team.Id && e.Cycle == Cycles.Year(due) && e.Status != EvaluationStatuses.Cancelled, ct).ConfigureAwait(false))
            {
                year = due;
                open = true;
            }
            else if (trigger == Domain.EvaluationTriggers.EndDate && !team.Permanent && team.EndDate < today
                && !await db.Evaluations.AnyAsync(e => e.TeamId == team.Id && e.Cycle == Cycles.Completion && e.Status != EvaluationStatuses.Cancelled, ct).ConfigureAwait(false))
            {
                open = true;
            }

            if (!open)
            {
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var opened = await opener.OpenAsync(team, year is null ? Domain.EvaluationTriggers.EndDate : Domain.EvaluationTriggers.Annual, year, closeAfter: year is null, ct)
                .ConfigureAwait(false);
            if (opened.IsSuccess)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                if (year is null)
                {
                    endDate++;
                }
                else
                {
                    annual++;
                }
            }
        }

        db.ChangeTracker.Clear();
        var configuration = await settings.GetAsync<TeamSettings>(ct).ConfigureAwait(false);
        var reminders = 0;
        var waiting = await db.Evaluations.Where(e => (e.Status == EvaluationStatuses.Open || e.Status == EvaluationStatuses.Returned) && e.RemindedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var evaluation in waiting)
        {
            var team = await db.Teams.AsNoTracking().FirstAsync(t => t.Id == evaluation.TeamId, ct).ConfigureAwait(false);
            var remindAt = await deadlines.AfterWorkingDaysAsync(team.OwningUnitId, evaluation.OpenedAt, configuration.EvaluationReminderDays, ct).ConfigureAwait(false);
            if (clock.UtcNow < remindAt)
            {
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            evaluation.MarkReminded(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.SendAsync(TeamNotifications.EvaluationReminder, team, [evaluation.EvaluatorEmployeeId, evaluation.LeaderEvaluatorEmployeeId], null, evaluation.Cycle,
                evaluation.OpenedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), db, ct, $"/teams/evaluations/{evaluation.Id:D}").ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            reminders++;
        }

        return (annual, endDate, reminders);
    }
}

/// <summary>Periodic reports per cadence (SPEC §4.4 C): creates the current period's report, reminds before due, notifies when overdue.</summary>
internal sealed class ReportScheduler(TeamsDbContext db, TeamDeadlines deadlines, TeamNotifier notifier, ISettingsStore settings, IClock clock)
{
    public async Task<(int Created, int Reminded, int Overdue)> RunAsync(CancellationToken ct)
    {
        var configuration = await settings.GetAsync<TeamSettings>(ct).ConfigureAwait(false);
        var today = clock.Today;
        var teams = await db.Teams.AsNoTracking().Where(t => t.Status == TeamStatuses.Active || t.Status == TeamStatuses.Inactive || t.Status == TeamStatuses.PendingEvaluation)
            .ToListAsync(ct).ConfigureAwait(false);
        var created = 0;
        foreach (var team in teams)
        {
            if (TeamCalendar.PeriodOf(team.Cadence, today) is not { } period || (team.StartDate is { } start && start > period.End))
            {
                continue;
            }

            if (await db.Reports.AnyAsync(r => r.TeamId == team.Id && r.PeriodStart == period.Start, ct).ConfigureAwait(false))
            {
                continue;
            }

            var (dueAt, remindAt) = await deadlines.ReportAsync(team.OwningUnitId, period.End, configuration.ReportReminderDays, ct).ConfigureAwait(false);
            db.Reports.Add(TeamReport.Create(team.Id, period.Start, period.End, dueAt, remindAt));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            created++;
        }

        var now = clock.UtcNow;
        int reminded = 0, overdue = 0;
        var due = await db.Reports.Where(r => r.Status == ReportStatuses.Due && ((r.RemindedAt == null && r.RemindAt <= now) || (r.OverdueNotifiedAt == null && r.DueAt < now)))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var report in due)
        {
            var team = teams.FirstOrDefault(t => t.Id == report.TeamId) ?? await db.Teams.AsNoTracking().FirstAsync(t => t.Id == report.TeamId, ct).ConfigureAwait(false);
            var period = $"{D(report.PeriodStart)} – {D(report.PeriodEnd)}";
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (report.DueAt < now && report.OverdueNotifiedAt is null)
            {
                report.MarkOverdueNotified(now);
                report.MarkReminded(report.RemindedAt ?? now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await notifier.SendAsync(TeamNotifications.ReportOverdue, team, [team.LeaderEmployeeId, team.SponsorEmployeeId], null, period, D(report.DueDate), db, ct,
                    $"/teams/{team.Id:D}?tab=reports").ConfigureAwait(false);
                overdue++;
            }
            else if (report.RemindedAt is null)
            {
                report.MarkReminded(now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await notifier.SendAsync(TeamNotifications.ReportDue, team, [team.LeaderEmployeeId], null, period, D(report.DueDate), db, ct, $"/teams/{team.Id:D}?tab=reports")
                    .ConfigureAwait(false);
                reminded++;
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return (created, reminded, overdue);
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

[DisallowConcurrentExecution]
internal sealed class EvaluationTriggersJob(RequestContext requestContext, EvaluationTriggerRunner triggers) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "teams.evaluation-triggers", "تقييمات الفرق التلقائية", "Team evaluation triggers", "0 10 1 * * ?", typeof(EvaluationTriggersJob),
        "يفتح التقييم السنوي للفرق الدائمة وتقييم انتهاء الفرق المؤقتة، ويذكّر المقيمين.",
        "Opens annual evaluations of permanent teams and completion evaluations of temporary teams, and reminds evaluators.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (annual, endDate, reminders) = await triggers.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"annual={annual} end-date={endDate} reminders={reminders}";
    }
}

[DisallowConcurrentExecution]
internal sealed class TeamReportsJob(RequestContext requestContext, ReportScheduler scheduler) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "teams.reports", "التقارير الدورية للفرق", "Team periodic reports", "0 10 2 * * ?", typeof(TeamReportsJob),
        "ينشئ تقرير الفترة الحالية لكل فريق ويرسل التذكيرات وإشعارات التأخر.", "Creates each team's current report and sends reminders and overdue notices.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var (created, reminded, overdue) = await scheduler.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"created={created} reminded={reminded} overdue={overdue}";
    }
}
