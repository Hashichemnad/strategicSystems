using Strategy.Platform.Identity.Application;

namespace Strategy.UnitTests.Identity;

public sealed class CommonPasswordValidatorTests
{
    [Theory]
    [InlineData("password", true)]
    [InlineData("Password2026!", true)]
    [InlineData("!!Qwerty123456", true)]
    [InlineData("Welcome#2026", true)]
    [InlineData("P@ssw0rd2026", true)]
    [InlineData("mariam.alhammadi2026!", true)]
    [InlineData("Strategy#Demo2026", false)]
    [InlineData("Test#Password2026", false)]
    [InlineData("Brand#New-Pass2026", false)]
    [InlineData("", false)]
    public void Common_passwords_and_e_mail_names_are_refused(string password, bool common) =>
        CommonPasswordValidator.IsCommon(password, "Mariam.AlHammadi@example.org").ShouldBe(common);

    [Fact]
    public void Short_e_mail_names_are_not_matched() => CommonPasswordValidator.IsCommon("Bob#Strong-Pass77", "bob@example.org").ShouldBeFalse();
}
