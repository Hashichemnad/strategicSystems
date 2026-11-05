using NSubstitute;
using Quartz;
using Strategy.Platform.Scheduling.Application;

namespace Strategy.UnitTests.Scheduling;

public class ScheduleSwitchTests
{
    private static ITrigger TriggerIn(string group)
    {
        var trigger = Substitute.For<ITrigger>();
        trigger.Key.Returns(new TriggerKey("some.job", group));
        return trigger;
    }

    [Theory]
    [InlineData(true, JobScheduleSynchroniser.Group, false)] // schedules on: cron runs execute
    [InlineData(false, JobScheduleSynchroniser.Group, true)] // schedules off: cron runs are vetoed
    [InlineData(false, "MANUAL_TRIGGER", false)] // runs started on request always execute
    public async Task Only_schedule_fired_runs_are_vetoed_and_only_when_schedules_are_off(bool runSchedules, string group, bool vetoed)
    {
        var listener = new Strategy.Platform.Scheduling.ScheduleSwitchListener(new Strategy.Platform.Scheduling.ScheduleSwitch(runSchedules));
        (await listener.VetoJobExecution(TriggerIn(group), Substitute.For<IJobExecutionContext>(), TestContext.Current.CancellationToken)).ShouldBe(vetoed);
    }
}
