using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Settings.Domain;

/// <summary>One typed settings area as JSON.</summary>
public sealed class SettingEntry : AuditableEntity
{
    private SettingEntry()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = "{}";

    public static SettingEntry Create(string key, string value) => new() { Key = key, Value = value };

    public void Set(string value) => Value = value;
}

public sealed class LookupType : AuditableEntity
{
    public const int CodeMaxLength = 100;

    private LookupType()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? ParentTypeCode { get; private set; }

    public bool Builtin { get; private set; }

    public static LookupType Create(string code, string systemCode, string? nameAr, string? nameEn, string? parentTypeCode, bool builtin) => new()
    {
        Code = code,
        SystemCode = systemCode,
        NameAr = nameAr,
        NameEn = nameEn,
        ParentTypeCode = parentTypeCode,
        Builtin = builtin,
    };

    public void Sync(LookupTypeDefinition definition)
    {
        SystemCode = definition.SystemCode;
        NameAr = definition.NameAr;
        NameEn = definition.NameEn;
        ParentTypeCode = definition.ParentTypeCode;
        Builtin = true;
    }
}

public sealed class LookupValue : AuditableEntity
{
    public const int CodeMaxLength = 100;
    public const int LabelMaxLength = 300;

    private LookupValue()
    {
    }

    public string TypeCode { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? LabelAr { get; private set; }

    public string? LabelEn { get; private set; }

    public int Sort { get; private set; }

    public bool Active { get; private set; } = true;

    public Guid? ParentValueId { get; private set; }

    public string? Metadata { get; private set; }

    public static LookupValue Create(string typeCode, string code, string? labelAr, string? labelEn, int sort, Guid? parentValueId, string? metadata) => new()
    {
        TypeCode = typeCode,
        Code = code,
        LabelAr = Clean(labelAr),
        LabelEn = Clean(labelEn),
        Sort = sort,
        ParentValueId = parentValueId,
        Metadata = metadata,
    };

    public void Update(string? labelAr, string? labelEn, int sort, bool active, Guid? parentValueId, string? metadata)
    {
        LabelAr = Clean(labelAr);
        LabelEn = Clean(labelEn);
        Sort = sort;
        Active = active;
        ParentValueId = parentValueId;
        Metadata = metadata;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class NumberingFormat : AuditableEntity
{
    private NumberingFormat()
    {
    }

    public string Key { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string Pattern { get; private set; } = string.Empty;

    public string DefaultPattern { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public static NumberingFormat Create(NumberingDefinition definition) => new()
    {
        Key = definition.Key,
        SystemCode = definition.SystemCode,
        Pattern = definition.DefaultPattern,
        DefaultPattern = definition.DefaultPattern,
        NameAr = definition.NameAr,
        NameEn = definition.NameEn,
    };

    public void Sync(NumberingDefinition definition)
    {
        SystemCode = definition.SystemCode;
        DefaultPattern = definition.DefaultPattern;
        NameAr = definition.NameAr;
        NameEn = definition.NameEn;
    }

    public void SetPattern(string pattern) => Pattern = pattern;
}

/// <summary>Counter per format and rendered prefix (ADR 0017). Infrastructure: incremented by an atomic upsert.</summary>
public sealed class NumberingCounter
{
    public string FormatKey { get; set; } = string.Empty;

    public string Partition { get; set; } = string.Empty;

    public long LastValue { get; set; }
}

public sealed class FeatureFlag : AuditableEntity
{
    private FeatureFlag()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public static FeatureFlag Create(FeatureDefinition definition) => new()
    {
        Code = definition.Code,
        SystemCode = definition.SystemCode,
        NameAr = definition.NameAr,
        NameEn = definition.NameEn,
        Enabled = definition.DefaultEnabled,
    };

    public void Sync(FeatureDefinition definition)
    {
        SystemCode = definition.SystemCode;
        NameAr = definition.NameAr;
        NameEn = definition.NameEn;
    }

    public void Set(bool enabled) => Enabled = enabled;
}

/// <summary>Logo bytes until the Files service (ADR 0017).</summary>
public sealed class BrandingAsset
{
    public string Kind { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class BrandingAssetKinds
{
    public const string LogoLight = "logo-light";
    public const string LogoDark = "logo-dark";

    public static readonly IReadOnlyList<string> All = [LogoLight, LogoDark];
}

/// <summary>An administrator's replacement for one bundled label in one language. A null value means "back to default".</summary>
public sealed class TranslationOverride : AuditableEntity
{
    public const int ValueMaxLength = 2000;

    private TranslationOverride()
    {
    }

    public string Language { get; private set; } = string.Empty;

    public string Namespace { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string? Value { get; private set; }

    public static TranslationOverride Create(string language, string ns, string key, string? value) => new()
    {
        Language = language,
        Namespace = ns,
        Key = key,
        Value = value,
    };

    public void Set(string? value) => Value = value;
}
