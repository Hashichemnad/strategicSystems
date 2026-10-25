using Strategy.Platform.Kernel.Text;

namespace Strategy.UnitTests.Organisation;

public class SearchTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إدارة", "اداره")]
    [InlineData("آمنة", "امنه")]
    [InlineData("مُصطفى", "مصطفي")]
    [InlineData("مـــالية", "ماليه")]
    [InlineData("٢٠٢٥", "2025")]
    [InlineData("  Finance   DEPT ", "finance dept")]
    public void Normalises_arabic_and_latin_text(string input, string expected) => SearchText.Normalise(input).ShouldBe(expected);

    [Fact]
    public void Joins_parts_and_skips_empty_ones() => SearchText.Normalise("E1", null, " ", "سارة").ShouldBe("e1 ساره");
}
