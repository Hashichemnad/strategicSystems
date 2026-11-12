using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Audit;

namespace Strategy.Platform.AI.Application;

/// <summary>
/// Every tool registered by components and modules (ADR 0031). Start-up guard: an approve tool may never be enabled by default.
/// <see cref="AvailableAsync"/> applies what every caller shares — the tool's system is enabled and the user holds its permission; the MCP server
/// adds its own admin switches on top.
/// </summary>
public sealed class ToolCatalog(IEnumerable<IAppTool> tools, IAccessService access, ISystemState systems)
{
    private readonly IReadOnlyList<IAppTool> _tools = Validate(tools);

    public IReadOnlyList<IAppTool> All => _tools;

    public IAppTool? Find(string name) => _tools.FirstOrDefault(t => t.Definition.Name == name);

    public async Task<IReadOnlyList<IAppTool>> AvailableAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var result = new List<IAppTool>();
        foreach (var tool in _tools)
        {
            if (await IsAvailableAsync(tool, user, ct).ConfigureAwait(false))
            {
                result.Add(tool);
            }
        }

        return result;
    }

    public async Task<bool> IsAvailableAsync(IAppTool tool, ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return user.IsInternal()
            && await systems.IsEnabledAsync(tool.Definition.SystemCode, ct).ConfigureAwait(false)
            && (tool.Definition.Permission is not { } permission || await access.CanAsync(user, permission, cancellationToken: ct).ConfigureAwait(false));
    }

    private static List<IAppTool> Validate(IEnumerable<IAppTool> tools)
    {
        var list = tools.ToList();
        foreach (var tool in list)
        {
            if (tool.Definition.Kind == ToolKind.Approve && tool.Definition.DefaultEnabled)
            {
                throw new InvalidOperationException($"Approve tool '{tool.Definition.Name}' must not be enabled by default (SPEC §0.5).");
            }
        }

        var duplicate = list.GroupBy(t => t.Definition.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? list : throw new InvalidOperationException($"Tool '{duplicate.Key}' is registered twice.");
    }
}

/// <summary>Runs a tool for a caller and writes the audit event (<c>&lt;channel&gt;.tool.call</c>, channel <c>mcp</c> or <c>ai</c>).</summary>
public sealed partial class ToolExecutor(IAuditLog audit, ILogger<ToolExecutor> logger)
{
    public const int MaxLoggedArguments = 500;

    public async Task<ToolResult> InvokeAsync(IAppTool tool, JsonElement arguments, ToolContext context, string? client, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);
        var watch = Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = await tool.InvokeAsync(arguments, context, ct).ConfigureAwait(false);
        }
        catch (Strategy.Platform.Kernel.Integration.RecordReadOnlyException)
        {
            // Standby (ADR 0206): the record is synced from a source system and read-only while that system is primary.
            result = ToolResult.Fail(ToolErrors.ReadOnly, "The record is synced from its source system and read-only while that system is primary.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogToolFailed(logger, tool.Definition.Name, exception);
            result = ToolResult.Fail(ToolErrors.Failed, "The tool failed.");
        }

        await WriteAuditAsync(tool.Definition.Name, tool.Definition.SystemCode, arguments, context.Channel, client, result.Success,
            result.Success ? "ok" : result.ErrorCode, (int)watch.ElapsedMilliseconds).ConfigureAwait(false);
        return result;
    }

    /// <summary>Audit of a call refused before the tool ran (disabled, rate limited, forbidden).</summary>
    public Task WriteRefusalAsync(string tool, JsonElement arguments, string channel, string? client, string status) =>
        WriteAuditAsync(tool, null, arguments, channel, client, false, status, 0);

    private Task WriteAuditAsync(string tool, string? system, JsonElement arguments, string channel, string? client, bool success, string? status, int ms)
    {
        var args = arguments.ValueKind == JsonValueKind.Undefined ? "{}" : arguments.GetRawText();
        if (args.Length > MaxLoggedArguments)
        {
            args = args[..MaxLoggedArguments] + "…";
        }

        return audit.WriteAsync(new AuditEvent($"{channel}.tool.call", success, system ?? channel, "tool", tool,
            new { tool, client, arguments = args, status, durationMs = ms })
        { Channel = channel }, CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {Tool} failed")]
    private static partial void LogToolFailed(ILogger logger, string tool, Exception exception);
}
