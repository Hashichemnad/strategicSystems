using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Domain;

/// <summary>Template aggregate: bilingual text, a status lifecycle and optimistic concurrency.</summary>
[Audited(SampleItem.EntityType)]
public sealed class SampleItem : AuditableEntity
{
    public const string EntityType = "sample.item";
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

    /// <summary>An amount (AED) the approval process can test in an entry condition (Phase 4 worked example).</summary>
    public decimal? Amount { get; private set; }

    /// <summary>Where the item stands in its approval (<see cref="SampleApprovalStatus"/>), moved by the approval engine's hook.</summary>
    public string ApprovalStatus { get; private set; } = SampleApprovalStatus.Draft;

    public static SampleItem Create(
        string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, string? number = null, Guid? ownerEmployeeId = null, Guid? unitId = null,
        decimal? amount = null)
    {
        var item = new SampleItem { Number = number, OwnerEmployeeId = ownerEmployeeId, UnitId = unitId, Amount = amount };
        item.SetText(nameAr, nameEn, descriptionAr, descriptionEn);
        return item;
    }

    public Result Update(string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, decimal? amount)
    {
        if (Status == SampleItemStatus.Inactive)
        {
            return Error.Rule("sample.item.inactive", "sample.errors.inactive");
        }

        if (ApprovalStatus == SampleApprovalStatus.Pending)
        {
            return Error.Rule("sample.item.pendingApproval", "sample.errors.pendingApproval");
        }

        SetText(nameAr, nameEn, descriptionAr, descriptionEn);
        Amount = amount;
        return Result.Success();
    }

    /// <summary>Draft or returned → pending (the engine starts the approval in the same transaction).</summary>
    public Result SubmitForApproval()
    {
        if (Status == SampleItemStatus.Inactive)
        {
            return Error.Rule("sample.item.inactive", "sample.errors.inactive");
        }

        if (!SampleApprovalStatus.Submittable.Contains(ApprovalStatus))
        {
            return Error.Rule("sample.item.notSubmittable", "sample.errors.notSubmittable");
        }

        ApprovalStatus = SampleApprovalStatus.Pending;
        return Result.Success();
    }

    /// <summary>Pending → approved / rejected / returned, from the approval engine's hook.</summary>
    public Result ApplyApprovalOutcome(string status)
    {
        if (ApprovalStatus != SampleApprovalStatus.Pending
            || status is not (SampleApprovalStatus.Approved or SampleApprovalStatus.Rejected or SampleApprovalStatus.Returned or SampleApprovalStatus.Draft))
        {
            return Error.Rule("sample.item.notPending", "sample.errors.notPending");
        }

        ApprovalStatus = status;
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

/// <summary>Approval status codes (docs/design/sample.md, Phase 4).</summary>
public static class SampleApprovalStatus
{
    public const string Draft = "draft";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";

    public static readonly IReadOnlyList<string> All = [Draft, Pending, Approved, Rejected, Returned];

    /// <summary>A rejected item is final (as a rejected program request, SPEC §1.5 W1); draft and returned items may be submitted.</summary>
    public static readonly IReadOnlyList<string> Submittable = [Draft, Returned];
}

/// <summary>Status codes. Business logic checks codes only; labels live in the translation files.</summary>
public static class SampleItemStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}
