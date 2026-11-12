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

    /// <summary>The person the caller acts for (delegation, "on behalf of"); set by the approval engine.</summary>
    Guid? OnBehalfOfId { get; }

    string? OnBehalfOfName { get; }
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

    public Guid? OnBehalfOfId { get; private set; }

    public string? OnBehalfOfName { get; private set; }

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

    /// <summary>Records that the caller acts on behalf of another person (delegation); stored on every audit entry.</summary>
    public void SetOnBehalfOf(Guid? id, string? name)
    {
        OnBehalfOfId = id;
        OnBehalfOfName = id is null ? null : name;
    }

    /// <summary>Sets the authenticated caller once authentication has run (after <see cref="Initialise"/>).</summary>
    public void SetUser(ClaimsPrincipal user) => User = user;

    /// <summary>Sets the channel when the entry point learns it later, e.g. a personal access token makes a call <c>api</c>.</summary>
    public void SetChannel(string channel) =>
        Channel = Channels.IsKnown(channel) ? channel : throw new ArgumentException($"Unknown channel '{channel}'.", nameof(channel));
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

    /// <summary>Writes of a one-way sync from a source system (ADR 0200); the only channel that may write synced records in standby.</summary>
    public const string Sync = "sync";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { Ui, Api, Mcp, Ai, Job, Import, Wasel, System, Sync };

    public static bool IsKnown(string channel) => All.Contains(channel);
}
