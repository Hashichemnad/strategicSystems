using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.AI.Features;

public sealed record AskTurn(string? Role, string? Text);

public sealed record AskInput(string? Question, IReadOnlyList<AskTurn>? History);

public sealed record AskReferenceDto(string ResourceType, Guid Id, string? Number, string? TitleAr, string? TitleEn, string Link);

public sealed record AskToolCallDto(string Tool, bool Success);

public sealed record AskAnswerDto(string Answer, IReadOnlyList<AskReferenceDto> References, IReadOnlyList<AskToolCallDto> ToolCalls, string Model, Guid UsageId);

/// <summary>
/// "Ask the system" (docs/design/ai.md §4.3): the model may call the <b>read</b> tools the user can use, executed with the user's own principal
/// through the same access checks as the UI. At most <see cref="MaxRounds"/> rounds; write and approve tools are never offered.
/// </summary>
public sealed class AskService(AiGateway gateway, ToolCatalog catalog, ToolExecutor executor, PromptLibrary prompts, IRequestContext context)
{
    public const int MaxRounds = 5;
    public const int MaxQuestion = 1000;
    public const int MaxToolResultCharacters = 8000;

    public async Task<Result<AskAnswerDto>> AskAsync(AskInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Question) || input.Question.Length > MaxQuestion)
        {
            return Error.Validation([new FieldError("question", "validation.length", "validation.length",
                new Dictionary<string, object?> { ["max"] = MaxQuestion })]);
        }

        var prepared = await gateway.PrepareAsync(AiFeatures.Ask, createClient: true, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        using var call = prepared.Value;
        var sharing = call.Settings.Sharing;
        var masker = sharing.MaskPersonalData ? new PersonalDataMasker([]) : null;
        var toolContext = new ToolContext(context.User, Channels.Ai, call.Language);
        var references = new List<AskReferenceDto>();
        var calls = new List<AskToolCallDto>();
        var tools = (await catalog.AvailableAsync(context.User, ct).ConfigureAwait(false))
            .Where(t => t.Definition.Kind == ToolKind.Read)
            .Select(t => (AITool)new ToolFunction(t, executor, toolContext, sharing, masker, references, calls))
            .ToList();

        var template = prompts.Get(AiFeatures.Ask);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = call.Language == "ar" ? "Arabic" : "English",
            ["today"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["schema"] = string.Empty,
            ["documents"] = string.Empty,
            ["nonce"] = string.Empty,
        };
        var messages = new List<ChatMessage> { new(ChatRole.System, PromptLibrary.Render(template.System, values)) };
        foreach (var turn in (input.History ?? []).TakeLast(10).Where(t => !string.IsNullOrWhiteSpace(t.Text)))
        {
            var text = turn.Text!.Length > 2000 ? turn.Text[..2000] : turn.Text;
            messages.Add(new ChatMessage(turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, masker?.Mask(text) ?? text));
        }

        messages.Add(new ChatMessage(ChatRole.User, masker?.Mask(input.Question) ?? input.Question));

#pragma warning disable CA2000 // the wrapper shares the prepared client, which the prepared call disposes
        var client = new ChatClientBuilder(call.Client!).UseFunctionInvocation(configure: f =>
        {
            f.MaximumIterationsPerRequest = MaxRounds;
            f.AllowConcurrentInvocation = false;
            f.IncludeDetailedErrors = false;
        }).Build();
#pragma warning restore CA2000
        var options = new ChatOptions { ModelId = call.Model, Tools = tools, MaxOutputTokens = call.Provider!.MaxOutputTokens };
        var watch = Stopwatch.StartNew();
        var response = await gateway.SendAsync(call with { Client = client }, messages, options, ct).ConfigureAwait(false);
        if (response.IsFailure)
        {
            await gateway.RecordAsync(call, null, null, false, response.Error.Code, (int)watch.ElapsedMilliseconds, 0, 0, ct).ConfigureAwait(false);
            return response.Error;
        }

        var answer = response.Value.Text?.Trim();
        var usageId = await gateway.RecordAsync(call, null, null, true, null, (int)watch.ElapsedMilliseconds,
            response.Value.Usage?.InputTokenCount ?? 0, response.Value.Usage?.OutputTokenCount ?? 0, ct, new { tools = calls.Select(c => c.Tool) }).ConfigureAwait(false);
        return new AskAnswerDto(masker?.Unmask(answer ?? string.Empty) ?? answer ?? string.Empty,
            references.DistinctBy(r => (r.ResourceType, r.Id)).Take(10).ToList(), calls, call.Model!, usageId);
    }

    /// <summary>A registry tool as a model function: runs through <see cref="ToolExecutor"/> (audited, channel <c>ai</c>) with the asking user.</summary>
    private sealed class ToolFunction(
        IAppTool tool, ToolExecutor executor, ToolContext context, AiDataSharing sharing, PersonalDataMasker? masker, List<AskReferenceDto> references,
        List<AskToolCallDto> calls) : AIFunction
    {
        public override string Name => tool.Definition.Name;

        public override string Description => tool.Definition.Description;

        public override JsonElement JsonSchema => tool.Definition.InputSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var args = JsonSerializer.SerializeToElement(arguments.ToDictionary(a => a.Key, a => a.Value));
            var result = await executor.InvokeAsync(tool, args, context, null, cancellationToken).ConfigureAwait(false);
            calls.Add(new AskToolCallDto(Name, result.Success));
            if (!result.Success)
            {
                return JsonSerializer.Serialize(new { error = result.ErrorCode, message = result.Message });
            }

            var node = JsonSerializer.SerializeToNode(result.Data, JsonSerializerOptions.Web);
            node = Filter(node);
            var json = node?.ToJsonString() ?? "null";
            if (json.Length > MaxToolResultCharacters)
            {
                json = json[..MaxToolResultCharacters] + "…(truncated)";
            }

            return masker?.Mask(json) ?? json;
        }

        /// <summary>Drops results of systems not shared with AI and collects references (records with a link) for the answer.</summary>
        private JsonNode? Filter(JsonNode? node)
        {
            switch (node)
            {
                case JsonArray array:
                    foreach (var item in array.ToList())
                    {
                        if (item is JsonObject o && o["systemCode"]?.GetValue<string>() is { } system && !sharing.System(system).Allowed)
                        {
                            array.Remove(item);
                        }
                        else
                        {
                            Filter(item);
                        }
                    }

                    return array;
                case JsonObject obj:
                    if (obj["link"]?.GetValue<string>() is { } link && obj["resourceType"]?.GetValue<string>() is { } type
                        && Guid.TryParse(obj["id"]?.ToString(), out var id))
                    {
                        references.Add(new AskReferenceDto(type, id, obj["number"]?.GetValue<string>(), obj["titleAr"]?.GetValue<string>(),
                            obj["titleEn"]?.GetValue<string>(), link));
                    }

                    foreach (var property in obj.ToList())
                    {
                        Filter(property.Value);
                    }

                    return obj;
                default:
                    return node;
            }
        }
    }
}
