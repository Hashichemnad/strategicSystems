namespace Strategy.Platform.Settings.Application;

public sealed record SettingsDocument<T>(T Value, uint Version);

public sealed record PublicSettingsDto(
    string? OrganisationNameAr, string? OrganisationNameEn, string PrimaryColor, bool HasLogoLight, bool HasLogoDark,
    RegionalSettings Regional, long TranslationsVersion);

public sealed record LookupTypeDto(Guid Id, string Code, string SystemCode, string? NameAr, string? NameEn, string? ParentTypeCode, bool Builtin, int ValueCount, uint Version);

public sealed record LookupValueDto(
    Guid Id, string TypeCode, string Code, string? LabelAr, string? LabelEn, int Sort, bool Active, Guid? ParentValueId, string? Metadata, uint Version);

public sealed record NumberingFormatDto(
    string Key, string SystemCode, string NameAr, string NameEn, string Pattern, string DefaultPattern, string Preview, bool UsesUnit, uint Version);

public sealed record NumberingPreviewDto(string Pattern, string Preview);

public sealed record FeatureFlagDto(string Code, string SystemCode, string NameAr, string NameEn, bool Enabled, uint Version);

public sealed record TranslationOverridesDto(IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Languages, long Version);
