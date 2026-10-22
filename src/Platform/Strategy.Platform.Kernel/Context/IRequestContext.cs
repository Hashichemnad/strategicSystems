using System.Security.Claims;

namespace Strategy.Platform.Kernel.Context;

/// <summary>
/// Ambient facts about the current unit of work (HTTP request, job run, import), used by audit,
/// logging and access checks. Set once at the entry point (middleware, job runner) and read everywhere else.
/// </summary>
public interface IRequestContext
{
    string CorrelationId { get; }

    /// <summary>One of <see cref="Channels"/>.</summary>
    string Channel { get; }

    ClaimsPrincipal User { get; }

    /// <summary>Employee / user id of the caller, or null for anonymous and system work.</summary>
    Guid? UserId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }
}

/// <summary>Mutable implementation registered per scope; entry points call <see cref="Initialise"/>.</summary>
public sealed class RequestContext : IRequestContext
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public string CorrelationId { get; private set; } = Guid.CreateVersion7().ToString("N");

    public string Channel { get; private set; } = Channels.System;

    public ClaimsPrincipal User { get; private set; } = Anonymous;

    public Guid? UserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public void Initialise(
        string correlationId,
        string channel,
        ClaimsPrincipal? user = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        CorrelationId = correlationId;
        Channel = Channels.IsKnown(channel) ? channel : throw new ArgumentException($"Unknown channel '{channel}'.", nameof(channel));
        User = user ?? Anonymous;
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }
}

/// <summary>How a change entered the system (CLAUDE.md §8). Stored on every audit entry.</summary>
public static class Channels
{
    public const string Ui = "ui";
    public const string Api = "api";
    public const string Mcp = "mcp";
    public const string Ai = "ai";
    public const string Job = "job";
    public const string Import = "import";
    public const string Wasel = "wasel";
    public const string System = "system";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Ui, Api, Mcp, Ai, Job, Import, Wasel, System };

    public static bool IsKnown(string channel) => All.Contains(channel);
}
