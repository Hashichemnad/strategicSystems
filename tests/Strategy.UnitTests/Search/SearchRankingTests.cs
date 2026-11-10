using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Persistence;
using Strategy.Platform.Search;

namespace Strategy.UnitTests.Search;

/// <summary>Global search ranking (ADR 0160): exact number &gt; number prefix &gt; number contains &gt; title &gt; key field.</summary>
public class SearchRankingTests
{
    private static readonly Guid Id = Guid.CreateVersion7();

    private static SearchQuery Q(string text) => new(text, SearchText.Normalise(text), 10);

    [Theory]
    [InlineData("RISK-2026-0012", "risk-2026-0012", 1.0, "number")]
    [InlineData("RISK-2026-0012", "RISK-2026", 0.9, "number")]
    [InlineData("RISK-2026-0012", "0012", 0.75, "number")]
    [InlineData("RISK-٢٠٢٦-0012", "risk-2026-0012", 1.0, "number")]
    public void Number_matches_rank_exact_then_prefix_then_contains(string number, string typed, double score, string matchedOn)
    {
        var hit = SearchRanking.Rank("risk.risk", Id, Q(typed), number, ["Data centre outage"])!;

        hit.Score.ShouldBe(score);
        hit.MatchedOn.ShouldBe(matchedOn);
        hit.MatchedText.ShouldBeNull();
    }

    [Fact]
    public void Title_matches_score_between_number_and_key_field_by_similarity()
    {
        var exact = SearchRanking.Rank("risk.risk", Id, Q("outage"), "RISK-1", ["Outage"])!;
        var partial = SearchRanking.Rank("risk.risk", Id, Q("outage"), "RISK-1", ["Data centre outage in the summer peak"])!;

        exact.MatchedOn.ShouldBe(SearchRanking.Title);
        exact.Score.ShouldBe(0.7);
        partial.Score.ShouldBeInRange(0.5, 0.7);
        partial.Score.ShouldBeLessThan(exact.Score);
        partial.Score.ShouldBeLessThan(SearchRanking.NumberContains);
    }

    [Fact]
    public void Key_fields_rank_last_and_carry_a_snippet()
    {
        var hit = SearchRanking.Rank("risk.risk", Id, Q("cooling"), "RISK-1", ["Data centre outage"], [new SearchField("event", "Cooling failure in summer")])!;

        hit.MatchedOn.ShouldBe("event");
        hit.MatchedText.ShouldBe("Cooling failure in summer");
        hit.Score.ShouldBeInRange(SearchRanking.FieldBase, SearchRanking.FieldBase + SearchRanking.FieldSpread);
        hit.Score.ShouldBeLessThan(SearchRanking.TitleBase);
    }

    [Fact]
    public void A_long_key_field_gives_a_normalised_snippet_around_the_match()
    {
        var text = new string('a', 300) + " needle " + new string('b', 300);
        var hit = SearchRanking.Rank("risk.risk", Id, Q("needle"), null, [], [new SearchField("event", text)])!;

        hit.MatchedText!.ShouldContain("needle");
        hit.MatchedText.ShouldStartWith("…");
        hit.MatchedText.ShouldEndWith("…");
        hit.MatchedText.Length.ShouldBeLessThanOrEqualTo(122);
    }

    [Fact]
    public void Every_word_must_appear_in_any_order()
    {
        SearchRanking.Rank("sample.item", Id, Q("plan budget"), null, ["Annual budget plan"])!.MatchedOn.ShouldBe(SearchRanking.Title);
        SearchRanking.Rank("sample.item", Id, Q("plan budget 2027"), null, ["Annual budget plan"]).ShouldBeNull();
        SearchRanking.Words("a b a c").ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void No_hit_when_nothing_contains_the_term()
    {
        SearchRanking.Rank("risk.risk", Id, Q("budget"), "RISK-1", ["Data centre outage"], [new SearchField("event", "Cooling failure")]).ShouldBeNull();
        SearchRanking.Rank("risk.risk", Id, Q("  "), "RISK-1", ["Data centre outage"]).ShouldBeNull();
    }

    [Theory]
    [InlineData("مدرسة أحمد الإبتدائيَّة", "مدرسه احمد")]
    [InlineData("مستشفى الأمل", "مستشفي الامل")]
    [InlineData("خطة ٢٠٢٦ للتحـــول", "خطة 2026 للتحول")]
    [InlineData("آمنة", "امنه")]
    public void Arabic_forms_match_after_normalisation(string title, string typed)
    {
        var hit = SearchRanking.Rank("sample.item", Id, Q(typed), null, [title]);

        hit.ShouldNotBeNull();
        hit.MatchedOn.ShouldBe(SearchRanking.Title);
    }

    [Theory]
    [InlineData("word", "word", 1.0)]
    [InlineData("word", "", 0.0)]
    [InlineData("abc", "xyz", 0.0)]
    public void Similarity_follows_pg_trgm(string left, string right, double expected) =>
        SearchRanking.Similarity(left, right).ShouldBe(expected);

    [Fact]
    public void Similarity_of_a_word_inside_a_longer_text_is_partial() =>
        SearchRanking.Similarity("budget plan for the year", "budget").ShouldBeInRange(0.1, 0.9);

    [Fact]
    public void Generated_column_sql_uses_only_immutable_functions_over_the_given_columns()
    {
        var sql = QuickSearch.Sql("number", "title_ar");

        sql.ShouldContain("coalesce(number::text, '') || ' ' || coalesce(title_ar::text, '')");
        sql.ShouldStartWith("btrim(regexp_replace(lower(translate(regexp_replace(");
        sql.ShouldNotContain("concat_ws"); // stable, not immutable: refused in a generated column
        Should.Throw<ArgumentException>(() => QuickSearch.Sql());
    }

    [Fact]
    public void Like_patterns_are_escaped() => QuickSearch.Escape(@"50%_a\b").ShouldBe(@"50\%\_a\\b");
}
