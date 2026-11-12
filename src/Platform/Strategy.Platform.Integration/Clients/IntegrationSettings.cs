using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Clients;

/// <summary>Settings → Integrations (docs/design/integrations.md §8).</summary>
public sealed record IntegrationSettings(
    int MaxClientDays = 730,
    int DefaultClientDays = 365,
    int AccessTokenMinutes = 60,
    int ODataPageSize = 1000,
    int ExpandLimit = 10_000,
    int ExtractMaxPageSize = 5000,
    int UsageRetentionDays = 90,
    int WebhookMaxAttempts = 8,
    int WebhookRetryBaseSeconds = 60,
    int WebhookTimeoutSeconds = 10) : ISettingsArea
{
    public static string Key => "integration";
}
