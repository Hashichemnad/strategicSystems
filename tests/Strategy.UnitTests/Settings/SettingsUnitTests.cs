using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.UnitTests.Settings;

public class NumberingPatternTests
{
    private static readonly NumberingContext Context = new(new DateOnly(2026, 3, 7), "spmo");

    [Theory]
    [InlineData("PR-{SEQ:4}", 12, "PR-0012")]
    [InlineData("PROJ-{UNIT}-{YYYY}-{SEQ:3}", 1, "PROJ-SPMO-2026-001")]
    [InlineData("CAP/{YY}/{MM}/{SEQ}", 7, "CAP/26/03/0007")]
    [InlineData("{SEQ:1}", 123, "123")]
    public void Renders_tokens(string pattern, long sequence, string expected)
    {
        NumberingPattern.Validate(pattern).ShouldBeNull();
        NumberingPattern.Render(pattern, Context, sequence).ShouldBe(expected);
    }

    [Theory]
    [InlineData("PROJ-{UNIT}-{YYYY}-{SEQ:3}", "PROJ-SPMO-2026-{SEQ}")]
    [InlineData("PR-{SEQ:4}", "PR-{SEQ}")]
    [InlineData("PR-{SEQ:6}", "PR-{SEQ}")]
    public void Partition_is_the_rendered_prefix_so_padding_changes_keep_the_counter(string pattern, string partition) =>
        NumberingPattern.Partition(pattern, Context).ShouldBe(partition);

    [Theory]
    [InlineData("", "settings.errors.patternInvalid")]
    [InlineData("PR-0001", "settings.errors.patternSequence")]
    [InlineData("{SEQ}-{SEQ}", "settings.errors.patternSequence")]
    [InlineData("{SEQ}-{YYYY}", "settings.errors.patternSequence")]
    [InlineData("PR {SEQ}", "settings.errors.patternInvalid")]
    [InlineData("PR-{DAY}-{SEQ}", "settings.errors.patternInvalid")]
    [InlineData("PR-<script>-{SEQ}", "settings.errors.patternInvalid")]
    [InlineData("{SEQ:0}", "settings.errors.patternInvalid")]
    public void Refuses_invalid_patterns(string pattern, string messageKey) =>
        NumberingPattern.Validate(pattern).ShouldBe(messageKey);

    [Fact]
    public void Unit_token_requires_a_unit_code() =>
        Should.Throw<InvalidOperationException>(() => NumberingPattern.Partition("X-{UNIT}-{SEQ}", new NumberingContext(new DateOnly(2026, 1, 1))));
}

public class SettingsValidationTests
{
    [Fact]
    public void Regional_defaults_are_valid_and_hijri_is_off()
    {
        var defaults = SettingsStore.Deserialize<RegionalSettings>(null);
        defaults.HijriDisplay.ShouldBeFalse();
        defaults.CurrencyCode.ShouldBe("AED");
        SettingsAdministration.ValidateRegional(defaults).ShouldBeEmpty();
    }

    [Fact]
    public void Regional_validation_reports_each_field() =>
        SettingsAdministration.ValidateRegional(new RegionalSettings("Nowhere/City", CurrencyCode: "dirham", DateFormat: "d.M.y"))
            .Select(e => e.Field).ShouldBe(["timeZone", "currencyCode", "dateFormat"]);

    [Fact]
    public void Branding_validation_checks_colour_and_lengths()
    {
        SettingsAdministration.ValidateBranding(new BrandingSettings()).ShouldBeEmpty();
        SettingsAdministration.ValidateBranding(new BrandingSettings(PrimaryColor: "#12345", EmailFooterEn: new string('x', 501)))
            .Select(e => e.Field).ShouldBe(["primaryColor", "emailFooterEn"]);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)]
    public void Logo_type_is_decided_by_magic_number(byte[] content, string? expected) =>
        SettingsAdministration.ImageType(content).ShouldBe(expected);
}
