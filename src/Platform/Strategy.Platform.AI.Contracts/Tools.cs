using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.AI.Tools;

/// <summary>Read tools are on by default; write tools are opt-in; approve tools are never enabled by default (SPEC §0.5, ADR 0031).</summary>
public enum ToolKind
{
    Read = 1,
    Write = 2,
    Approve = 3,
}

/// <summary>
/// A tool exposed to MCP clients (and, for read tools, to "Ask the system"). <c>Permission</c> (optional) must be held for the tool to
/// be listed; the tool still checks access to each record it touches.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string SystemCode,
    ToolKind Kind,
    string TitleAr,
    string TitleEn,
    string Description,
    JsonElement InputSchema,
    string? Permission = null,
    bool DefaultEnabled = true);

/// <summary>The caller of a tool: the token owner (MCP) or the asking user (AI), with the channel for the audit trail.</summary>
public sealed record ToolContext(ClaimsPrincipal User, string Channel, string Language);

/// <summary>A tool outcome. Errors use stable codes: <c>not_found</c>, <c>forbidden</c>, <c>validation</c>, <c>disabled</c>, <c>rate_limited</c>.</summary>
public sealed record ToolResult(bool Success, object? Data, string? ErrorCode = null, string? Message = null)
{
    public static ToolResult Ok(object? data) => new(true, data);

    public static ToolResult Fail(string code, string message) => new(false, null, code, message);

    public static ToolResult NotFound() => Fail(ToolErrors.NotFound, "The record does not exist or you may not see it.");
}

public static class ToolErrors
{
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string Validation = "validation";
    public const string Disabled = "disabled";
    public const string RateLimited = "rate_limited";
    public const string Failed = "failed";

    /// <summary>The record is read-only while its source system is primary (standby sync, ADR 0206).</summary>
    public const string ReadOnly = "read_only";
}

public interface IAppTool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken);
}

public static class ToolRegistrationExtensions
{
    public static IServiceCollection AddAppTool<TTool>(this IServiceCollection services)
        where TTool : class, IAppTool => services.AddScoped<IAppTool, TTool>();

    /// <summary>Parses a JSON schema literal for <see cref="ToolDefinition.InputSchema"/>.</summary>
    public static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Reads typed arguments of a tool call.</summary>
public static class ToolArguments
{
    public static string? Text(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Number(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : null;

    public static bool? Flag(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public static Guid? Id(this JsonElement arguments, string name) =>
        System.Guid.TryParse(arguments.Text(name), out var id) ? id : null;
}
