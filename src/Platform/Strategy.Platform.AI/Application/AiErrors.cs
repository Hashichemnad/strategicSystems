using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.AI.Application;

/// <summary>Stable codes and message keys of AI failures (translated on the client).</summary>
public static class AiErrors
{
    /// <summary>Master switch or feature switch off: 404, as for a disabled module.</summary>
    public static Error FeatureDisabled(string feature) =>
        new("ai.featureDisabled", "ai.errors.featureDisabled", ErrorKind.Disabled, new Dictionary<string, object?> { ["feature"] = feature });

    public static readonly Error NotConfigured = Error.Rule("ai.notConfigured", "ai.errors.notConfigured");

    public static readonly Error VendorRefused = new("ai.vendorRefused", "errors.forbidden", ErrorKind.Forbidden);

    public static readonly Error DataNotAllowed = Error.Rule("ai.dataNotAllowed", "ai.errors.dataNotAllowed");

    public static readonly Error DocumentsNotAllowed = Error.Rule("ai.documentsNotAllowed", "ai.errors.documentsNotAllowed");

    public static readonly Error ProviderError = Error.Rule("ai.providerError", "ai.errors.providerError");

    public static readonly Error Timeout = Error.Rule("ai.timeout", "ai.errors.timeout");

    public static readonly Error InvalidOutput = Error.Rule("ai.invalidOutput", "ai.errors.invalidOutput");

    public static Error LimitReached(string which, long limit) =>
        Error.Rule("ai.limitReached", "ai.errors.limitReached", new Dictionary<string, object?> { ["which"] = which, ["limit"] = limit });
}
