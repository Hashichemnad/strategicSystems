using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Settings;

/// <summary>A typed settings area stored as one JSON document (<c>settings.setting</c>). Defaults come from the record.</summary>
public interface ISettingsArea
{
    static abstract string Key { get; }
}

/// <summary>Regional settings (SPEC §0, §0.2 localisation). Hijri display is off by default.</summary>
public sealed record RegionalSettings(
    string TimeZone = "Asia/Dubai",
    bool HijriDisplay = false,
    bool ArabicIndicDigits = false,
    string CurrencyCode = "AED",
    string DateFormat = "dd/MM/yyyy") : ISettingsArea
{
    public static string Key => "regional";

    public static readonly IReadOnlyList<string> DateFormats = ["dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];
}

/// <summary>Branding per installation (SPEC §0.2): names, colour, email header / footer. Logos are separate assets.</summary>
public sealed record BrandingSettings(
    string? OrganisationNameAr = null,
    string? OrganisationNameEn = null,
    string PrimaryColor = "#0f766e",
    string? EmailHeaderAr = null,
    string? EmailHeaderEn = null,
    string? EmailFooterAr = null,
    string? EmailFooterEn = null) : ISettingsArea
{
    public static string Key => "branding";
}

public interface ISettingsStore
{
    Task<T> GetAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettingsArea;
}

/// <summary>A picklist declared by its owning component or module, with seed values (inserted once, then editable).</summary>
public sealed record LookupTypeDefinition(
    string Code, string SystemCode, string NameAr, string NameEn, IReadOnlyList<LookupSeed> Values, string? ParentTypeCode = null);

public sealed record LookupSeed(string Code, string LabelAr, string LabelEn);

public sealed record LookupValueReference(Guid Id, string Code, string? LabelAr, string? LabelEn, int Sort, bool Active, Guid? ParentValueId);

/// <summary>Picklists (CLAUDE.md §6): business logic checks codes only.</summary>
public interface ILookupService
{
    Task<IReadOnlyList<LookupValueReference>> GetValuesAsync(string typeCode, bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<bool> IsActiveCodeAsync(string typeCode, string code, CancellationToken cancellationToken = default);
}

/// <summary>Existing codes to add to a lookup type at start-up (e.g. Phase 1 free-text grades, ADR 0014).</summary>
public interface ILookupBackfillSource
{
    string TypeCode { get; }

    Task<IReadOnlyList<string>> GetCodesInUseAsync(CancellationToken cancellationToken);
}

/// <summary>A numbering format declared in code with its default pattern (editable in Settings → Numbering).</summary>
public sealed record NumberingDefinition(string Key, string SystemCode, string NameAr, string NameEn, string DefaultPattern);

/// <summary>Values for the pattern tokens: the date (<c>{YYYY}</c>, <c>{YY}</c>, <c>{MM}</c>) and the unit code (<c>{UNIT}</c>).</summary>
public sealed record NumberingContext(DateOnly Date, string? UnitCode = null);

/// <summary>Human-readable numbers (CLAUDE.md §6), generated in the caller's transaction (ADR 0017).</summary>
public interface INumberingService
{
    /// <summary>
    /// The next number for <paramref name="formatKey"/>. Runs on <paramref name="caller"/>'s connection, so it joins its open
    /// transaction: open one around "next number + save" so a failed save does not consume the number.
    /// </summary>
    Task<string> NextAsync(DbContext caller, string formatKey, NumberingContext context, CancellationToken cancellationToken = default);
}

/// <summary>A switchable feature declared in code (Settings → Features).</summary>
public sealed record FeatureDefinition(string Code, string SystemCode, string NameAr, string NameEn, bool DefaultEnabled = true);

public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string code, CancellationToken cancellationToken = default);
}

public static class SettingsRegistrationExtensions
{
    public static IServiceCollection AddLookupType(this IServiceCollection services, LookupTypeDefinition definition) =>
        services.AddSingleton(definition);

    public static IServiceCollection AddNumberingFormat(this IServiceCollection services, NumberingDefinition definition) =>
        services.AddSingleton(definition);

    public static IServiceCollection AddFeature(this IServiceCollection services, FeatureDefinition definition) =>
        services.AddSingleton(definition);
}
