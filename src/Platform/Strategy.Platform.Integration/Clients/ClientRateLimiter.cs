using System.Collections.Concurrent;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Integration.Clients;

/// <summary>Fixed one-minute windows per client, in memory (single-instance host, like the MCP limiter — ADR 0032 / 0191).</summary>
public sealed class ClientRateLimiter(IClock clock)
{
    private readonly ConcurrentDictionary<Guid, (long Window, int Count)> _windows = new();

    /// <summary>True when the call is allowed; otherwise the seconds until the window ends.</summary>
    public bool TryAcquire(Guid client, int perMinute, out int retryAfterSeconds)
    {
        var now = clock.UtcNow.ToUnixTimeSeconds();
        var window = now / 60;
        var allowed = false;
        _windows.AddOrUpdate(client, _ =>
        {
            allowed = perMinute > 0;
            return (window, 1);
        }, (_, current) =>
        {
            var count = current.Window == window ? current.Count + 1 : 1;
            allowed = count <= perMinute;
            return (window, count);
        });
        retryAfterSeconds = allowed ? 0 : (int)(60 - (now % 60));
        return allowed;
    }
}
