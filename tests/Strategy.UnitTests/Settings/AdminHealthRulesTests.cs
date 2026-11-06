using Strategy.Platform.Files.Application;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Notifications.Application;
using Strategy.Platform.Scheduling.Application;

namespace Strategy.UnitTests.Settings;

/// <summary>ADR 0151: status rules of the administrator Health page.</summary>
public class AdminHealthRulesTests
{
    [Theory]
    [InlineData(new[] { "ok", "notConfigured" }, "ok")]
    [InlineData(new[] { "ok", "warning", "notConfigured" }, "warning")]
    [InlineData(new[] { "warning", "error", "ok" }, "error")]
    [InlineData(new string[0], "ok")]
    public void Overall_status_is_the_worst_check(string[] statuses, string expected) => AdminHealthStatuses.Worst(statuses).ShouldBe(expected);

    [Theory]
    [InlineData(null, 0, false, true, true, "ok", "health.job.notRunYet")]
    [InlineData("succeeded", 0, false, true, true, "ok", "health.job.ok")]
    [InlineData("succeeded", 2, false, true, true, "warning", "health.job.recentFailures")]
    [InlineData("failed", 1, false, true, true, "error", "health.job.lastFailed")]
    [InlineData("failed", 1, true, true, true, "warning", "health.job.paused")]
    [InlineData("succeeded", 0, false, false, true, "warning", "health.job.noNextRun")]
    [InlineData("succeeded", 0, false, false, false, "error", "health.job.notScheduled")]
    public void Job_status(string? last, int failures, bool paused, bool hasNext, bool scheduled, string status, string key) =>
        SchedulingHealthContributor.JobStatus(last, failures, paused, hasNext, scheduled).ShouldBe((status, key));

    [Theory]
    [InlineData(10, 0, "sent", "ok", "health.email.ok")]
    [InlineData(0, 0, null, "ok", "health.email.idle")]
    [InlineData(10, 1, "sent", "warning", "health.email.someFailed")]
    [InlineData(10, 1, "failed", "error", "health.email.lastFailed")]
    public void Email_status(int sent, int failed, string? last, string status, string key) =>
        EmailHealthContributor.Status(sent, failed, last).ShouldBe((status, key));

    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(50 * Gb, 100 * Gb, "ok")]
    [InlineData(10 * Gb, 100 * Gb, "warning")] // below 15 %
    [InlineData(1 * Gb, 4 * Gb, "warning")] // below 2 GB
    [InlineData(4 * Gb, 100 * Gb, "error")] // below 5 %
    [InlineData(Gb / 4, 2 * Gb, "error")] // below 500 MB
    public void Disk_status(long free, long total, string expected) => FilesHealthContributor.DiskStatus(free, total, 15).ShouldBe(expected);

    [Fact]
    public void Params_are_invariant_strings_and_nulls_are_left_out()
    {
        var at = new DateTimeOffset(2026, 11, 6, 8, 30, 0, TimeSpan.FromHours(4));
        var values = HealthParams.Of(("count", 1234.5m), ("at", at), ("flag", true), ("missing", null));
        values["count"].ShouldBe("1234.5");
        values["at"].ShouldBe("2026-11-06T04:30:00.0000000+00:00");
        values["flag"].ShouldBe("true");
        values.ContainsKey("missing").ShouldBeFalse();
    }
}
