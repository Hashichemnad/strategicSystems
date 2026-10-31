using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings.Domain;
using Strategy.Platform.Settings.Infrastructure;

namespace Strategy.Platform.Settings.Application;

/// <summary>Settings centre use cases (docs/design/settings.md). Every change invalidates the settings cache.</summary>
internal sealed partial class SettingsAdministration(SettingsDbContext db, SettingsCache cache, ISettingsStore store, IClock clock)
{
    public const int MaxLogoBytes = 512 * 1024;
    private const int EmailTextMax = 500;

    // ---- Typed areas ----

    public async Task<SettingsDocument<T>> GetAsync<T>(CancellationToken ct)
        where T : class, ISettingsArea
    {
        var key = T.Key;
        var entry = await db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, ct).ConfigureAwait(false);
        return new SettingsDocument<T>(SettingsStore.Deserialize<T>(entry?.Value), entry?.Version ?? 0);
    }

    public async Task<Result<SettingsDocument<T>>> SaveAsync<T>(T value, uint version, Func<T, List<FieldError>> validate, CancellationToken ct)
        where T : class, ISettingsArea
    {
        var errors = validate(value);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var json = JsonSerializer.Serialize(value, SettingsStore.Json);
        var key = T.Key;
        var entry = await db.Entries.FirstOrDefaultAsync(e => e.Key == key, ct).ConfigureAwait(false);
        if (entry is null)
        {
            if (version != 0)
            {
                return Error.Conflict();
            }

            entry = SettingEntry.Create(T.Key, json);
            db.Entries.Add(entry);
        }
        else
        {
            entry.Set(json);
            db.Entry(entry).Property(e => e.Version).OriginalValue = version;
        }

        await SaveChangesAsync(ct).ConfigureAwait(false);
        return new SettingsDocument<T>(value, entry.Version);
    }

    public static List<FieldError> ValidateRegional(RegionalSettings value)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(value.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(value.TimeZone, out _))
        {
            errors.Add(new FieldError("timeZone", "validation.unknownCode", "validation.unknownCode"));
        }

        if (value.CurrencyCode is null || !Currency().IsMatch(value.CurrencyCode))
        {
            errors.Add(new FieldError("currencyCode", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!RegionalSettings.DateFormats.Contains(value.DateFormat))
        {
            errors.Add(new FieldError("dateFormat", "validation.unknownCode", "validation.unknownCode"));
        }

        return errors;
    }

    public static List<FieldError> ValidateBranding(BrandingSettings value)
    {
        var errors = new List<FieldError>();
        if (value.PrimaryColor is null || !Colour().IsMatch(value.PrimaryColor))
        {
            errors.Add(new FieldError("primaryColor", "settings.errors.colour", "settings.errors.colour"));
        }

        foreach (var (field, text, max) in new[]
                 {
                     ("organisationNameAr", value.OrganisationNameAr, 200), ("organisationNameEn", value.OrganisationNameEn, 200),
                     ("emailHeaderAr", value.EmailHeaderAr, EmailTextMax), ("emailHeaderEn", value.EmailHeaderEn, EmailTextMax),
                     ("emailFooterAr", value.EmailFooterAr, EmailTextMax), ("emailFooterEn", value.EmailFooterEn, EmailTextMax),
                 })
        {
            if (text?.Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max }));
            }
        }

        return errors;
    }

    public async Task<PublicSettingsDto> PublicAsync(CancellationToken ct)
    {
        var branding = await store.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var regional = await store.GetAsync<RegionalSettings>(ct).ConfigureAwait(false);
        var logos = await cache.GetAsync("logos", async () => (IReadOnlyList<string>)await db.BrandingAssets.AsNoTracking().Select(a => a.Kind).ToListAsync(ct)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var translations = await TranslationsVersionAsync(ct).ConfigureAwait(false);
        return new PublicSettingsDto(
            branding.OrganisationNameAr, branding.OrganisationNameEn, branding.PrimaryColor,
            logos.Contains(BrandingAssetKinds.LogoLight), logos.Contains(BrandingAssetKinds.LogoDark), regional, translations);
    }

    // ---- Branding assets ----

    public Task<BrandingAsset?> GetLogoAsync(string kind, CancellationToken ct) =>
        db.BrandingAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Kind == kind, ct);

    public async Task<Result> SetLogoAsync(string kind, byte[] content, CancellationToken ct)
    {
        if (!BrandingAssetKinds.All.Contains(kind))
        {
            return Error.NotFound();
        }

        if (content.Length == 0 || content.Length > MaxLogoBytes)
        {
            return Error.Validation([new FieldError("file", "settings.errors.logoSize", "settings.errors.logoSize",
                new Dictionary<string, object?> { ["maxKb"] = MaxLogoBytes / 1024 })]);
        }

        if (ImageType(content) is not { } contentType)
        {
            return Error.Validation([new FieldError("file", "settings.errors.logoType", "settings.errors.logoType")]);
        }

        var asset = await db.BrandingAssets.FirstOrDefaultAsync(a => a.Kind == kind, ct).ConfigureAwait(false);
        if (asset is null)
        {
            asset = new BrandingAsset { Kind = kind };
            db.BrandingAssets.Add(asset);
        }

        asset.ContentType = contentType;
        asset.Content = content;
        asset.UpdatedAt = clock.UtcNow;
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> RemoveLogoAsync(string kind, CancellationToken ct)
    {
        var asset = await db.BrandingAssets.FirstOrDefaultAsync(a => a.Kind == kind, ct).ConfigureAwait(false);
        if (asset is null)
        {
            return Error.NotFound();
        }

        db.BrandingAssets.Remove(asset);
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>PNG, JPEG or WebP by magic number; SVG and everything else refused (ADR 0017).</summary>
    internal static string? ImageType(ReadOnlySpan<byte> content) => content switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    // ---- Lookups ----

    public async Task<IReadOnlyList<LookupTypeDto>> ListLookupTypesAsync(CancellationToken ct)
    {
        var counts = await db.LookupValues.AsNoTracking().GroupBy(v => v.TypeCode).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        return (await db.LookupTypes.AsNoTracking().OrderBy(t => t.SystemCode).ThenBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => new LookupTypeDto(t.Id, t.Code, t.SystemCode, t.NameAr, t.NameEn, t.ParentTypeCode, t.Builtin, counts.GetValueOrDefault(t.Code), t.Version))
            .ToList();
    }

    public async Task<Result<LookupTypeDto>> CreateLookupTypeAsync(string? code, string? systemCode, string? nameAr, string? nameEn, string? parentTypeCode, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (code is null || !LookupTypeCode().IsMatch(code))
        {
            errors.Add(new FieldError("code", "validation.code", "validation.code"));
        }

        if (systemCode is null || !SystemCode().IsMatch(systemCode))
        {
            errors.Add(new FieldError("systemCode", "validation.code", "validation.code"));
        }

        AddTextErrors(errors, nameAr, nameEn, "nameAr", 200);
        if (parentTypeCode is not null && !await db.LookupTypes.AnyAsync(t => t.Code == parentTypeCode, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("parentTypeCode", "validation.unknownReference", "validation.unknownReference"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (await db.LookupTypes.AnyAsync(t => t.Code == code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("settings.lookupType.duplicateCode", "settings.errors.duplicateCode");
        }

        var type = LookupType.Create(code!, systemCode!, nameAr?.Trim(), nameEn?.Trim(), parentTypeCode, builtin: false);
        db.LookupTypes.Add(type);
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return new LookupTypeDto(type.Id, type.Code, type.SystemCode, type.NameAr, type.NameEn, type.ParentTypeCode, false, 0, type.Version);
    }

    public async Task<Result<IReadOnlyList<LookupValueDto>>> ListValuesAsync(string typeCode, CancellationToken ct)
    {
        if (!await db.LookupTypes.AnyAsync(t => t.Code == typeCode, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return (await db.LookupValues.AsNoTracking().Where(v => v.TypeCode == typeCode).OrderBy(v => v.Sort).ThenBy(v => v.Code).ToListAsync(ct)
            .ConfigureAwait(false)).Select(ToDto).ToList();
    }

    public async Task<Result<LookupValueDto>> CreateValueAsync(string typeCode, LookupValueInput input, CancellationToken ct)
    {
        var type = await db.LookupTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Code == typeCode, ct).ConfigureAwait(false);
        if (type is null)
        {
            return Error.NotFound();
        }

        var errors = new List<FieldError>();
        if (input.Code is null || !ValueCode().IsMatch(input.Code))
        {
            errors.Add(new FieldError("code", "validation.code", "validation.code"));
        }

        await ValidateValueAsync(type, input, errors, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        if (await db.LookupValues.AnyAsync(v => v.TypeCode == typeCode && v.Code == input.Code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("settings.lookupValue.duplicateCode", "settings.errors.duplicateCode");
        }

        var value = LookupValue.Create(typeCode, input.Code!, input.LabelAr, input.LabelEn, input.Sort ?? 0, input.ParentValueId, input.Metadata);
        db.LookupValues.Add(value);
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(value);
    }

    public async Task<Result<LookupValueDto>> UpdateValueAsync(Guid id, LookupValueInput input, uint version, CancellationToken ct)
    {
        var value = await db.LookupValues.FirstOrDefaultAsync(v => v.Id == id, ct).ConfigureAwait(false);
        if (value is null)
        {
            return Error.NotFound();
        }

        var type = await db.LookupTypes.AsNoTracking().FirstAsync(t => t.Code == value.TypeCode, ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        await ValidateValueAsync(type, input, errors, ct).ConfigureAwait(false);
        if (input.ParentValueId == id)
        {
            errors.Add(new FieldError("parentValueId", "validation.unknownReference", "validation.unknownReference"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        value.Update(input.LabelAr, input.LabelEn, input.Sort ?? value.Sort, input.Active ?? value.Active, input.ParentValueId, input.Metadata);
        db.Entry(value).Property(v => v.Version).OriginalValue = version;
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(value);
    }

    private async Task ValidateValueAsync(LookupType type, LookupValueInput input, List<FieldError> errors, CancellationToken ct)
    {
        AddTextErrors(errors, input.LabelAr, input.LabelEn, "labelAr", LookupValue.LabelMaxLength);
        if (input.ParentValueId is { } parentId)
        {
            var parentOk = type.ParentTypeCode is not null
                && await db.LookupValues.AnyAsync(v => v.Id == parentId && v.TypeCode == type.ParentTypeCode, ct).ConfigureAwait(false);
            if (!parentOk)
            {
                errors.Add(new FieldError("parentValueId", "validation.unknownReference", "validation.unknownReference"));
            }
        }

        if (input.Metadata is { } metadata)
        {
            try
            {
                using var document = JsonDocument.Parse(metadata);
                if (document.RootElement.ValueKind != JsonValueKind.Object || metadata.Length > 4000)
                {
                    errors.Add(new FieldError("metadata", "validation.json", "validation.json"));
                }
            }
            catch (JsonException)
            {
                errors.Add(new FieldError("metadata", "validation.json", "validation.json"));
            }
        }
    }

    // ---- Numbering ----

    public async Task<IReadOnlyList<NumberingFormatDto>> ListNumberingAsync(CancellationToken ct) =>
        (await db.NumberingFormats.AsNoTracking().OrderBy(f => f.SystemCode).ThenBy(f => f.Key).ToListAsync(ct).ConfigureAwait(false))
        .Select(ToDto).ToList();

    public async Task<Result<NumberingFormatDto>> UpdateNumberingAsync(string key, string? pattern, uint version, CancellationToken ct)
    {
        var format = await db.NumberingFormats.FirstOrDefaultAsync(f => f.Key == key, ct).ConfigureAwait(false);
        if (format is null)
        {
            return Error.NotFound();
        }

        if (NumberingPattern.Validate(pattern) is { } problem)
        {
            return Error.Validation([new FieldError("pattern", problem, problem)]);
        }

        if (NumberingPattern.UsesUnit(pattern!) != NumberingPattern.UsesUnit(format.DefaultPattern))
        {
            // The caller passes a unit code only when its default pattern uses one.
            return Error.Validation([new FieldError("pattern", "settings.errors.patternUnit", "settings.errors.patternUnit")]);
        }

        format.SetPattern(pattern!.Trim());
        db.Entry(format).Property(f => f.Version).OriginalValue = version;
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(format);
    }

    public Result<NumberingPreviewDto> Preview(string? pattern)
    {
        if (NumberingPattern.Validate(pattern) is { } problem)
        {
            return Error.Validation([new FieldError("pattern", problem, problem)]);
        }

        return new NumberingPreviewDto(pattern!, NumberingPattern.Render(pattern!, new NumberingContext(clock.Today, "UNIT"), 1));
    }

    // ---- Feature flags ----

    public async Task<IReadOnlyList<FeatureFlagDto>> ListFeaturesAsync(CancellationToken ct) =>
        (await db.FeatureFlags.AsNoTracking().OrderBy(f => f.SystemCode).ThenBy(f => f.Code).ToListAsync(ct).ConfigureAwait(false))
        .Select(f => new FeatureFlagDto(f.Code, f.SystemCode, f.NameAr, f.NameEn, f.Enabled, f.Version)).ToList();

    public async Task<Result<FeatureFlagDto>> SetFeatureAsync(string code, bool enabled, uint version, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Code == code, ct).ConfigureAwait(false);
        if (flag is null)
        {
            return Error.NotFound();
        }

        flag.Set(enabled);
        db.Entry(flag).Property(f => f.Version).OriginalValue = version;
        await SaveChangesAsync(ct).ConfigureAwait(false);
        return new FeatureFlagDto(flag.Code, flag.SystemCode, flag.NameAr, flag.NameEn, flag.Enabled, flag.Version);
    }

    // ---- Translation overrides ----

    public async Task<TranslationOverridesDto> TranslationsAsync(CancellationToken ct)
    {
        var rows = await cache.GetAsync("translations", async () => (IReadOnlyList<TranslationOverride>)await db.TranslationOverrides.AsNoTracking()
            .Where(t => t.Value != null).ToListAsync(ct).ConfigureAwait(false)).ConfigureAwait(false);
        var languages = rows.GroupBy(r => r.Language, StringComparer.Ordinal).ToDictionary(
            l => l.Key,
            l => (IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>)l.GroupBy(r => r.Namespace, StringComparer.Ordinal)
                .ToDictionary(n => n.Key, n => (IReadOnlyDictionary<string, string>)n.ToDictionary(r => r.Key, r => r.Value!, StringComparer.Ordinal), StringComparer.Ordinal),
            StringComparer.Ordinal);
        return new TranslationOverridesDto(languages, await TranslationsVersionAsync(ct).ConfigureAwait(false));
    }

    public async Task<Result<TranslationOverridesDto>> SetTranslationAsync(string? language, string? ns, string? key, string? value, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (language is not ("ar" or "en"))
        {
            errors.Add(new FieldError("language", "validation.unknownCode", "validation.unknownCode"));
        }

        if (ns is null || !Namespace().IsMatch(ns))
        {
            errors.Add(new FieldError("namespace", "validation.code", "validation.code"));
        }

        if (key is null || !TranslationKey().IsMatch(key))
        {
            errors.Add(new FieldError("key", "validation.code", "validation.code"));
        }

        if (value?.Length > TranslationOverride.ValueMaxLength)
        {
            errors.Add(new FieldError("value", "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = TranslationOverride.ValueMaxLength }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var text = string.IsNullOrWhiteSpace(value) ? null : value;
        var existing = await db.TranslationOverrides.FirstOrDefaultAsync(t => t.Language == language && t.Namespace == ns && t.Key == key, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            if (text is not null)
            {
                db.TranslationOverrides.Add(TranslationOverride.Create(language!, ns!, key!, text));
            }
        }
        else
        {
            existing.Set(text);
        }

        await SaveChangesAsync(ct).ConfigureAwait(false);
        return await TranslationsAsync(ct).ConfigureAwait(false);
    }

    private async Task<long> TranslationsVersionAsync(CancellationToken ct) =>
        await cache.GetAsync("translations-version", async () =>
        {
            var last = await db.TranslationOverrides.AsNoTracking().MaxAsync(t => (DateTimeOffset?)t.UpdatedAt, ct).ConfigureAwait(false);
            return (object)(last?.ToUnixTimeMilliseconds() ?? 0L);
        }).ConfigureAwait(false) is long version ? version : 0L;

    private async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            cache.Invalidate();
        }
    }

    private static void AddTextErrors(List<FieldError> errors, string? ar, string? en, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(ar) && string.IsNullOrWhiteSpace(en))
        {
            errors.Add(new FieldError(field, "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        foreach (var (name, text) in new[] { (field, ar), (field.Replace("Ar", "En", StringComparison.Ordinal), en) })
        {
            if (text?.Trim().Length > max)
            {
                errors.Add(new FieldError(name, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max }));
            }
        }
    }

    private NumberingFormatDto ToDto(NumberingFormat f) => new(
        f.Key, f.SystemCode, f.NameAr, f.NameEn, f.Pattern, f.DefaultPattern,
        NumberingPattern.Render(f.Pattern, new NumberingContext(clock.Today, "UNIT"), 1), NumberingPattern.UsesUnit(f.Pattern), f.Version);

    private static LookupValueDto ToDto(LookupValue v) =>
        new(v.Id, v.TypeCode, v.Code, v.LabelAr, v.LabelEn, v.Sort, v.Active, v.ParentValueId, v.Metadata, v.Version);

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Currency();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Colour();

    [GeneratedRegex(@"^[a-z][a-z0-9-]*\.[a-z][a-z0-9-]*$")]
    private static partial Regex LookupTypeCode();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$")]
    private static partial Regex SystemCode();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-.]{0,99}$")]
    private static partial Regex ValueCode();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9]{0,63}$")]
    private static partial Regex Namespace();

    [GeneratedRegex(@"^[A-Za-z0-9_\-.]{1,200}$")]
    private static partial Regex TranslationKey();
}

public sealed record LookupValueInput(string? Code, string? LabelAr, string? LabelEn, int? Sort, bool? Active, Guid? ParentValueId, string? Metadata);
