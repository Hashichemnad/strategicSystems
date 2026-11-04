using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Strategy.Modules.Benchmarking.Application;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.Benchmarking.Jobs;

public sealed record BenchMonitorResult(int SearchNotices, int ReadyNotices, int ImpactNotices);

/// <summary>
/// Monitoring (ADR 0127 / 0128): the pre-quarter search notice, the ready-for-closure catch-up and the impact follow-up notice. Each once (stamps).
/// </summary>
internal sealed class BenchMonitorRunner(BenchDbContext db, IServiceScopeFactory scopes, IClock clock)
{
    public async Task<BenchMonitorResult> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        int search = 0, ready = 0, impact = 0;
        var searchIds = await db.Studies.AsNoTracking()
            .Where(s => s.Status == StudyStatuses.Active && s.Stage == StudyStages.Search && !s.SearchSubmitted && s.SearchNotifiedAt == null && s.PendingProcess == null)
            .Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in searchIds)
        {
            await using var scope = BenchJobScope.Create(scopes);
            var sp = scope.ServiceProvider;
            var scoped = sp.GetRequiredService<BenchDbContext>();
            var settings = await sp.GetRequiredService<BenchReader>().SettingsAsync(ct).ConfigureAwait(false);
            var study = await scoped.Studies.FirstAsync(s => s.Id == id, ct).ConfigureAwait(false);
            var (day, from) = await sp.GetRequiredService<BenchCalendar>().SearchNoticeAsync(study.ResponsibleUnitId, study.TargetYear, study.TargetQuarter,
                settings.SearchNoticeDays, settings.CountWorkingDays, ct).ConfigureAwait(false);
            if (now < from)
            {
                continue;
            }

            await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            study.MarkSearchNotified(now);
            await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
            var head = await sp.GetRequiredService<BenchReader>().HeadOfUnitAsync(study.ResponsibleUnitId, ct).ConfigureAwait(false);
            await sp.GetRequiredService<BenchNotifier>().SendAsync(BenchNotifications.SearchDue, [study.ResponsibleEmployeeId, head],
                BenchNotifier.Values(study, BenchCalendar.Date(Quarters.Start(study.TargetYear, study.TargetQuarter))), BenchNotifier.Link(study, "search"), scoped, ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _ = day;
            search++;
        }

        var readyIds = await db.Studies.AsNoTracking()
            .Where(s => s.Status == StudyStatuses.Active && s.AnalysisApprovedAt != null && s.ReadyNotifiedAt == null).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var id in readyIds)
        {
            await using var scope = BenchJobScope.Create(scopes);
            if (await ReadyCheck.RunAsync(scope.ServiceProvider, id, ct).ConfigureAwait(false))
            {
                ready++;
            }
        }

        await using (var scope = BenchJobScope.Create(scopes))
        {
            var sp = scope.ServiceProvider;
            var scoped = sp.GetRequiredService<BenchDbContext>();
            var today = await sp.GetRequiredService<BenchCalendar>().TodayAsync(ct).ConfigureAwait(false);
            var due = await scoped.Studies.Where(s => s.Status == StudyStatuses.Completed && s.ImpactDueOn != null && s.ImpactDueOn <= today && s.ImpactNotifiedAt == null)
                .Take(200).ToListAsync(ct).ConfigureAwait(false);
            if (due.Count > 0)
            {
                await using var transaction = await scoped.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                foreach (var study in due)
                {
                    study.MarkImpactNotified(now);
                    await sp.GetRequiredService<BenchNotifier>().SendAsync(BenchNotifications.ImpactDue, [study.ResponsibleEmployeeId], BenchNotifier.Values(study),
                        BenchNotifier.Link(study, "impact"), scoped, ct, [BenchPermissions.OfficeRoleKey]).ConfigureAwait(false);
                    impact++;
                }

                await scoped.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }

        return new BenchMonitorResult(search, ready, impact);
    }
}

[DisallowConcurrentExecution]
internal sealed class BenchMonitorJob(RequestContext requestContext, BenchMonitorRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "bench.monitor", "متابعة المقارنات المعيارية", "Benchmarking monitoring", "0 35 * * * ?", typeof(BenchMonitorJob),
        "يرسل تذكير نموذج البحث قبل الربع المستهدف، وإشعار جاهزية الإغلاق، وتذكير متابعة الأثر.",
        "Sends the search-form notice before the target quarter, the ready-for-closure notice and the impact follow-up reminder.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var r = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return $"search={r.SearchNotices} ready={r.ReadyNotices} impact={r.ImpactNotices}";
    }
}

/// <summary>A child scope that runs as the job (system principal, channel job).</summary>
internal static class BenchJobScope
{
    public static AsyncServiceScope Create(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, Platform.Access.StrategyClaims.CreateSystemPrincipal());
        return scope;
    }
}
