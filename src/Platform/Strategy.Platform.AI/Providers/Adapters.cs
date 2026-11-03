using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using Strategy.Platform.AI.Domain;

namespace Strategy.Platform.AI.Providers;

/// <summary>What an adapter needs to reach a provider (key already decrypted, never logged).</summary>
public sealed record ProviderConnection(string Type, string? Endpoint, string? ApiKey, string Model, int TimeoutSeconds, int MaxOutputTokens);

/// <summary>
/// One adapter per provider type (ADR 0028): builds a Microsoft.Extensions.AI <see cref="IChatClient"/> and lists models. Features never see the
/// provider SDKs, so a new provider is one class.
/// </summary>
public interface IAiProviderAdapter
{
    string Type { get; }

    /// <summary>True when the endpoint must be given (Azure, self-hosted).</summary>
    bool RequiresEndpoint { get; }

    /// <summary>True when an API key must be given.</summary>
    bool RequiresApiKey { get; }

    IChatClient Create(ProviderConnection connection);

    Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken);
}

/// <summary>Anthropic Claude through the official C# SDK (<c>Anthropic</c> NuGet, MIT).</summary>
internal sealed class AnthropicAdapter : IAiProviderAdapter
{
    public string Type => AiProviderTypes.Anthropic;

    public bool RequiresEndpoint => false;

    public bool RequiresApiKey => true;

    public IChatClient Create(ProviderConnection connection) => Client(connection).AsIChatClient(connection.Model, connection.MaxOutputTokens);

    public async Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        var page = await Client(connection).Models.List(cancellationToken: cancellationToken).ConfigureAwait(false);
        return page.Items.Select(m => m.ID).Order(StringComparer.Ordinal).ToList();
    }

    private static AnthropicClient Client(ProviderConnection connection) => new()
    {
        ApiKey = connection.ApiKey,
        MaxRetries = 1,
        Timeout = TimeSpan.FromSeconds(connection.TimeoutSeconds),
        BaseUrl = string.IsNullOrWhiteSpace(connection.Endpoint) ? Anthropic.Core.EnvironmentUrl.Production : connection.Endpoint,
    };
}

/// <summary>
/// OpenAI, Azure OpenAI (v1 endpoint <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>, the deployment name as the model) and any
/// OpenAI-compatible server (Ollama, vLLM) through the OpenAI SDK (MIT).
/// </summary>
internal sealed class OpenAiFamilyAdapter(string type) : IAiProviderAdapter
{
    public string Type => type;

    public bool RequiresEndpoint => type != AiProviderTypes.OpenAi;

    public bool RequiresApiKey => type != AiProviderTypes.OpenAiCompatible;

    public IChatClient Create(ProviderConnection connection) => Client(connection).GetChatClient(connection.Model).AsIChatClient();

    public async Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken)
    {
        var models = await Client(connection).GetOpenAIModelClient().GetModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.Value.Select(m => m.Id).Order(StringComparer.Ordinal).ToList();
    }

    private OpenAIClient Client(ProviderConnection connection)
    {
        var options = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromSeconds(connection.TimeoutSeconds), RetryPolicy = new ClientRetryPolicy(1) };
        if (!string.IsNullOrWhiteSpace(connection.Endpoint))
        {
            options.Endpoint = new Uri(connection.Endpoint);
        }

        var key = string.IsNullOrEmpty(connection.ApiKey) ? "not-needed" : connection.ApiKey;
        if (type == AiProviderTypes.AzureOpenAi)
        {
            options.AddPolicy(new HeaderPolicy("api-key", key), PipelinePosition.PerCall);
        }

        return new OpenAIClient(new ApiKeyCredential(key), options);
    }

    private sealed class HeaderPolicy(string name, string value) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(name, value);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(name, value);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
