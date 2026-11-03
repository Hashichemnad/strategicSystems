using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Kpi.Domain;

/// <summary>Vision → perspective (pillar) → objective (SPEC §2.7, ADR 0062).</summary>
[Audited(EntityType)]
public sealed class StrategyElement : AuditableEntity
{
    public const string EntityType = "kpi.strategy-element";

    private StrategyElement()
    {
    }

    public string Kind { get; private set; } = ElementKinds.Objective;

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public Guid? ParentId { get; private set; }

    public decimal? Weight { get; private set; }

    public int Sort { get; private set; }

    public bool Active { get; private set; } = true;

    public int? CycleStartYear { get; private set; }

    public int? CycleEndYear { get; private set; }

    public static StrategyElement Create(string kind, string code) => new() { Kind = kind, Code = code.Trim() };

    public void Edit(string code, string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, Guid? parentId, decimal? weight, int sort, bool active,
        int? cycleStartYear, int? cycleEndYear)
    {
        Code = code.Trim();
        NameAr = Clean(nameAr);
        NameEn = Clean(nameEn);
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
        ParentId = parentId;
        Weight = weight;
        Sort = sort;
        Active = active;
        CycleStartYear = cycleStartYear;
        CycleEndYear = cycleEndYear;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
