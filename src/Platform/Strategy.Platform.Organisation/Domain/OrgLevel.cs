using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Organisation.Domain;

/// <summary>A configurable level name (Organisation, Sector, Department, …); <see cref="Sort"/> orders parent before child.</summary>
public sealed class OrgLevel : AuditableEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;

    private OrgLevel()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public int Sort { get; private set; }

    public bool Active { get; private set; } = true;

    public static OrgLevel Create(string code, string? nameAr, string? nameEn, int sort) =>
        new() { Code = code.Trim(), NameAr = Text.Clean(nameAr), NameEn = Text.Clean(nameEn), Sort = sort };

    public void Update(string? nameAr, string? nameEn, int sort, bool active)
    {
        NameAr = Text.Clean(nameAr);
        NameEn = Text.Clean(nameEn);
        Sort = sort;
        Active = active;
    }
}

/// <summary>Singleton organisation settings (moves into the Settings centre UI in Phase 2).</summary>
public sealed class OrgSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; private set; } = SingletonId;

    /// <summary>When on, a child unit's level must sort after its parent's level.</summary>
    public bool EnforceLevelOrder { get; set; } = true;

    public uint Version { get; private set; }
}

internal static class Text
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
