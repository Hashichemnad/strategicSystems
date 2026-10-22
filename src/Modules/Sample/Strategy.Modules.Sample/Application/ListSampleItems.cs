using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Sample.Application;

public sealed record ListSampleItems(PageRequest Paging, string? Search, string? Status) : IQuery<PagedResult<SampleItemDto>>;

public sealed class ListSampleItemsValidator : AbstractValidator<ListSampleItems>
{
    public ListSampleItemsValidator()
    {
        RuleFor(x => x.Paging.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(x => x.Paging.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(x => x.Paging.SortOrder!.Value.Field)
            .Must(field => ListSampleItemsHandler.SortableFields.Contains(field))
            .When(x => x.Paging.SortOrder is not null)
            .WithErrorCode("validation.unknownSortField")
            .OverridePropertyName("sort");
        RuleFor(x => x.Status)
            .Must(status => SampleItemStatus.All.Contains(status!))
            .When(x => x.Status is not null)
            .WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class ListSampleItemsHandler(SampleDbContext db, IAccessService access, IRequestContext context)
    : IQueryHandler<ListSampleItems, PagedResult<SampleItemDto>>
{
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "nameAr", "nameEn", "status", "createdAt", "updatedAt" };

    public async Task<Result<PagedResult<SampleItemDto>>> HandleAsync(ListSampleItems query, CancellationToken cancellationToken)
    {
        var items = access.ApplyScope(db.Items.AsNoTracking(), context.User, SamplePermissions.Read);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            items = items.Where(x => EF.Functions.ILike(x.NameAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(x.NameEn ?? string.Empty, pattern, "\\"));
        }

        if (query.Status is not null)
        {
            items = items.Where(x => x.Status == query.Status);
        }

        var total = await items.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await Sort(items, query.Paging.SortOrder)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<SampleItemDto>(
            page.Select(SampleItemDto.From).ToList(), query.Paging.Page, query.Paging.PageSize, total);
    }

    private static IQueryable<SampleItem> Sort(IQueryable<SampleItem> items, (string Field, bool Descending)? order) =>
        order switch
        {
            ("nameAr", var desc) => desc ? items.OrderByDescending(x => x.NameAr) : items.OrderBy(x => x.NameAr),
            ("nameEn", var desc) => desc ? items.OrderByDescending(x => x.NameEn) : items.OrderBy(x => x.NameEn),
            ("status", var desc) => desc ? items.OrderByDescending(x => x.Status) : items.OrderBy(x => x.Status),
            ("updatedAt", var desc) => desc ? items.OrderByDescending(x => x.UpdatedAt) : items.OrderBy(x => x.UpdatedAt),
            ("createdAt", false) => items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            _ => items.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
        };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
