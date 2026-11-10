using FluentValidation;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Platform.Kernel.Validation;

/// <summary>FluentValidation rule of a rich-text field (ADR 0161): the value is measured after <see cref="RichText.Sanitize"/>.</summary>
public static class RichTextRules
{
    /// <summary>
    /// At most <paramref name="plainMax"/> visible characters and <paramref name="storedMax"/> stored characters after sanitising;
    /// error code / message key <c>validation.richTextTooLong</c> with the parameter <c>maxLength</c>.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> RichTextLength<T>(this IRuleBuilder<T, string?> rule, int plainMax, int storedMax = RichText.DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Must((_, value, context) =>
            {
                context.MessageFormatter.AppendArgument("MaxLength", plainMax);
                return RichText.Fits(RichText.Sanitize(value), plainMax, storedMax);
            })
            .WithErrorCode(RichText.TooLongKey)
            .WithMessage(RichText.TooLongKey);
    }
}
