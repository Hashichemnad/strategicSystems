using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.AI.Domain;
using Strategy.Platform.AI.Infrastructure;
using Strategy.Platform.AI.Providers;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;

namespace Strategy.Platform.AI.Application;

public sealed record AiProviderDto(
    Guid Id, string Name, string Type, string? Endpoint, bool HasApiKey, string? DefaultModel, IReadOnlyList<string> Models, decimal? Temperature,
    int MaxOutputTokens, int TimeoutSeconds, bool Active, uint Version)
{
    public static AiProviderDto From(AiProvider p) =>
        new(p.Id, p.Name, p.Type, p.Endpoint, p.ApiKeyProtected is not null, p.DefaultModel, p.Models, p.Temperature, p.MaxOutputTokens, p.TimeoutSeconds, p.Active, p.Version);
}

/// <summary>A provider to save. <c>ApiKey</c> null keeps the stored key; <c>ClearApiKey</c> removes it.</summary>
public sealed record AiProviderInput(
    string? Name, string? Type, string? Endpoint, string? ApiKey, bool ClearApiKey, string? DefaultModel, IReadOnlyList<string>? Models, decimal? Temperature,
    int? MaxOutputTokens, int? TimeoutSeconds, bool? Active, uint Version = 0);

/// <summary>Values to try before saving (all optional: the stored ones are used).</summary>
public sealed record AiProviderProbeInput(string? ApiKey, string? Endpoint, string? Model);

public sealed record AiProviderTestDto(bool Success, int LatencyMs, string? Model, string? MessageKey, string? Reply);

public sealed record AiProviderTypeDto(string Type, bool RequiresEndpoint, bool RequiresApiKey);

public sealed record AiFeatureDto(string Key, string NameAr, string NameEn, string Permission, bool NeedsProvider);

public sealed record AiSystemDto(string Code, string NameAr, string NameEn);

/// <summary>Settings → AI. <c>OcrAvailable</c>: Tesseract and the configured languages are installed on the server (ADR 0033).</summary>
public sealed record AiSettingsDto(
    AiSettings Settings, uint Version, IReadOnlyList<AiFeatureDto> Features, IReadOnlyList<AiProviderTypeDto> ProviderTypes, IReadOnlyList<AiSystemDto> Systems,
    bool OcrAvailable);

public sealed record AiSettingsInput(AiSettings Settings, uint Version);

public sealed record AiUsageRowDto(
    Guid Id, DateTimeOffset OccurredAt, Guid? UserId, string? UserName, string Feature, string? ProviderType, string? Model, long InputTokens, long OutputTokens,
    int LatencyMs, bool Success, string? ErrorCode, string? ResourceType, string? ResourceId, string Channel);

public sealed record AiUsageFeatureTotalDto(string Feature, int Requests, long Tokens, int Failures);

public sealed record AiUsageSummaryDto(DateTimeOffset MonthStart, int Requests, long Tokens, long? MonthlyTokenLimit, int? MonthlyRequestsPerUser,
    IReadOnlyList<AiUsageFeatureTotalDto> ByFeature);

public sealed record AiCapabilitiesDto(bool Enabled, IReadOnlyDictionary<string, bool> Features);

