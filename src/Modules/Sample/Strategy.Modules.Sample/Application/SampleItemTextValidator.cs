using FluentValidation;
using Strategy.Modules.Sample.Domain;

namespace Strategy.Modules.Sample.Application;

/// <summary>Bilingual text fields shared by create and update.</summary>
public interface ISampleItemText
{
    string? NameAr { get; }

    string? NameEn { get; }

    string? DescriptionAr { get; }

    string? DescriptionEn { get; }
}

internal static class SampleItemTextRules
{
    /// <summary>At least one name (Arabic or English); length limits on all text.</summary>
    public static void AddTextRules<T>(this AbstractValidator<T> validator)
        where T : ISampleItemText
    {
        validator.RuleFor(x => x.NameAr)
            .NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.NameEn))
            .WithErrorCode("validation.atLeastOneLanguage");
        validator.RuleFor(x => x.NameAr).MaximumLength(SampleItem.NameMaxLength);
        validator.RuleFor(x => x.NameEn).MaximumLength(SampleItem.NameMaxLength);
        validator.RuleFor(x => x.DescriptionAr).MaximumLength(SampleItem.DescriptionMaxLength);
        validator.RuleFor(x => x.DescriptionEn).MaximumLength(SampleItem.DescriptionMaxLength);
    }
}
