using Strategy.Platform.Kernel.Text;

namespace Strategy.Platform.Search;

/// <summary>A key field of a record that search also matches (e.g. a risk's event text), with its stable code.</summary>
public sealed record SearchField(string Key, string? Value);

/// <summary>
/// Ranking of a fetched record against the query (ADR 0160): exact number &gt; number prefix &gt; number contains &gt; title (trigram
/// similarity) &gt; key field. A title or key field matches when it contains every word of the term, in any order. Pure and deterministic; every value is compared in its <see cref="SearchText.Normalise"/> form.
/// </summary>
public static class SearchRanking
{
    public const string Number = "number";
    public const string Title = "title";

    public const double ExactNumber = 1.0;
    public const double NumberPrefix = 0.9;
    public const double NumberContains = 0.75;
    public const double TitleBase = 0.5;
    public const double TitleSpread = 0.2;
    public const double FieldBase = 0.3;
    public const double FieldSpread = 0.1;

    /// <summary>Most words of a term used for matching (the rest are ignored).</summary>
    public const int MaxWords = 8;

    private const int SnippetLength = 120;

    /// <summary>The hit for this record, or null when neither its number, a title nor a key field contains the term.</summary>
    public static SearchHit? Rank(
        string resourceType, Guid id, SearchQuery query, string? number, IReadOnlyList<string?> titles, IReadOnlyList<SearchField>? fields = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(titles);
        var term = query.Normalised;
        if (term.Length == 0)
        {
            return null;
        }

        var normalisedNumber = SearchText.Normalise(number);
        if (normalisedNumber.Length > 0)
        {
            if (string.Equals(normalisedNumber, term, StringComparison.Ordinal))
            {
                return new SearchHit(resourceType, id, ExactNumber, Number, null);
            }

            if (normalisedNumber.StartsWith(term, StringComparison.Ordinal))
            {
                return new SearchHit(resourceType, id, NumberPrefix, Number, null);
            }

            if (normalisedNumber.Contains(term, StringComparison.Ordinal))
            {
                return new SearchHit(resourceType, id, NumberContains, Number, null);
            }
        }

        var words = Words(term);
        var bestTitle = titles.Select(t => SearchText.Normalise(t)).Where(t => ContainsAll(t, words))
            .Select(t => Similarity(t, term)).DefaultIfEmpty(-1).Max();
        if (bestTitle >= 0)
        {
            return new SearchHit(resourceType, id, Math.Round(TitleBase + (TitleSpread * bestTitle), 4), Title, null);
        }

        foreach (var field in fields ?? [])
        {
            var value = SearchText.Normalise(field.Value);
            if (ContainsAll(value, words))
            {
                var at = value.IndexOf(words[0], StringComparison.Ordinal);
                // A short field is shown as written; a long one as a normalised snippet around the match.
                var shown = field.Value!.Trim().Length <= SnippetLength ? field.Value.Trim() : Snippet(value, at, words[0].Length);
                return new SearchHit(resourceType, id, Math.Round(FieldBase + (FieldSpread * Similarity(value, term)), 4), field.Key, shown);
            }
        }

        return null;
    }

    /// <summary>The words of a normalised term (at most <see cref="MaxWords"/>): a record matches when it contains every one, in any order.</summary>
    public static IReadOnlyList<string> Words(string normalised)
    {
        ArgumentNullException.ThrowIfNull(normalised);
        return normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(MaxWords).ToList();
    }

    private static bool ContainsAll(string text, IReadOnlyList<string> words) =>
        words.Count > 0 && words.All(w => text.Contains(w, StringComparison.Ordinal));

    /// <summary>
    /// Trigram similarity as pg_trgm computes it: each word padded with two spaces before and one after, the share of common trigrams
    /// (|A ∩ B| / |A ∪ B|). 1 for equal texts, 0 when nothing is shared.
    /// </summary>
    public static double Similarity(string? left, string? right)
    {
        var a = Trigrams(SearchText.Normalise(left));
        var b = Trigrams(SearchText.Normalise(right));
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var common = a.Count(b.Contains);
        return common / (double)(a.Count + b.Count - common);
    }

    private static HashSet<string> Trigrams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var padded = "  " + word + " ";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                set.Add(padded.Substring(i, 3));
            }
        }

        return set;
    }

    /// <summary>About <see cref="SnippetLength"/> characters of the (normalised) field around the match, with ellipses where cut.</summary>
    public static string Snippet(string value, int at, int length)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length <= SnippetLength)
        {
            return value;
        }

        var start = Math.Max(0, Math.Min(at - ((SnippetLength - length) / 2), value.Length - SnippetLength));
        var snippet = value.Substring(start, SnippetLength);
        return (start > 0 ? "…" : string.Empty) + snippet + (start + SnippetLength < value.Length ? "…" : string.Empty);
    }
}
