using Strategy.Modules.DocumentRegistry.Reporting;
using Strategy.Modules.Requests.Reporting;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Reporting;
using Strategy.Modules.Tasks.Reporting;
using Strategy.Platform.Reporting;

namespace Strategy.UnitTests.Reporting;

/// <summary>Pure logic of the module reporting sources (docs/design/reporting.md §2.5, ADR 0141): movement, severe bands, partitions, merging.</summary>
public sealed class ModuleReportingSourceLogicTests
{
    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, 12, RiskMovement.Unchanged)]
    [InlineData(12, 12, RiskMovement.Unchanged)]
    [InlineData(16, 12, RiskMovement.Improved)]
    [InlineData(6, 12, RiskMovement.Worsened)]
    public void Direction_compares_the_residual_score_with_the_baseline(int? baseline, int current, string expected) =>
        RiskMovement.Direction(baseline, current).ShouldBe(expected);

    [Fact]
    public void Monthly_movement_counts_each_reassessed_month_against_the_last_score_before_it()
    {
        // Registered in January at 16, improved to 12 in March (twice: 14 then 12), unchanged review in April, worsened to 20 in June.
        var history = new[]
        {
            (At(2026, 1, 10), 16m), (At(2026, 3, 2), 14m), (At(2026, 3, 20), 12m), (At(2026, 4, 15), 12m), (At(2026, 6, 1), 20m),
        };
        RiskMovement.Monthly(history, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)).ShouldBe(
        [
            (new DateOnly(2026, 3, 1), RiskMovement.Improved),
            (new DateOnly(2026, 4, 1), RiskMovement.Unchanged),
            (new DateOnly(2026, 6, 1), RiskMovement.Worsened),
        ]);

        // The month of registration has no baseline; months outside the range are not emitted.
        RiskMovement.Monthly(history, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)).ShouldBeEmpty();
        RiskMovement.Monthly(history, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)).Single().Direction.ShouldBe(RiskMovement.Worsened);
    }

    [Fact]
    public void Severe_bands_are_the_two_highest_whatever_their_order_or_codes()
    {
        RiskMetricSource.SevereBands(DefaultFramework.Content.Bands).ShouldBe(["critical", "high"], ignoreOrder: true);
        var custom = new[]
        {
            new RatingBand("x3", null, "Extreme", "#000", 20, 25, ResidualStatuses.ActiveManagement),
            new RatingBand("x1", null, "Minor", "#fff", 1, 9, ResidualStatuses.NoIssue),
            new RatingBand("x2", null, "Major", "#888", 10, 19, ResidualStatuses.ContinuousReview),
        };
        RiskMetricSource.SevereBands(custom).ShouldBe(["x3", "x2"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("[\"strategic\",\"operational\"]", "strategic")]
    [InlineData("[]", RiskMetricSource.NoCategory)]
    [InlineData(null, RiskMetricSource.NoCategory)]
    public void First_category_is_the_one_the_dashboard_counts(string? json, string expected) => RiskMetricSource.FirstCategory(json).ShouldBe(expected);

    [Fact]
    public void Facts_with_the_same_key_are_merged_and_different_partitions_stay_apart()
    {
        var facts = new RiskFacts();
        var unit = Guid.CreateVersion7();
        var month = new DateOnly(2026, 11, 1);
        facts.Add("risk.risks", month, unit, RiskFacts.Dims(("band", "high"), ("category", "none")), 1);
        facts.Add("risk.risks", month, unit, RiskFacts.Dims(("category", "none"), ("band", "high")), 2);
        facts.Add("risk.risks", month, unit, RiskFacts.Dims(("band", "high"), ("category", "none")), 4, partition: "confidential");
        facts.Add("tasks.on-time", month, null, RiskFacts.Dims(), 1, 2);
        facts.Add("tasks.on-time", month, null, RiskFacts.Dims(), 0, 3);
        var list = facts.ToList();
        list.Single(f => f.Metric == "risk.risks" && f.Partition == ReportingPartitions.Public).Value.ShouldBe(3);
        list.Single(f => f.Metric == "risk.risks" && f.Partition == "confidential").Value.ShouldBe(4);
        var ratio = list.Single(f => f.Metric == "tasks.on-time");
        (ratio.Value, ratio.Weight).ShouldBe((1m, 5m));
    }

    [Theory]
    [InlineData(true, "secret", ReportingPartitions.Public)]
    [InlineData(false, "internal", ReportingPartitions.Public)]
    [InlineData(false, "confidential", "sensitivity:confidential")]
    [InlineData(false, null, "sensitivity:none")]
    public void Document_partition_is_public_only_when_every_reader_sees_it(bool shareToAll, string? sensitivity, string expected) =>
        DocRegMetricSource.PartitionOf(shareToAll, sensitivity, ["public", "internal"]).ShouldBe(expected);

    [Fact]
    public void Confidential_tasks_have_their_own_partition_and_cycle_time_is_in_days()
    {
        TaskMetricSource.PartitionOf(true).ShouldBe(ReportingPartitions.Confidential);
        TaskMetricSource.PartitionOf(false).ShouldBe(ReportingPartitions.Public);
        RequestMetricSource.CycleTime(At(2026, 3, 1), At(2026, 3, 4).AddHours(12)).ShouldBe(3.5m);
        RequestMetricSource.CycleTime(At(2026, 3, 4), At(2026, 3, 1)).ShouldBe(0m);
    }
}
