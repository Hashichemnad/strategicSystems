using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Strategy.Platform.AI.Domain;
using Strategy.Platform.AI.Infrastructure;
using Strategy.Platform.AI.Providers;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.AI.Application;

/// <summary>Untrusted content for a prompt (a document, a record's values). Masked and wrapped by the gateway, never placed in instructions.</summary>
public sealed record AiDocument(string Id, string Content);

/// <summary>A JSON-producing AI call. <c>Values</c> are trusted template values; <c>Documents</c> are untrusted data.</summary>
public sealed record AiJsonRequest(
    string Feature,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<AiDocument> Documents,
    JsonElement Schema,
    string? SystemCode = null,
    IReadOnlyList<string>? People = null,
    string? ResourceType = null,
    string? ResourceId = null,
    bool IsUpload = false,
    object? AuditDetail = null);

public sealed record AiJsonResult(JsonElement Output, Guid UsageId, string ProviderType, string Model, long InputTokens, long OutputTokens);

/// <summary>A call ready to run: switches, permission, provider and limits checked.</summary>
public sealed record AiPreparedCall(
    string Feature, AiSettings Settings, AiProvider? Provider, IChatClient? Client, string? Model, string Language, ClaimsPrincipal User) : IDisposable
{
    public void Dispose() => Client?.Dispose();
}

