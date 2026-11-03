using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Mcp.Application;
using Strategy.Platform.Mcp.OAuth;

namespace Strategy.UnitTests.Mcp;

public sealed class RedirectRulesTests
{
    private static readonly IReadOnlyList<string> Hosts = McpSettings.DefaultRedirectHosts;

    [Theory]
    [InlineData("http://127.0.0.1:33418/callback", true)]
    [InlineData("http://localhost:6274/oauth/callback", true)]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://CLAUDE.COM/cb", true)]
    [InlineData("http://claude.ai/cb", false)] // http only for loopback
    [InlineData("https://evil.example/cb", false)]
    [InlineData("https://claude.ai.evil.example/cb", false)]
    [InlineData("https://claude.ai/cb#fragment", false)]
    [InlineData("cursor://callback", false)]
    [InlineData("not a uri", false)]
    public void Default_hosts(string uri, bool allowed) => AuthorizationEndpoints.RedirectAllowed(uri, Hosts).ShouldBe(allowed);

    [Theory]
    [InlineData("https://app.example.org/cb", "*.example.org", true)]
    [InlineData("https://example.org/cb", "*.example.org", false)]
    [InlineData("https://anything.test/cb", "*", true)]
    public void Wildcards(string uri, string host, bool allowed) => AuthorizationEndpoints.RedirectAllowed(uri, [host]).ShouldBe(allowed);
}

public sealed class McpRateLimiterTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 3, 9, 0, 5, TimeSpan.Zero);
    }

    [Fact]
    public void Limits_per_user_per_tool_and_per_user_in_one_minute_windows()
    {
        var clock = new Clock();
        var limiter = new McpRateLimiter(clock);
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        limiter.TryAcquire(alice, "search", 3, 2).ShouldBeTrue();
        limiter.TryAcquire(alice, "search", 3, 2).ShouldBeTrue();
        limiter.TryAcquire(alice, "search", 3, 2).ShouldBeFalse(); // per-tool limit
        limiter.TryAcquire(alice, "whoami", 3, null).ShouldBeTrue();
        limiter.TryAcquire(alice, "whoami", 3, null).ShouldBeFalse(); // per-user limit (3 calls)
        limiter.TryAcquire(bob, "search", 3, 2).ShouldBeTrue(); // other users unaffected

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        limiter.TryAcquire(alice, "search", 3, 2).ShouldBeTrue(); // next window
    }
}
