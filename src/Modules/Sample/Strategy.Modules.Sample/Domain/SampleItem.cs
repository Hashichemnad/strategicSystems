using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Domain;

/// <summary>Template aggregate: bilingual text, a status lifecycle and optimistic concurrency.</summary>
public sealed class SampleItem : AuditableEntity
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private SampleItem()
    {
    }

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string Status { get; private set; } = SampleItemStatus.Active;

    /// <summary>Human-readable number from the Numbering service (format <c>sample.item</c>). Null for items created before Phase 2.</summary>
    public string? Number { get; private set; }

    /// <summary>Owner (the creator's employee): the "own" and "team" data scopes (ADR 0015).</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    /// <summary>The owner's unit at creation: the "unit" and "unit and below" data scopes.</summary>
    public Guid? UnitId { get; private set; }

    public static SampleItem Create(
        string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, string? number = null, Guid? ownerEmployeeId = null, Guid? unitId = null)
    {
        var item = new SampleItem { Number = number, OwnerEmployeeId = ownerEmployeeId, UnitId = unitId };
        item.SetText(nameAr, nameEn, descriptionAr, descriptionEn);
        return item;
    }

    public Result Update(string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn)
    {
        if (Status == SampleItemStatus.Inactive)
        {
            return Error.Rule("sample.item.inactive", "sample.errors.inactive");
        }

        SetText(nameAr, nameEn, descriptionAr, descriptionEn);
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status == SampleItemStatus.Active)
        {
            return Error.Rule("sample.item.alreadyActive", "sample.errors.alreadyActive");
        }

        Status = SampleItemStatus.Active;
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (Status == SampleItemStatus.Inactive)
        {
            return Error.Rule("sample.item.alreadyInactive", "sample.errors.alreadyInactive");
        }

        Status = SampleItemStatus.Inactive;
        return Result.Success();
    }

    private void SetText(string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn)
    {
        NameAr = Clean(nameAr);
        NameEn = Clean(nameEn);
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Status codes. Business logic checks codes only; labels live in the translation files.</summary>
public static class SampleItemStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}
