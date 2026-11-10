using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Global-search columns (ADR 0160): a generated, stored <c>quick_search</c> column whose SQL mirrors
/// <see cref="Kernel.Text.SearchText.Normalise"/>, with a GIN trigram index, and the query helper the search contributors use.
/// </summary>
public static class QuickSearch
{
    /// <summary>Shadow property (column <c>quick_search</c>) added by <see cref="HasQuickSearch{T}"/>.</summary>
    public const string Property = "QuickSearch";

    /// <summary>Most words of a term matched (the same limit as <c>SearchRanking.MaxWords</c>).</summary>
    public const int MaxWords = 8;

    private const string Tashkeel = "[ً-ٰٟـ]";
    private const string FromChars = "آأإٱىة٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹";
    private const string ToChars = "اااايه" + "0123456789" + "0123456789";

    private static readonly MethodInfo ILike = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlDbFunctionsExtensions.ILike), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    /// <summary>
    /// The SQL twin of <c>SearchText.Normalise(columns…)</c>: tashkeel and tatweel removed, alef forms → ا, ى → ي, ة → ه, Arabic-Indic and
    /// extended digits → ASCII, lower case, whitespace collapsed. Immutable functions only (usable in a generated column).
    /// </summary>
    /// <param name="columns">Column names (snake_case) in the order they are joined.</param>
    public static string Sql(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0)
        {
            throw new ArgumentException("At least one column is needed.", nameof(columns));
        }

        var joined = string.Join(" || ' ' || ", columns.Select(c => $"coalesce({c}::text, '')"));
        return $"btrim(regexp_replace(lower(translate(regexp_replace({joined}, '{Tashkeel}', '', 'g'), '{FromChars}', '{ToChars}')), '\\s+', ' ', 'g'))";
    }

    /// <summary>Adds the generated <c>quick_search</c> column over <paramref name="columns"/> and its GIN trigram index.</summary>
    public static EntityTypeBuilder<T> HasQuickSearch<T>(this EntityTypeBuilder<T> builder, params string[] columns)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property<string>(Property).HasColumnType("text").HasComputedColumnSql(Sql(columns), stored: true);
        builder.HasIndex(Property).HasMethod("gin").HasOperators("gin_trgm_ops");
        return builder;
    }

    /// <summary>A GIN trigram index on an existing (C#-maintained) normalised column.</summary>
    public static EntityTypeBuilder<T> HasTrigramIndex<T>(this EntityTypeBuilder<T> builder, string property)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasIndex(property).HasMethod("gin").HasOperators("gin_trgm_ops");
        return builder;
    }

    /// <summary>
    /// Rows whose normalised search column contains every word of <paramref name="normalised"/> (any order), ordered by exact number, number prefix, then trigram
    /// similarity, cut to <paramref name="limit"/>. Apply it to a query that is already filtered by the user's data scope.
    /// </summary>
    /// <param name="source">The scoped query.</param>
    /// <param name="normalised">The normalised term (<c>SearchQuery.Normalised</c>).</param>
    /// <param name="limit">Most rows returned.</param>
    /// <param name="number">The record's number (null: no number ordering).</param>
    /// <param name="column">The normalised column: <see cref="Property"/> or an existing mapped property (e.g. <c>SearchText</c>).</param>
    public static IQueryable<T> WhereQuickSearch<T>(
        this IQueryable<T> source, string normalised, int limit, Expression<Func<T, string?>>? number, string column = Property)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(normalised);
        var escaped = Escape(normalised);
        var filtered = source.Where(x => EF.Property<string?>(x, column) != null);
        // Every word, in any order (at most MaxWords): one ILIKE per word, each served by the trigram index.
        foreach (var word in normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(MaxWords))
        {
            var contains = "%" + Escape(word) + "%";
            filtered = filtered.Where(x => EF.Functions.ILike(EF.Property<string>(x, column), contains, "\\"));
        }

        IOrderedQueryable<T> ordered;
        if (number is null)
        {
            ordered = filtered.OrderByDescending(x => EF.Functions.TrigramsSimilarity(EF.Property<string>(x, column), normalised));
        }
        else
        {
            ordered = filtered.OrderByDescending(NumberMatches(number, escaped)).ThenByDescending(NumberMatches(number, escaped + "%"))
                .ThenByDescending(x => EF.Functions.TrigramsSimilarity(EF.Property<string>(x, column), normalised));
        }

        return ordered.Take(limit);
    }

    /// <summary>Escapes <c>\</c>, <c>%</c> and <c>_</c> for <c>ILIKE … ESCAPE '\'</c>.</summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    }

    /// <summary><c>coalesce(number, '') ILIKE @pattern ESCAPE '\'</c> as a lambda over the entity (the pattern is a query parameter).</summary>
    private static Expression<Func<T, bool>> NumberMatches<T>(Expression<Func<T, string?>> number, string pattern)
    {
        var holder = new PatternHolder(pattern);
        var body = Expression.Call(
            ILike,
            Expression.Constant(EF.Functions),
            Expression.Coalesce(number.Body, Expression.Constant(string.Empty)),
            Expression.Property(Expression.Constant(holder), nameof(PatternHolder.Value)),
            Expression.Constant("\\"));
        return Expression.Lambda<Func<T, bool>>(body, number.Parameters);
    }

    private sealed class PatternHolder(string value)
    {
        public string Value { get; } = value;
    }
}
