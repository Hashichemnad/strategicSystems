using System.Text.Json.Serialization;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Mcp.Application;

/// <summary>Settings → MCP (docs/design/mcp.md §4). The server is off on a new installation.</summary>
public sealed record McpSettings(
    bool Enabled = false,
    bool AllowDynamicRegistration = true,
    IReadOnlyList<string>? AllowedRedirectHosts = null,
    int AccessTokenMinutes = 60,
    int RefreshTokenDays = 30,
    int RateLimitPerMinute = 60,
    IReadOnlyDictionary<string, McpToolSettings>? Tools = null) : ISettingsArea
{
    public static string Key => "mcp";

    public static readonly IReadOnlyList<string> DefaultRedirectHosts = ["localhost", "127.0.0.1", "claude.ai", "claude.com"];

    [JsonIgnore]
    public IReadOnlyList<string> RedirectHosts => AllowedRedirectHosts ?? DefaultRedirectHosts;

    public McpToolSettings Tool(string name) => Tools?.GetValueOrDefault(name) ?? new McpToolSettings();
}

/// <summary>Per tool: null keeps the tool's default (read on, write and approve off).</summary>
public sealed record McpToolSettings(bool? Enabled = null, int? RateLimitPerMinute = null);
