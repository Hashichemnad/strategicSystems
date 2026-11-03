using System.Text.Json.Serialization;
using Strategy.Platform.Settings;

namespace Strategy.Platform.AI.Application;

/// <summary>Settings → AI (docs/design/ai.md §3). Master switch off on a new installation: everything works without AI.</summary>
public sealed record AiSettings(
    bool Enabled = false,
    Guid? DefaultProviderId = null,
    IReadOnlyDictionary<string, AiFeatureSettings>? Features = null,
    AiDataSharing? DataSharing = null,
    AiLimits? Limits = null,
    AiOcrSettings? Ocr = null) : ISettingsArea
{
    public static string Key => "ai";

    public AiFeatureSettings Feature(string key) => Features?.GetValueOrDefault(key) ?? new AiFeatureSettings();

    [JsonIgnore]
    public AiDataSharing Sharing => DataSharing ?? new AiDataSharing();

    [JsonIgnore]
    public AiLimits Quota => Limits ?? new AiLimits();

    [JsonIgnore]
    public AiOcrSettings Recognition => Ocr ?? new AiOcrSettings();
}

/// <summary>Per feature: switch, provider and model (else the default provider and its default model).</summary>
public sealed record AiFeatureSettings(bool Enabled = true, Guid? ProviderId = null, string? Model = null);

/// <summary>What may be sent to the provider: per system and fields, documents, and masking of personal data.</summary>
public sealed record AiDataSharing(
    bool MaskPersonalData = true,
    bool AllowDocuments = true,
    IReadOnlyDictionary<string, AiSystemSharing>? Systems = null)
{
    public AiSystemSharing System(string code) => Systems?.GetValueOrDefault(code) ?? new AiSystemSharing();
}

public sealed record AiSystemSharing(bool Allowed = true, IReadOnlyList<string>? ExcludedFields = null)
{
    public bool Excludes(string field) => ExcludedFields?.Contains(field, StringComparer.OrdinalIgnoreCase) == true;
}

/// <summary>Monthly limits (null = none), counted in the installation's time zone.</summary>
public sealed record AiLimits(long? MonthlyTokens = null, int? MonthlyRequestsPerUser = null);

/// <summary>
/// Text recognition (OCR) for scanned PDF pages and images (ADR 0033): off by default; Tesseract language codes joined by <c>+</c>; at most
/// <c>MaxPages</c> scanned pages per document.
/// </summary>
public sealed record AiOcrSettings(bool Enabled = false, string Languages = "ara+eng", int MaxPages = 20);
