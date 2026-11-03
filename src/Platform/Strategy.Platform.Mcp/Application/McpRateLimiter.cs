using System.Collections.Concurrent;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Mcp.Application;

/// <summary>
/// Tool-call limits per user per minute (all tools) and per user per tool, in fixed one-minute windows held in memory. The host is single
/// instance today (ADR 0011); a multi-node install would move the counters to a shared store (ADR 0032).
/// </summary>
public sealed class McpRateLimiter(IClock clock)
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public bool TryAcquire(Guid userId, string tool, int perUserLimit, int? perToolLimit)
    {
        var minute = clock.UtcNow.ToUnixTimeSeconds() / 60;
        if (_windows.Count > 10_000)
        {
            foreach (var stale in _windows.Where(w => w.Value.Minute < minute).Select(w => w.Key).ToList())
            {
                _windows.TryRemove(stale, out _);
            }
        }

        var all = _windows.GetOrAdd($"{userId:N}", _ => new Window());
        var one = _windows.GetOrAdd($"{userId:N}:{tool}", _ => new Window());
        lock (all)
        {
            lock (one)
            {
                all.Roll(minute);
                one.Roll(minute);
                if (all.Count >= perUserLimit || (perToolLimit is { } limit && one.Count >= limit))
                {
                    return false;
                }

                all.Count++;
                one.Count++;
                return true;
            }
        }
    }

    private sealed class Window
    {
        public long Minute { get; private set; }

        public int Count { get; set; }

        public void Roll(long minute)
        {
            if (Minute != minute)
            {
                Minute = minute;
                Count = 0;
            }
        }
    }
}
