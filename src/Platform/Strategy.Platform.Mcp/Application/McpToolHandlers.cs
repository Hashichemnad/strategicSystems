using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Mcp.Application;

/// <summary>
/// <c>tools/list</c> and <c>tools/call</c> from the tool registry (ADR 0031): a tool is offered when MCP has it enabled (read tools by default,
/// write / approve only when an administrator enables them), its system is enabled and the token owner holds its permission. Calls run with
/// the token owner's principal, are rate limited and audited (channel <c>mcp</c>).
/// </summary>
internal static class McpToolHandlers
{
    public static bool IsEnabled(McpSettings settings, ToolDefinition definition) =>
        settings.Tool(definition.Name).Enabled ?? (definition.DefaultEnabled && definition.Kind == ToolKind.Read);

    public static async ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken ct)
    {
        var services = RequestServices(request.Services);
        var settings = await services.GetRequiredService<ISettingsStore>().GetAsync<McpSettings>(ct).ConfigureAwait(false);
        var catalog = services.GetRequiredService<ToolCatalog>();
        var user = services.GetRequiredService<IRequestContext>().User;
        var tools = (await catalog.AvailableAsync(user, ct).ConfigureAwait(false)).Where(t => IsEnabled(settings, t.Definition));
        return new ListToolsResult
        {
            Tools = tools.Select(t => new Tool
            {
                Name = t.Definition.Name,
                Title = t.Definition.TitleEn,
                Description = t.Definition.Description,
                InputSchema = t.Definition.InputSchema,
                Annotations = new ToolAnnotations
                {
                    Title = t.Definition.TitleEn,
                    ReadOnlyHint = t.Definition.Kind == ToolKind.Read,
                    DestructiveHint = false,
                    OpenWorldHint = false,
                },
            }).ToList(),
        };
    }

    public static async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        var services = RequestServices(request.Services);
        var settings = await services.GetRequiredService<ISettingsStore>().GetAsync<McpSettings>(ct).ConfigureAwait(false);
        var catalog = services.GetRequiredService<ToolCatalog>();
        var executor = services.GetRequiredService<ToolExecutor>();
        var context = services.GetRequiredService<IRequestContext>();
        var name = request.Params?.Name ?? string.Empty;
        var arguments = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
        var client = context.User.FindFirst(StrategyClaims.Client)?.Value;
        var tool = catalog.Find(name);
        if (tool is null || !IsEnabled(settings, tool.Definition) || !await catalog.IsAvailableAsync(tool, context.User, ct).ConfigureAwait(false))
        {
            await executor.WriteRefusalAsync(name.Length > 100 ? name[..100] : name, arguments, Channels.Mcp, client, ToolErrors.Disabled).ConfigureAwait(false);
            return Error(ToolErrors.Disabled, $"Unknown or disabled tool: {name}");
        }

        var limiter = services.GetRequiredService<McpRateLimiter>();
        if (!limiter.TryAcquire(context.UserId ?? Guid.Empty, name, settings.RateLimitPerMinute, settings.Tool(name).RateLimitPerMinute))
        {
            await executor.WriteRefusalAsync(name, arguments, Channels.Mcp, client, ToolErrors.RateLimited).ConfigureAwait(false);
            return Error(ToolErrors.RateLimited, "Too many calls; try again in a minute.");
        }

        var language = services.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers.AcceptLanguage.ToString()
            .StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true ? "ar" : "en";
        var result = await executor.InvokeAsync(tool, arguments, new ToolContext(context.User, Channels.Mcp, language), client, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            return Error(result.ErrorCode ?? ToolErrors.Failed, result.Message ?? "The tool failed.");
        }

        var data = JsonSerializer.SerializeToElement(result.Data, JsonSerializerOptions.Web);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = data.GetRawText() }],
            StructuredContent = data.ValueKind == JsonValueKind.Object ? data : null,
            IsError = false,
        };
    }

    private static CallToolResult Error(string code, string message)
    {
        var data = JsonSerializer.SerializeToElement(new { error = code, message });
        return new CallToolResult { Content = [new TextContentBlock { Text = data.GetRawText() }], StructuredContent = data, IsError = true };
    }

    /// <summary>The HTTP request's scope (its request context holds the token owner and the channel), whatever scope the SDK created.</summary>
    private static IServiceProvider RequestServices(IServiceProvider? services) =>
        services?.GetService<IHttpContextAccessor>()?.HttpContext?.RequestServices ?? services ?? throw new InvalidOperationException("No services.");
}
