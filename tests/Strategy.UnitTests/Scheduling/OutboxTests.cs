using Strategy.Platform.Scheduling.Application;

namespace Strategy.UnitTests.Scheduling;

public class OutboxTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 1800)]
    [InlineData(5, 7200)]
    [InlineData(6, 21600)]
    [InlineData(9, 21600)]
    public void Retries_back_off(int attempts, int seconds) => OutboxDispatcher.DelayAfter(attempts).ShouldBe(TimeSpan.FromSeconds(seconds));
}
