namespace Strategy.Platform.Kernel.Paging;

/// <summary>Paging and sorting request shared by every list endpoint.</summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page, 1 to <see cref="MaxPageSize"/>.</param>
/// <param name="Sort">Field name, optionally prefixed with <c>-</c> for descending (e.g. <c>-createdAt</c>).</param>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize, string? Sort = null)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public int Skip => (Page - 1) * PageSize;

    public (string Field, bool Descending)? SortOrder =>
        string.IsNullOrWhiteSpace(Sort)
            ? null
            : Sort.StartsWith('-') ? (Sort[1..], true) : (Sort, false);

    /// <summary>Returns a copy with page and size clamped to valid values.</summary>
    public PageRequest Normalised() => this with
    {
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize),
    };
}

/// <summary>One page of items plus the total count across all pages.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
