using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Notifications;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Scheduling.Application;

namespace Strategy.IntegrationTests.Scheduling;

/// <summary>BUILD_PLAN Phase 3: "Job dashboard shows scheduled jobs and their runs".</summary>
public class JobDashboardTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Dashboard_lists_every_scheduled_job_with_its_next_run_and_records_a_manual_run()
    {
        var admin = await api.AdminClientAsync();
        var jobs = (await NotificationTests.EventuallyAsync(
            () => admin.GetFromJsonAsync<List<JobDto>>("/api/v1/scheduling/jobs"), j => j!.All(x => x.NextRunAt is not null)))!;
        jobs.Select(j => j.Key).ShouldBe(
            ["audit.verify-chain", "docreg.validity", "files.rescan-pending", "ip.renewals", "kpi.period-windows", "notifications.digest", "org.directory-sync", "portfolio.monthly-cycle", "portfolio.communication-reminders", "scheduling.cleanup", "tasks.digest", "tasks.recurrence", "tasks.reminders", "teams.evaluation-triggers", "teams.reports", "workflow.idempotency-cleanup",
                "workflow.sla", "requests.stage-overdue", "risk.monitor"], ignoreOrder: true);
        jobs.ShouldAllBe(j => j.NameAr.Length > 0 && j.NameEn.Length > 0 && !j.Paused);

        (await admin.PostAsync("/api/v1/scheduling/jobs/audit.verify-chain/run", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var runs = await NotificationTests.EventuallyAsync(
            () => admin.GetFromJsonAsync<List<JobRunDto>>("/api/v1/scheduling/jobs/audit.verify-chain/runs"),
            r => r!.Count > 0 && r[0].Status != "running", timeoutSeconds: 30);
        runs![0].Status.ShouldBe("succeeded");
        runs[0].Message!.ShouldStartWith("ok entries=");
        runs[0].TriggeredBy.ShouldNotBeNull();

        var listed = (await admin.GetFromJsonAsync<List<JobDto>>("/api/v1/scheduling/jobs"))!.Single(j => j.Key == "audit.verify-chain");
        listed.LastRun!.Id.ShouldBe(runs[0].Id);

        var audit = await admin.GetFromJsonAsync<PagedResult<Strategy.Platform.Audit.AuditEntryDto>>("/api/v1/audit/entries?action=job.run");
        audit!.Items.ShouldContain(e => e.RecordId == "audit.verify-chain");
    }

    [Fact]
    public async Task A_job_can_be_paused_and_resumed_and_unknown_jobs_are_404()
    {
        var admin = await api.AdminClientAsync();
        (await admin.PostAsync("/api/v1/scheduling/jobs/org.directory-sync/pause", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<JobDto>>("/api/v1/scheduling/jobs"))!.Single(j => j.Key == "org.directory-sync").Paused.ShouldBeTrue();
        (await admin.PostAsync("/api/v1/scheduling/jobs/org.directory-sync/resume", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<JobDto>>("/api/v1/scheduling/jobs"))!.Single(j => j.Key == "org.directory-sync").Paused.ShouldBeFalse();
        (await admin.PostAsync("/api/v1/scheduling/jobs/no.such-job/run", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var outbox = await admin.GetFromJsonAsync<OutboxStatusDto>("/api/v1/scheduling/outbox");
        outbox!.Dead.ShouldBe(0);

        var viewer = await api.Factory.CreateSignedInClientAsync();
        (await viewer.GetAsync("/api/v1/scheduling/jobs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var readOnly = await api.ClientWithOnlyAsync("settings.job.view");
        (await readOnly.GetAsync("/api/v1/scheduling/jobs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await readOnly.PostAsync("/api/v1/scheduling/jobs/scheduling.cleanup/run", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
