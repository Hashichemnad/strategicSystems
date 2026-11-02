using Strategy.Modules.Sample.Domain;

namespace Strategy.Modules.Sample.Application;

public sealed record SampleItemDto(
    Guid Id,
    string? Number,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Status,
    Guid? OwnerEmployeeId,
    Guid? UnitId,
    decimal? Amount,
    string ApprovalStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version)
{
    public static SampleItemDto From(SampleItem item) => new(
        item.Id,
        item.Number,
        item.NameAr,
        item.NameEn,
        item.DescriptionAr,
        item.DescriptionEn,
        item.Status,
        item.OwnerEmployeeId,
        item.UnitId,
        item.Amount,
        item.ApprovalStatus,
        item.CreatedAt,
        item.UpdatedAt,
        item.Version);
}
