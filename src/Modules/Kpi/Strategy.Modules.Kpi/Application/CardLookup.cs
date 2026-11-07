using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Domain;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Modules.Kpi.Application;

/// <summary>A KPI card as the shared picker shows it (ADR 0169).</summary>
public sealed record KpiCardLookupDto(Guid Id, string Code, string? NameAr, string? NameEn, string Status);

/// <summary>
/// Search as you type for the KPI card picker: code or name (Arabic-normalised), or the given <c>Ids</c> (the selected cards' labels). Scoped to
/// the cards the caller may read; at most <see cref="MaxPageSize"/> per page. <c>Status</c>: <c>active</c> (default), <c>inactive</c> or <c>all</c>.
/// </summary>
public sealed record LookupKpiCards(string? Search, IReadOnlyList<Guid>? Ids, string? Status, int Page, int PageSize) : IQuery<PagedResult<KpiCardLookupDto>>
{
    public const int MaxPageSize = 50;
}

internal sealed class CardLookup(KpiReader reader, IRequestContext context) : IQueryHandler<LookupKpiCards, PagedResult<KpiCardLookupDto>>
{
    /// <summary>
    /// The SQL side of <see cref="SearchText.Normalise"/>: tashkeel (U+064B–U+065F), superscript alef and tatweel removed, alef forms → ا,
    /// alef maqsura → ي, ta marbuta → ه. Applied as nested <c>replace()</c> calls; case is ignored by ILIKE.
    /// </summary>
    private static readonly (string From, string To)[] Folding =
    [
        .. Enumerable.Range(0x064B, 0x065F - 0x064B + 1).Select(c => (((char)c).ToString(), string.Empty)),
        ("ٰ", string.Empty), ("ـ", string.Empty),
        ("أ", "ا"), ("إ", "ا"), ("آ", "ا"), ("ٱ", "ا"), ("ى", "ي"), ("ة", "ه"),
    ];

    private static readonly System.Reflection.MethodInfo Replace = typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;

    private static readonly System.Reflection.MethodInfo ILike = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    public async Task<Result<PagedResult<KpiCardLookupDto>>> HandleAsync(LookupKpiCards query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, LookupKpiCards.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        var source = await reader.CardsAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (query.Ids is { Count: > 0 } ids)
        {
            var wanted = ids.Take(LookupKpiCards.MaxPageSize).ToList();
            source = source.Where(c => wanted.Contains(c.Id));
        }
        else
        {
            source = query.Status switch
            {
                "all" => source,
                CardStatuses.Inactive => source.Where(c => c.Status == CardStatuses.Inactive),
                _ => source.Where(c => c.Status == CardStatuses.Active),
            };
            var term = SearchText.Normalise(query.Search);
            if (term.Length > 0)
            {
                source = source.Where(Matches(Like.Pattern(term)));
            }
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await source.OrderBy(c => c.Code).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new KpiCardLookupDto(c.Id, c.Code, c.NameAr, c.NameEn, c.Status))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<KpiCardLookupDto>(items, page, pageSize, total);
    }

    /// <summary><c>ILIKE(fold(code || ' ' || name_ar || ' ' || name_en), pattern)</c> — no stored search column (ADR 0169).</summary>
    private static Expression<Func<KpiCard, bool>> Matches(string pattern)
    {
        Expression<Func<KpiCard, string>> text = c => c.Code + " " + (c.NameAr ?? string.Empty) + " " + (c.NameEn ?? string.Empty);
        var folded = text.Body;
        foreach (var (from, to) in Folding)
        {
            folded = Expression.Call(folded, Replace, Expression.Constant(from), Expression.Constant(to));
        }

        Expression<Func<string>> value = () => pattern; // a parameter, not a literal
        var call = Expression.Call(ILike, Expression.Constant(EF.Functions), folded, value.Body, Expression.Constant("\\"));
        return Expression.Lambda<Func<KpiCard, bool>>(call, text.Parameters);
    }
}
