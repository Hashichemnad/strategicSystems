using FluentValidation;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Validation;

namespace Strategy.UnitTests.Kernel;

public class RichTextRulesTests
{
    public sealed record Input(string? Text);

    private sealed class InputValidator : AbstractValidator<Input>
    {
        public InputValidator() => RuleFor(x => x.Text).RichTextLength(5, 40);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("<p><strong>abcde</strong></p>", true)]
    [InlineData("<p>abcdef</p>", false)]
    [InlineData("abcdef", false)]
    [InlineData("<p><strong><em><u><s>abc</s></u></em></strong></p>", false)]
    public void Measures_the_sanitised_value(string? text, bool valid) => new InputValidator().Validate(new Input(text)).IsValid.ShouldBe(valid);

    [Fact]
    public void Maps_to_the_rich_text_message_key_with_max_length()
    {
        var failure = new InputValidator().Validate(new Input("<p>abcdefgh</p>")).Errors.ShouldHaveSingleItem();
        var error = ValidationErrorMapper.ToFieldError(failure);

        error.MessageKey.ShouldBe("validation.richTextTooLong");
        error.Params.ShouldNotBeNull()["maxLength"].ShouldBe(5);
        RichText.LengthError("notes", "<p>abcdefgh</p>", 5).ShouldNotBeNull().Params!["maxLength"].ShouldBe(5);
        RichText.LengthError("notes", "<p>abc</p>", 5).ShouldBeNull();
    }
}