/// <summary>Settings → AI use cases (docs/design/ai.md §2–3, §8). Keys go through <see cref="ISecretProtector"/> and are never returned.</summary>
public sealed partial class AiAdministration(
    AiDbContext db,
    ISettingsEditor editor,
    ISettingsStore store,
    ISecretProtector secrets,
    IEnumerable<IAiProviderAdapter> adapters,
    IEnumerable<IAccessCatalogueSource> catalogue,
    AiGateway gateway,
    IAuditLog audit,
    IClock clock,
    IOcrEngine ocr)
{
    public const int MaxTestTokens = 20;

    public async Task<AiCapabilitiesDto> CapabilitiesAsync(CancellationToken ct)
    {
        var settings = await store.GetAsync<AiSettings>(ct).ConfigureAwait(false);
        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var feature in gateway.Features)
        {
            features[feature.Key] = settings.Enabled && await gateway.IsAvailableAsync(feature.Key, ct).ConfigureAwait(false);
        }

        return new AiCapabilitiesDto(settings.Enabled && features.Values.Any(v => v), features);
    }

    public async Task<IReadOnlyList<AiProviderDto>> ProvidersAsync(CancellationToken ct) =>
        (await db.Providers.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false)).Select(AiProviderDto.From).ToList();

    public async Task<Result<AiProviderDto>> SaveProviderAsync(Guid? id, AiProviderInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        AiProvider provider;
        if (id is null)
        {
            provider = AiProvider.Create(input.Name!, input.Type!);
            db.Providers.Add(provider);
        }
        else
        {
            var found = await db.Providers.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            provider = found;
            db.Entry(provider).Property(p => p.Version).OriginalValue = input.Version;
        }

        provider.Update(input.Name!, input.Type!, input.Endpoint, input.DefaultModel, input.Models ?? [], input.Temperature, input.MaxOutputTokens ?? 2000,
            input.TimeoutSeconds ?? 60, input.Active ?? true);
        if (input.ClearApiKey)
        {
            provider.SetApiKey(null);
        }
        else if (!string.IsNullOrWhiteSpace(input.ApiKey))
        {
            provider.SetApiKey(secrets.Protect(AiGateway.KeyPurpose, input.ApiKey.Trim()));
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return AiProviderDto.From(provider);
    }

    /// <summary>Test connection (SPEC §0.5): a one-line prompt through the provider's adapter; works while AI is switched off.</summary>
    public async Task<Result<AiProviderTestDto>> TestAsync(Guid id, AiProviderProbeInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return Error.NotFound();
        }

        var adapter = adapters.FirstOrDefault(a => a.Type == provider.Type);
        var model = string.IsNullOrWhiteSpace(input.Model) ? provider.DefaultModel : input.Model.Trim();
        AiProviderTestDto result;
        if (adapter is null || string.IsNullOrWhiteSpace(model))
        {
            result = new AiProviderTestDto(false, 0, model, "ai.errors.notConfigured", null);
        }
        else
        {
            var watch = Stopwatch.StartNew();
            using var client = adapter.Create(gateway.Connection(provider, model, Blank(input.ApiKey), Blank(input.Endpoint)));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
            try
            {
                var response = await client.GetResponseAsync(
                    [new ChatMessage(ChatRole.System, "This is a connection test."), new ChatMessage(ChatRole.User, "Reply with the single word OK.")],
                    new ChatOptions { ModelId = model, MaxOutputTokens = MaxTestTokens }, timeout.Token).ConfigureAwait(false);
                var reply = response.Text?.Trim();
                result = new AiProviderTestDto(!string.IsNullOrEmpty(reply), (int)watch.ElapsedMilliseconds, model,
                    string.IsNullOrEmpty(reply) ? "ai.errors.emptyReply" : "ai.provider.testPassed", reply is { Length: > 100 } ? reply[..100] : reply);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result = new AiProviderTestDto(false, (int)watch.ElapsedMilliseconds, model, "ai.errors.timeout", null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new AiProviderTestDto(false, (int)watch.ElapsedMilliseconds, model, "ai.errors.providerError", null);
            }
        }

        await audit.WriteAsync(new AuditEvent("ai.provider.test", result.Success, "ai", AiProvider.EntityType, id.ToString(),
            new { provider.Type, model, result.LatencyMs, result.MessageKey }), CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<IReadOnlyList<string>>> ModelsAsync(Guid id, AiProviderProbeInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        var adapter = provider is null ? null : adapters.FirstOrDefault(a => a.Type == provider.Type);
        if (provider is null || adapter is null)
        {
            return Error.NotFound();
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
            var connection = gateway.Connection(provider, provider.DefaultModel ?? "-", Blank(input.ApiKey), Blank(input.Endpoint));
            var models = await adapter.ListModelsAsync(connection, timeout.Token).ConfigureAwait(false);
            return models.ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Error.Rule("ai.modelsUnavailable", "ai.errors.modelsUnavailable");
        }
    }

    public async Task<AiSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var document = await editor.GetDocumentAsync<AiSettings>(ct).ConfigureAwait(false);
        return new AiSettingsDto(document.Value, document.Version,
            gateway.Features.Select(f => new AiFeatureDto(f.Key, f.NameAr, f.NameEn, f.Permission, f.NeedsProvider)).ToList(),
            adapters.Select(a => new AiProviderTypeDto(a.Type, a.RequiresEndpoint, a.RequiresApiKey)).ToList(),
            catalogue.Select(c => c.System).DistinctBy(s => s.Code).OrderBy(s => s.Sort).Select(s => new AiSystemDto(s.Code, s.NameAr, s.NameEn)).ToList(),
            await ocr.IsAvailableAsync(document.Value.Recognition.Languages, ct).ConfigureAwait(false));
    }

    public async Task<Result<AiSettingsDto>> SaveSettingsAsync(AiSettingsInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var providerIds = await db.Providers.Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        var featureKeys = gateway.Features.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var saved = await editor.SaveAsync(input.Settings, input.Version, value =>
        {
            var errors = new List<FieldError>();
            if (value.DefaultProviderId is { } d && !providerIds.Contains(d))
            {
                errors.Add(new FieldError("defaultProviderId", "validation.unknownCode", "validation.unknownCode"));
            }

            foreach (var (key, feature) in value.Features ?? new Dictionary<string, AiFeatureSettings>())
            {
                if (!featureKeys.Contains(key) || (feature.ProviderId is { } p && !providerIds.Contains(p)))
                {
                    errors.Add(new FieldError($"features.{key}", "validation.unknownCode", "validation.unknownCode"));
                }
            }

            if (!OcrLanguages().IsMatch(value.Recognition.Languages))
            {
                errors.Add(new FieldError("ocr.languages", "validation.invalid", "validation.invalid"));
            }

            if (value.Recognition.MaxPages is < 1 or > 100)
            {
                errors.Add(new FieldError("ocr.maxPages", "validation.inclusiveBetween", "validation.inclusiveBetween",
                    new Dictionary<string, object?> { ["from"] = 1, ["to"] = 100 }));
            }

            if (value.Quota.MonthlyTokens is <= 0 || value.Quota.MonthlyRequestsPerUser is <= 0)
            {
                errors.Add(new FieldError("limits", "validation.greaterThanOrEqual", "validation.greaterThanOrEqual", new Dictionary<string, object?> { ["comparisonValue"] = 1 }));
            }

            return errors;
        }, ct).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : await SettingsAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<AiUsageRowDto>> UsageAsync(PageRequest page, string? feature, Guid? userId, bool? success, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = db.Usage.AsNoTracking();
        if (!string.IsNullOrEmpty(feature))
        {
            query = query.Where(u => u.Feature == feature);
        }

        if (userId is { } id)
        {
            query = query.Where(u => u.UserId == id);
        }

        if (success is { } ok)
        {
            query = query.Where(u => u.Success == ok);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(u => u.OccurredAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(u => new AiUsageRowDto(u.Id, u.OccurredAt, u.UserId, u.UserName, u.Feature, u.ProviderType, u.Model, u.InputTokens, u.OutputTokens,
                u.LatencyMs, u.Success, u.ErrorCode, u.ResourceType, u.ResourceId, u.Channel))
            .ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<AiUsageRowDto>(rows, page.Page, page.PageSize, total);
    }

    public async Task<AiUsageSummaryDto> UsageSummaryAsync(CancellationToken ct)
    {
        var settings = await store.GetAsync<AiSettings>(ct).ConfigureAwait(false);
        var zone = (await store.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone;
        var from = AiGateway.MonthStart(clock.UtcNow, zone);
        var byFeature = await db.Usage.AsNoTracking().Where(u => u.OccurredAt >= from).GroupBy(u => u.Feature)
            .Select(g => new AiUsageFeatureTotalDto(g.Key, g.Count(), g.Sum(u => u.InputTokens + u.OutputTokens), g.Count(u => !u.Success)))
            .ToListAsync(ct).ConfigureAwait(false);
        return new AiUsageSummaryDto(from, byFeature.Sum(f => f.Requests), byFeature.Sum(f => f.Tokens), settings.Quota.MonthlyTokens,
            settings.Quota.MonthlyRequestsPerUser, byFeature.OrderBy(f => f.Feature, StringComparer.Ordinal).ToList());
    }

    private List<FieldError> Validate(AiProviderInput input)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
        {
            errors.Add(new FieldError("name", "validation.notEmpty", "validation.notEmpty"));
        }

        var adapter = adapters.FirstOrDefault(a => a.Type == input.Type);
        if (adapter is null)
        {
            errors.Add(new FieldError("type", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!string.IsNullOrWhiteSpace(input.Endpoint)
            && (!Uri.TryCreate(input.Endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            errors.Add(new FieldError("endpoint", "ai.validation.endpoint", "ai.validation.endpoint"));
        }
        else if (adapter is { RequiresEndpoint: true } && string.IsNullOrWhiteSpace(input.Endpoint))
        {
            errors.Add(new FieldError("endpoint", "validation.notEmpty", "validation.notEmpty"));
        }

        if (input.Temperature is < 0 or > 2)
        {
            errors.Add(new FieldError("temperature", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 0, ["to"] = 2 }));
        }

        if (input.MaxOutputTokens is < 100 or > 64000)
        {
            errors.Add(new FieldError("maxOutputTokens", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 100, ["to"] = 64000 }));
        }

        if (input.TimeoutSeconds is < 5 or > 600)
        {
            errors.Add(new FieldError("timeoutSeconds", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 5, ["to"] = 600 }));
        }

        if (input.ApiKey is { Length: > 500 })
        {
            errors.Add(new FieldError("apiKey", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 500 }));
        }

        return errors;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z_]{3,20}(\\+[a-z_]{3,20}){0,4}$")]
    private static partial System.Text.RegularExpressions.Regex OcrLanguages();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