/// <summary>
/// The one way features reach a model (CLAUDE.md §12, docs/design/ai.md §1). Order: internal principal → master switch → feature switch →
/// permission → provider → monthly limits → data controls and masking → prompt → call with time-out → JSON + schema (one repair) → unmask
/// → usage log + audit (channel <c>ai</c>).
/// </summary>
public sealed partial class AiGateway(
    AiDbContext db,
    ISettingsStore settingsStore,
    IAccessService access,
    IRequestContext context,
    IHttpContextAccessor http,
    ISecretProtector secrets,
    IEnumerable<IAiProviderAdapter> adapters,
    IEnumerable<AiFeatureDefinition> features,
    PromptLibrary prompts,
    IAuditLog audit,
    IClock clock,
    ILogger<AiGateway> logger)
{
    public const string KeyPurpose = "ai.provider.key.v1";
    private const int MaxDocumentCharacters = 60_000;

    public IReadOnlyList<AiFeatureDefinition> Features { get; } = [.. features];

    /// <summary>The user's language for AI output: the SPA's <c>Accept-Language</c> (ar / en), else English.</summary>
    public string Language => http.HttpContext?.Request.Headers.AcceptLanguage.ToString() is { } header
        && header.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

    /// <summary>Is the feature usable by the current user now (for the capabilities endpoint and UI switches)?</summary>
    public async Task<bool> IsAvailableAsync(string feature, CancellationToken ct)
    {
        var prepared = await PrepareAsync(feature, createClient: false, ct).ConfigureAwait(false);
        return prepared.IsSuccess;
    }

    /// <summary>Checks everything up to the provider call. With <paramref name="createClient"/> the provider's <see cref="IChatClient"/> is built.</summary>
    public async Task<Result<AiPreparedCall>> PrepareAsync(string feature, bool createClient, CancellationToken ct)
    {
        var user = context.User;
        if (!user.IsInternal())
        {
            return AiErrors.VendorRefused;
        }

        var definition = Features.FirstOrDefault(f => f.Key == feature);
        var settings = await settingsStore.GetAsync<AiSettings>(ct).ConfigureAwait(false);
        if (definition is null || !settings.Enabled || !settings.Feature(feature).Enabled)
        {
            return AiErrors.FeatureDisabled(feature);
        }

        if (!await access.CanAsync(user, definition.Permission, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!definition.NeedsProvider)
        {
            return new AiPreparedCall(feature, settings, null, null, null, Language, user);
        }

        var featureSettings = settings.Feature(feature);
        var providerId = featureSettings.ProviderId ?? settings.DefaultProviderId;
        var provider = providerId is null ? null : await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == providerId, ct).ConfigureAwait(false);
        var adapter = provider is null ? null : adapters.FirstOrDefault(a => a.Type == provider.Type);
        var model = featureSettings.Model ?? provider?.DefaultModel;
        if (provider is not { Active: true } || adapter is null || string.IsNullOrWhiteSpace(model))
        {
            return AiErrors.NotConfigured;
        }

        var limit = await CheckLimitsAsync(settings.Quota, context.UserId, ct).ConfigureAwait(false);
        if (limit is not null)
        {
            return limit;
        }

        var client = createClient ? adapter.Create(Connection(provider, model)) : null;
        return new AiPreparedCall(feature, settings, provider, client, model, Language, user);
    }

    public ProviderConnection Connection(AiProvider provider, string model, string? apiKeyOverride = null, string? endpointOverride = null) => new(
        provider.Type,
        endpointOverride ?? provider.Endpoint,
        apiKeyOverride ?? secrets.Unprotect(KeyPurpose, provider.ApiKeyProtected),
        model,
        provider.TimeoutSeconds,
        provider.MaxOutputTokens);

    /// <summary>Runs a prompt that must return JSON matching <see cref="AiJsonRequest.Schema"/>.</summary>
    public async Task<Result<AiJsonResult>> CompleteJsonAsync(AiJsonRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await PrepareAsync(request.Feature, createClient: true, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        using var call = prepared.Value;
        var sharing = call.Settings.Sharing;
        if (request.SystemCode is { } system && !sharing.System(system).Allowed)
        {
            return await FailAsync(call, request, AiErrors.DataNotAllowed, 0, ct).ConfigureAwait(false);
        }

        if (request.IsUpload && !sharing.AllowDocuments)
        {
            return await FailAsync(call, request, AiErrors.DocumentsNotAllowed, 0, ct).ConfigureAwait(false);
        }

        var masker = sharing.MaskPersonalData ? new PersonalDataMasker(request.People ?? []) : null;
        var nonce = UntrustedContent.NewNonce();
        var documents = string.Join("\n\n", request.Documents.Select(d =>
            UntrustedContent.Wrap(d.Id, Limit(masker?.Mask(d.Content) ?? d.Content), nonce)));
        var template = prompts.Get(request.Feature);
        var values = new Dictionary<string, string>(request.Values, StringComparer.Ordinal)
        {
            ["language"] = call.Language == "ar" ? "Arabic" : "English",
            ["schema"] = request.Schema.GetRawText(),
            ["documents"] = documents,
            ["nonce"] = nonce,
        };
        List<ChatMessage> messages =
        [
            new(ChatRole.System, PromptLibrary.Render(template.System, values)),
            new(ChatRole.User, PromptLibrary.Render(template.User, values)),
        ];
        var options = new ChatOptions
        {
            ModelId = call.Model,
            MaxOutputTokens = call.Provider!.MaxOutputTokens,
            Temperature = call.Provider.Temperature is { } t ? (float)t : null,
        };

        var watch = Stopwatch.StartNew();
        long input = 0, output = 0;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await SendAsync(call, messages, options, ct).ConfigureAwait(false);
            if (response.IsFailure)
            {
                return await FailAsync(call, request, response.Error, (int)watch.ElapsedMilliseconds, ct, input, output).ConfigureAwait(false);
            }

            input += response.Value.Usage?.InputTokenCount ?? 0;
            output += response.Value.Usage?.OutputTokenCount ?? 0;
            var text = response.Value.Text;
            var errors = JsonOutput.TryParse(text, out var json) ? JsonSchemaValidator.Validate(request.Schema, json) : ["$: no JSON object found"];
            if (errors.Count == 0)
            {
                var result = masker is null ? json : masker.Unmask(json);
                var usageId = await RecordAsync(call, request.ResourceType, request.ResourceId, true, null, (int)watch.ElapsedMilliseconds, input, output, ct, request.AuditDetail)
                    .ConfigureAwait(false);
                return new AiJsonResult(result, usageId, call.Provider.Type, call.Model!, input, output);
            }

            LogInvalidOutput(logger, request.Feature, attempt, string.Join("; ", errors));
            messages.Add(new ChatMessage(ChatRole.Assistant, text));
            messages.Add(new ChatMessage(ChatRole.User,
                "The reply did not match the schema: " + string.Join("; ", errors) + ". Reply again with only the JSON object that matches the schema."));
        }

        return await FailAsync(call, request, AiErrors.InvalidOutput, (int)watch.ElapsedMilliseconds, ct, input, output).ConfigureAwait(false);
    }

    /// <summary>Sends messages through the prepared client with the provider's time-out; provider failures become errors, never exceptions.</summary>
    public async Task<Result<ChatResponse>> SendAsync(AiPreparedCall call, IEnumerable<ChatMessage> messages, ChatOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, call.Provider?.TimeoutSeconds ?? 60)));
        try
        {
            return await call.Client!.GetResponseAsync(messages, options, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiErrors.Timeout;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProviderFailed(logger, call.Provider?.Type ?? "?", exception.GetType().Name, exception);
            return AiErrors.ProviderError;
        }
    }

    /// <summary>Usage log row and audit event (channel <c>ai</c>) for one call. Never contains prompts, document text or keys.</summary>
    public async Task<Guid> RecordAsync(
        AiPreparedCall call, string? resourceType, string? resourceId, bool success, string? errorCode, int latencyMs, long input, long output, CancellationToken ct,
        object? detail = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        var usage = new AiUsage
        {
            OccurredAt = clock.UtcNow,
            UserId = context.UserId,
            UserName = context.User.Identity?.Name,
            Feature = call.Feature,
            ProviderId = call.Provider?.Id,
            ProviderType = call.Provider?.Type,
            Model = call.Model,
            InputTokens = input,
            OutputTokens = output,
            LatencyMs = latencyMs,
            Success = success,
            ErrorCode = errorCode,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Channel = context.Channel,
        };
        db.Usage.Add(usage);
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent($"ai.{call.Feature}", success, "ai", resourceType, resourceId,
            new { usageId = usage.Id, provider = call.Provider?.Type, model = call.Model, inputTokens = input, outputTokens = output, latencyMs, error = errorCode, detail })
        {
            Channel = Channels.Ai,
        }, CancellationToken.None).ConfigureAwait(false);
        return usage.Id;
    }

    private async Task<Result<AiJsonResult>> FailAsync(
        AiPreparedCall call, AiJsonRequest request, Error error, int latencyMs, CancellationToken ct, long input = 0, long output = 0)
    {
        await RecordAsync(call, request.ResourceType, request.ResourceId, false, error.Code, latencyMs, input, output, ct, request.AuditDetail).ConfigureAwait(false);
        return error;
    }

    /// <summary>Monthly limits in the installation's time zone (ADR 0028); null when the call may go ahead.</summary>
    public async Task<Error?> CheckLimitsAsync(AiLimits limits, Guid? userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MonthlyTokens is null && limits.MonthlyRequestsPerUser is null)
        {
            return null;
        }

        var zone = (await settingsStore.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone;
        var from = MonthStart(clock.UtcNow, zone);
        if (limits.MonthlyTokens is { } tokens)
        {
            var used = await db.Usage.Where(u => u.OccurredAt >= from).SumAsync(u => u.InputTokens + u.OutputTokens, ct).ConfigureAwait(false);
            if (used >= tokens)
            {
                return AiErrors.LimitReached("tokens", tokens);
            }
        }

        if (limits.MonthlyRequestsPerUser is { } requests && userId is { } id)
        {
            var count = await db.Usage.CountAsync(u => u.UserId == id && u.OccurredAt >= from, ct).ConfigureAwait(false);
            if (count >= requests)
            {
                return AiErrors.LimitReached("requests", requests);
            }
        }

        return null;
    }

    /// <summary>The first instant of the month containing <paramref name="now"/> in <paramref name="timeZone"/>, in UTC.</summary>
    public static DateTimeOffset MonthStart(DateTimeOffset now, string timeZone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var start = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(start, zone.GetUtcOffset(start)).ToUniversalTime();
    }

    private static string Limit(string text) => text.Length <= MaxDocumentCharacters ? text : text[..MaxDocumentCharacters];

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI output for {Feature} failed validation (attempt {Attempt}): {Errors}")]
    private static partial void LogInvalidOutput(ILogger logger, string feature, int attempt, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI provider {ProviderType} failed: {ExceptionType}")]
    private static partial void LogProviderFailed(ILogger logger, string providerType, string exceptionType, Exception exception);
}
