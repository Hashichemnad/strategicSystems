using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

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
    string DateFormat = "dd/MM/yyyy",
    string? DefaultLanguage = null) : ISettingsArea
{
    public static string Key => "regional";

    public static readonly IReadOnlyList<string> DateFormats = ["dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];

    /// <summary>Interface languages. <c>DefaultLanguage</c> (null: follow the browser) is used until a user chooses one (ADR 0150).</summary>
    public static readonly IReadOnlyList<string> Languages = ["ar", "en"];
}

/// <summary>Validation of the typed areas for components that save them outside the Settings screens (setup wizard, ADR 0150).</summary>
public static class SettingsValidation
{
    public static List<Kernel.Results.FieldError> Regional(RegionalSettings value) => Application.SettingsAdministration.ValidateRegional(value);

    public static List<Kernel.Results.FieldError> Branding(BrandingSettings value) => Application.SettingsAdministration.ValidateBranding(value);
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

/// <summary>Reads and saves a typed area with its version (409 on conflict) for components that own a settings screen.</summary>
public interface ISettingsEditor
{
    Task<Application.SettingsDocument<T>> GetDocumentAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettingsArea;

    Task<Kernel.Results.Result<Application.SettingsDocument<T>>> SaveAsync<T>(
        T value, uint version, Func<T, List<Kernel.Results.FieldError>> validate, CancellationToken cancellationToken = default)
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

/// <summary>
/// Adds lookup values that a mapping pack ships (release 1.0.2, ADR 0216): only codes the type does not have yet are added (active, after the
/// existing values); values that exist — possibly renamed, re-sorted or deactivated by an administrator — are never changed. Audited by the
/// entity interceptor. Returns the number of values added; unknown types are refused.
/// </summary>
public interface ILookupProvisioning
{
    Task<Result<int>> AddMissingAsync(string typeCode, IReadOnlyList<LookupSeed> values, CancellationToken cancellationToken = default);
}

/// <summary>Existing codes to add to a lookup type at start-up (e.g. Phase 1 free-text grades, ADR 0014).</summary>
public interface ILookupBackfillSource
{
    string TypeCode { get; }

    Task<IReadOnlyList<string>> GetCodesInUseAsync(CancellationToken cancellationToken);
}

/// <summary>A numbering format declared in code with its default pattern (editable in Settings → Numbering).</summary>
public sealed record NumberingDefinition(string Key, string SystemCode, string NameAr, string NameEn, string DefaultPattern);

/// <summary>
/// Values for the pattern tokens: the date (<c>{YYYY}</c>, <c>{YY}</c>, <c>{MM}</c>), the unit code (<c>{UNIT}</c>), a type code
/// (<c>{TYPE}</c>, e.g. a document type) and the organisation code (<c>{ORG}</c>) — ADR 0082.
/// </summary>
public sealed record NumberingContext(DateOnly Date, string? UnitCode = null, string? TypeCode = null, string? OrgCode = null);

/// <summary>Human-readable numbers (CLAUDE.md §6), generated in the caller's transaction (ADR 0017).</summary>
public interface INumberingService
{
    /// <summary>
    /// The next number for <paramref name="formatKey"/>. Runs on <paramref name="caller"/>'s connection, so it joins its open
    /// transaction: open one around "next number + save" so a failed save does not consume the number.
    /// </summary>
    Task<string> NextAsync(DbContext caller, string formatKey, NumberingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that <paramref name="number"/> exists already (a display number synced from another system, ADR 0220): when it has the shape of
    /// the format's pattern, the counter of its partition is raised to at least its sequence, so <see cref="NextAsync"/> continues after it.
    /// A number of another shape changes nothing. Runs on <paramref name="caller"/>'s connection and transaction.
    /// </summary>
    Task ObserveAsync(DbContext caller, string formatKey, string number, CancellationToken cancellationToken = default);
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
