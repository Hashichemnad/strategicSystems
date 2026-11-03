using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.AI.Domain;

/// <summary>Provider types with an adapter (ADR 0028). <see cref="Demo"/> exists only when <c>AI:EnableDemoProvider</c> is set (Development, tests).</summary>
public static class AiProviderTypes
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
    public const string OpenAiCompatible = "openai-compatible";
    public const string Demo = "demo";
}

/// <summary>A configured AI provider (Settings → AI). The API key is protected with Data Protection and never returned.</summary>
[Audited(EntityType)]
public sealed class AiProvider : AuditableEntity
{
    public const string EntityType = "ai.provider";

    private AiProvider()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public string? Endpoint { get; private set; }

    [AuditMasked]
    public string? ApiKeyProtected { get; private set; }

    public string? DefaultModel { get; private set; }

    public List<string> Models { get; private set; } = [];

    public decimal? Temperature { get; private set; }

    public int MaxOutputTokens { get; private set; } = 2000;

    public int TimeoutSeconds { get; private set; } = 60;

    public bool Active { get; private set; } = true;

    public static AiProvider Create(string name, string type) => new() { Name = name, Type = type };

    public void Update(
        string name, string type, string? endpoint, string? defaultModel, IEnumerable<string> models, decimal? temperature, int maxOutputTokens,
        int timeoutSeconds, bool active)
    {
        Name = name.Trim();
        Type = type;
        Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
        DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? null : defaultModel.Trim();
        Models = models.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).Distinct(StringComparer.Ordinal).ToList();
        Temperature = temperature;
        MaxOutputTokens = maxOutputTokens;
        TimeoutSeconds = timeoutSeconds;
        Active = active;
    }

    public void SetApiKey(string? protectedKey) => ApiKeyProtected = protectedKey;
}

/// <summary>One AI call (CLAUDE.md §12): append-only usage log, the basis of monthly limits and the usage screen.</summary>
[NotAudited]
public sealed class AiUsage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string Feature { get; set; } = string.Empty;

    public Guid? ProviderId { get; set; }

    public string? ProviderType { get; set; }

    public string? Model { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public int LatencyMs { get; set; }

    public bool Success { get; set; }

    public string? ErrorCode { get; set; }

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public string Channel { get; set; } = string.Empty;
}
