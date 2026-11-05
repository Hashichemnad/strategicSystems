using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.AuditManagement.Application;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.AuditManagement.Jobs;

public sealed record AuditMonitorResult(int Recomputed, int LateNotices, int FollowUpReminders, int DueReminders, int KpiDrafts);

/// <summary>
/// Monitoring (docs/design/audit.md §4.5, ADR 0133): late status by due date, late notices, reminders to the responsible auditor before the next
/// follow-up and due dates (each once per date), optional KPI publishing. Idempotent.
/// </summary>
internal sealed class AuditMonitorRunner(AuditMgmtDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public async Task<AuditMonitorResult> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        int recomputed = 0, late = 0, followUps = 0, dues = 0, kpis = 0;
        var findingIds = await db.Findings.AsNoTracking().Where(f => f.Status == AuditFindingStatuses.Open || f.Status == AuditFindingStatuses.Late)
            .Select(f => f.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in findingIds)
        {
            await using var scope = AuditJobScope.Create(scopes);
            var sp = scope.ServiceProvider;
            var scoped = sp.GetRequiredService<AuditMgmtDbContext>();
            var service = sp.GetRequiredService<AuditService>();
            var notifier = sp.GetRequiredService<AuditNotifier>();
            var deadlines = sp.GetRequiredService<AuditDeadlines>();
            var settings = await sp.GetRequiredService<AuditReader>().SettingsAsync(ct).ConfigureAwait(false);
            var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var finding = await scoped.Findings.FirstAsync(f => f.Id == id, ct).ConfigureAwait(false);
            var report = await scoped.Reports.AsNoTracking().FirstAsync(r => r.Id == finding.ReportId, ct).ConfigureAwait(false);
            var before = finding.Status;
            await service.RecomputeAsync(finding, ct).ConfigureAwait(false);
            if (finding.Status != before)
            {
                recomputed++;
            }

            if (report.Status != ReportStatuses.Draft)
            {
                var auditor = finding.AuditorEmployeeId ?? report.LeadAuditorEmployeeId;
                var link = AuditService.Link(finding);
                foreach (var r in await scoped.Recommendations.Where(r => r.FindingId == finding.Id && (r.Status == RecommendationStatuses.NotStarted
                             || r.Status == RecommendationStatuses.Continuing || r.Status == RecommendationStatuses.Late)).ToListAsync(ct).ConfigureAwait(false))
                {
                    if (r.Status == RecommendationStatuses.Late && r.LateNotifiedAt is null)
                    {
                        r.MarkLateNotified(now);
                        await notifier.SendAsync(AuditNotifications.RecommendationLate, [auditor],
                            AuditNotifier.Values(r.Number, r.ActionAr, r.ActionEn, AuditDeadlines.Date(r.EffectiveDue)), link, scoped, ct).ConfigureAwait(false);
                        late++;
                    }

                    if (r.NextFollowUpOn is { } next && r.FollowUpRemindedFor != next
                        && today >= await deadlines.ReminderDayAsync(finding.ResponsibleUnitId, next, settings.FollowUpReminderDays, settings.ReminderWorkingDays, ct)
                            .ConfigureAwait(false))
                    {
                        r.MarkFollowUpReminded(next);
                        await notifier.SendAsync(AuditNotifications.FollowUpDue, [auditor], AuditNotifier.Values(r.Number, r.ActionAr, r.ActionEn, AuditDeadlines.Date(next)),
                            link, scoped, ct).ConfigureAwait(false);
                        followUps++;
                    }

                    if (r.Status != RecommendationStatuses.Late && r.DueRemindedFor != r.EffectiveDue && r.EffectiveDue >= today
                        && today >= await deadlines.ReminderDayAsync(finding.ResponsibleUnitId, r.EffectiveDue, settings.DueReminderDays, settings.ReminderWorkingDays, ct)
                            .ConfigureAwait(false))
                    {
                        r.MarkDueReminded(r.EffectiveDue);
                        await notifier.SendAsync(AuditNotifications.DueSoon, [auditor], AuditNotifier.Values(r.Number, r.ActionAr, r.ActionEn,
                            AuditDeadlines.Date(r.EffectiveDue), $"{Math.Round(r.LatestPct * 100, 0)} %"), link, scoped, ct).ConfigureAwait(false);
                        dues++;
                    }
                }
            }

            await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        kpis = await PublishKpisAsync(ct).ConfigureAwait(false);
        return new AuditMonitorResult(recomputed, late, followUps, dues, kpis);
    }

    /// <summary>Configured measures → machine drafts on the mapped KPI cards (ADR 0135). Nothing happens when no card is mapped.</summary>
    private async Task<int> PublishKpisAsync(CancellationToken ct)
    {
        await using var scope = AuditJobScope.Create(scopes);
        var sp = scope.ServiceProvider;
        var settings = await sp.GetRequiredService<AuditReader>().SettingsAsync(ct).ConfigureAwait(false);
        var feed = sp.GetService<IKpiResultFeed>();
        if (feed is null || (settings.KpiClosureRateCardId ?? settings.KpiOverdueCardId ?? settings.KpiPlanCompletionCardId ?? settings.KpiReportTimelinessCardId
                ?? settings.KpiSatisfactionCardId) is null)
        {
            return 0;
        }

        var today = await sp.GetRequiredService<AuditDeadlines>().TodayAsync(ct).ConfigureAwait(false);
        var qaip = await sp.GetRequiredService<AuditLifecycle>().QaipAsync(today.Year, null, ct).ConfigureAwait(false);
        var scoped = sp.GetRequiredService<AuditMgmtDbContext>();
        var overdue = await scoped.Findings.AsNoTracking().CountAsync(f => f.Status == AuditFindingStatuses.Late, ct).ConfigureAwait(false);
        var count = 0;
        foreach (var (card, value, note) in new (Guid?, decimal?, string)[]
                 {
                     (settings.KpiClosureRateCardId, qaip.ClosureRate * 100, "Audit findings closure rate (%)"),
                     (settings.KpiOverdueCardId, overdue, "Overdue audit findings"),
                     (settings.KpiPlanCompletionCardId, qaip.PlanCompletion * 100, "Audit plan completion (%)"),
                     (settings.KpiReportTimelinessCardId, qaip.ReportTimeliness * 100, "Audit report timeliness (%)"),
                     (settings.KpiSatisfactionCardId, qaip.Satisfaction, "Audit client satisfaction (1-5)"),
                 })
        {
            if (card is { } id && value is { } v && await feed.ProposeAsync(id, today, Math.Round(v, 2), $"{note} — Audit Management", ct).ConfigureAwait(false)
                == KpiFeedOutcomes.Drafted)
            {
                count++;
            }
        }

        return count;
    }
}

[DisallowConcurrentExecution]
internal sealed class AuditMonitorJob(RequestContext requestContext, AuditMonitorRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "auditmgmt.monitor", "متابعة إدارة التدقيق", "Audit management monitoring", "0 35 * * * ?", typeof(AuditMonitorJob),
        "يحدد التوصيات والملاحظات المتأخرة، ويذكّر المدقق المسؤول قبل مواعيد المتابعة والتنفيذ، وينشر مؤشرات التدقيق عند ضبطها.",
        "Flags late recommendations and findings, reminds the responsible auditor before follow-up and due dates, publishes the audit KPIs when configured.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var r = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"recomputed={r.Recomputed} late={r.LateNotices} followUps={r.FollowUpReminders} due={r.DueReminders} kpi={r.KpiDrafts}";
    }
}

/// <summary>A child scope that runs as the job (system principal, channel job).</summary>
internal static class AuditJobScope
{
    public static AsyncServiceScope Create(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
