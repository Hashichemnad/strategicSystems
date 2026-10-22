using Strategy.Modules.Sample.Domain;

namespace Strategy.Modules.Sample.Application;

public sealed record SampleItemDto(
    Guid Id,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version)
{
    public static SampleItemDto From(SampleItem item) => new(
        item.Id,
        item.NameAr,
        item.NameEn,
        item.DescriptionAr,
        item.DescriptionEn,
        item.Status,
        item.CreatedAt,
        item.UpdatedAt,
        item.Version);
}
